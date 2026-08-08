using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CraftStation.Core.Models;

namespace CraftStation.Core.Services;

public sealed class ServerService : IServerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private readonly ISettingsService _settings;
    private readonly List<ServerEntry> _servers = new();
    private string FilePath => Path.Combine(_settings.DataDirectory, Config.ServersFileName);

    public ServerService(ISettingsService settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<ServerEntry> Servers => _servers;

    public async Task LoadAsync()
    {
        _servers.Clear();
        if (File.Exists(FilePath))
        {
            try
            {
                await using var stream = File.OpenRead(FilePath);
                var list = await JsonSerializer.DeserializeAsync<List<ServerEntry>>(stream, JsonOptions);
                if (list != null)
                    _servers.AddRange(list);
            }
            catch (JsonException)
            {
                _servers.Clear();
            }
        }
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(_settings.DataDirectory);
        await using var stream = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(stream, _servers, JsonOptions);
    }

    public async Task<ServerEntry> AddAsync(string name, string address, int port = Config.DefaultServerPort, string? notes = null)
    {
        var server = new ServerEntry
        {
            Name = name,
            Address = address,
            Port = port,
            Notes = notes
        };
        _servers.Add(server);
        await SaveAsync();
        return server;
    }

    public async Task UpdateAsync(ServerEntry server)
    {
        var index = _servers.FindIndex(s => s.Id == server.Id);
        if (index >= 0)
            _servers[index] = server;
        await SaveAsync();
    }

    public async Task DeleteAsync(string id)
    {
        _servers.RemoveAll(s => s.Id == id);
        await SaveAsync();
    }

    public async Task<ServerStatus> PingAsync(ServerEntry server, CancellationToken ct = default)
    {
        try
        {
            using var tcp = new TcpClient();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await tcp.ConnectAsync(server.Address, server.Port, ct);
            var stream = tcp.GetStream();
            stream.ReadTimeout = Config.ServerPingTimeoutMs;
            stream.WriteTimeout = Config.ServerPingTimeoutMs;
            var buffer = new List<byte>();
            WriteVarInt(buffer, -1);
            WriteString(buffer, server.Address);
            buffer.Add((byte)(server.Port >> 8));
            buffer.Add((byte)(server.Port & 0xFF));
            WriteVarInt(buffer, 1);
            var handshake = Packet(buffer);
            await stream.WriteAsync(handshake, ct);

            var request = Packet(new byte[] { 0x00 });
            await stream.WriteAsync(request, ct);
            var responseLength = await ReadVarIntAsync(stream, ct);
            if (responseLength <= 0)
                return new ServerStatus { Online = false, Error = "服务器返回了空响应。" };
            var response = new byte[responseLength];
            var read = 0;
            while (read < responseLength)
            {
                var n = await stream.ReadAsync(response.AsMemory(read, responseLength - read), ct);
                if (n == 0)
                    break;
                read += n;
            }
            stopwatch.Stop();
            var packetId = ReadVarInt(response, ref read);
            if (packetId != 0)
                return new ServerStatus { Online = false, Error = "协议响应异常。" };
            var jsonLength = ReadVarInt(response, ref read);
            var json = Encoding.UTF8.GetString(response, read, Math.Min(jsonLength, response.Length - read));
            return ParseStatus(json, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return new ServerStatus { Online = false, Error = ex.Message };
        }
    }

    private static ServerStatus ParseStatus(string json, long latencyMs)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var status = new ServerStatus
        {
            Online = true,
            LatencyMs = latencyMs,
            Motd = GetMotd(root.GetProperty("description")),
            Version = root.TryGetProperty("version", out var version) && version.TryGetProperty("name", out var name)
                ? name.GetString()
                : null,
            IconBase64 = root.TryGetProperty("favicon", out var favicon) ? favicon.GetString() : null
        };
        if (root.TryGetProperty("players", out var players))
        {
            if (players.TryGetProperty("online", out var online))
                status.PlayersOnline = online.GetInt32();
            if (players.TryGetProperty("max", out var max))
                status.PlayersMax = max.GetInt32();
        }
        return status;
    }

    private static string? GetMotd(JsonElement description)
    {
        if (description.ValueKind == JsonValueKind.String)
            return description.GetString();
        if (description.TryGetProperty("text", out var text))
            return text.GetString();
        if (description.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in extra.EnumerateArray())
                sb.Append(GetMotd(item));
            return sb.ToString();
        }
        return description.ToString();
    }

    private static byte[] Packet(IEnumerable<byte> payload)
    {
        var data = payload.ToArray();
        var result = new List<byte>();
        WriteVarInt(result, data.Length);
        result.AddRange(data);
        return result.ToArray();
    }

    private static void WriteString(List<byte> buffer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarInt(buffer, bytes.Length);
        buffer.AddRange(bytes);
    }

    private static void WriteVarInt(List<byte> buffer, int value)
    {
        var v = (uint)value;
        while ((v & ~0x7F) != 0)
        {
            buffer.Add((byte)((v & 0x7F) | 0x80));
            v >>= 7;
        }
        buffer.Add((byte)v);
    }

    private static int ReadVarInt(byte[] buffer, ref int offset)
    {
        var result = 0;
        var shift = 0;
        while (offset < buffer.Length)
        {
            var b = buffer[offset++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
            if (shift > 35)
                throw new InvalidDataException("VarInt 过长");
        }
        throw new InvalidDataException("数据不足");
    }

    private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken ct)
    {
        var result = 0;
        var shift = 0;
        while (true)
        {
            var b = new byte[1];
            var n = await stream.ReadAsync(b, ct);
            if (n == 0)
                throw new IOException("连接被关闭");
            result |= (b[0] & 0x7F) << shift;
            if ((b[0] & 0x80) == 0)
                return result;
            shift += 7;
            if (shift > 35)
                throw new InvalidDataException("VarInt 过长");
        }
    }
}
