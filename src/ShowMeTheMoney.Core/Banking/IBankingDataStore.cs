namespace ShowMeTheMoney.Core.Banking;

public interface IBankingDataStore : IBankingDataSource
{
    Task AddAccountAsync(
        BankAccount account,
        CancellationToken cancellationToken = default);

    Task UpdateAccountAsync(
        string accountId,
        string name,
        decimal? balance,
        CancellationToken cancellationToken = default);

    Task SetTransactionCategoryAsync(
        string transactionId,
        string category,
        CancellationToken cancellationToken = default);

    Task<int> ApplyTransactionCategoryRulesAsync(
        string accountId,
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
