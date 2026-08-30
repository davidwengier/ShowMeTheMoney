namespace ShowMeTheMoney.Core.Banking;

public interface IBankingDataSource
{
    Task<BankingOverview> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<BankingSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
