using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public interface ISkinService
{
    Task UploadSkinAsync(AccountEntry account, string pngPath, bool slim = false, CancellationToken ct = default);
    Task DownloadSkinAsync(AccountEntry account, string savePath, CancellationToken ct = default);
}
