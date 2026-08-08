using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public interface ISettingsService
{
    LauncherSettings Settings { get; }
    string LauncherRoot { get; }
    string DataDirectory { get; }
    string DefaultGameDirectory { get; }
    string InstancesDirectory { get; }
    string LogsDirectory { get; }
    string AccountsFile { get; }
    string OfflineAccountsFile { get; }
    string ConfigFile { get; }
    string ResolveGameDirectory();
    Task LoadAsync();
    Task SaveAsync();
}
