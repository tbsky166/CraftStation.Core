namespace CraftStation.Core.Models;

public enum ModLoader
{
    Unknown,
    Forge,
    NeoForge,
    Fabric,
    Quilt
}

public enum DependencyKind
{
    Required,
    Optional,
    Incompatible,
    Breaks,
    Recommends,
    Suggests
}

public sealed class ModDependency
{
    public string ModId { get; set; } = "";
    public DependencyKind Kind { get; set; }
    public string? VersionRange { get; set; }
    public bool IsSatisfied { get; set; }
}

public sealed class ModEntry
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public bool IsDisabled { get; set; }
    public string? ModId { get; set; }
    public string? DisplayName { get; set; }
    public string? Version { get; set; }
    public string? MinecraftVersionRange { get; set; }
    public ModLoader Loader { get; set; } = ModLoader.Unknown;
    public List<ModDependency> Dependencies { get; set; } = new();
    public List<string> Provides { get; set; } = new();
    public bool IsValidMetadata => !string.IsNullOrEmpty(ModId);
    public string Display => DisplayName ?? FileName;
}

public enum HealthSeverity
{
    Info,
    Warning,
    Error
}

public sealed class HealthIssue
{
    public HealthSeverity Severity { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? ModId { get; set; }
    public string? FilePath { get; set; }
    public string? Suggestion { get; set; }

    public string SeverityLabel => Severity switch
    {
        HealthSeverity.Error => "错误",
        HealthSeverity.Warning => "警告",
        _ => "提示"
    };
}

public enum ResourceKind
{
    Mod,
    ResourcePack,
    ShaderPack
}

public sealed class ResourceEntry
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public ResourceKind Kind { get; set; }
    public long SizeBytes { get; set; }
    public bool IsDisabled { get; set; }
    public string? DisplayName { get; set; }
    public string? Version { get; set; }
    public string? ModId { get; set; }

    public string KindLabel => Kind switch
    {
        ResourceKind.Mod => "模组",
        ResourceKind.ResourcePack => "资源包",
        _ => "光影包"
    };

    public string SizeLabel
    {
        get
        {
            double mb = SizeBytes / 1024d / 1024d;
            return mb >= 1 ? $"{mb:0.##} MB" : $"{SizeBytes / 1024d:0.#} KB";
        }
    }
}

public sealed class SaveEntry
{
    public string FolderName { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? GameMode { get; set; }
    public string? Difficulty { get; set; }
    public DateTime? LastPlayedUtc { get; set; }
    public string? IconPath { get; set; }
}
