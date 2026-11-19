using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Sumoos.Controller.Configuration;
namespace Sumoos.Controller.Communication;

// Narrow MQTT 3.1.1 client: clean-session CONNECT and non-retained QoS 0 PUBLISH only.
// The 20 Hz publish loop satisfies the keep-alive; no subscriptions or offline queue.
public sealed class RobotPublisher(AppOptions settings, string? username = null, string? password = null) : IDisposable
{
    private TcpClient? client;
    private readonly string session = Guid.NewGuid().ToString("N");
    private long sequence;
    public string LastPayload { get; private set; } = "";
    public bool Connected => client?.Connected == true;
    public async Task ConnectAsync(CancellationToken token)
    {
        Dispose();
        client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(settings.BrokerHost, settings.BrokerPort, token);
            using var body = new MemoryStream();
            Text(body, "MQTT"); body.WriteByte(4);
            var user = username ?? Environment.GetEnvironmentVariable("SUMOOS_MQTT_USERNAME");
            var secret = password ?? Environment.GetEnvironmentVariable("SUMOOS_MQTT_PASSWORD");
            var flags = 2; // clean session
            if (!string.IsNullOrEmpty(user)) flags |= 128;
            if (secret is not null && !string.IsNullOrEmpty(user)) flags |= 64;
            body.WriteByte((byte)flags);
            body.WriteByte(0); body.WriteByte(10); // 10 second keep-alive
            Text(body, $"sumoos-{session}");
            if ((flags & 128) != 0) Text(body, user!);
            if ((flags & 64) != 0) Text(body, secret!);
            await PacketAsync(0x10, body.ToArray(), token);
            var reply = new byte[4];
            await client.GetStream().ReadExactlyAsync(reply, token);
            if (reply[0] != 0x20 || reply[1] != 2 || reply[2] != 0 || reply[3] != 0)
                throw new IOException("Broker refused MQTT connection.");
        }
        catch { Dispose(); throw; }
    }
    public async Task SendAsync(bool armed, int left, int right, CancellationToken token)
    {
        var payload = JsonSerializer.Serialize(new {
            version = 1, robotId = settings.RobotId, sessionId = session, sequence = ++sequence,
            sentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ttlMs = 250,
            command = armed ? "drive" : "stop", left, right
        });
        LastPayload = payload;
        using var body = new MemoryStream();
        Text(body, $"sumoos/{settings.RobotId}/command");
        body.Write(Encoding.UTF8.GetBytes(payload));
        try { await PacketAsync(0x30, body.ToArray(), token); }
        catch { Dispose(); throw; }
    }
    private async Task PacketAsync(byte header, byte[] body, CancellationToken token)
    {
        if (client is null) throw new IOException("Not connected.");
        using var packet = new MemoryStream();
        packet.WriteByte(header);
        var remaining = body.Length;
        do { var digit = remaining % 128; remaining /= 128; if (remaining > 0) digit |= 128; packet.WriteByte((byte)digit); } while (remaining > 0);
        packet.Write(body);
        await client.GetStream().WriteAsync(packet.ToArray(), token);
    }
    private static void Text(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue) throw new ArgumentException("MQTT string too long.");
        stream.WriteByte((byte)(bytes.Length >> 8)); stream.WriteByte((byte)bytes.Length); stream.Write(bytes);
    }
    public void Dispose() { client?.Dispose(); client = null; }
}
