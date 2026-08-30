using Microsoft.Data.Sqlite;
using ShowMeTheMoney.Core.Banking;
using ShowMeTheMoney.Storage.Sqlite;
using Xunit;

namespace ShowMeTheMoney.Storage.Sqlite.Tests;

public sealed class SqliteBankingDataStoreTests
{
    [Fact]
    public async Task GetSnapshotAsync_NewDatabaseReturnsEmptySnapshot()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var store = new SqliteBankingDataStore(databasePath);

            var snapshot = await store.GetSnapshotAsync(TestContext.Current.CancellationToken);

            Assert.Empty(snapshot.Accounts);
            Assert.Empty(snapshot.Transactions);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task ReplaceSnapshotAsync_PersistsCompleteSnapshot()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var snapshot = new BankingSnapshot(
                "Imported bank account",
                "Imported from transactions.qif",
                [
                    new BankAccount("account-1", "Everyday", "Imported QIF", null, "AUD"),
                    new BankAccount("account-2", "Savings", "1234", 1234.56m, "AUD")
                ],
                [
                    new BankTransaction(
                        "transaction-1",
                        "account-1",
                        new DateOnly(2026, 8, 30),
                        "Groceries",
                        "Food",
                        -84.62m,
                        "AUD",
                        false),
                    new BankTransaction(
                        "transaction-2",
                        "account-2",
                        new DateOnly(2026, 8, 29),
                        "Interest",
                        "Income",
                        12.3456m,
                        "AUD",
                        true)
                ]);
            var store = new SqliteBankingDataStore(databasePath);
            await store.ReplaceSnapshotAsync(
                snapshot,
                TestContext.Current.CancellationToken);

            var reopenedStore = new SqliteBankingDataStore(databasePath);
            var loaded = await reopenedStore.GetSnapshotAsync(
                TestContext.Current.CancellationToken);

            Assert.Equal(snapshot.InstitutionName, loaded.InstitutionName);
            Assert.Equal(snapshot.DataSourceDescription, loaded.DataSourceDescription);
            Assert.Equal(snapshot.Accounts, loaded.Accounts);
            Assert.Equal(snapshot.Transactions, loaded.Transactions);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task AccountOperations_AddRenameAndImportIntoSelectedAccount()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var store = new SqliteBankingDataStore(databasePath);
            var everyday = new BankAccount(
                "everyday",
                "Everyday",
                "Manual account",
                null,
                "AUD");
            var savings = new BankAccount(
                "savings",
                "Savings",
                "Manual account",
                null,
                "AUD");
            await store.AddAccountAsync(everyday, cancellationToken);
            await store.AddAccountAsync(savings, cancellationToken);
            await store.UpdateAccountAsync(
                "everyday",
                "Daily spending",
                987.65m,
                cancellationToken);
            var importedTransaction = new BankTransaction(
                "everyday:transaction-1",
                "everyday",
                new DateOnly(2026, 8, 30),
                "Groceries",
                "Food",
                -84.62m,
                "AUD",
                false);
            await store.ImportTransactionsAsync(
                "everyday",
                [importedTransaction],
                "Last imported from everyday.qif",
                cancellationToken);
            await store.ImportTransactionsAsync(
                "everyday",
                [importedTransaction with { Category = "Household" }],
                "Last imported from everyday.qif",
                cancellationToken);

            var loaded = await store.GetSnapshotAsync(cancellationToken);

            Assert.Equal(2, loaded.Accounts.Count);
            Assert.Equal("Daily spending", loaded.Accounts[0].Name);
            Assert.Equal(987.65m, loaded.Accounts[0].Balance);
            var transaction = Assert.Single(loaded.Transactions);
            Assert.Equal("everyday", transaction.AccountId);
            Assert.Equal("Household", transaction.Category);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task CategoryRules_CategorizeImportsAndLearnFromCorrections()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var store = new SqliteBankingDataStore(databasePath);
            await store.AddAccountAsync(
                new BankAccount("everyday", "Everyday", "Manual account", null, "AUD"),
                cancellationToken);
            var woolworths = CreateTransaction(
                "woolworths",
                "Woolworths Metro",
                TransactionCategories.Uncategorised);
            var coffeeClub = CreateTransaction(
                "coffee-1",
                "Coffee-Club",
                TransactionCategories.Uncategorised);
            var matchingCoffeeClub = CreateTransaction(
                "coffee-2",
                "coffee club",
                TransactionCategories.Uncategorised);

            await store.ImportTransactionsAsync(
                "everyday",
                [woolworths, coffeeClub, matchingCoffeeClub],
                "Imported transactions",
                cancellationToken);
            await store.SetTransactionCategoryAsync(
                coffeeClub.Id,
                "Coffee",
                cancellationToken);
            await store.ImportTransactionsAsync(
                "everyday",
                [matchingCoffeeClub with { Category = TransactionCategories.Uncategorised }],
                "Reimported transactions",
                cancellationToken);

            var loaded = await store.GetSnapshotAsync(cancellationToken);
            var learnedRules = await store.GetLearnedTransactionCategoryRulesAsync(
                cancellationToken);

            Assert.Equal(
                "Groceries",
                loaded.Transactions.Single(transaction => transaction.Id == woolworths.Id).Category);
            Assert.All(
                loaded.Transactions.Where(transaction => transaction.Description.Contains(
                    "coffee",
                    StringComparison.OrdinalIgnoreCase)),
                transaction => Assert.Equal("Coffee", transaction.Category));
            var learnedRule = Assert.Single(learnedRules);
            Assert.Equal("COFFEE CLUB", learnedRule.Pattern);
            Assert.Equal("Coffee", learnedRule.Category);
            Assert.Equal(TransactionCategoryRuleMatch.ExactDescription, learnedRule.Match);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task ApplyTransactionCategoryRulesAsync_CategorizesExistingTransactions()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var account = new BankAccount(
                "everyday",
                "Everyday",
                "Manual account",
                null,
                "AUD");
            var transaction = CreateTransaction(
                "netflix",
                "Netflix.com",
                TransactionCategories.Uncategorised);
            var store = new SqliteBankingDataStore(databasePath);
            await store.ReplaceSnapshotAsync(
                new BankingSnapshot(
                    "Bank",
                    "Existing data",
                    [account],
                    [transaction]),
                cancellationToken);

            var updatedCount = await store.ApplyTransactionCategoryRulesAsync(
                account.Id,
                cancellationToken);
            var loaded = await store.GetSnapshotAsync(cancellationToken);

            Assert.Equal(1, updatedCount);
            Assert.Equal("Entertainment", Assert.Single(loaded.Transactions).Category);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task TransactionPages_ReturnRequestedRowsAndCachedRunningBalances()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var store = new SqliteBankingDataStore(databasePath);
            await store.AddAccountAsync(
                new BankAccount("everyday", "Everyday", "Manual account", 1000m, "AUD"),
                cancellationToken);
            var transactions = Enumerable.Range(0, 125)
                .Select(index => new BankTransaction(
                    $"transaction-{index}",
                    "everyday",
                    new DateOnly(2026, 1, 1).AddDays(index),
                    $"Transaction {index}",
                    "Test",
                    -1m,
                    "AUD",
                    false))
                .ToArray();
            await store.ImportTransactionsAsync(
                "everyday",
                transactions,
                "Imported transactions",
                cancellationToken);

            var firstPage = await store.GetTransactionPageAsync(
                "everyday",
                0,
                50,
                cancellationToken);
            var secondPage = await store.GetTransactionPageAsync(
                "everyday",
                1,
                50,
                cancellationToken);
            var lastPage = await store.GetTransactionPageAsync(
                "everyday",
                99,
                50,
                cancellationToken);

            Assert.Equal(125, firstPage.TotalCount);
            Assert.Equal(50, firstPage.Entries.Count);
            Assert.Equal("transaction-124", firstPage.Entries[0].Transaction.Id);
            Assert.Equal(1000m, firstPage.Entries[0].RunningBalance);
            Assert.Equal("transaction-75", firstPage.Entries[^1].Transaction.Id);
            Assert.Equal(1049m, firstPage.Entries[^1].RunningBalance);
            Assert.Equal("transaction-74", secondPage.Entries[0].Transaction.Id);
            Assert.Equal(1050m, secondPage.Entries[0].RunningBalance);
            Assert.Equal(2, lastPage.PageIndex);
            Assert.Equal(25, lastPage.Entries.Count);
            Assert.Equal("transaction-24", lastPage.Entries[0].Transaction.Id);
            Assert.Equal(1100m, lastPage.Entries[0].RunningBalance);

            await store.UpdateAccountAsync(
                "everyday",
                "Everyday",
                2000m,
                cancellationToken);
            var updatedSecondPage = await store.GetTransactionPageAsync(
                "everyday",
                1,
                50,
                cancellationToken);

            Assert.Equal(2050m, updatedSecondPage.Entries[0].RunningBalance);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task TransactionPages_UpgradeAndPopulateExistingBalanceCache()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            await using (var connection = new SqliteConnection(
                $"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE accounts (
                        id TEXT NOT NULL PRIMARY KEY,
                        name TEXT NOT NULL,
                        masked_number TEXT NOT NULL,
                        balance TEXT NULL,
                        currency TEXT NOT NULL,
                        display_order INTEGER NOT NULL
                    );

                    CREATE TABLE transactions (
                        id TEXT NOT NULL PRIMARY KEY,
                        account_id TEXT NOT NULL,
                        posted_on TEXT NOT NULL,
                        description TEXT NOT NULL,
                        category TEXT NOT NULL,
                        amount TEXT NOT NULL,
                        currency TEXT NOT NULL,
                        is_pending INTEGER NOT NULL,
                        display_order INTEGER NOT NULL
                    );

                    INSERT INTO accounts
                        (id, name, masked_number, balance, currency, display_order)
                    VALUES
                        ('everyday', 'Everyday', 'Manual account', '100', 'AUD', 0);

                    INSERT INTO transactions
                        (id, account_id, posted_on, description, category, amount,
                         currency, is_pending, display_order)
                    VALUES
                        ('transaction-1', 'everyday', '2026-08-30', 'Purchase',
                         'Test', '-10', 'AUD', 0, 0);
                    """;
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }

            var store = new SqliteBankingDataStore(databasePath);
            var page = await store.GetTransactionPageAsync(
                "everyday",
                0,
                50,
                TestContext.Current.CancellationToken);
            var categories = await store.GetTransactionCategoriesAsync(
                TestContext.Current.CancellationToken);

            Assert.Equal(100m, Assert.Single(page.Entries).RunningBalance);
            Assert.Contains(
                categories,
                category => category.Name == "Test" && !category.IsBuiltIn);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public async Task CategoryManagement_UpdatesTransactionsRulesAndReviewQueue()
    {
        var databasePath = CreateDatabasePath();
        try
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var store = new SqliteBankingDataStore(databasePath);
            await store.AddAccountAsync(
                new BankAccount("everyday", "Everyday", "Manual account", null, "AUD"),
                cancellationToken);
            var coffee = CreateTransaction(
                "coffee",
                "Coffee Club",
                TransactionCategories.Uncategorised);
            var unknown = CreateTransaction(
                "unknown",
                "Unknown Merchant",
                TransactionCategories.Uncategorised);
            await store.ImportTransactionsAsync(
                "everyday",
                [coffee, unknown],
                "Imported transactions",
                cancellationToken);

            await store.AddTransactionCategoryAsync("Coffee", cancellationToken);
            await store.SetTransactionCategoryAsync(
                coffee.Id,
                "Coffee",
                cancellationToken);
            await store.RenameTransactionCategoryAsync(
                "Coffee",
                "Cafes",
                cancellationToken);

            var renamedSnapshot = await store.GetSnapshotAsync(cancellationToken);
            var renamedRules = await store.GetLearnedTransactionCategoryRulesAsync(
                cancellationToken);
            var categories = await store.GetTransactionCategoriesAsync(cancellationToken);
            var reviewTransaction = await store.GetRandomUncategorisedTransactionAsync(
                cancellationToken);

            Assert.Equal(
                "Cafes",
                renamedSnapshot.Transactions.Single(
                    transaction => transaction.Id == coffee.Id).Category);
            Assert.Equal("Cafes", Assert.Single(renamedRules).Category);
            Assert.Contains(
                categories,
                category => category.Name == "Cafes" && !category.IsBuiltIn);
            Assert.Equal(unknown.Id, reviewTransaction?.Transaction.Id);
            Assert.Equal("Everyday", reviewTransaction?.AccountName);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.RenameTransactionCategoryAsync(
                    "Groceries",
                    "Food",
                    cancellationToken));

            await store.DeleteTransactionCategoryAsync("Cafes", cancellationToken);

            var deletedSnapshot = await store.GetSnapshotAsync(cancellationToken);
            var deletedRules = await store.GetLearnedTransactionCategoryRulesAsync(
                cancellationToken);
            var deletedCategories = await store.GetTransactionCategoriesAsync(
                cancellationToken);

            Assert.Equal(
                TransactionCategories.Uncategorised,
                deletedSnapshot.Transactions.Single(
                    transaction => transaction.Id == coffee.Id).Category);
            Assert.Empty(deletedRules);
            Assert.DoesNotContain(
                deletedCategories,
                category => category.Name == "Cafes");
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    private static BankTransaction CreateTransaction(
        string id,
        string description,
        string category) =>
        new(
            id,
            "everyday",
            new DateOnly(2026, 8, 30),
            description,
            category,
            -10m,
            "AUD",
            false);

    private static string CreateDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"show-me-the-money-{Guid.NewGuid():N}.db");

    private static void DeleteDatabase(string databasePath)
    {
        foreach (var path in new[]
                 {
                     databasePath,
                     $"{databasePath}-shm",
                     $"{databasePath}-wal"
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
