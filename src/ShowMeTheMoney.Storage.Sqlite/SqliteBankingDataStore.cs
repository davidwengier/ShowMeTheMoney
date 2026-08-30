using System.Globalization;
using Microsoft.Data.Sqlite;
using ShowMeTheMoney.Core.Banking;

namespace ShowMeTheMoney.Storage.Sqlite;

public sealed class SqliteBankingDataStore : IBankingDataStore
{
    private const string EmptyInstitutionName = "No bank data imported";
    private const string EmptyDataSourceDescription = "Import a QIF file to get started";

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SqliteBankingDataStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var directory = Path.GetDirectoryName(databasePath)
            ?? throw new ArgumentException(
                "The database path must have a parent directory.",
                nameof(databasePath));
        Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public async Task<BankingSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);

            var metadata = await ReadMetadataAsync(connection, cancellationToken);
            var accounts = await ReadAccountsAsync(connection, cancellationToken);
            var transactions = await ReadTransactionsAsync(connection, cancellationToken);

            return new BankingSnapshot(
                metadata.GetValueOrDefault("institution_name", EmptyInstitutionName),
                metadata.GetValueOrDefault(
                    "data_source_description",
                    EmptyDataSourceDescription),
                accounts,
                transactions);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReplaceSnapshotAsync(
        BankingSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var transaction = connection.BeginTransaction();

            await ExecuteAsync(
                connection,
                transaction,
                "DELETE FROM transactions; DELETE FROM accounts; DELETE FROM metadata;",
                cancellationToken);
            await InsertMetadataAsync(connection, transaction, snapshot, cancellationToken);
            await InsertAccountsAsync(connection, transaction, snapshot.Accounts, cancellationToken);
            await InsertTransactionsAsync(
                connection,
                transaction,
                snapshot.Transactions,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAccountAsync(
        BankAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var transaction = connection.BeginTransaction();

            var displayOrder = await GetNextDisplayOrderAsync(
                connection,
                transaction,
                "accounts",
                cancellationToken);
            await InsertAccountAsync(
                connection,
                transaction,
                account,
                displayOrder,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAccountAsync(
        string accountId,
        string name,
        decimal? balance,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE accounts
                SET name = $name, balance = $balance
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$name", name.Trim());
            command.Parameters.AddWithValue(
                "$balance",
                balance is null
                    ? DBNull.Value
                    : FormatDecimal(balance.Value));
            command.Parameters.AddWithValue("$id", accountId);

            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new InvalidOperationException(
                    $"Account '{accountId}' does not exist.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ImportTransactionsAsync(
        string accountId,
        IReadOnlyList<BankTransaction> transactions,
        string dataSourceDescription,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceDescription);

        if (transactions.Any(transaction => transaction.AccountId != accountId))
        {
            throw new ArgumentException(
                "Every imported transaction must belong to the selected account.",
                nameof(transactions));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var transaction = connection.BeginTransaction();

            await UpsertMetadataValueAsync(
                connection,
                transaction,
                "institution_name",
                "Imported bank accounts",
                cancellationToken);
            await UpsertMetadataValueAsync(
                connection,
                transaction,
                "data_source_description",
                dataSourceDescription,
                cancellationToken);

            var learnedRules = await ReadCategoryRulesAsync(
                connection,
                transaction,
                cancellationToken);
            foreach (var bankTransaction in transactions)
            {
                var categorizedTransaction = bankTransaction with
                {
                    Category = TransactionCategoryRules.Categorize(
                        bankTransaction,
                        learnedRules)
                };
                await UpsertTransactionAsync(
                    connection,
                    transaction,
                    categorizedTransaction,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetTransactionCategoryAsync(
        string transactionId,
        string category,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var transaction = connection.BeginTransaction();

            var description = await ReadTransactionDescriptionAsync(
                connection,
                transaction,
                transactionId,
                cancellationToken);
            var merchantKey = TransactionCategoryRules.NormalizeDescription(description);
            await UpsertCategoryRuleAsync(
                connection,
                transaction,
                merchantKey,
                category.Trim(),
                cancellationToken);
            await UpdateMatchingTransactionCategoriesAsync(
                connection,
                transaction,
                merchantKey,
                category.Trim(),
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> ApplyTransactionCategoryRulesAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var transaction = connection.BeginTransaction();
            var learnedRules = await ReadCategoryRulesAsync(
                connection,
                transaction,
                cancellationToken);
            var transactions = await ReadTransactionsAsync(
                connection,
                transaction,
                accountId,
                cancellationToken);
            var updatedCount = 0;

            foreach (var bankTransaction in transactions)
            {
                var category = TransactionCategoryRules.Categorize(
                    bankTransaction,
                    learnedRules);
                if (category == bankTransaction.Category)
                {
                    continue;
                }

                await UpdateTransactionCategoryAsync(
                    connection,
                    transaction,
                    bankTransaction.Id,
                    category,
                    cancellationToken);
                updatedCount++;
            }

            await transaction.CommitAsync(cancellationToken);
            return updatedCount;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS metadata (
                key TEXT NOT NULL PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS accounts (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                masked_number TEXT NOT NULL,
                balance TEXT NULL,
                currency TEXT NOT NULL,
                display_order INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS transactions (
                id TEXT NOT NULL PRIMARY KEY,
                account_id TEXT NOT NULL,
                posted_on TEXT NOT NULL,
                description TEXT NOT NULL,
                category TEXT NOT NULL,
                amount TEXT NOT NULL,
                currency TEXT NOT NULL,
                is_pending INTEGER NOT NULL,
                display_order INTEGER NOT NULL,
                FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS category_rules (
                merchant_key TEXT NOT NULL PRIMARY KEY,
                category TEXT NOT NULL
            );

            PRAGMA user_version = 2;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadCategoryRulesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var rules = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT merchant_key, category FROM category_rules;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rules.Add(reader.GetString(0), reader.GetString(1));
        }

        return rules;
    }

    private static async Task<string> ReadTransactionDescriptionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string transactionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT description FROM transactions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", transactionId);
        var description = await command.ExecuteScalarAsync(cancellationToken) as string;
        return description
            ?? throw new InvalidOperationException(
                $"Transaction '{transactionId}' does not exist.");
    }

    private static async Task<IReadOnlyList<BankTransaction>> ReadTransactionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string accountId,
        CancellationToken cancellationToken)
    {
        var transactions = new List<BankTransaction>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, account_id, posted_on, description, category, amount,
                   currency, is_pending
            FROM transactions
            WHERE account_id = $account_id;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            transactions.Add(new BankTransaction(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.ParseExact(
                    reader.GetString(2),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture),
                reader.GetString(3),
                reader.GetString(4),
                ParseDecimal(reader.GetString(5)),
                reader.GetString(6),
                reader.GetBoolean(7)));
        }

        return transactions;
    }

    private static async Task UpsertCategoryRuleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string merchantKey,
        string category,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO category_rules (merchant_key, category)
            VALUES ($merchant_key, $category)
            ON CONFLICT(merchant_key) DO UPDATE SET category = excluded.category;
            """;
        command.Parameters.AddWithValue("$merchant_key", merchantKey);
        command.Parameters.AddWithValue("$category", category);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateMatchingTransactionCategoriesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string merchantKey,
        string category,
        CancellationToken cancellationToken)
    {
        var matches = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT id, description FROM transactions;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (TransactionCategoryRules.NormalizeDescription(reader.GetString(1)) == merchantKey)
                {
                    matches.Add(reader.GetString(0));
                }
            }
        }

        foreach (var matchingTransactionId in matches)
        {
            await UpdateTransactionCategoryAsync(
                connection,
                transaction,
                matchingTransactionId,
                category,
                cancellationToken);
        }
    }

    private static async Task UpdateTransactionCategoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string transactionId,
        string category,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE transactions SET category = $category WHERE id = $id;";
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$id", transactionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, string>> ReadMetadataAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM metadata;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            metadata.Add(reader.GetString(0), reader.GetString(1));
        }

        return metadata;
    }

    private static async Task<IReadOnlyList<BankAccount>> ReadAccountsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var accounts = new List<BankAccount>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, name, masked_number, balance, currency
            FROM accounts
            ORDER BY display_order;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            accounts.Add(new BankAccount(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : ParseDecimal(reader.GetString(3)),
                reader.GetString(4)));
        }

        return accounts;
    }

    private static async Task<IReadOnlyList<BankTransaction>> ReadTransactionsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var transactions = new List<BankTransaction>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, account_id, posted_on, description, category, amount,
                   currency, is_pending
            FROM transactions
            ORDER BY display_order;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            transactions.Add(new BankTransaction(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.ParseExact(
                    reader.GetString(2),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture),
                reader.GetString(3),
                reader.GetString(4),
                ParseDecimal(reader.GetString(5)),
                reader.GetString(6),
                reader.GetBoolean(7)));
        }

        return transactions;
    }

    private static async Task InsertMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BankingSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await InsertMetadataValueAsync(
            connection,
            transaction,
            "institution_name",
            snapshot.InstitutionName,
            cancellationToken);
        await InsertMetadataValueAsync(
            connection,
            transaction,
            "data_source_description",
            snapshot.DataSourceDescription,
            cancellationToken);
    }

    private static async Task InsertMetadataValueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO metadata (key, value) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAccountsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<BankAccount> accounts,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < accounts.Count; index++)
        {
            await InsertAccountAsync(
                connection,
                transaction,
                accounts[index],
                index,
                cancellationToken);
        }
    }

    private static async Task InsertAccountAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BankAccount account,
        int displayOrder,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO accounts (
                id, name, masked_number, balance, currency, display_order
            )
            VALUES (
                $id, $name, $masked_number, $balance, $currency, $display_order
            );
            """;
        command.Parameters.AddWithValue("$id", account.Id);
        command.Parameters.AddWithValue("$name", account.Name);
        command.Parameters.AddWithValue("$masked_number", account.MaskedNumber);
        command.Parameters.AddWithValue(
            "$balance",
            account.Balance is null
                ? DBNull.Value
                : FormatDecimal(account.Balance.Value));
        command.Parameters.AddWithValue("$currency", account.Currency);
        command.Parameters.AddWithValue("$display_order", displayOrder);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertTransactionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<BankTransaction> transactions,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < transactions.Count; index++)
        {
            var bankTransaction = transactions[index];
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO transactions (
                    id, account_id, posted_on, description, category, amount,
                    currency, is_pending, display_order
                )
                VALUES (
                    $id, $account_id, $posted_on, $description, $category, $amount,
                    $currency, $is_pending, $display_order
                );
                """;
            command.Parameters.AddWithValue("$id", bankTransaction.Id);
            command.Parameters.AddWithValue("$account_id", bankTransaction.AccountId);
            command.Parameters.AddWithValue(
                "$posted_on",
                bankTransaction.PostedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$description", bankTransaction.Description);
            command.Parameters.AddWithValue("$category", bankTransaction.Category);
            command.Parameters.AddWithValue("$amount", FormatDecimal(bankTransaction.Amount));
            command.Parameters.AddWithValue("$currency", bankTransaction.Currency);
            command.Parameters.AddWithValue("$is_pending", bankTransaction.IsPending);
            command.Parameters.AddWithValue("$display_order", index);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task UpsertTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        BankTransaction bankTransaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO transactions (
                id, account_id, posted_on, description, category, amount,
                currency, is_pending, display_order
            )
            VALUES (
                $id, $account_id, $posted_on, $description, $category, $amount,
                $currency, $is_pending,
                (SELECT COALESCE(MAX(display_order), -1) + 1 FROM transactions)
            )
            ON CONFLICT(id) DO UPDATE SET
                account_id = excluded.account_id,
                posted_on = excluded.posted_on,
                description = excluded.description,
                category = excluded.category,
                amount = excluded.amount,
                currency = excluded.currency,
                is_pending = excluded.is_pending;
            """;
        command.Parameters.AddWithValue("$id", bankTransaction.Id);
        command.Parameters.AddWithValue("$account_id", bankTransaction.AccountId);
        command.Parameters.AddWithValue(
            "$posted_on",
            bankTransaction.PostedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$description", bankTransaction.Description);
        command.Parameters.AddWithValue("$category", bankTransaction.Category);
        command.Parameters.AddWithValue("$amount", FormatDecimal(bankTransaction.Amount));
        command.Parameters.AddWithValue("$currency", bankTransaction.Currency);
        command.Parameters.AddWithValue("$is_pending", bankTransaction.IsPending);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertMetadataValueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO metadata (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> GetNextDisplayOrderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COALESCE(MAX(display_order), -1) + 1 FROM {tableName};";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string FormatDecimal(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static decimal ParseDecimal(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
