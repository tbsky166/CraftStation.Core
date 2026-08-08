namespace CraftStation.Core.Models;

public sealed class VersionInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime? ReleaseTimeUtc { get; set; }
    public bool IsInstalled { get; set; }

    public string TypeLabel => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "旧版 Beta",
        "old_alpha" => "旧版 Alpha",
        "local" => "本地",
        _ => Type
    };
}
