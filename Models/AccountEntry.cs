namespace CraftStation.Core.Models;

public enum AccountKind
{
    Offline,
    Microsoft
}

public sealed class AccountEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AccountKind Kind { get; set; }
    public string UserName { get; set; } = "";
    public string? Uuid { get; set; }
    public string? AccessToken { get; set; }
    public string? SkinUrl { get; set; }
    public string? CapeUrl { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

    public string DisplayName => UserName;
    public string KindLabel => Kind == AccountKind.Microsoft ? "微软账户" : "离线账户";
}
