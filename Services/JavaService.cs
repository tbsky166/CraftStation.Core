using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CraftStation.Core.Services;

public sealed class JavaService : IJavaService
{
    private readonly ISettingsService _settings;
    private IReadOnlyList<JavaInfo>? _cache;
    private DateTime _cacheStampUtc;

    public JavaService(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<JavaInfo>> ScanInstalledJavaAsync(
        bool refresh = false,
        CancellationToken ct = default)
    {
        if (!refresh &&
            _cache != null &&
            DateTime.UtcNow - _cacheStampUtc < TimeSpan.FromSeconds(Config.JavaScanCacheSeconds))
        {
            return _cache;
        }

        var candidates = new List<string>();
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        var jdkHome = Environment.GetEnvironmentVariable("JDK_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            candidates.Add(Path.Combine(javaHome, "bin", Config.JavaExecutableName));
        if (!string.IsNullOrWhiteSpace(jdkHome))
            candidates.Add(Path.Combine(jdkHome, "bin", Config.JavaExecutableName));

        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("ProgramW6432") ?? "",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks")
        };
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                continue;
            foreach (var vendor in Config.JavaVendorDirectoryNames)
            {
                var vendorDir = Path.Combine(root, vendor);
                if (!Directory.Exists(vendorDir))
                    continue;
                foreach (var jdk in Directory.EnumerateDirectories(vendorDir))
                {
                    var exe = Path.Combine(jdk, "bin", Config.JavaExecutableName);
                    if (File.Exists(exe))
                        candidates.Add(exe);
                }
            }
        }

        // JetBrains Toolbox 自带的 JBR
        var toolboxApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JetBrains", "Toolbox", "apps");
        if (Directory.Exists(toolboxApps))
        {
            foreach (var ide in Directory.EnumerateDirectories(toolboxApps))
            {
                foreach (var build in Directory.EnumerateDirectories(ide))
                {
                    foreach (var binDir in new[] { "jbr", "jre" })
                    {
                        var exe = Path.Combine(build, binDir, "bin", Config.JavaExecutableName);
                        if (File.Exists(exe))
                            candidates.Add(exe);
                    }
                }
            }
        }

        // Minecraft 官方运行时（.minecraft/runtime/java-runtime-*）
        foreach (var gameDir in new[] { _settings.ResolveGameDirectory(), _settings.DefaultGameDirectory })
        {
            var runtimeRoot = Path.Combine(gameDir, Config.MinecraftRuntimeDirectoryName);
            if (!Directory.Exists(runtimeRoot))
                continue;
            foreach (var runtime in Directory.EnumerateDirectories(runtimeRoot))
            {
                var exe = Path.Combine(runtime, "bin", Config.JavaExecutableName);
                if (File.Exists(exe))
                    candidates.Add(exe);
            }
        }

        // Oracle 公共路径（javapath 快捷入口）
        var oraclePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            "Oracle", "Java", "javapath", Config.JavaExecutableName);
        if (File.Exists(oraclePath))
            candidates.Add(oraclePath);

        // 名字引导扫盘：只进入名称含 java 的目录（0 损伤、只读、限深度/次数）
        await Task.Run(() => AddNameGuidedCandidates(candidates), ct);

        var result = new List<JavaInfo>();
        await Parallel.ForEachAsync(
            candidates.Distinct(StringComparer.OrdinalIgnoreCase),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Config.JavaScanMaxDegreeOfParallelism,
                CancellationToken = ct
            },
            async (path, token) =>
            {
                var info = await ReadJavaInfoAsync(path, token);
                if (info != null)
                {
                    lock (result)
                        result.Add(info);
                }
            });

        _cache = result
            .OrderBy(j => j.MajorVersion)
            .ThenBy(j => j.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _cacheStampUtc = DateTime.UtcNow;
        return _cache;
    }

    private static void AddNameGuidedCandidates(List<string> candidates)
    {
        var visits = 0;
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                continue;
            WalkJavaFolders(drive.RootDirectory.FullName, candidates, 0, ref visits);
            if (visits >= Config.JavaScanMaxDirectoryVisits)
                break;
        }
    }

    private static void WalkJavaFolders(
        string dir,
        List<string> candidates,
        int depth,
        ref int visits)
    {
        if (depth > Config.JavaScanMaxDepth || ++visits > Config.JavaScanMaxDirectoryVisits)
            return;

        string[] children;
        try
        {
            children = Directory.GetDirectories(dir);
        }
        catch
        {
            return; // 无权限等目录直接跳过，保证只读不报错
        }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);
            if (Config.JavaScanPrunedDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;

            var direct = Path.Combine(child, Config.JavaExecutableName);
            if (File.Exists(direct))
                candidates.Add(direct);
            var bin = Path.Combine(child, "bin", Config.JavaExecutableName);
            if (File.Exists(bin))
                candidates.Add(bin);

            var containsJava =
                name.Contains("java", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("jdk", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("jre", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("jbr", StringComparison.OrdinalIgnoreCase);
            var isStructural = Config.JavaScanStructuralDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase);
            if (containsJava || isStructural)
                WalkJavaFolders(child, candidates, depth + 1, ref visits);
        }
    }

    public async Task<string?> FindRecommendedJavaAsync(int requiredMajorVersion, CancellationToken ct = default)
    {
        var installed = await ScanInstalledJavaAsync(ct: ct);
        return installed
            .Where(j => j.MajorVersion == requiredMajorVersion)
            .OrderByDescending(j => j.Version)
            .FirstOrDefault()?.Path;
    }

    private static async Task<JavaInfo?> ReadJavaInfoAsync(string javaExe, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(javaExe, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null)
                return null;

            var errTask = process.StandardError.ReadToEndAsync(ct);
            var outTask = process.StandardOutput.ReadToEndAsync(ct);
            try
            {
                await process.WaitForExitAsync(ct)
                    .WaitAsync(TimeSpan.FromSeconds(Config.JavaScanTimeoutSeconds), ct);
            }
            catch (TimeoutException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
                return null;
            }

            string output;
            try
            {
                output = await errTask + await outTask;
            }
            catch
            {
                return null;
            }

            var match = Regex.Match(output, @"version\s+\""([^\""]+)\""", RegexOptions.IgnoreCase);
            var vendor = output.Split('\n').FirstOrDefault()?.Trim() ?? "";
            var version = match.Success ? match.Groups[1].Value : "unknown";
            var major = 0;
            var digits = Regex.Match(version, @"^(\d+)");
            if (digits.Success)
                major = int.Parse(digits.Groups[1].Value);
            else if (version.StartsWith("1.", StringComparison.Ordinal))
            {
                var second = Regex.Match(version, @"^1\.(\d+)");
                if (second.Success)
                    major = int.Parse(second.Groups[1].Value);
            }
            return new JavaInfo
            {
                Path = javaExe,
                Version = version,
                Vendor = vendor,
                MajorVersion = major
            };
        }
        catch
        {
            return null;
        }
    }
}
