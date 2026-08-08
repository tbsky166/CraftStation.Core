using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public interface ILogService
{
    string CreateGameLogPath(Instance instance);
    Task AppendAsync(Instance instance, string line);
    Task<IReadOnlyList<string>> ReadLatestAsync(Instance instance, int maxLines = 500);
    void OpenLogsFolder();
}
