using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public interface IResourceManager
{
    Task<IReadOnlyList<ResourceEntry>> ListModsAsync(Instance instance, CancellationToken ct = default);
    Task<IReadOnlyList<ResourceEntry>> ListResourcePacksAsync(Instance instance, CancellationToken ct = default);
    Task<IReadOnlyList<ResourceEntry>> ListShaderPacksAsync(Instance instance, CancellationToken ct = default);
    Task<IReadOnlyList<SaveEntry>> ListSavesAsync(Instance instance, CancellationToken ct = default);
    Task SetEnabledAsync(ResourceEntry entry, bool enabled);
    Task DeleteAsync(ResourceEntry entry);
    Task<string> ImportFileAsync(Instance instance, string sourcePath, ResourceKind kind);
    string GetFolder(Instance instance, ResourceKind kind);
}
