using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public interface IUpdateService
{
    Task<UpdateInfo?> CheckAsync(CancellationToken ct = default);
}
