using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Sumoos.Controller.Communication;
using Sumoos.Controller.Configuration;
using Sumoos.Core;
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine($"PASS {name}"); checks++; }
var s = new ControlState();
Check(!s.Armed && s.Output() == (0, 0), "startup stopped");
Check(!s.Apply("ARM", 0), "arm requires neutral");
s.Apply("DRIVE,50,50", 1); Check(s.Output() == (0,0), "disarmed input cannot move");
Check(!s.Apply("ARM", 2), "moving stick cannot arm");
s.Apply("DRIVE,0,0", 3); Check(s.Apply("ARM", 4), "fresh neutral can arm");
s.Apply("DRIVE,-100,75", 5); Check(s.Output() == (-100,75), "signed motor commands");
s.Tick(254); Check(s.Armed, "no early timeout");
s.Tick(255); Check(!s.Armed && s.Output() == (0,0), "250ms timeout stops");
s.Apply("DRIVE,20,20", 256); Check(!s.Armed, "fresh motion cannot auto rearm");
s.Apply("DRIVE,0,0", 257); s.Apply("ARM",258);
Check(!s.Apply("DRIVE,101,0",259) && !s.Armed, "out of range disarms");
foreach (var bad in new[]{"", "DRIVE,0", "DRIVE,x,0", "DRIVE,-2147483648,0", "AUTO", "DRIVE,0,0,0"}) {
 s.Apply("DRIVE,0,0",300); s.Apply("ARM",301);
 Check(!s.Apply(bad,302) && !s.Armed, $"reject {bad}");
}
s.Apply("DRIVE,0,0",400); Check(!s.Apply("ARM",650), "stale neutral cannot arm");
s.Apply("DRIVE,0,0",700); s.Apply("ARM",701); s.Apply("STOP",702);
Check(!s.Armed && s.Output()==(0,0), "explicit stop");
Console.WriteLine($"{checks} checks passed.");

// Local fake broker validates real TCP framing and the outbound command envelope.
using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
using var deadline = new CancellationTokenSource(5000);
async Task<(byte Header, byte[] Body)> ReadPacket(NetworkStream stream)
{
    var one = new byte[1];
    await stream.ReadExactlyAsync(one, deadline.Token); var header = one[0];
    int length = 0, multiplier = 1;
    do {
        await stream.ReadExactlyAsync(one, deadline.Token);
        length += (one[0] & 127) * multiplier; multiplier *= 128;
    } while ((one[0] & 128) != 0);
    var body = new byte[length]; await stream.ReadExactlyAsync(body, deadline.Token);
    return (header,body);
}
var broker = Task.Run(async () => {
    using var peer = await listener.AcceptTcpClientAsync(deadline.Token);
    var stream = peer.GetStream();
    var connect = await ReadPacket(stream);
    Check(connect.Header == 0x10 && Encoding.UTF8.GetString(connect.Body,2,4) == "MQTT"
        && connect.Body[6] == 4 && (connect.Body[7] & 2) != 0, "MQTT 3.1.1 clean CONNECT");
    await stream.WriteAsync(new byte[]{0x20,2,0,0}, deadline.Token);
    string? session = null; long previous = 0;
    foreach (var expected in new[]{"drive","stop"}) {
        var packet = await ReadPacket(stream);
        Check(packet.Header == 0x30, "QoS0 non-retained publish");
        int topicLength = packet.Body[0]*256 + packet.Body[1];
        Check(Encoding.UTF8.GetString(packet.Body,2,topicLength) == "sumoos/bot-2/command", "robot topic");
        using var json = JsonDocument.Parse(packet.Body.AsMemory(2+topicLength));
        var j = json.RootElement;
        Check(j.GetProperty("command").GetString() == expected && j.GetProperty("ttlMs").GetInt32()==250, "command and expiry");
        var id = j.GetProperty("sessionId").GetString();
        Check(session is null || session == id, "stable session");
        session=id;
        var seq = j.GetProperty("sequence").GetInt64();
        Check(seq > previous, "increasing sequence"); previous=seq;
        Check(j.GetProperty("left").GetInt32() == (expected=="drive" ? -12 : 0), "motor payload");
    }
});
using (var publisher = new RobotPublisher(new AppOptions { BrokerHost="127.0.0.1", BrokerPort=port })) {
    await publisher.ConnectAsync(deadline.Token);
    await publisher.SendAsync(true,-12,12,deadline.Token);
    await publisher.SendAsync(false,0,0,deadline.Token);
}
await broker;
Console.WriteLine($"Total {checks} checks passed.");
