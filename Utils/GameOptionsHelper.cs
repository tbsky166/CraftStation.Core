namespace CraftStation.Core.Utils;

public static class GameOptionsHelper
{
    /// <summary>
    /// 确保游戏目录的 options.txt 中语言为中文。
    /// 文件不存在时创建，已有其它语言时覆盖。
    /// </summary>
    public static void EnsureChineseLanguage(string gameDirectory)
    {
        try
        {
            Directory.CreateDirectory(gameDirectory);
            var optionsPath = Path.Combine(gameDirectory, "options.txt");
            var lines = new List<string>();
            if (File.Exists(optionsPath))
                lines.AddRange(File.ReadAllLines(optionsPath));

            var langLine = $"lang:{Config.DefaultGameLanguage}";
            var index = lines.FindIndex(l => l.StartsWith("lang:", StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                if (!string.Equals(lines[index], langLine, StringComparison.OrdinalIgnoreCase))
                    lines[index] = langLine;
            }
            else
            {
                lines.Add(langLine);
            }

            File.WriteAllLines(optionsPath, lines);
        }
        catch
        {
            // 语言写入失败不阻断实例创建/导入流程
        }
    }
}
