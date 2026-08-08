using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public interface IInstanceManager
{
    IReadOnlyList<Instance> Instances { get; }
    Instance? Current { get; }
    Task LoadAsync();
    Task SaveAsync();
    Task<Instance> CreateAsync(string name, string versionId, LoaderKind loader = LoaderKind.Vanilla);
    Task DeleteAsync(string id);
    Task UpdateAsync(Instance instance);
    Task SetCurrentAsync(string id);
    string GetGameDirectory(Instance instance);
}
