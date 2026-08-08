using System.Reflection;
using System.Text.Json;
using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public sealed class UpdateService : IUpdateService
{
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;

    public UpdateService(ISettingsService settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Config.UpdateTimeoutSeconds) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(Config.UserAgent);
    }

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var endpoint = _settings.Settings.UpdateEndpoint.Trim();
        if (string.IsNullOrEmpty(endpoint))
            return null;
        var repo = ParseRepo(endpoint);
        if (repo == null)
            return null;
        try
        {
            var json = await _http.GetStringAsync(
                $"{Config.GitHubApiBase}/repos/{repo.Value.owner}/{repo.Value.repo}/releases/latest", ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagNode) ? tagNode.GetString() ?? "" : "";
            var url = root.TryGetProperty("html_url", out var urlNode) ? urlNode.GetString() ?? "" : "";
            var body = root.TryGetProperty("body", out var bodyNode) ? bodyNode.GetString() : null;
            var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? Config.AppVersion;
            var isNewer = !string.IsNullOrEmpty(tag) &&
                          string.Compare(tag.TrimStart('v'), current, StringComparison.OrdinalIgnoreCase) > 0;
            return new UpdateInfo
            {
                Version = tag,
                Url = url,
                Notes = body,
                IsNewer = isNewer
            };
        }
        catch
        {
            return null;
        }
    }

    private static (string owner, string repo)? ParseRepo(string endpoint)
    {
        try
        {
            var uri = new Uri(endpoint.EndsWith("/", StringComparison.Ordinal) ? endpoint : endpoint + "/");
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length >= 2)
                return (parts[0], parts[1]);
        }
        catch
        {
            // ignore
        }
        return null;
    }
}
