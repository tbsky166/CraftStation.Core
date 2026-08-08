using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public interface IModrinthService
{
    Task<IReadOnlyList<ModrinthProject>> SearchAsync(
        string query,
        string projectType = "mod",
        string? gameVersion = null,
        string? loader = null,
        int limit = 30,
        CancellationToken ct = default);
    Task<ModrinthProject?> GetProjectAsync(string idOrSlug, CancellationToken ct = default);
    Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
        string projectIdOrSlug,
        string? gameVersion = null,
        string? loader = null,
        CancellationToken ct = default);
    Task DownloadFileAsync(
        ModrinthVersion version,
        ModrinthFile file,
        string targetPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);
}
