using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public sealed class ModHealthReport
{
    public List<ModEntry> Mods { get; set; } = new();
    public List<HealthIssue> Issues { get; set; } = new();
}

public interface IModHealthService
{
    Task<ModHealthReport> ScanAsync(Instance instance, CancellationToken ct = default);
    IReadOnlyList<ModEntry> GetDependencyTree(ModHealthReport report, string modId);
    IReadOnlyList<ModEntry> GetReverseDependencies(ModHealthReport report, string modId);
    Task DisableAsync(ModEntry mod);
    Task EnableAsync(ModEntry mod);
    Task DeleteAsync(ModEntry mod);
    string ExportReport(ModHealthReport report);
}
