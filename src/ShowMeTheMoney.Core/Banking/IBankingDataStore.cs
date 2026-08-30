namespace ShowMeTheMoney.Core.Banking;

public interface IBankingDataStore : IBankingDataSource
{
    Task AddAccountAsync(
        BankAccount account,
        CancellationToken cancellationToken = default);

    Task RenameAccountAsync(
        string accountId,
        string name,
        CancellationToken cancellationToken = default);

    Task ImportTransactionsAsync(
        string accountId,
        IReadOnlyList<BankTransaction> transactions,
        string dataSourceDescription,
        CancellationToken cancellationToken = default);

    Task ReplaceSnapshotAsync(
        BankingSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
