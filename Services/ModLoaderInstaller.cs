using CmlLib.Core;
using CmlLib.Core.Installers;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ModLoaders.LiteLoader;
using CmlLib.Core.ModLoaders.QuiltMC;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;
using CraftStation.Core.Models;
using Optifine.Installer;

namespace CraftStation.Core.Services;

public sealed class ModLoaderInstaller : IModLoaderInstaller
{
    private readonly ILauncherService _launcherService;
    private readonly IDownloadMirror _mirror;
    private readonly IInstanceManager _instances;

    public ModLoaderInstaller(ILauncherService launcherService, IDownloadMirror mirror, IInstanceManager instances)
    {
        _launcherService = launcherService;
        _mirror = mirror;
        _instances = instances;
    }

    public async Task<IReadOnlyList<string>> GetVersionsAsync(string mcVersion, LoaderKind loader, CancellationToken ct = default)
    {
        var http = _mirror.CreateHttpClient();
        switch (loader)
        {
            case LoaderKind.Fabric:
            {
                var installer = new FabricInstaller(http);
                var versions = await installer.GetLoaders(mcVersion);
                return versions.Select(v => v.Version ?? "").ToList();
            }
            case LoaderKind.Quilt:
            {
                var installer = new QuiltInstaller(http);
                var versions = await installer.GetLoaders(mcVersion);
                return versions.Select(v => v.Version ?? "").ToList();
            }
            case LoaderKind.Forge:
            {
                var installer = new ForgeInstaller(_launcherService.Launcher, http);
                var versions = await installer.GetForgeVersions(mcVersion);
                return versions.Select(v => v.ForgeVersionName).ToList();
            }
            case LoaderKind.NeoForge:
            {
                var installer = new NeoForgeInstaller(_launcherService.Launcher);
                var versions = await installer.GetForgeVersions(mcVersion);
                return versions.Select(v => v.VersionName).ToList();
            }
            case LoaderKind.OptiFine:
            {
                var installer = new OptifineInstaller(http);
                var versions = await installer.GetOptifineVersionsAsync();
                return versions
                    .Where(v => v.MinecraftVersion == mcVersion)
                    .Select(v => v.Version)
                    .ToList();
            }
            case LoaderKind.LiteLoader:
            {
                var installer = new LiteLoaderInstaller(http);
                var versions = await installer.GetAllLiteLoaders();
                return versions
                    .Where(v => v.BaseVersion == mcVersion)
                    .Select(v => v.Version ?? "")
                    .Where(v => v.Length > 0)
                    .ToList();
            }
            default:
                return Array.Empty<string>();
        }
    }

    public async Task<string> InstallAsync(
        string mcVersion,
        LoaderKind loader,
        string? loaderVersion,
        IProgress<DownloadProgress>? progress = null,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        var http = _mirror.CreateHttpClient();
        var path = new MinecraftPath(_instances.GetGameDirectory(_instances.Current ?? new Instance()));
        var fileProgress = progress == null
            ? null
            : new Progress<InstallerProgressChangedEventArgs>(e => progress.Report(new DownloadProgress
            {
                CurrentFile = e.Name,
                CompletedFiles = e.ProgressedTasks,
                TotalFiles = e.TotalTasks
            }));
        var byteProgress = progress == null
            ? null
            : new Progress<ByteProgress>(e => progress.Report(new DownloadProgress
            {
                CompletedBytes = e.ProgressedBytes,
                TotalBytes = e.TotalBytes
            }));

        switch (loader)
        {
            case LoaderKind.Fabric:
            {
                var installer = new FabricInstaller(http);
                if (string.IsNullOrEmpty(loaderVersion))
                    return await installer.Install(mcVersion, path);
                return await installer.Install(mcVersion, loaderVersion, path);
            }
            case LoaderKind.Quilt:
            {
                var installer = new QuiltInstaller(http);
                if (string.IsNullOrEmpty(loaderVersion))
                    return await installer.Install(mcVersion, path);
                return await installer.Install(mcVersion, loaderVersion, path);
            }
            case LoaderKind.Forge:
            {
                var installer = new ForgeInstaller(_launcherService.Launcher, http);
                var options = new ForgeInstallOptions
                {
                    FileProgress = fileProgress,
                    ByteProgress = byteProgress,
                    InstallerOutput = log,
                    CancellationToken = ct
                };
                if (string.IsNullOrEmpty(loaderVersion))
                    return await installer.Install(mcVersion, options);
                return await installer.Install(mcVersion, loaderVersion, options);
            }
            case LoaderKind.NeoForge:
            {
                var installer = new NeoForgeInstaller(_launcherService.Launcher);
                var options = new NeoForgeInstallOptions
                {
                    FileProgress = fileProgress,
                    ByteProgress = byteProgress,
                    InstallerOutput = log,
                    CancellationToken = ct
                };
                if (string.IsNullOrEmpty(loaderVersion))
                    return await installer.Install(mcVersion, options);
                return await installer.Install(mcVersion, loaderVersion, options);
            }
            case LoaderKind.OptiFine:
            {
                var installer = new OptifineInstaller(http);
                var versions = await installer.GetOptifineVersionsAsync();
                var selected = versions.FirstOrDefault(v =>
                    v.MinecraftVersion == mcVersion &&
                    (string.IsNullOrEmpty(loaderVersion) || v.Version == loaderVersion));
                if (selected == null)
                    throw new InvalidOperationException($"未找到 {mcVersion} 对应的 OptiFine 版本。");
                return await installer.InstallOptifineAsync(path.BasePath, selected);
            }
            case LoaderKind.LiteLoader:
            {
                var installer = new LiteLoaderInstaller(http);
                var versions = await installer.GetAllLiteLoaders();
                var selected = versions.FirstOrDefault(v =>
                    v.BaseVersion == mcVersion &&
                    (string.IsNullOrEmpty(loaderVersion) || v.Version == loaderVersion));
                if (selected == null)
                    throw new InvalidOperationException($"未找到 {mcVersion} 对应的 LiteLoader 版本。");
                var baseVersion = await _launcherService.Launcher.GetVersionAsync(mcVersion, ct);
                return await installer.Install(selected, baseVersion, path);
            }
            default:
                throw new NotSupportedException("不支持的加载器类型。");
        }
    }
}
