using ShowMeTheMoney.UI.Services;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace ShowMeTheMoney.Desktop;

internal sealed class VelopackApplicationUpdateService : IApplicationUpdateService
{
    private const string RepositoryUrl = "https://github.com/davidwengier/ShowMeTheMoney";

    private readonly UpdateManager _updateManager = new(
        new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

    public bool CanUpdate => _updateManager.IsInstalled;

    public async Task<ApplicationUpdate?> CheckForUpdateAsync()
    {
        if (!CanUpdate)
        {
            return null;
        }

        try
        {
            var update = await _updateManager.CheckForUpdatesAsync();
            return update is null
                ? null
                : new ApplicationUpdate(update.TargetFullRelease.Version.ToString());
        }
        catch (NotInstalledException exception)
        {
            throw new ApplicationUpdateException(
                "Updates are only available for an installed copy of Show Me The Money.",
                exception);
        }
    }
}
