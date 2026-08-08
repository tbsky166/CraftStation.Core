namespace CraftStation.Core.Models;

public sealed class DownloadProgress
{
    public string? CurrentFile { get; set; }
    public int CompletedFiles { get; set; }
    public int TotalFiles { get; set; }
    public long CompletedBytes { get; set; }
    public long TotalBytes { get; set; }

    public double Percent =>
        TotalFiles == 0 ? 0 : Math.Clamp(CompletedFiles * 100d / TotalFiles, 0, 100);
}
