using CraftStation.Core.Models;
using CmlLib.Core;

namespace CraftStation.Core.Services;

public interface ILauncherService
{
    MinecraftLauncher Launcher { get; }
    bool IsVersionListLoaded { get; }
    Task<IReadOnlyList<VersionInfo>> GetVersionsAsync(bool refresh = false, CancellationToken ct = default);
    Task InstallAsync(string versionId, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);
    Task DeleteVersionAsync(string versionId);
    Task RepairAsync(string versionId, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);
    Task<System.Diagnostics.Process> LaunchAsync(
        Instance instance,
        AccountEntry account,
        ServerEntry? server,
        IProgress<string>? log = null,
        CancellationToken ct = default);
    System.Diagnostics.Process? RunningProcess { get; }
    Task StopAsync();
    void ResetLauncher();
    Task<string?> GetJavaPathForVersionAsync(string versionId, CancellationToken ct = default);
}
