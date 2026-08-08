using System.Text.RegularExpressions;

namespace CraftStation.Core.Utils;

/// <summary>
/// 版本分类：正式版 / 快照 / 愚人节 / 远古版。
/// Mojang 清单只标注 release / snapshot / old_beta / old_alpha，
/// 愚人节版本需要靠已知 ID + 快照命名规律识别。
/// </summary>
public static class VersionCategoryUtil
{
    private static readonly HashSet<string> AprilFoolsIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "2.0",
        "1.RV-Pre1",
        "15w14a",
        "3D Shareware v1.34",
        "20w14infinite",
        "22w13oneblockatatime",
        "23w13a_or_b",
        "24w14potato",
        "25w14craftmine"
    };

    // 标准快照形如 24w14a；20w14infinite / 23w13a_or_b 这类非标准命名基本都是愚人节
    private static readonly Regex StandardSnapshot = new(@"^\d{2}w\d{2}[a-z]$", RegexOptions.IgnoreCase);

    public static string GetCategory(string name, string type)
    {
        if (type is "old_beta" or "old_alpha")
            return "old";
        if (AprilFoolsIds.Contains(name))
            return "aprilfools";
        if (type == "snapshot" && !StandardSnapshot.IsMatch(name))
            return "aprilfools";
        return type switch
        {
            "release" => "release",
            "snapshot" => "snapshot",
            _ => "other"
        };
    }
}
