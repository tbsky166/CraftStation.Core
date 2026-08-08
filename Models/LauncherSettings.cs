namespace CraftStation.Core.Models;

public enum DownloadSourceKind
{
    Bmclapi,
    Mojang,
    Custom
}

public sealed class LauncherSettings
{
    public string GameDirectory { get; set; } = "";
    public DownloadSourceKind DownloadSource { get; set; } = Config.DefaultDownloadSource;
    public bool FallbackToOfficial { get; set; } = Config.DefaultFallbackToOfficial;
    public string CustomDownloadSource { get; set; } = "";
    public string MicrosoftClientId { get; set; } = Config.MicrosoftClientId;
    public string MicrosoftRedirectUri { get; set; } = Config.MicrosoftRedirectUri;
    public bool UseDeviceCodeFallback { get; set; } = Config.DefaultUseDeviceCodeFallback;
    public string Language { get; set; } = Config.DefaultUiLanguage;
    public int MaxConcurrency { get; set; } = Config.DefaultMaxConcurrency;
    public string? Proxy { get; set; }
    public string UpdateEndpoint { get; set; } = "";
    public string CurseForgeApiKey { get; set; } = "";
    public bool AnimationsEnabled { get; set; } = Config.DefaultAnimationsEnabled;
    public string? CurrentAccountId { get; set; }
    public string? CurrentInstanceId { get; set; }
}
