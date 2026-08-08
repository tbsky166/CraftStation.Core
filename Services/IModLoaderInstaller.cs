using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public interface IModLoaderInstaller
{
    Task<IReadOnlyList<string>> GetVersionsAsync(string mcVersion, LoaderKind loader, CancellationToken ct = default);
    Task<string> InstallAsync(
        string mcVersion,
        LoaderKind loader,
        string? loaderVersion,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? log = null,
        CancellationToken ct = default);
}
