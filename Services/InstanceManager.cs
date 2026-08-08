using System.Text.Json;
using CraftStation.Core.Models;
using CraftStation.Core.Utils;

namespace CraftStation.Core.Services;

public sealed class InstanceManager : IInstanceManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ISettingsService _settings;
    private readonly List<Instance> _instances = new();
    private string IndexFile => Path.Combine(_settings.DataDirectory, Config.InstancesIndexFileName);

    public InstanceManager(ISettingsService settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<Instance> Instances => _instances;
    public Instance? Current => _instances.FirstOrDefault(i => i.Id == _settings.Settings.CurrentInstanceId) ?? _instances.FirstOrDefault();

    public async Task LoadAsync()
    {
        _instances.Clear();
        if (File.Exists(IndexFile))
        {
            try
            {
                await using var stream = File.OpenRead(IndexFile);
                var list = await JsonSerializer.DeserializeAsync<List<Instance>>(stream, JsonOptions);
                if (list != null)
                    _instances.AddRange(list);
            }
            catch (JsonException)
            {
                _instances.Clear();
            }
        }
        if (_instances.Count == 0)
        {
            var first = new Instance
            {
                Name = Config.DefaultInstanceName,
                VersionId = Config.DefaultInstanceVersion,
                Description = $"{Config.AppName} 默认实例"
            };
            _instances.Add(first);
            await SaveAsync();
            GameOptionsHelper.EnsureChineseLanguage(GetGameDirectory(first));
        }
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(_settings.DataDirectory);
        await using var stream = File.Create(IndexFile);
        await JsonSerializer.SerializeAsync(stream, _instances, JsonOptions);
        await _settings.SaveAsync();
    }

    public async Task<Instance> CreateAsync(string name, string versionId, LoaderKind loader = LoaderKind.Vanilla)
    {
        var instance = new Instance
        {
            Name = string.IsNullOrWhiteSpace(name) ? versionId : name,
            VersionId = versionId,
            Loader = loader
        };
        _instances.Add(instance);
        _settings.Settings.CurrentInstanceId = instance.Id;
        await SaveAsync();
        GameOptionsHelper.EnsureChineseLanguage(GetGameDirectory(instance));
        return instance;
    }

    public async Task DeleteAsync(string id)
    {
        var instance = _instances.FirstOrDefault(i => i.Id == id);
        if (instance == null)
            return;
        _instances.Remove(instance);
        if (_settings.Settings.CurrentInstanceId == id)
            _settings.Settings.CurrentInstanceId = _instances.FirstOrDefault()?.Id;
        await SaveAsync();
    }

    public async Task UpdateAsync(Instance instance)
    {
        var index = _instances.FindIndex(i => i.Id == instance.Id);
        if (index >= 0)
            _instances[index] = instance;
        await SaveAsync();
    }

    public Task SetCurrentAsync(string id)
    {
        _settings.Settings.CurrentInstanceId = id;
        return SaveAsync();
    }

    public string GetGameDirectory(Instance instance)
    {
        var baseDir = _settings.ResolveGameDirectory();
        return instance.VersionIsolation
            ? Path.Combine(baseDir, Config.MinecraftVersionsDirectoryName, instance.VersionId)
            : baseDir;
    }
}
