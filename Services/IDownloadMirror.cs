using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public interface IDownloadMirror
{
    string VersionManifestUrl { get; }
    Uri? Rewrite(Uri uri);
    Uri? RewriteToOfficial(Uri uri);
    HttpClient CreateHttpClient();
}
