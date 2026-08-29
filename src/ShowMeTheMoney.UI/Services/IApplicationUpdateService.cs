namespace ShowMeTheMoney.UI.Services;

public interface IApplicationUpdateService
{
    bool CanUpdate { get; }

    Task<ApplicationUpdate?> CheckForUpdateAsync();
}
