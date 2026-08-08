using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public interface IServerService
{
    IReadOnlyList<ServerEntry> Servers { get; }
    Task LoadAsync();
    Task SaveAsync();
    Task<ServerEntry> AddAsync(string name, string address, int port = Config.DefaultServerPort, string? notes = null);
    Task UpdateAsync(ServerEntry server);
    Task DeleteAsync(string id);
    Task<ServerStatus> PingAsync(ServerEntry server, CancellationToken ct = default);
}
