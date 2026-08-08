namespace CraftStation.Core.Models;

public sealed class ModrinthProject
{
    public string Id { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? IconUrl { get; set; }
    public long Downloads { get; set; }
    public string ProjectType { get; set; } = "mod";
    public List<string> Categories { get; set; } = new();
    public List<string> GameVersions { get; set; } = new();
    public List<string> Loaders { get; set; } = new();
    public string? SourceUrl { get; set; }
    public int Followers { get; set; }

    public string TypeLabel => ProjectType switch
    {
        "mod" => "模组",
        "resourcepack" => "资源包",
        "shader" => "光影包",
        "modpack" => "整合包",
        _ => ProjectType
    };
}

public sealed class ModrinthVersion
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string VersionNumber { get; set; } = "";
    public string? Changelog { get; set; }
    public DateTime DatePublished { get; set; }
    public List<string> GameVersions { get; set; } = new();
    public List<string> Loaders { get; set; } = new();
    public List<ModrinthFile> Files { get; set; } = new();
    public List<ModrinthDependency> Dependencies { get; set; } = new();
}

public sealed class ModrinthFile
{
    public string Filename { get; set; } = "";
    public string Url { get; set; } = "";
    public long Size { get; set; }
    public Dictionary<string, string> Hashes { get; set; } = new();
    public bool Primary { get; set; }
}

public sealed class ModrinthDependency
{
    public string? ProjectId { get; set; }
    public string? VersionId { get; set; }
    public string DependencyType { get; set; } = "";
}

public sealed class UpdateInfo
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Notes { get; set; }
    public bool IsNewer { get; set; }
}
