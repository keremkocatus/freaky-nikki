using FreakyNikki.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace FreakyNikki.Update;

/// <summary>
/// Checks GitHub Releases for a newer build, downloads it in the background, and
/// can apply it with a restart. No-ops when the app isn't running as an installed
/// Velopack build (e.g. `dotnet run` during development).
/// </summary>
public sealed class UpdateService
{
    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    public UpdateService(string repoUrl)
    {
        _manager = new UpdateManager(new GithubSource(repoUrl, accessToken: null, prerelease: false));
    }

    /// <summary>Raised (on a background thread) when an update is downloaded and ready; carries the version.</summary>
    public event Action<string>? UpdateReady;

    /// <summary>Look for, download and stage an update. Safe to fire-and-forget.</summary>
    public async Task CheckAsync()
    {
        if (!_manager.IsInstalled)
        {
            return; // not an installed build — nothing to update
        }

        try
        {
            UpdateInfo? info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                return; // already up to date
            }

            await _manager.DownloadUpdatesAsync(info);
            _pending = info;
            UpdateReady?.Invoke(info.TargetFullRelease.Version.ToString());
        }
        catch (Exception ex)
        {
            Log.Error("Update check failed", ex);
        }
    }

    /// <summary>Apply the staged update and relaunch the app.</summary>
    public void ApplyAndRestart()
    {
        if (_pending is not null)
        {
            _manager.ApplyUpdatesAndRestart(_pending);
        }
    }
}
