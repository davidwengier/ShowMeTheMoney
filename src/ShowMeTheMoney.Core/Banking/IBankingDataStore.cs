namespace ShowMeTheMoney.Core.Banking;

public interface IBankingDataStore : IBankingDataSource
{
    Task ReplaceSnapshotAsync(
        BankingSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
