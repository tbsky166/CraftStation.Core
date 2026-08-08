using System.Net.Http.Headers;
using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public sealed class SkinService : ISkinService
{
    private readonly IAccountService _accounts;

    public SkinService(IAccountService accounts)
    {
        _accounts = accounts;
    }

    public async Task UploadSkinAsync(AccountEntry account, string pngPath, bool slim = false, CancellationToken ct = default)
    {
        if (!File.Exists(pngPath))
            throw new FileNotFoundException("皮肤文件不存在", pngPath);
        var session = await _accounts.GetLaunchSessionAsync(account, ct);
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(slim ? "slim" : "classic"), "variant");
        var bytes = await File.ReadAllBytesAsync(pngPath, ct);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(fileContent, "file", Path.GetFileName(pngPath));
        using var response = await http.PostAsync(
            Config.MinecraftSkinUploadUrl, form, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DownloadSkinAsync(AccountEntry account, string savePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(account.SkinUrl))
            throw new InvalidOperationException("该账户没有可下载的皮肤。");
        using var http = new HttpClient();
        var bytes = await http.GetByteArrayAsync(account.SkinUrl, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        await File.WriteAllBytesAsync(savePath, bytes, ct);
    }
}
