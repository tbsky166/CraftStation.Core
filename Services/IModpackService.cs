using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public interface IModpackService
{
    Task<Instance> ImportAsync(string packPath, string instanceName, IProgress<DownloadProgress>? progress = null, IProgress<string>? log = null, CancellationToken ct = default);
    Task ExportAsync(Instance instance, string outputPath, bool modrinthFormat = true, CancellationToken ct = default);
}
