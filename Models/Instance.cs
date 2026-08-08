namespace CraftStation.Core.Models;

public enum LoaderKind
{
    Vanilla,
    Forge,
    Fabric,
    Quilt,
    NeoForge,
    OptiFine,
    LiteLoader
}

public sealed class Instance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string VersionId { get; set; } = "";
    public LoaderKind Loader { get; set; } = LoaderKind.Vanilla;
    public string? LoaderVersion { get; set; }
    public bool VersionIsolation { get; set; }
    public string? JavaPath { get; set; }
    public int MinMemoryMb { get; set; } = Config.DefaultMinMemoryMb;
    public int MaxMemoryMb { get; set; } = Config.DefaultMaxMemoryMb;
    public string JvmArgs { get; set; } = "";
    public string GameArgs { get; set; } = "";
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool Fullscreen { get; set; }
    public string? ServerId { get; set; }
    public bool CloseLauncherAfterLaunch { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime? LastPlayedUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string ResolvedVersionName =>
        Loader == LoaderKind.Vanilla ? VersionId : (LoaderVersion ?? VersionId);
}
