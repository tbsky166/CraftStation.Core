using System.Text.Json;
using System.IO.Compression;
using CraftStation.Core.Models;

namespace CraftStation.Core.Utils;

public static class ModMetadataReader
{
    public static ModEntry Read(string filePath)
    {
        var entry = new ModEntry
        {
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            IsDisabled = filePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
        };

        string? content;
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            content = ReadEntry(archive, Config.QuiltModJsonFileName);
            if (content != null)
            {
                ParseQuilt(content, entry);
                return entry;
            }
            content = ReadEntry(archive, Config.FabricModJsonFileName);
            if (content != null)
            {
                ParseFabric(content, entry);
                return entry;
            }
            content = ReadEntry(archive, Config.NeoForgeModsTomlPath);
            if (content != null)
            {
                ParseModsToml(content, entry, ModLoader.NeoForge);
                return entry;
            }
            content = ReadEntry(archive, Config.ForgeModsTomlPath);
            if (content != null)
            {
                ParseModsToml(content, entry, ModLoader.Forge);
                return entry;
            }
        }
        catch
        {
            // 损坏或不支持的 jar，保留文件名信息
        }
        return entry;
    }

    private static string? ReadEntry(ZipArchive archive, string key)
    {
        var zipEntry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.FullName, key, StringComparison.OrdinalIgnoreCase));
        if (zipEntry == null)
            return null;
        using var stream = zipEntry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void ParseFabric(string json, ModEntry entry)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        entry.Loader = ModLoader.Fabric;
        entry.ModId = GetString(root, "id");
        entry.DisplayName = GetString(root, "name") ?? entry.ModId;
        entry.Version = GetString(root, "version");
        entry.MinecraftVersionRange = GetRange(root, "depends", "minecraft");
        AddDependencies(entry, root, "depends", DependencyKind.Required);
        AddDependencies(entry, root, "recommends", DependencyKind.Recommends);
        AddDependencies(entry, root, "suggests", DependencyKind.Suggests);
        AddDependencies(entry, root, "breaks", DependencyKind.Breaks);
        AddDependencies(entry, root, "conflicts", DependencyKind.Incompatible);
    }

    private static void ParseQuilt(string json, ModEntry entry)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        entry.Loader = ModLoader.Quilt;
        if (root.TryGetProperty("quilt_loader", out var loader))
            root = loader;
        entry.ModId = GetString(root, "id");
        entry.DisplayName = GetString(root, "name") ?? GetString(root, "id");
        entry.Version = GetString(root, "version");
        entry.MinecraftVersionRange = GetRange(root, "depends", "minecraft");
        AddDependencies(entry, root, "depends", DependencyKind.Required);
        AddDependencies(entry, root, "recommends", DependencyKind.Recommends);
        AddDependencies(entry, root, "suggests", DependencyKind.Suggests);
        AddDependencies(entry, root, "breaks", DependencyKind.Breaks);
        AddDependencies(entry, root, "conflicts", DependencyKind.Incompatible);
    }

    private static void ParseModsToml(string toml, ModEntry entry, ModLoader loader)
    {
        var root = TomlParser.Parse(toml);
        entry.Loader = loader;
        if (root.TryGetValue("mods", out var modsObj) && modsObj is List<object?> mods && mods.Count > 0)
        {
            var mod = mods[0] as TomlTable;
            if (mod != null)
            {
                entry.ModId = mod.GetValueOrDefault("modId") as string ?? mod.GetValueOrDefault("modid") as string;
                entry.DisplayName = mod.GetValueOrDefault("displayName") as string ?? entry.ModId;
                entry.Version = mod.GetValueOrDefault("version") as string;
            }
        }
        if (root.TryGetValue("dependencies", out var depsObj) && depsObj is TomlTable deps)
        {
            foreach (var kv in deps)
            {
                if (kv.Value is not List<object?> depList)
                    continue;
                foreach (var item in depList)
                {
                    if (item is not TomlTable dep)
                        continue;
                    var depModId = dep.GetValueOrDefault("modId") as string ?? dep.GetValueOrDefault("modid") as string;
                    if (string.IsNullOrEmpty(depModId))
                        continue;
                    var kind = (dep.GetValueOrDefault("type") as string)?.ToLowerInvariant() switch
                    {
                        "required" => DependencyKind.Required,
                        "optional" => DependencyKind.Optional,
                        "incompatible" => DependencyKind.Incompatible,
                        "breaks" => DependencyKind.Breaks,
                        _ => DependencyKind.Required
                    };
                    entry.Dependencies.Add(new ModDependency
                    {
                        ModId = depModId,
                        Kind = kind,
                        VersionRange = dep.GetValueOrDefault("versionRange") as string
                    });
                    if (kind == DependencyKind.Required && depModId == "minecraft")
                        entry.MinecraftVersionRange = dep.GetValueOrDefault("versionRange") as string;
                }
            }
        }
    }

    private static void AddDependencies(ModEntry entry, JsonElement root, string property, DependencyKind kind)
    {
        if (!root.TryGetProperty(property, out var deps) || deps.ValueKind != JsonValueKind.Object)
            return;
        foreach (var dep in deps.EnumerateObject())
        {
            entry.Dependencies.Add(new ModDependency
            {
                ModId = dep.Name,
                Kind = kind,
                VersionRange = dep.Value.ValueKind == JsonValueKind.String ? dep.Value.GetString() : dep.Value.ToString()
            });
        }
    }

    private static string? GetRange(JsonElement root, string section, string modId)
    {
        if (!root.TryGetProperty(section, out var deps) || deps.ValueKind != JsonValueKind.Object)
            return null;
        if (!deps.TryGetProperty(modId, out var range))
            return null;
        return range.ValueKind == JsonValueKind.String ? range.GetString() : range.ToString();
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
