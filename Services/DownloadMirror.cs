using System.Net;
using System.Net.Http.Headers;
using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public sealed class DownloadMirror : IDownloadMirror
{
    private readonly LauncherSettings _settings;
    private readonly string _primaryBase;

    public DownloadMirror(LauncherSettings settings)
    {
        _settings = settings;
        _primaryBase = settings.DownloadSource switch
        {
            DownloadSourceKind.Bmclapi => Config.BmclapiBase,
            DownloadSourceKind.Custom => settings.CustomDownloadSource.TrimEnd('/'),
            _ => ""
        };
    }

    public string VersionManifestUrl =>
        _settings.DownloadSource == DownloadSourceKind.Mojang
            ? Config.MojangVersionManifestV2
            : _primaryBase + Config.MojangVersionManifestV2Path;

    public Uri? Rewrite(Uri uri)
    {
        if (_settings.DownloadSource == DownloadSourceKind.Mojang)
            return null;
        return _primaryBase == Config.BmclapiBase ? ToBmclapi(uri) : ToCustom(uri);
    }

    public Uri? RewriteToOfficial(Uri uri)
    {
        if (uri.Host != "bmclapi2.bangbang93.com" &&
            !uri.Host.Equals(new Uri(_primaryBase).Host, StringComparison.OrdinalIgnoreCase))
            return null;

        var path = uri.AbsolutePath;
        if (path == Config.MojangVersionManifestV2Path)
            return new Uri(Config.MojangVersionManifestV2);
        if (path == Config.MojangVersionManifestPath)
            return new Uri(Config.MojangVersionManifest);
        if (path.StartsWith(Config.MojangLibrariesPathPrefix, StringComparison.OrdinalIgnoreCase))
            return new Uri(Config.MojangLibrariesBase + "/" + path[Config.MojangLibrariesPathPrefix.Length..]);
        if (path.StartsWith(Config.MojangAssetsPathPrefix, StringComparison.OrdinalIgnoreCase))
            return new Uri(Config.MojangResourcesBase + "/" + path[Config.MojangAssetsPathPrefix.Length..]);
        return null;
    }

    public HttpClient CreateHttpClient()
    {
        var inner = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(Config.PooledConnectionLifetimeMinutes),
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = Math.Max(Config.MinConnectionsPerServer, _settings.MaxConcurrency)
        };
        if (!string.IsNullOrWhiteSpace(_settings.Proxy) && Uri.TryCreate(_settings.Proxy, UriKind.Absolute, out var proxyUri))
        {
            inner.Proxy = new WebProxy(proxyUri);
            inner.UseProxy = true;
        }

        HttpMessageHandler handler = inner;
        if (_settings.DownloadSource != DownloadSourceKind.Mojang && _settings.FallbackToOfficial)
        {
            handler = new MirrorFallbackHandler(inner, Rewrite, RewriteToOfficial);
        }
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Config.HttpClientTimeoutSeconds)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Config.UserAgent);
        return client;
    }

    private static Uri? ToBmclapi(Uri uri)
    {
        if (uri.Host == Config.MojangVersionManifestHost)
        {
            if (uri.AbsolutePath == Config.MojangVersionManifestV2Path)
                return new Uri(Config.BmclapiBase + Config.MojangVersionManifestV2Path);
            if (uri.AbsolutePath == Config.MojangVersionManifestPath)
                return new Uri(Config.BmclapiBase + Config.MojangVersionManifestPath);
            return null;
        }
        if (uri.Host == Config.MojangLibrariesHost)
            return new Uri($"{Config.BmclapiBase}/libraries/{uri.PathAndQuery.TrimStart('/')}");
        if (uri.Host == Config.MojangResourcesHost)
            return new Uri($"{Config.BmclapiBase}/assets/{uri.PathAndQuery.TrimStart('/')}");
        return null;
    }

    private Uri? ToCustom(Uri uri)
    {
        if (string.IsNullOrEmpty(_primaryBase))
            return null;
        if (uri.Host == Config.MojangVersionManifestHost)
        {
            if (uri.AbsolutePath == Config.MojangVersionManifestV2Path)
                return new Uri(_primaryBase + Config.MojangVersionManifestV2Path);
            if (uri.AbsolutePath == Config.MojangVersionManifestPath)
                return new Uri(_primaryBase + Config.MojangVersionManifestPath);
            return null;
        }
        if (uri.Host == Config.MojangLibrariesHost)
            return new Uri($"{_primaryBase}/libraries/{uri.PathAndQuery.TrimStart('/')}");
        if (uri.Host == Config.MojangResourcesHost)
            return new Uri($"{_primaryBase}/assets/{uri.PathAndQuery.TrimStart('/')}");
        return null;
    }

    private sealed class MirrorFallbackHandler : DelegatingHandler
    {
        private readonly Func<Uri, Uri?> _toPrimary;
        private readonly Func<Uri, Uri?> _toOfficial;

        public MirrorFallbackHandler(
            HttpMessageHandler inner,
            Func<Uri, Uri?> toPrimary,
            Func<Uri, Uri?> toOfficial)
            : base(inner)
        {
            _toPrimary = toPrimary;
            _toOfficial = toOfficial;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var original = request.RequestUri!;
            var primary = _toPrimary(original) ?? original;
            try
            {
                if (primary != original)
                    request.RequestUri = primary;
                var response = await base.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (HttpRequestException) when (primary != original)
            {
                request.RequestUri = original;
                return await base.SendAsync(request, cancellationToken);
            }
            catch (TaskCanceledException) when (primary != original && !cancellationToken.IsCancellationRequested)
            {
                request.RequestUri = original;
                return await base.SendAsync(request, cancellationToken);
            }
        }
    }
}
