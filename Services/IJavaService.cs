namespace CraftStation.Core.Services;

public sealed class JavaInfo
{
    public string Path { get; set; } = "";
    public string Version { get; set; } = "";
    public string Vendor { get; set; } = "";
    public int MajorVersion { get; set; }
}

public interface IJavaService
{
    Task<IReadOnlyList<JavaInfo>> ScanInstalledJavaAsync(bool refresh = false, CancellationToken ct = default);
    Task<string?> FindRecommendedJavaAsync(int requiredMajorVersion, CancellationToken ct = default);
}
