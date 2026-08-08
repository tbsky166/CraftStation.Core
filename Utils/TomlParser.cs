using System.Globalization;
using System.Text;

namespace CraftStation.Core.Utils;

public sealed class TomlTable : Dictionary<string, object?>
{
    public TomlTable() : base(StringComparer.OrdinalIgnoreCase) { }
}

public static class TomlParser
{
    public static TomlTable Parse(string text)
    {
        var root = new TomlTable();
        var current = root;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        foreach (var rawLine in lines)
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("[[", StringComparison.Ordinal))
            {
                var path = line[2..^2].Trim();
                current = CreateArrayItem(root, path);
            }
            else if (line.StartsWith("[", StringComparison.Ordinal))
            {
                var path = line[1..^1].Trim();
                current = CreateTable(root, path);
            }
            else
            {
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var key = line[..eq].Trim();
                var valueText = line[(eq + 1)..].Trim();
                var value = ParseValue(valueText);
                SetKey(current, key, value);
            }
        }
        return root;
    }

    private static string StripComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
                inString = !inString;
            if (c == '#' && !inString)
                return line[..i];
        }
        return line;
    }

    private static TomlTable CreateTable(TomlTable root, string path)
    {
        var parts = path.Split('.');
        var start = ResolveFromListIfNeeded(root, parts);
        TomlTable table = start;
        var begin = table == root ? 0 : 1;
        for (var i = begin; i < parts.Length; i++)
        {
            var part = parts[i];
            if (!table.TryGetValue(part, out var next) || next is not TomlTable nextTable)
            {
                nextTable = new TomlTable();
                table[part] = nextTable;
            }
            table = nextTable;
        }
        return table;
    }

    private static TomlTable CreateArrayItem(TomlTable root, string path)
    {
        var parts = path.Split('.');
        var start = ResolveFromListIfNeeded(root, parts);
        TomlTable table = start;
        var begin = table == root ? 0 : 1;
        for (var i = begin; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (!table.TryGetValue(part, out var next) || next is not TomlTable nextTable)
            {
                nextTable = new TomlTable();
                table[part] = nextTable;
            }
            table = nextTable;
        }

        var last = parts[^1];
        if (!table.TryGetValue(last, out var listObj) || listObj is not List<object?> list)
        {
            list = new List<object?>();
            table[last] = list;
        }
        var item = new TomlTable();
        list.Add(item);
        return item;
    }

    private static TomlTable ResolveFromListIfNeeded(TomlTable root, string[] parts)
    {
        if (parts.Length == 0)
            return root;
        if (root.TryGetValue(parts[0], out var first) && first is List<object?> list)
        {
            if (list.Count > 0 && list[^1] is TomlTable item)
                return item;
        }
        return root;
    }

    private static void SetKey(TomlTable table, string key, object? value)
    {
        var parts = key.Split('.');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i];
            if (!table.TryGetValue(part, out var next) || next is not TomlTable nextTable)
            {
                nextTable = new TomlTable();
                table[part] = nextTable;
            }
            table = nextTable;
        }
        table[parts[^1]] = value;
    }

    private static object? ParseValue(string text)
    {
        if (text.Length == 0)
            return null;
        if (text[0] == '"')
        {
            var sb = new StringBuilder();
            for (var i = 1; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    var next = text[++i];
                    sb.Append(next switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '"' => '"',
                        '\\' => '\\',
                        _ => next
                    });
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
        if (text == "true")
            return true;
        if (text == "false")
            return false;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            return intValue;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;
        if (text.StartsWith('[') && text.EndsWith(']'))
        {
            var items = text[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return items.Select(ParseValue).ToList();
        }
        return text;
    }
}
