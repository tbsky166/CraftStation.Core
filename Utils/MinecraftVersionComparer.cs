using System.Globalization;

namespace CraftStation.Core.Utils;

public static class MinecraftVersionComparer
{
    public static int Compare(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b))
            return 0;
        if (string.IsNullOrEmpty(a))
            return -1;
        if (string.IsNullOrEmpty(b))
            return 1;

        var pa = Split(a);
        var pb = Split(b);
        var count = Math.Max(pa.Count, pb.Count);
        for (var i = 0; i < count; i++)
        {
            var xa = i < pa.Count ? pa[i] : null;
            var xb = i < pb.Count ? pb[i] : null;
            if (xa == null)
                return xb is not null && char.IsDigit(xb[0]) ? -1 : 1;
            if (xb == null)
                return char.IsDigit(xa[0]) ? 1 : -1;

            var na = TryParseNumber(xa);
            var nb = TryParseNumber(xb);
            if (na.HasValue && nb.HasValue)
            {
                if (na.Value != nb.Value)
                    return na.Value.CompareTo(nb.Value);
            }
            else
            {
                var cmp = string.Compare(xa, xb, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0)
                    return cmp;
            }
        }
        return 0;
    }

    private static int? TryParseNumber(string s) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static List<string> Split(string v)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var c in v)
        {
            if (char.IsDigit(c) || char.IsLetter(c))
            {
                if (current.Length > 0 && char.IsDigit(current[^1]) != char.IsDigit(c))
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                current.Append(c);
            }
            else
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
        }
        if (current.Length > 0)
            result.Add(current.ToString());
        return result;
    }
}

public static class MinecraftVersionRange
{
    public static bool Matches(string? range, string? version)
    {
        if (string.IsNullOrWhiteSpace(range) || string.IsNullOrWhiteSpace(version))
            return true;

        range = range.Trim();
        if (range.StartsWith('[') || range.StartsWith('('))
        {
            var lowerInclusive = range[0] == '[';
            var upperInclusive = range[^1] == ']';
            var inner = range[1..^1];
            var parts = inner.Split(',', 2);
            var lower = parts[0].Trim();
            var upper = parts.Length > 1 ? parts[1].Trim() : "";
            var cmpLower = string.IsNullOrEmpty(lower) ? 0 : MinecraftVersionComparer.Compare(version, lower);
            var cmpUpper = string.IsNullOrEmpty(upper) ? 0 : MinecraftVersionComparer.Compare(version, upper);
            if (!string.IsNullOrEmpty(lower) && (lowerInclusive ? cmpLower < 0 : cmpLower <= 0))
                return false;
            if (!string.IsNullOrEmpty(upper) && (upperInclusive ? cmpUpper > 0 : cmpUpper >= 0))
                return false;
            return true;
        }

        var cmpExact = MinecraftVersionComparer.Compare(version, range.Trim('"'));
        return cmpExact == 0;
    }
}
