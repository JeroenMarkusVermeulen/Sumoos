using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Sumoos.Controller.Configuration;
namespace Sumoos.Controller.Communication;

// MQTT 3.1.1 clean session with non-retained QoS 0 commands and optional feedback.
public sealed class RobotPublisher(AppOptions settings, string? username = null, string? password = null,
    bool receiveStatus = false, bool simulated = true) : IDisposable
{
    private TcpClient? client;
    private Task? reader;
    private RobotFeedbackTracker? feedback;
    private readonly string session = Guid.NewGuid().ToString("N");
    private long sequence;
    public string LastPayload { get; private set; } = "";
    public bool Connected => client?.Connected == true && reader is { IsCompleted: false };
    public FeedbackSnapshot Feedback(long now) => feedback?.Read(now) ?? new(FeedbackState.Waiting);
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
            if (reply[0] != 0x20 || reply[1] != 2 || reply[2] != 0)
                throw new IOException("Invalid MQTT connection response.");
            if (reply[3] is 4 or 5)
                throw new BrokerCredentialsException();
            if (reply[3] != 0)
                throw new IOException($"The MQTT broker refused the connection (CONNACK {reply[3]}).");
            var stream = client.GetStream();
            if (receiveStatus) {
                using var subscribe = new MemoryStream();
                subscribe.WriteByte(0); subscribe.WriteByte(1);
                Text(subscribe, $"sumoos/{settings.RobotId}/status"); subscribe.WriteByte(0);
                await PacketAsync(0x82, subscribe.ToArray(), token);
                var subAck = new byte[5];
                await stream.ReadExactlyAsync(subAck, token);
                if (!subAck.AsSpan().SequenceEqual(new byte[] { 0x90, 3, 0, 1, 0 }))
                    throw new IOException("MQTT status subscription rejected.");
                feedback = new(settings.RobotId, simulated, session, Interlocked.Read(ref sequence) + 1);
            }
            reader = ReadStatusAsync(stream, feedback);
        }
        catch { Dispose(); throw; }
    }
    private async Task ReadStatusAsync(NetworkStream stream, RobotFeedbackTracker? tracker)
    {
        try {
            var one = new byte[1];
            while (true) {
                await stream.ReadExactlyAsync(one); var header = one[0];
                int length = 0, multiplier = 1;
                for (int i=0;;i++) {
                    if (i == 4) throw new IOException("Invalid MQTT length.");
                    await stream.ReadExactlyAsync(one);
                    length += (one[0] & 127) * multiplier; multiplier *= 128;
                    if (length > 16384) throw new IOException("MQTT packet too large.");
                    if ((one[0] & 128)==0) break;
                }
                var body = new byte[length]; await stream.ReadExactlyAsync(body);
                if (tracker is null || header != 0x30 || length < 2) continue;
                var n = body[0]*256 + body[1];
                if (n > length-2 || Encoding.UTF8.GetString(body,2,n) != $"sumoos/{settings.RobotId}/status") continue;
                var payload = Encoding.UTF8.GetString(body,2+n,length-2-n);
                tracker.Receive(payload, Environment.TickCount64, Interlocked.Read(ref sequence));
            }
        } catch (IOException) { stream.Dispose(); }
        catch (ObjectDisposedException) { }
    }
    public async Task SendAsync(bool armed, int left, int right, CancellationToken token)
    {
        var payload = JsonSerializer.Serialize(new {
            version = 1, robotId = settings.RobotId, sessionId = session, sequence = Interlocked.Increment(ref sequence),
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
    public void Dispose()
    {
        client?.Dispose();
        client = null;
        reader = null;
        feedback = null;
    }
}

public sealed class BrokerCredentialsException() : IOException("MQTT broker rejected the credentials.");
