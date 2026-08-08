using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CraftStation.Core.Services;

public sealed class JavaService : IJavaService
{
    public async Task<IReadOnlyList<JavaInfo>> ScanInstalledJavaAsync(CancellationToken ct = default)
    {
        var candidates = new List<string>();
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            candidates.Add(Path.Combine(javaHome, "bin", "java.exe"));

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        foreach (var root in roots)
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
                    var exe = Path.Combine(jdk, "bin", "java.exe");
                    if (File.Exists(exe))
                        candidates.Add(exe);
                }
            }
        }

        var result = new List<JavaInfo>();
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var info = await ReadJavaInfoAsync(path, ct);
            if (info != null)
                result.Add(info);
        }
        return result;
    }

    public async Task<string?> FindRecommendedJavaAsync(int requiredMajorVersion, CancellationToken ct = default)
    {
        var installed = await ScanInstalledJavaAsync(ct);
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
            var output = await process.StandardError.ReadToEndAsync(ct) + await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
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
