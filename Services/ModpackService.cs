using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using CraftStation.Core.Models;
using CraftStation.Core.Utils;

namespace CraftStation.Core.Services;

public sealed class ModpackService : IModpackService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IInstanceManager _instances;
    private readonly IModLoaderInstaller _loaderInstaller;
    private readonly IModrinthService _modrinth;
    private readonly ISettingsService _settings;

    public ModpackService(
        IInstanceManager instances,
        IModLoaderInstaller loaderInstaller,
        IModrinthService modrinth,
        ISettingsService settings)
    {
        _instances = instances;
        _loaderInstaller = loaderInstaller;
        _modrinth = modrinth;
        _settings = settings;
    }

    public async Task<Instance> ImportAsync(
        string packPath,
        string instanceName,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(packPath))
            throw new FileNotFoundException("整合包文件不存在", packPath);

        using var archive = ZipFile.OpenRead(packPath);
        var indexEntry = archive.Entries.FirstOrDefault(e => e.FullName == Config.ModrinthIndexFileName);
        if (indexEntry != null)
            return await ImportModrinthAsync(archive, indexEntry, instanceName, progress, log, ct);

        var manifestEntry = archive.Entries.FirstOrDefault(e => e.FullName == Config.CurseForgeManifestFileName);
        if (manifestEntry != null)
            return await ImportCurseForgeAsync(archive, manifestEntry, instanceName, progress, log, ct);

        throw new InvalidDataException(
            $"无法识别整合包格式：缺少 {Config.ModrinthIndexFileName} 或 {Config.CurseForgeManifestFileName}。");
    }

    private async Task<Instance> ImportModrinthAsync(
        ZipArchive archive,
        ZipArchiveEntry indexEntry,
        string instanceName,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? log,
        CancellationToken ct)
    {
        string json;
        using (var stream = indexEntry.Open())
        using (var reader = new StreamReader(stream))
            json = await reader.ReadToEndAsync(ct);
        var index = JsonNode.Parse(json)!.AsObject();
        var mcVersion = index["dependencies"]?["minecraft"]?.GetValue<string>() ?? Config.DefaultModpackMinecraftVersion;
        var loaderName = index["dependencies"]?.AsObject()
            .FirstOrDefault(kv => kv.Key is "fabric-loader" or "quilt-loader" or "forge" or "neoforge").Key;
        var loader = loaderName switch
        {
            "fabric-loader" => LoaderKind.Fabric,
            "quilt-loader" => LoaderKind.Quilt,
            "forge" => LoaderKind.Forge,
            "neoforge" => LoaderKind.NeoForge,
            _ => LoaderKind.Vanilla
        };
        var instance = await _instances.CreateAsync(instanceName, mcVersion, loader);
        if (loader != LoaderKind.Vanilla)
            await _loaderInstaller.InstallAsync(mcVersion, loader, null, progress, log, ct);
        instance.Loader = loader;
        instance.LoaderVersion = loader == LoaderKind.Vanilla ? null : "latest";
        await _instances.UpdateAsync(instance);

        var files = index["files"]?.AsArray() ?? new JsonArray();
        foreach (var fileNode in files)
        {
            var file = fileNode!.AsObject();
            var relPath = file["path"]?.GetValue<string>() ?? "";
            var downloads = file["downloads"]?.AsArray();
            var url = downloads?.FirstOrDefault()?.GetValue<string>();
            var target = Path.Combine(_instances.GetGameDirectory(instance), relPath.Replace('/', Path.DirectorySeparatorChar));
            if (!string.IsNullOrEmpty(url))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                log?.Report($"下载 {Path.GetFileName(target)}");
                using var http = new HttpClient();
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using var src = await response.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(target);
                await src.CopyToAsync(dst, ct);
            }
        }
        await ExtractOverridesAsync(archive, _instances.GetGameDirectory(instance), ct);
        GameOptionsHelper.EnsureChineseLanguage(_instances.GetGameDirectory(instance));
        return instance;
    }

    private async Task<Instance> ImportCurseForgeAsync(
        ZipArchive archive,
        ZipArchiveEntry manifestEntry,
        string instanceName,
        IProgress<DownloadProgress>? progress,
        IProgress<string>? log,
        CancellationToken ct)
    {
        string json;
        using (var stream = manifestEntry.Open())
        using (var reader = new StreamReader(stream))
            json = await reader.ReadToEndAsync(ct);
        var manifest = JsonNode.Parse(json)!.AsObject();
        var mcVersion = manifest["minecraft"]?["version"]?.GetValue<string>() ?? Config.DefaultModpackMinecraftVersion;
        var loaderId = manifest["minecraft"]?["modLoaders"]?[0]?["id"]?.GetValue<string>() ?? "";
        var loader = loaderId.StartsWith("forge", StringComparison.OrdinalIgnoreCase)
            ? LoaderKind.Forge
            : loaderId.StartsWith("fabric", StringComparison.OrdinalIgnoreCase)
                ? LoaderKind.Fabric
                : loaderId.StartsWith("neoforge", StringComparison.OrdinalIgnoreCase)
                    ? LoaderKind.NeoForge
                    : loaderId.StartsWith("quilt", StringComparison.OrdinalIgnoreCase)
                        ? LoaderKind.Quilt
                        : LoaderKind.Vanilla;

        var instance = await _instances.CreateAsync(instanceName, mcVersion, loader);
        if (loader != LoaderKind.Vanilla)
            await _loaderInstaller.InstallAsync(mcVersion, loader, null, progress, log, ct);

        var apiKey = _settings.Settings.CurseForgeApiKey;
        if (!string.IsNullOrEmpty(apiKey))
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            foreach (var fileNode in manifest["files"]?.AsArray() ?? new JsonArray())
            {
                var projectId = fileNode?["projectID"]?.GetValue<int>();
                var fileId = fileNode?["fileID"]?.GetValue<int>();
                if (projectId == null || fileId == null)
                    continue;
                var dlUrl = await http.GetStringAsync(
                    $"{Config.CurseForgeApiBase}/mods/{projectId}/files/{fileId}/download-url", ct);
                var urlNode = JsonNode.Parse(dlUrl)?["data"];
                var url = urlNode?.GetValue<string>();
                if (string.IsNullOrEmpty(url))
                    continue;
                var target = Path.Combine(
                    _instances.GetGameDirectory(instance), Config.MinecraftModsDirectoryName, $"{projectId}-{fileId}.jar");
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                await using var src = await response.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(target);
                await src.CopyToAsync(dst, ct);
            }
        }
        else if ((manifest["files"]?.AsArray() ?? new JsonArray()).Count > 0)
        {
            log?.Report("未配置 CurseForge API Key，在线文件已跳过；overrides 文件仍会导入。");
        }

        await ExtractOverridesAsync(archive, _instances.GetGameDirectory(instance), ct);
        GameOptionsHelper.EnsureChineseLanguage(_instances.GetGameDirectory(instance));
        return instance;
    }

    public async Task ExportAsync(Instance instance, string outputPath, bool modrinthFormat = true, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var gameDir = _instances.GetGameDirectory(instance);
        var modsDir = Path.Combine(gameDir, Config.MinecraftModsDirectoryName);
        var files = new JsonArray();
        if (Directory.Exists(modsDir))
        {
            foreach (var jar in Directory.EnumerateFiles(modsDir, "*.jar"))
            {
                var sha1 = await ComputeSha1Async(jar, ct);
                files.Add(new JsonObject
                {
                    ["path"] = $"{Config.MinecraftModsDirectoryName}/{Path.GetFileName(jar)}",
                    ["hashes"] = new JsonObject { ["sha1"] = sha1 },
                    ["downloads"] = new JsonArray(),
                    ["fileSize"] = new FileInfo(jar).Length
                });
            }
        }

        using var fs = File.Create(outputPath);
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (modrinthFormat)
            {
                var index = new JsonObject
                {
                    ["formatVersion"] = 1,
                    ["game"] = "minecraft",
                    ["versionId"] = instance.VersionId,
                    ["name"] = instance.Name,
                    ["summary"] = instance.Description ?? "",
                    ["dependencies"] = new JsonObject
                    {
                        ["minecraft"] = instance.VersionId,
                        [LoaderKey(instance.Loader)] = instance.LoaderVersion ?? "latest"
                    },
                    ["files"] = files
                };
                WriteEntry(archive, Config.ModrinthIndexFileName, index.ToJsonString(JsonOptions));
            }
            else
            {
                var manifest = new JsonObject
                {
                    ["minecraft"] = new JsonObject
                    {
                        ["version"] = instance.VersionId,
                        ["modLoaders"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = $"{LoaderKey(instance.Loader)}-{instance.LoaderVersion ?? "latest"}",
                                ["primary"] = true
                            }
                        }
                    },
                    ["manifestType"] = "minecraftModpack",
                    ["manifestVersion"] = 1,
                    ["name"] = instance.Name,
                    ["version"] = "1.0.0",
                    ["author"] = Config.ModpackAuthor,
                    ["files"] = new JsonArray(),
                    ["overrides"] = Config.ModpackOverridesDirectoryName
                };
                WriteEntry(archive, Config.CurseForgeManifestFileName, manifest.ToJsonString(JsonOptions));
            }

            foreach (var file in files)
            {
                var rel = file?["path"]?.GetValue<string>() ?? "";
                var src = Path.Combine(gameDir, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(src))
                {
                    var zipEntry = archive.CreateEntry(rel);
                    await using var entry = zipEntry.Open();
                    await using var source = File.OpenRead(src);
                    await source.CopyToAsync(entry, ct);
                }
            }

            foreach (var folder in Config.ModpackExportDirectoryNames)
            {
                var srcDir = Path.Combine(gameDir, folder);
                if (!Directory.Exists(srcDir))
                    continue;
                foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(gameDir, file).Replace('\\', '/');
                    var zipEntry = archive.CreateEntry($"{Config.ModpackOverridesDirectoryName}/{rel}");
                    await using var entry = zipEntry.Open();
                    await using var source = File.OpenRead(file);
                    await source.CopyToAsync(entry, ct);
                }
            }
        }
    }

    private static string LoaderKey(LoaderKind loader) => loader switch
    {
        LoaderKind.Fabric => "fabric-loader",
        LoaderKind.Quilt => "quilt-loader",
        LoaderKind.Forge => "forge",
        LoaderKind.NeoForge => "neoforge",
        _ => "minecraft"
    };

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    private static async Task ExtractOverridesAsync(ZipArchive archive, string gameDir, CancellationToken ct)
    {
        foreach (var entry in archive.Entries.Where(e =>
                     e.FullName.StartsWith(Config.ModpackOverridesDirectoryName + "/", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrEmpty(e.Name)))
        {
            var rel = entry.FullName[(Config.ModpackOverridesDirectoryName.Length + 1)..]
                .Replace('/', Path.DirectorySeparatorChar);
            var target = Path.Combine(gameDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var src = entry.Open();
            await using var dst = File.Create(target);
            await src.CopyToAsync(dst, ct);
        }
    }

    private static async Task<string> ComputeSha1Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
