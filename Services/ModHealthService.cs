using System.Text;
using CraftStation.Core.Models;
using CraftStation.Core.Utils;

namespace CraftStation.Core.Services;

public sealed class ModHealthService : IModHealthService
{
    private readonly IInstanceManager _instances;
    private readonly IResourceManager _resources;

    public ModHealthService(IInstanceManager instances, IResourceManager resources)
    {
        _instances = instances;
        _resources = resources;
    }

    public async Task<ModHealthReport> ScanAsync(Instance instance, CancellationToken ct = default)
    {
        var report = new ModHealthReport();
        var modsDir = Path.Combine(_instances.GetGameDirectory(instance), Config.MinecraftModsDirectoryName);
        if (!Directory.Exists(modsDir))
            return report;

        foreach (var jar in Directory.EnumerateFiles(modsDir, "*.jar"))
        {
            ct.ThrowIfCancellationRequested();
            report.Mods.Add(ModMetadataReader.Read(jar));
        }

        var provided = report.Mods
            .Where(m => !string.IsNullOrEmpty(m.ModId))
            .SelectMany(m => new[] { m.ModId! }.Concat(m.Provides))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var duplicates = report.Mods
            .Where(m => !string.IsNullOrEmpty(m.ModId))
            .GroupBy(m => m.ModId!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);
        foreach (var group in duplicates)
        {
            report.Issues.Add(new HealthIssue
            {
                Severity = HealthSeverity.Error,
                Title = $"重复模组 ID：{group.Key}",
                Detail = $"以下文件声明了相同的 modId，游戏可能加载异常：{string.Join("、", group.Select(m => m.FileName))}",
                ModId = group.Key,
                Suggestion = "只保留其中一个文件，其余禁用或删除。"
            });
        }

        foreach (var mod in report.Mods.Where(m => m.IsValidMetadata))
        {
            if (!string.IsNullOrEmpty(mod.MinecraftVersionRange) &&
                !MinecraftVersionRange.Matches(mod.MinecraftVersionRange, instance.VersionId))
            {
                report.Issues.Add(new HealthIssue
                {
                    Severity = HealthSeverity.Warning,
                    Title = $"{mod.Display} 的游戏版本不匹配",
                    Detail = $"模组要求 {mod.MinecraftVersionRange}，当前实例为 {instance.VersionId}。",
                    ModId = mod.ModId,
                    FilePath = mod.FilePath,
                    Suggestion = "安装适配该版本的模组，或为模组创建对应版本的实例。"
                });
            }

            if (instance.Loader != LoaderKind.Vanilla && !IsLoaderCompatible(instance.Loader, mod.Loader))
            {
                report.Issues.Add(new HealthIssue
                {
                    Severity = HealthSeverity.Error,
                    Title = $"{mod.Display} 与当前加载器不兼容",
                    Detail = $"实例使用 {LoaderLabel(instance.Loader)}，而 {mod.FileName} 是 {LoaderLabel(mod.Loader)} 模组。",
                    ModId = mod.ModId,
                    FilePath = mod.FilePath,
                    Suggestion = "删除或禁用该模组，并安装对应加载器的版本。"
                });
            }

            foreach (var dep in mod.Dependencies)
            {
                var target = report.Mods.FirstOrDefault(m =>
                    !string.IsNullOrEmpty(m.ModId) &&
                    string.Equals(m.ModId, dep.ModId, StringComparison.OrdinalIgnoreCase));

                if (dep.Kind is DependencyKind.Incompatible or DependencyKind.Breaks)
                {
                    if (target != null)
                    {
                        report.Issues.Add(new HealthIssue
                        {
                            Severity = HealthSeverity.Error,
                            Title = $"{mod.Display} 与 {target.Display} 冲突",
                            Detail = $"{mod.FileName} 声明与 {dep.ModId} 不兼容。",
                            ModId = mod.ModId,
                            FilePath = mod.FilePath,
                            Suggestion = "禁用其中一个模组。"
                        });
                    }
                    continue;
                }

                if (dep.Kind != DependencyKind.Required && dep.Kind != DependencyKind.Optional)
                    continue;

                if (target == null)
                {
                    if (dep.Kind == DependencyKind.Required)
                    {
                        report.Issues.Add(new HealthIssue
                        {
                            Severity = HealthSeverity.Error,
                            Title = $"{mod.Display} 缺少前置 {dep.ModId}",
                            Detail = $"依赖 {dep.ModId}{(string.IsNullOrEmpty(dep.VersionRange) ? "" : $"（要求版本 {dep.VersionRange}）")} 未安装。",
                            ModId = mod.ModId,
                            FilePath = mod.FilePath,
                            Suggestion = "在 Modrinth 搜索并安装该前置模组。"
                        });
                    }
                    continue;
                }

                if (!string.IsNullOrEmpty(dep.VersionRange) &&
                    !MinecraftVersionRange.Matches(dep.VersionRange, target.Version))
                {
                    report.Issues.Add(new HealthIssue
                    {
                        Severity = HealthSeverity.Warning,
                        Title = $"{mod.Display} 的前置版本不匹配",
                        Detail = $"{dep.ModId} 当前版本 {target.Version} 不在要求范围 {dep.VersionRange} 内。",
                        ModId = mod.ModId,
                        FilePath = mod.FilePath,
                        Suggestion = "更新或回退前置模组版本。"
                    });
                }
            }
        }

        foreach (var mod in report.Mods.Where(m => !m.IsValidMetadata))
        {
            report.Issues.Add(new HealthIssue
            {
                Severity = HealthSeverity.Warning,
                Title = $"无法解析模组元数据：{mod.FileName}",
                Detail = "该 jar 中没有找到 mods.toml / fabric.mod.json / quilt.mod.json，可能是非模组文件或已损坏。",
                FilePath = mod.FilePath,
                Suggestion = "确认文件是否为有效的模组 jar，必要时重新下载。"
            });
        }
        return report;
    }

    public IReadOnlyList<ModEntry> GetDependencyTree(ModHealthReport report, string modId)
    {
        var result = new List<ModEntry>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string id)
        {
            if (!visited.Add(id))
                return;
            var mod = report.Mods.FirstOrDefault(m =>
                string.Equals(m.ModId, id, StringComparison.OrdinalIgnoreCase));
            if (mod == null)
                return;
            result.Add(mod);
            foreach (var dep in mod.Dependencies.Where(d => d.Kind is DependencyKind.Required or DependencyKind.Optional))
                Walk(dep.ModId);
        }
        Walk(modId);
        return result;
    }

    public IReadOnlyList<ModEntry> GetReverseDependencies(ModHealthReport report, string modId)
    {
        return report.Mods
            .Where(m => m.Dependencies.Any(d =>
                string.Equals(d.ModId, modId, StringComparison.OrdinalIgnoreCase) &&
                d.Kind is DependencyKind.Required or DependencyKind.Optional))
            .ToList();
    }

    public async Task DisableAsync(ModEntry mod) =>
        await _resources.SetEnabledAsync(new ResourceEntry
        {
            FileName = mod.FileName,
            FilePath = mod.FilePath,
            Kind = ResourceKind.Mod
        }, false);

    public async Task EnableAsync(ModEntry mod) =>
        await _resources.SetEnabledAsync(new ResourceEntry
        {
            FileName = mod.FileName,
            FilePath = mod.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? mod.FilePath[..^".disabled".Length]
                : mod.FilePath,
            Kind = ResourceKind.Mod
        }, true);

    public async Task DeleteAsync(ModEntry mod) =>
        await _resources.DeleteAsync(new ResourceEntry
        {
            FileName = mod.FileName,
            FilePath = mod.FilePath,
            Kind = ResourceKind.Mod
        });

    public string ExportReport(ModHealthReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# CraftStation 模组体检报告");
        sb.AppendLine();
        sb.AppendLine($"模组数量：{report.Mods.Count}，问题数量：{report.Issues.Count}");
        sb.AppendLine();
        if (report.Issues.Count == 0)
        {
            sb.AppendLine("未发现问题。");
            return sb.ToString();
        }
        foreach (var issue in report.Issues)
        {
            sb.AppendLine($"- [{issue.SeverityLabel}] {issue.Title}");
            sb.AppendLine($"  {issue.Detail}");
            if (!string.IsNullOrEmpty(issue.Suggestion))
                sb.AppendLine($"  建议：{issue.Suggestion}");
        }
        return sb.ToString();
    }

    private static bool IsLoaderCompatible(LoaderKind instanceLoader, ModLoader modLoader)
    {
        if (modLoader == ModLoader.Unknown)
            return true;
        return instanceLoader switch
        {
            LoaderKind.Fabric => modLoader == ModLoader.Fabric,
            LoaderKind.Quilt => modLoader == ModLoader.Quilt || modLoader == ModLoader.Fabric,
            LoaderKind.Forge => modLoader == ModLoader.Forge,
            LoaderKind.NeoForge => modLoader == ModLoader.NeoForge || modLoader == ModLoader.Forge,
            _ => false
        };
    }

    private static string LoaderLabel(ModLoader loader) => loader switch
    {
        ModLoader.Fabric => "Fabric",
        ModLoader.Quilt => "Quilt",
        ModLoader.Forge => "Forge",
        ModLoader.NeoForge => "NeoForge",
        _ => "未知"
    };

    private static string LoaderLabel(LoaderKind loader) => loader switch
    {
        LoaderKind.Fabric => "Fabric",
        LoaderKind.Quilt => "Quilt",
        LoaderKind.Forge => "Forge",
        LoaderKind.NeoForge => "NeoForge",
        _ => "Vanilla"
    };
}
