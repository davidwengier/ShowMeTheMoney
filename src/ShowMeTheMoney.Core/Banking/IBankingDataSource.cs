namespace ShowMeTheMoney.Core.Banking;

public interface IBankingDataSource
{
    Task<BankingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
