namespace CraftStation.Core.Models;

public sealed class ServerEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public int Port { get; set; } = Config.DefaultServerPort;
    public string? Notes { get; set; }
    public string? IconBase64 { get; set; }
    public DateTime? LastPingUtc { get; set; }
}

public sealed class ServerStatus
{
    public bool Online { get; set; }
    public string? Motd { get; set; }
    public string? Version { get; set; }
    public int PlayersOnline { get; set; }
    public int PlayersMax { get; set; }
    public long LatencyMs { get; set; }
    public string? IconBase64 { get; set; }
    public string? Error { get; set; }
}
