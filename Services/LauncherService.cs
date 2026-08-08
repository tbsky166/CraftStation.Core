using System.Diagnostics;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.VersionMetadata;
using CraftStation.Core.Models;
using CraftStation.Core.Utils;

namespace CraftStation.Core.Services;

public sealed class LauncherService : ILauncherService
{
    private readonly ISettingsService _settings;
    private readonly IDownloadMirror _mirror;
    private readonly ILogService _logs;
    private MinecraftLauncher? _launcher;
    private Process? _runningProcess;
    private readonly object _processLock = new();
    private readonly SemaphoreSlim _versionLock = new(1, 1);

    public LauncherService(ISettingsService settings, IDownloadMirror mirror, ILogService logs)
    {
        _settings = settings;
        _mirror = mirror;
        _logs = logs;
    }

    public Process? RunningProcess
    {
        get
        {
            lock (_processLock)
            {
                if (_runningProcess == null)
                    return null;
                try
                {
                    if (_runningProcess.HasExited)
                        _runningProcess = null;
                }
                catch
                {
                    _runningProcess = null;
                }
                return _runningProcess;
            }
        }
    }

    public MinecraftLauncher Launcher => GetLauncher();

    private MinecraftLauncher GetLauncher()
    {
        if (_launcher == null)
        {
            var path = new MinecraftPath(_settings.ResolveGameDirectory());
            var httpClient = _mirror.CreateHttpClient();
            var parameters = MinecraftLauncherParameters.CreateDefault(path, httpClient);
            _launcher = new MinecraftLauncher(parameters);
        }
        return _launcher;
    }

    public async Task<IReadOnlyList<VersionInfo>> GetVersionsAsync(bool refresh = false, CancellationToken ct = default)
    {
        var launcher = GetLauncher();
        if (refresh || launcher.Versions == null)
        {
            await _versionLock.WaitAsync(ct);
            try
            {
                if (refresh || launcher.Versions == null)
                    await launcher.GetAllVersionsAsync(ct);
            }
            finally
            {
                _versionLock.Release();
            }
        }

        var result = new List<VersionInfo>();
        foreach (var metadata in launcher.Versions!)
        {
            result.Add(new VersionInfo
            {
                Name = metadata.Name,
                Type = metadata.Type ?? "unknown",
                Category = VersionCategoryUtil.GetCategory(metadata.Name, metadata.Type ?? "unknown"),
                ReleaseTimeUtc = metadata.ReleaseTime.UtcDateTime,
                IsInstalled = Directory.Exists(Path.Combine(
                    _settings.ResolveGameDirectory(), Config.MinecraftVersionsDirectoryName, metadata.Name))
            });
        }
        return result;
    }

    public async Task InstallAsync(string versionId, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var launcher = GetLauncher();
        var fileProgress = progress == null
            ? null
            : new Progress<InstallerProgressChangedEventArgs>(e => progress.Report(new DownloadProgress
            {
                CurrentFile = e.Name,
                CompletedFiles = e.ProgressedTasks,
                TotalFiles = e.TotalTasks
            }));
        var byteProgress = progress == null
            ? null
            : new Progress<ByteProgress>(e => progress.Report(new DownloadProgress
            {
                CompletedBytes = e.ProgressedBytes,
                TotalBytes = e.TotalBytes
            }));
        await launcher.InstallAsync(versionId, fileProgress, byteProgress, ct);
    }

    public async Task RepairAsync(string versionId, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var versionDir = Path.Combine(
            _settings.ResolveGameDirectory(), Config.MinecraftVersionsDirectoryName, versionId);
        if (Directory.Exists(versionDir))
            Directory.Delete(versionDir, true);
        await InstallAsync(versionId, progress, ct);
    }

    public Task DeleteVersionAsync(string versionId)
    {
        var versionDir = Path.Combine(
            _settings.ResolveGameDirectory(), Config.MinecraftVersionsDirectoryName, versionId);
        if (Directory.Exists(versionDir))
            Directory.Delete(versionDir, true);
        return Task.CompletedTask;
    }

    public async Task<string?> GetJavaPathForVersionAsync(string versionId, CancellationToken ct = default)
    {
        var launcher = GetLauncher();
        var version = await launcher.GetVersionAsync(versionId, ct);
        return launcher.GetJavaPath(version) ?? launcher.GetDefaultJavaPath();
    }

    public async Task<Process> LaunchAsync(
        Instance instance,
        AccountEntry account,
        ServerEntry? server,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        var launcher = GetLauncher();
        var versionName = instance.ResolvedVersionName;
        await launcher.InstallAsync(versionName, null, null, ct);
        var version = await launcher.GetVersionAsync(versionName, ct);

        var option = new MLaunchOption
        {
            Session = account.Kind == AccountKind.Offline
                ? MSession.CreateOfflineSession(account.UserName)
                : new MSession
                {
                    Username = account.UserName,
                    UUID = account.Uuid,
                    AccessToken = account.AccessToken,
                    UserType = "msa"
                },
            MinimumRamMb = instance.MinMemoryMb,
            MaximumRamMb = instance.MaxMemoryMb,
            JavaPath = ResolveJavaPath(launcher, version, instance),
            GameLauncherName = Config.AppName,
            GameLauncherVersion = Config.AppVersion,
            ClientId = _settings.Settings.MicrosoftClientId
        };

        if (!string.IsNullOrWhiteSpace(instance.JvmArgs))
            option.JvmArgumentOverrides = new[] { MArgument.FromCommandLine(instance.JvmArgs) };
        if (!string.IsNullOrWhiteSpace(instance.GameArgs))
            option.ExtraGameArguments = new[] { MArgument.FromCommandLine(instance.GameArgs) };
        if (instance.WindowWidth > 0)
            option.ScreenWidth = instance.WindowWidth;
        if (instance.WindowHeight > 0)
            option.ScreenHeight = instance.WindowHeight;
        option.FullScreen = instance.Fullscreen;

        if (server != null)
        {
            option.ServerIp = server.Address;
            option.ServerPort = server.Port;
        }

        if (instance.VersionIsolation)
        {
            var isolated = Path.Combine(
                _settings.ResolveGameDirectory(), Config.MinecraftVersionsDirectoryName, instance.VersionId);
            Directory.CreateDirectory(isolated);
            option.Path = new MinecraftPath(isolated);
        }

        var process = launcher.BuildProcess(version, option);
        await _logs.AppendAsync(instance, $"[{Config.AppName}] 启动 {versionName}，账户 {account.DisplayName}");
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardInput = true;
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
                return;
            _ = _logs.AppendAsync(instance, e.Data);
            log?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
                return;
            _ = _logs.AppendAsync(instance, e.Data);
            log?.Report(e.Data);
        };
        process.Exited += (_, _) =>
        {
            _ = _logs.AppendAsync(instance, $"[{Config.AppName}] 游戏退出，代码 {process.ExitCode}");
            lock (_processLock) _runningProcess = null;
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        lock (_processLock)
            _runningProcess = process;
        return process;
    }

    public async Task StopAsync()
    {
        var process = RunningProcess;
        if (process == null)
            return;
        try
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        catch
        {
            // already exited
        }
        finally
        {
            lock (_processLock)
                _runningProcess = null;
        }
    }

    public void ResetLauncher()
    {
        lock (_processLock)
            _launcher = null;
    }

    private static string ResolveJavaPath(MinecraftLauncher launcher, CmlLib.Core.Version.IVersion version, Instance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.JavaPath) && File.Exists(instance.JavaPath))
            return instance.JavaPath;
        return launcher.GetJavaPath(version) ?? launcher.GetDefaultJavaPath()
            ?? throw new InvalidOperationException("未找到可用的 Java，请先在设置中指定 Java 路径。");
    }
}
