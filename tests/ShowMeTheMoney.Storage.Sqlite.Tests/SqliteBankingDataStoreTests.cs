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
            await store.RenameAccountAsync("everyday", "Daily spending", cancellationToken);
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
            var transaction = Assert.Single(loaded.Transactions);
            Assert.Equal("everyday", transaction.AccountId);
            Assert.Equal("Household", transaction.Category);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

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
