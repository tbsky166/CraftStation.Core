using System.Text.Json;
using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public sealed class ModrinthService : IModrinthService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly HttpClient _http;

    public ModrinthService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Config.ModrinthTimeoutSeconds) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(Config.UserAgent);
    }

    public async Task<IReadOnlyList<ModrinthProject>> SearchAsync(
        string query,
        string projectType = "mod",
        string? gameVersion = null,
        string? loader = null,
        int limit = Config.ModrinthDefaultSearchLimit,
        CancellationToken ct = default)
    {
        var facets = new List<string> { $"[[\"project_type:{projectType}\"]" };
        if (!string.IsNullOrEmpty(gameVersion))
            facets.Add($"[\"versions:{gameVersion}\"]");
        if (!string.IsNullOrEmpty(loader))
            facets.Add($"[\"categories:{loader}\"]");
        var facetsJson = string.Join(",", facets) + "]";
        var url = $"{Config.ModrinthApiBase}/search?query={Uri.EscapeDataString(query)}&limit={limit}&facets={Uri.EscapeDataString(facetsJson)}";
        await using var stream = await _http.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        if (!root.TryGetProperty("hits", out var hits))
            return Array.Empty<ModrinthProject>();
        return hits.Deserialize<List<ModrinthProject>>(JsonOptions) ?? new List<ModrinthProject>();
    }

    public async Task<ModrinthProject?> GetProjectAsync(string idOrSlug, CancellationToken ct = default)
    {
        var url = $"{Config.ModrinthApiBase}/project/{Uri.EscapeDataString(idOrSlug)}";
        await using var stream = await _http.GetStreamAsync(url, ct);
        return await JsonSerializer.DeserializeAsync<ModrinthProject>(stream, JsonOptions, ct);
    }

    public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
        string projectIdOrSlug,
        string? gameVersion = null,
        string? loader = null,
        CancellationToken ct = default)
    {
        var url = $"{Config.ModrinthApiBase}/project/{Uri.EscapeDataString(projectIdOrSlug)}/version";
        var query = new List<string>();
        if (!string.IsNullOrEmpty(gameVersion))
            query.Add($"game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion }))}");
        if (!string.IsNullOrEmpty(loader))
            query.Add($"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}");
        if (query.Count > 0)
            url += "?" + string.Join("&", query);
        await using var stream = await _http.GetStreamAsync(url, ct);
        return await JsonSerializer.DeserializeAsync<List<ModrinthVersion>>(stream, JsonOptions, ct) ?? new List<ModrinthVersion>();
    }

    public async Task DownloadFileAsync(
        ModrinthVersion version,
        ModrinthFile file,
        string targetPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        using var response = await _http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? file.Size;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var dest = File.Create(targetPath);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await source.ReadAsync(buffer, ct)) > 0)
        {
            await dest.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            progress?.Report(new DownloadProgress
            {
                CurrentFile = file.Filename,
                CompletedBytes = read,
                TotalBytes = total
            });
        }
    }
}
