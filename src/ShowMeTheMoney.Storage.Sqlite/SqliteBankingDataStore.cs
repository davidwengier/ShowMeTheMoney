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
    private bool _schemaInitialized;

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

    public async Task<BankingOverview> GetOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);

            var metadata = await ReadMetadataAsync(connection, cancellationToken);
            var accounts = await ReadAccountsAsync(connection, cancellationToken);
            return new BankingOverview(
                metadata.GetValueOrDefault("institution_name", EmptyInstitutionName),
                metadata.GetValueOrDefault(
                    "data_source_description",
                    EmptyDataSourceDescription),
                accounts);
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
            var normalizedTransactions = snapshot.Transactions
                .Select(item => item with
                {
                    Category = NormalizeStoredCategory(item.Category)
                })
                .ToArray();
            foreach (var category in normalizedTransactions
                         .Select(item => item.Category)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await EnsureCategoryAsync(
                    connection,
                    transaction,
                    category,
                    cancellationToken);
            }
            await InsertTransactionsAsync(
                connection,
                transaction,
                normalizedTransactions,
                cancellationToken);
            foreach (var account in snapshot.Accounts)
            {
                await RebuildRunningBalancesAsync(
                    connection,
                    transaction,
                    account.Id,
                    cancellationToken);
            }

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
            await using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
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

            await RebuildRunningBalancesAsync(
                connection,
                transaction,
                accountId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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
                await EnsureCategoryAsync(
                    connection,
                    transaction,
                    categorizedTransaction.Category,
                    cancellationToken);
                await UpsertTransactionAsync(
                    connection,
                    transaction,
                    categorizedTransaction,
                    cancellationToken);
            }

            await RebuildRunningBalancesAsync(
                connection,
                transaction,
                accountId,
                cancellationToken);
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
            await EnsureCategoryAsync(
                connection,
                transaction,
                category.Trim(),
                cancellationToken);
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

    public async Task<IReadOnlyList<TransactionCategory>> GetTransactionCategoriesAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            var categories = new List<TransactionCategory>();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT name, is_system
                FROM categories
                ORDER BY is_system DESC, name COLLATE NOCASE;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                categories.Add(new TransactionCategory(
                    reader.GetString(0),
                    reader.GetBoolean(1)));
            }

            return categories;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddTransactionCategoryAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = ValidateCategoryName(name);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO categories (name, is_system) VALUES ($name, 0);";
            command.Parameters.AddWithValue("$name", normalizedName);
            try
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException exception)
                when (exception.SqliteErrorCode == 19)
            {
                throw new InvalidOperationException(
                    $"Category '{normalizedName}' already exists.",
                    exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RenameTransactionCategoryAsync(
        string currentName,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentName);
        var normalizedName = ValidateCategoryName(newName);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var transaction = connection.BeginTransaction();
            await EnsureCustomCategoryAsync(
                connection,
                transaction,
                currentName.Trim(),
                cancellationToken);
            if (!currentName.Trim().Equals(normalizedName, StringComparison.OrdinalIgnoreCase)
                && await CategoryExistsAsync(
                    connection,
                    transaction,
                    normalizedName,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Category '{normalizedName}' already exists.");
            }

            await ExecuteCategoryRenameAsync(
                connection,
                transaction,
                currentName.Trim(),
                normalizedName,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteTransactionCategoryAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalizedName = name.Trim();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var transaction = connection.BeginTransaction();
            await EnsureCustomCategoryAsync(
                connection,
                transaction,
                normalizedName,
                cancellationToken);

            await using (var updateTransactions = connection.CreateCommand())
            {
                updateTransactions.Transaction = transaction;
                updateTransactions.CommandText =
                    """
                    UPDATE transactions
                    SET category = $uncategorised
                    WHERE category = $category COLLATE NOCASE;
                    """;
                updateTransactions.Parameters.AddWithValue(
                    "$uncategorised",
                    TransactionCategories.Uncategorised);
                updateTransactions.Parameters.AddWithValue("$category", normalizedName);
                await updateTransactions.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deleteRules = connection.CreateCommand())
            {
                deleteRules.Transaction = transaction;
                deleteRules.CommandText =
                    "DELETE FROM category_rules WHERE category = $category COLLATE NOCASE;";
                deleteRules.Parameters.AddWithValue("$category", normalizedName);
                await deleteRules.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deleteCategory = connection.CreateCommand())
            {
                deleteCategory.Transaction = transaction;
                deleteCategory.CommandText =
                    "DELETE FROM categories WHERE name = $name COLLATE NOCASE;";
                deleteCategory.Parameters.AddWithValue("$name", normalizedName);
                await deleteCategory.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UncategorisedTransaction?> GetRandomUncategorisedTransactionAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            var count = await ReadUncategorisedTransactionCountAsync(
                connection,
                cancellationToken);
            if (count == 0)
            {
                return null;
            }

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT t.id, t.account_id, t.posted_on, t.description, t.category,
                       t.amount, t.currency, t.is_pending, a.name
                FROM transactions t
                INNER JOIN accounts a ON a.id = t.account_id
                WHERE t.category = $category COLLATE NOCASE
                ORDER BY t.posted_on DESC, t.display_order
                LIMIT 1 OFFSET $offset;
                """;
            command.Parameters.AddWithValue(
                "$category",
                TransactionCategories.Uncategorised);
            command.Parameters.AddWithValue("$offset", Random.Shared.Next(count));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "The uncategorised transaction count changed unexpectedly.");
            }

            return new UncategorisedTransaction(
                new BankTransaction(
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
                    reader.GetBoolean(7)),
                reader.GetString(8));
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

    public async Task<IReadOnlyList<TransactionCategoryRule>>
        GetLearnedTransactionCategoryRulesAsync(
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            var rules = await ReadCategoryRulesAsync(
                connection,
                transaction: null,
                cancellationToken);
            return rules
                .Select(rule => new TransactionCategoryRule(
                    rule.Key,
                    rule.Value,
                    TransactionCategoryRuleMatch.ExactDescription))
                .OrderBy(rule => rule.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(rule => rule.Pattern, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TransactionPage> GetTransactionPageAsync(
        string accountId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, 500);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            var totalCount = await ReadTransactionCountAsync(
                connection,
                accountId,
                cancellationToken);
            var pageCount = Math.Max(1, (totalCount + pageSize - 1) / pageSize);
            var actualPageIndex = Math.Min(pageIndex, pageCount - 1);
            var entries = await ReadTransactionPageAsync(
                connection,
                accountId,
                actualPageIndex,
                pageSize,
                cancellationToken);
            return new TransactionPage(entries, totalCount, actualPageIndex, pageSize);
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

    private async Task EnsureSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        if (_schemaInitialized)
        {
            return;
        }

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
                running_balance TEXT NULL,
                FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS ix_transactions_account_date
            ON transactions (account_id, posted_on DESC, display_order);

            CREATE INDEX IF NOT EXISTS ix_transactions_category
            ON transactions (category);

            CREATE TABLE IF NOT EXISTS category_rules (
                merchant_key TEXT NOT NULL PRIMARY KEY,
                category TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS categories (
                name TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                is_system INTEGER NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        var schemaVersion = await ReadSchemaVersionAsync(connection, cancellationToken);
        if (schemaVersion < 3)
        {
            await ExecuteAsync(
                connection,
                transaction: null,
                """
                UPDATE transactions
                SET category = 'Uncategorised'
                WHERE category COLLATE NOCASE = 'Other';

                DELETE FROM category_rules
                WHERE category COLLATE NOCASE = 'Other';
                """,
                cancellationToken);
        }

        if (!await ColumnExistsAsync(
                connection,
                "transactions",
                "running_balance",
                cancellationToken))
        {
            await ExecuteAsync(
                connection,
                transaction: null,
                "ALTER TABLE transactions ADD COLUMN running_balance TEXT NULL;",
                cancellationToken);
            var accounts = await ReadAccountsAsync(connection, cancellationToken);
            foreach (var account in accounts)
            {
                await RebuildRunningBalancesAsync(
                    connection,
                    transaction: null,
                    account.Id,
                    cancellationToken);
            }
        }

        if (schemaVersion < 4)
        {
            await ExecuteAsync(
                connection,
                transaction: null,
                "PRAGMA user_version = 4;",
                cancellationToken);
        }

        if (schemaVersion < 5)
        {
            await SeedCategoriesAsync(connection, cancellationToken);
            await ExecuteAsync(
                connection,
                transaction: null,
                "PRAGMA user_version = 5;",
                cancellationToken);
        }

        _schemaInitialized = true;
    }

    private static async Task SeedCategoriesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var category in TransactionCategories.All)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO categories (name, is_system)
                VALUES ($name, 1)
                ON CONFLICT(name) DO UPDATE SET is_system = 1;
                """;
            command.Parameters.AddWithValue("$name", category);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ExecuteAsync(
            connection,
            transaction: null,
            """
            INSERT OR IGNORE INTO categories (name, is_system)
            SELECT DISTINCT category, 0
            FROM transactions
            WHERE TRIM(category) <> '';

            INSERT OR IGNORE INTO categories (name, is_system)
            SELECT DISTINCT category, 0
            FROM category_rules
            WHERE TRIM(category) <> '';
            """,
            cancellationToken);
    }

    private static string ValidateCategoryName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalizedName = name.Trim();
        if (normalizedName.Equals("Other", StringComparison.OrdinalIgnoreCase)
            || normalizedName.Equals("New...", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"'{normalizedName}' cannot be used as a category name.");
        }

        return normalizedName;
    }

    private static string NormalizeStoredCategory(string category) =>
        string.IsNullOrWhiteSpace(category)
            || category.Equals("Other", StringComparison.OrdinalIgnoreCase)
                ? TransactionCategories.Uncategorised
                : category.Trim();

    private static async Task EnsureCategoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        CancellationToken cancellationToken)
    {
        var normalizedName = ValidateCategoryName(name);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT OR IGNORE INTO categories (name, is_system) VALUES ($name, 0);";
        command.Parameters.AddWithValue("$name", normalizedName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureCustomCategoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT is_system FROM categories WHERE name = $name COLLATE NOCASE;";
        command.Parameters.AddWithValue("$name", name);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null)
        {
            throw new InvalidOperationException($"Category '{name}' does not exist.");
        }

        if (Convert.ToBoolean(value, CultureInfo.InvariantCulture))
        {
            throw new InvalidOperationException(
                $"Built-in category '{name}' cannot be modified.");
        }
    }

    private static async Task<bool> CategoryExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string name,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT EXISTS(SELECT 1 FROM categories WHERE name = $name COLLATE NOCASE);";
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToBoolean(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteCategoryRenameAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string currentName,
        string newName,
        CancellationToken cancellationToken)
    {
        foreach (var commandText in new[]
                 {
                     "UPDATE transactions SET category = $new_name "
                         + "WHERE category = $current_name COLLATE NOCASE;",
                     "UPDATE category_rules SET category = $new_name "
                         + "WHERE category = $current_name COLLATE NOCASE;",
                     "UPDATE categories SET name = $new_name "
                         + "WHERE name = $current_name COLLATE NOCASE;"
                 })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            command.Parameters.AddWithValue("$current_name", currentName);
            command.Parameters.AddWithValue("$new_name", newName);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<int> ReadUncategorisedTransactionCountAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM transactions WHERE category = $category COLLATE NOCASE;";
        command.Parameters.AddWithValue(
            "$category",
            TransactionCategories.Uncategorised);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetString(1).Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<int> ReadTransactionCountAsync(
        SqliteConnection connection,
        string accountId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM transactions WHERE account_id = $account_id;";
        command.Parameters.AddWithValue("$account_id", accountId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<TransactionLedgerEntry>> ReadTransactionPageAsync(
        SqliteConnection connection,
        string accountId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var entries = new List<TransactionLedgerEntry>(pageSize);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, account_id, posted_on, description, category, amount,
                   currency, is_pending, running_balance
            FROM transactions
            WHERE account_id = $account_id
            ORDER BY posted_on DESC, display_order
            LIMIT $page_size OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$account_id", accountId);
        command.Parameters.AddWithValue("$page_size", pageSize);
        command.Parameters.AddWithValue("$offset", pageIndex * pageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new TransactionLedgerEntry(
                new BankTransaction(
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
                    reader.GetBoolean(7)),
                reader.IsDBNull(8) ? null : ParseDecimal(reader.GetString(8))));
        }

        return entries;
    }

    private static async Task RebuildRunningBalancesAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string accountId,
        CancellationToken cancellationToken)
    {
        decimal? runningBalance;
        await using (var accountCommand = connection.CreateCommand())
        {
            accountCommand.Transaction = transaction;
            accountCommand.CommandText = "SELECT balance FROM accounts WHERE id = $id;";
            accountCommand.Parameters.AddWithValue("$id", accountId);
            var balance = await accountCommand.ExecuteScalarAsync(cancellationToken);
            if (balance is null)
            {
                throw new InvalidOperationException($"Account '{accountId}' does not exist.");
            }

            runningBalance = balance == DBNull.Value
                ? null
                : ParseDecimal((string)balance);
        }

        var transactions = new List<(string Id, decimal Amount)>();
        await using (var transactionCommand = connection.CreateCommand())
        {
            transactionCommand.Transaction = transaction;
            transactionCommand.CommandText =
                """
                SELECT id, amount
                FROM transactions
                WHERE account_id = $account_id
                ORDER BY posted_on DESC, display_order;
                """;
            transactionCommand.Parameters.AddWithValue("$account_id", accountId);
            await using var reader =
                await transactionCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                transactions.Add((reader.GetString(0), ParseDecimal(reader.GetString(1))));
            }
        }

        foreach (var bankTransaction in transactions)
        {
            await using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText =
                "UPDATE transactions SET running_balance = $balance WHERE id = $id;";
            updateCommand.Parameters.AddWithValue(
                "$balance",
                runningBalance is null
                    ? DBNull.Value
                    : FormatDecimal(runningBalance.Value));
            updateCommand.Parameters.AddWithValue("$id", bankTransaction.Id);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            runningBalance -= bankTransaction.Amount;
        }
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadCategoryRulesAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
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
        SqliteTransaction? transaction,
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
