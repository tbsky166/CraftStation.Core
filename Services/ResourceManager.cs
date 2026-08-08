using CraftStation.Core.Models;
using CraftStation.Core.Utils;

namespace CraftStation.Core.Services;

public sealed class ResourceManager : IResourceManager
{
    private readonly IInstanceManager _instances;

    public ResourceManager(IInstanceManager instances)
    {
        _instances = instances;
    }

    public Task<IReadOnlyList<ResourceEntry>> ListModsAsync(Instance instance, CancellationToken ct = default) =>
        Task.FromResult(ListFolder(instance, ResourceKind.Mod));

    public Task<IReadOnlyList<ResourceEntry>> ListResourcePacksAsync(Instance instance, CancellationToken ct = default) =>
        Task.FromResult(ListFolder(instance, ResourceKind.ResourcePack));

    public Task<IReadOnlyList<ResourceEntry>> ListShaderPacksAsync(Instance instance, CancellationToken ct = default) =>
        Task.FromResult(ListFolder(instance, ResourceKind.ShaderPack));

    public Task<IReadOnlyList<SaveEntry>> ListSavesAsync(Instance instance, CancellationToken ct = default)
    {
        var savesDir = Path.Combine(_instances.GetGameDirectory(instance), Config.MinecraftSavesDirectoryName);
        var result = new List<SaveEntry>();
        if (!Directory.Exists(savesDir))
            return Task.FromResult<IReadOnlyList<SaveEntry>>(result);
        foreach (var dir in Directory.EnumerateDirectories(savesDir))
        {
            var levelDat = Path.Combine(dir, "level.dat");
            var icon = Path.Combine(dir, "icon.png");
            string? name = null;
            DateTime? lastPlayed = null;
            string? gameMode = null;
            string? difficulty = null;
            if (File.Exists(levelDat))
            {
                try
                {
                    using var fs = File.OpenRead(levelDat);
                    var nbt = NbtReader.Read(fs);
                    if (nbt.TryGetValue("Data", out var dataObj) && dataObj is Dictionary<string, object?> data)
                    {
                        name = data.GetValueOrDefault("LevelName") as string;
                        if (data.TryGetValue("LastPlayed", out var lp) && lp is long lpTicks)
                            lastPlayed = DateTimeOffset.FromUnixTimeMilliseconds(lpTicks).UtcDateTime;
                        if (data.TryGetValue("GameType", out var gt) && gt is int gameType)
                            gameMode = gameType switch
                            {
                                1 => "创造",
                                2 => "冒险",
                                3 => "旁观",
                                _ => "生存"
                            };
                        if (data.TryGetValue("Difficulty", out var df) && df is byte difficultyByte)
                            difficulty = difficultyByte switch
                            {
                                1 => "简单",
                                2 => "普通",
                                3 => "困难",
                                _ => "和平"
                            };
                    }
                }
                catch
                {
                    // level.dat 损坏时仅显示目录名
                }
            }
            result.Add(new SaveEntry
            {
                FolderName = Path.GetFileName(dir),
                FolderPath = dir,
                DisplayName = name ?? Path.GetFileName(dir),
                GameMode = gameMode,
                Difficulty = difficulty,
                LastPlayedUtc = lastPlayed,
                IconPath = File.Exists(icon) ? icon : null
            });
        }
        return Task.FromResult<IReadOnlyList<SaveEntry>>(result);
    }

    public Task SetEnabledAsync(ResourceEntry entry, bool enabled)
    {
        var disabledPath = entry.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? entry.FilePath
            : entry.FilePath + ".disabled";
        var normalPath = disabledPath[..^".disabled".Length];
        if (enabled && File.Exists(disabledPath) && !File.Exists(normalPath))
            File.Move(disabledPath, normalPath);
        else if (!enabled && File.Exists(normalPath) && !File.Exists(disabledPath))
            File.Move(normalPath, disabledPath);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(ResourceEntry entry)
    {
        if (File.Exists(entry.FilePath))
            File.Delete(entry.FilePath);
        return Task.CompletedTask;
    }

    public Task<string> ImportFileAsync(Instance instance, string sourcePath, ResourceKind kind)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("源文件不存在", sourcePath);
        var folder = GetFolder(instance, kind);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, target, overwrite: true);
        return Task.FromResult(target);
    }

    public string GetFolder(Instance instance, ResourceKind kind)
    {
        var gameDir = _instances.GetGameDirectory(instance);
        return kind switch
        {
            ResourceKind.Mod => Path.Combine(gameDir, Config.MinecraftModsDirectoryName),
            ResourceKind.ResourcePack => Path.Combine(gameDir, Config.MinecraftResourcePacksDirectoryName),
            _ => Path.Combine(gameDir, Config.MinecraftShaderPacksDirectoryName)
        };
    }

    private IReadOnlyList<ResourceEntry> ListFolder(Instance instance, ResourceKind kind)
    {
        var folder = GetFolder(instance, kind);
        var result = new List<ResourceEntry>();
        if (!Directory.Exists(folder))
            return result;
        foreach (var file in Directory.EnumerateFiles(folder)
                     .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)))
        {
            var info = new FileInfo(file);
            var disabled = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            var entry = new ResourceEntry
            {
                FileName = Path.GetFileName(file),
                FilePath = file,
                Kind = kind,
                SizeBytes = info.Length,
                IsDisabled = disabled
            };
            if (kind == ResourceKind.Mod && file.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            {
                var mod = ModMetadataReader.Read(file);
                entry.DisplayName = mod.DisplayName ?? mod.FileName;
                entry.Version = mod.Version;
                entry.ModId = mod.ModId;
            }
            result.Add(entry);
        }
        return result;
    }
}
