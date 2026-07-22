using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace Kiriha.Services;

public class UpdateService
{
    private readonly UpdateManager _updateManager;
    private UpdateInfo? _updateInfo;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private int _isChecking;    // 0/1 via Interlocked
    private int _isDownloading; // 0/1 via Interlocked

    public bool IsChecking => Volatile.Read(ref _isChecking) == 1;
    public bool IsDownloading => Volatile.Read(ref _isDownloading) == 1;

    public UpdateService()
    {
        // GitHubSource is used to check for updates from a GitHub repository
        _updateManager = new UpdateManager(new GithubSource(Constants.Links.GitHubRepo, null, false));
        Log.Information("UpdateService initialized. Installed: {IsInstalled}, Current Version: {Version}",
            _updateManager.IsInstalled, _updateManager.CurrentVersion);
    }

    public bool IsUpdateAvailable => _updateInfo != null;
    public string? NewVersion => _updateInfo?.TargetFullRelease?.Version?.ToString();

    public async Task<bool> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _isChecking, 1, 0) != 0) return false;

        await _syncLock.WaitAsync(ct);
        try
        {
            Log.Information("UpdateService: Requesting update check...");

            if (!_updateManager.IsInstalled)
            {
                Log.Information("UpdateService: Update check skipped. Application is not installed via Velopack (running in Dev, Debug or Portable mode). Current Version: {Version}", _updateManager.CurrentVersion);
                return false;
            }

            Log.Information("UpdateService: Checking GitHub ({RepoUrl}) for updates. Current version: {CurrentVersion}...",
                Constants.Links.GitHubRepo, _updateManager.CurrentVersion);

            _updateInfo = await _updateManager.CheckForUpdatesAsync().WaitAsync(ct);

            if (_updateInfo != null)
            {
                Log.Information("UpdateService: Check complete. NEW UPDATE FOUND: {Version}. Remote version is newer than {CurrentVersion}.",
                    NewVersion, _updateManager.CurrentVersion);
                return true;
            }

            Log.Information("UpdateService: Check complete. No updates found. You are already on the latest version ({Version}).", _updateManager.CurrentVersion);
            return false;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("UpdateService: Update check was cancelled or timed out.");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "UpdateService: Error during update check from {RepoUrl}.", Constants.Links.GitHubRepo);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
            _syncLock.Release();
        }
    }

    public async Task<bool> DownloadAndInstallAsync(Action<int>? progressCallback = null, CancellationToken ct = default)
    {
        if (_updateInfo == null)
        {
            Log.Warning("DownloadAndInstallAsync called but no update info is available.");
            return false;
        }
        if (Interlocked.CompareExchange(ref _isDownloading, 1, 0) != 0)
        {
            Log.Warning("DownloadAndInstallAsync called but a download is already in progress.");
            return false;
        }

        await _syncLock.WaitAsync(ct);
        try
        {
            Log.Information("Starting download of update {Version}...", NewVersion);
            await _updateManager.DownloadUpdatesAsync(_updateInfo, progressCallback, ct);

            Log.Information("Update {Version} downloaded successfully and is ready to be applied.", NewVersion);
            return true;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("UpdateService: Update download was cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download update {Version}", NewVersion);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _isDownloading, 0);
            _syncLock.Release();
        }
    }

    public void RestartAndApply()
    {
        if (_updateInfo == null) return;

        Log.Information("Restarting application to apply updates...");
        _updateManager.ApplyUpdatesAndRestart(_updateInfo);
    }
}
