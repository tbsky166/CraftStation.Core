using System.Text.Json;
using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsService(string? launcherRoot = null)
    {
        LauncherRoot = string.IsNullOrWhiteSpace(launcherRoot)
            ? ResolveDefaultLauncherRoot()
            : Path.GetFullPath(launcherRoot);
        DataDirectory = Path.Combine(LauncherRoot, Config.DataDirectoryName);
        DefaultGameDirectory = Path.Combine(LauncherRoot, Config.GameDirectoryName);
        InstancesDirectory = Path.Combine(LauncherRoot, Config.InstancesDirectoryName);
        LogsDirectory = Path.Combine(DataDirectory, Config.LogsDirectoryName);
        AccountsFile = Path.Combine(DataDirectory, Config.AccountsFileName);
        OfflineAccountsFile = Path.Combine(DataDirectory, Config.OfflineAccountsFileName);
        ConfigFile = Path.Combine(DataDirectory, Config.ConfigFileName);
    }

    private static string ResolveDefaultLauncherRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        try
        {
            // 探测 exe 同级目录是否可写：可写则保持便携布局（data/.minecraft 在 exe 旁）
            Directory.CreateDirectory(baseDir);
            var probe = Path.Combine(baseDir, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return baseDir;
        }
        catch
        {
            // Program Files 等只读目录：数据改放到 LocalAppData，避免启动即崩溃
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Config.AppName);
        }
    }

    public LauncherSettings Settings { get; private set; } = new();
    public string LauncherRoot { get; }
    public string DataDirectory { get; }
    public string DefaultGameDirectory { get; }
    public string InstancesDirectory { get; }
    public string LogsDirectory { get; }
    public string AccountsFile { get; }
    public string OfflineAccountsFile { get; }
    public string ConfigFile { get; }

    public async Task LoadAsync()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(InstancesDirectory);
        Directory.CreateDirectory(LogsDirectory);
        if (File.Exists(ConfigFile))
        {
            try
            {
                await using var stream = File.OpenRead(ConfigFile);
                Settings = await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions) ?? new LauncherSettings();
            }
            catch (JsonException)
            {
                Settings = new LauncherSettings();
            }
        }
        else
        {
            Settings = new LauncherSettings();
        }

        // 登录信息以代码内硬编码值为准，旧配置中的内置默认值/空值自动迁移。
        var migrated = false;
        if (string.IsNullOrWhiteSpace(Settings.MicrosoftClientId) ||
            Settings.MicrosoftClientId == Config.LegacyMicrosoftClientId)
        {
            Settings.MicrosoftClientId = Config.MicrosoftClientId;
            migrated = true;
        }
        if (string.IsNullOrWhiteSpace(Settings.MicrosoftRedirectUri))
        {
            Settings.MicrosoftRedirectUri = Config.MicrosoftRedirectUri;
            migrated = true;
        }
        if (migrated)
            await SaveAsync();

        Directory.CreateDirectory(ResolveGameDirectory());
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(DataDirectory);
        await using var stream = File.Create(ConfigFile);
        await JsonSerializer.SerializeAsync(stream, Settings, JsonOptions);
    }

    public string ResolveGameDirectory()
    {
        if (string.IsNullOrWhiteSpace(Settings.GameDirectory))
            return DefaultGameDirectory;
        return Path.IsPathRooted(Settings.GameDirectory)
            ? Settings.GameDirectory
            : Path.GetFullPath(Path.Combine(LauncherRoot, Settings.GameDirectory));
    }
}
