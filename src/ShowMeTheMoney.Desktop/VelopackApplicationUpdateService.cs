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
    private UpdateInfo? _pendingUpdate;

    public bool CanUpdate => _updateManager.IsInstalled;

    public async Task<ApplicationUpdate?> CheckForUpdateAsync()
    {
        if (!CanUpdate)
        {
            return null;
        }

        try
        {
            _pendingUpdate = await _updateManager.CheckForUpdatesAsync();
            return _pendingUpdate is null
                ? null
                : new ApplicationUpdate(_pendingUpdate.TargetFullRelease.Version.ToString());
        }
        catch (NotInstalledException exception)
        {
            throw new ApplicationUpdateException(
                "Updates are only available for an installed copy of Show Me The Money.",
                exception);
        }
    }

    public async Task DownloadUpdateAsync(CancellationToken cancellationToken = default)
    {
        var update = _pendingUpdate
            ?? throw new InvalidOperationException("No application update is available to download.");

        try
        {
            await _updateManager.DownloadUpdatesAsync(
                update,
                cancelToken: cancellationToken);
        }
        catch (AcquireLockFailedException exception)
        {
            throw new ApplicationUpdateException(
                "Another application update is already running.",
                exception);
        }
        catch (ChecksumFailedException exception)
        {
            throw new ApplicationUpdateException(
                "The downloaded update failed its integrity check.",
                exception);
        }
        catch (NotInstalledException exception)
        {
            throw new ApplicationUpdateException(
                "Updates are only available for an installed copy of Show Me The Money.",
                exception);
        }
    }

    public void ApplyUpdateAndRestart()
    {
        var update = _pendingUpdate
            ?? throw new InvalidOperationException("No application update is ready to install.");

        try
        {
            _updateManager.WaitExitThenApplyUpdates(update.TargetFullRelease);
            Application.Exit();
        }
        catch (NotInstalledException exception)
        {
            throw new ApplicationUpdateException(
                "Updates are only available for an installed copy of Show Me The Money.",
                exception);
        }
    }
}
