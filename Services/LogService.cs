using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public sealed class LogService : ILogService
{
    private readonly ISettingsService _settings;
    private readonly Dictionary<string, string> _instanceLogPaths = new();

    public LogService(ISettingsService settings)
    {
        _settings = settings;
    }

    public string CreateGameLogPath(Instance instance)
    {
        if (_instanceLogPaths.TryGetValue(instance.Id, out var existing))
            return existing;
        Directory.CreateDirectory(_settings.LogsDirectory);
        var path = Path.Combine(_settings.LogsDirectory,
            string.Format(Config.GameLogFilePattern, instance.Id, DateTime.Now));
        _instanceLogPaths[instance.Id] = path;
        return path;
    }

    public async Task AppendAsync(Instance instance, string line)
    {
        var path = CreateGameLogPath(instance);
        await File.AppendAllTextAsync(path, line + Environment.NewLine);
    }

    public async Task<IReadOnlyList<string>> ReadLatestAsync(Instance instance, int maxLines = 500)
    {
        var path = CreateGameLogPath(instance);
        if (!File.Exists(path))
            return Array.Empty<string>();
        var lines = await File.ReadAllLinesAsync(path);
        return lines.TakeLast(maxLines).ToArray();
    }

    public void OpenLogsFolder()
    {
        Directory.CreateDirectory(_settings.LogsDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _settings.LogsDirectory,
            UseShellExecute = true
        });
    }
}
