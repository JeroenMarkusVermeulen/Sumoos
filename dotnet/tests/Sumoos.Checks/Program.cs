using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Sumoos.Controller.Communication;
using Sumoos.Controller.Configuration;
using Sumoos.Controller.Input;
using Sumoos.Controller.Services;
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

var frames = new ControllerFrameReader();
Check(frames.Append("DRIVE,0,").Count == 0, "partial serial frame waits for delimiter");
Check(frames.Append("0\r\nARM\nDRIVE,-23,45\n").SequenceEqual(new[] { "DRIVE,0,0", "ARM", "DRIVE,-23,45" }),
    "sample MKR frames support CRLF, LF and multiple commands in one read");
var malformed = frames.Append("DRIVE,x,45\n").Single();
s.Apply("DRIVE,0,0", 800); s.Apply("ARM", 801);
Check(!s.Apply(malformed, 802) && !s.Armed, "malformed controller frame disarms");
try
{
    frames.Append(new string('A', 66));
    throw new Exception("Oversized frame was accepted.");
}
catch (InvalidDataException) { Check(true, "oversized serial frame rejected across reads"); }
Check(frames.Append("ARM\nDRIVE,0,0\n").SequenceEqual(new[] { "DRIVE,0,0" }),
    "oversized frame tail cannot be interpreted as arm");

string Status(string session, long sequence, string boot = "boot-a", bool ready = true,
    string id = "bot-2", bool simulated = true) => JsonSerializer.Serialize(new
{
    version = 1, robotId = id, bootId = boot, sessionId = session, sequence, simulated,
    ready, left = simulated ? (int?)0 : null, right = simulated ? (int?)0 : null, reason = "stopped"
});

Check(RobotFeedback.Parse(Status("current", 2, simulated: false), "bot-2", false) is { Left: null, Right: null, Ready: true },
    "live feedback leaves unavailable motors empty");
Check(RobotFeedback.Parse(Status("current", 2), "bot-2", false) is null, "live mode rejects simulated feedback");
Check(RobotFeedback.Parse(Status("current", 2, id: "another"), "bot-2", true) is null, "wrong robot feedback rejected");
Check(RobotFeedback.Parse("{wrong", "bot-2", true) is null, "malformed JSON rejected");
Check(RobotFeedback.Parse(Status("current", 2).Replace("\"left\":0", "\"left\":101"), "bot-2", true) is null,
    "reported motor range is validated");

var tracker = new RobotFeedbackTracker("bot-2", true, "current", minimumSequence: 2);
tracker.Receive(Status("old-session", 2), 10, 3);
Check(tracker.Read(10).State == FeedbackState.NotReady, "another session cannot establish readiness");
tracker.Receive(Status("current", 1), 20, 3);
tracker.Receive(Status("current", 4), 30, 3);
Check(tracker.Read(30).State == FeedbackState.NotReady, "old stop and future acknowledgements cannot establish readiness");
tracker.Receive(Status("current", 2), 40, 3);
tracker.Receive(Status("current", 3, ready: false), 50, 3);
tracker.Receive(Status("current", 3), 60, 3);
Check(tracker.Read(60) is { State: FeedbackState.Ready, FaultCount: 1 }, "brief not-ready report is preserved as a fault");
tracker.Receive(Status("current", 2), 700, 3);
Check(tracker.Read(810) is { State: FeedbackState.Stale, AgeMs: 750 }, "old sequence cannot refresh stale feedback");

// Local fake broker validates real TCP framing and the outbound command envelope.
using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
using var deadline = new CancellationTokenSource(5000);
async Task<(byte Header, byte[] Body)> ReadPacket(NetworkStream stream, CancellationToken token)
{
    var one = new byte[1];
    await stream.ReadExactlyAsync(one, token); var header = one[0];
    int length = 0, multiplier = 1;
    do {
        await stream.ReadExactlyAsync(one, token);
        length += (one[0] & 127) * multiplier; multiplier *= 128;
    } while ((one[0] & 128) != 0);
    var body = new byte[length]; await stream.ReadExactlyAsync(body, token);
    return (header,body);
}
var broker = Task.Run(async () => {
    using var peer = await listener.AcceptTcpClientAsync(deadline.Token);
    var stream = peer.GetStream();
    var connect = await ReadPacket(stream, deadline.Token);
    Check(connect.Header == 0x10 && Encoding.UTF8.GetString(connect.Body,2,4) == "MQTT"
        && connect.Body[6] == 4 && (connect.Body[7] & 2) != 0, "MQTT 3.1.1 clean CONNECT");
    await stream.WriteAsync(new byte[]{0x20,2,0,0}, deadline.Token);
    string? session = null; long previous = 0;
    foreach (var expected in new[]{"drive","stop"}) {
        var packet = await ReadPacket(stream, deadline.Token);
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

// Dashboard session is testable independently of WPF or physical hardware.
using (var cancellation = new CancellationTokenSource())
{
    var gateway = new Sumoos.Controller.Services.GatewaySession();
    var run = gateway.RunAsync(new AppOptions { MaxSpeedPercent = 30 }, true, null, null, cancellation.Token);
    async Task WaitUntil(Func<bool> condition, Action? feed = null)
    {
        var until = Environment.TickCount64 + 3000;
        while (!condition())
        {
            if (Environment.TickCount64 > until) throw new Exception("Dashboard session test timed out.");
            feed?.Invoke(); await Task.Delay(20);
        }
    }
    await WaitUntil(() => gateway.Snapshot.Running);
    Check(!gateway.Snapshot.Armed, "dashboard starts disarmed");
    gateway.FeedSimulation(0, 0); gateway.ArmSimulation();
    await WaitUntil(() => gateway.Snapshot.Armed, () => gateway.FeedSimulation(0, 0));
    gateway.FeedSimulation(50, -50);
    await WaitUntil(() => gateway.Snapshot.Left == 15, () => gateway.FeedSimulation(50, -50));
    Check(gateway.Snapshot.Right == -15, "dashboard speed limit and signed output");
    Check(gateway.Snapshot.Simulation && !gateway.Snapshot.BrokerConnected, "simulation never claims broker connection");
    gateway.RequestStop();
    await WaitUntil(() => !gateway.Snapshot.Armed);
    Check(gateway.Snapshot.Left == 0 && gateway.Snapshot.Right == 0, "dashboard stop overrides drive");
    gateway.FeedSimulation(0, 0); gateway.ArmSimulation();
    await WaitUntil(() => gateway.Snapshot.Armed, () => gateway.FeedSimulation(0, 0));
    gateway.FeedSimulation(50, 50);
    await WaitUntil(() => gateway.Snapshot.Left == 15, () => gateway.FeedSimulation(50, 50));
    await WaitUntil(() => !gateway.Snapshot.Armed); // stop all UI heartbeats
    Check(gateway.Snapshot.Left == 0 && gateway.Snapshot.Reason == "controller timeout", "frozen UI cannot sustain movement");
    gateway.FeedSimulation(50, 50);
    await Task.Delay(70);
    Check(!gateway.Snapshot.Armed, "new UI input cannot auto rearm");
    cancellation.Cancel(); await run;
    Check(!gateway.Snapshot.Running && gateway.Snapshot.Left == 0, "dashboard cancellation stops session");
    Check(!gateway.Logs.IsEmpty, "dashboard emits diagnostic events");
}
Console.WriteLine($"Total {checks} checks passed including dashboard session.");

// Exercise feedback and recovery through the gateway's actual MQTT connection.
using var feedbackDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
var feedbackToken = feedbackDeadline.Token;
var gatewayWithFeedback = new GatewaySession();
var feedbackRun = gatewayWithFeedback.RunAsync(new AppOptions { BrokerHost = "127.0.0.1", BrokerPort = port },
    OperatingMode.DockerSimulation, null, null, feedbackToken);

async Task WaitForFeedback(Func<GatewaySnapshot, bool> condition)
{
    while (!condition(gatewayWithFeedback.Snapshot))
    {
        gatewayWithFeedback.FeedSimulation(0, 0);
        await Task.Delay(15, feedbackToken);
    }
}

async Task<(string Session, long Sequence)> AcceptGateway(TcpClient peer, CancellationToken token)
{
    var stream = peer.GetStream();
    var connect = await ReadPacket(stream, token);
    Check(connect.Header == 0x10, "gateway opens MQTT connection");
    await stream.WriteAsync(new byte[] { 0x20, 2, 0, 0 }, token);
    var subscribe = await ReadPacket(stream, token);
    Check(subscribe.Header == 0x82 && Encoding.UTF8.GetString(subscribe.Body).Contains("sumoos/bot-2/status"),
        "gateway subscribes to the selected robot status");
    await stream.WriteAsync(new byte[] { 0x90, 3, 0, 1, 0 }, token);
    var stop = await ReadPacket(stream, token);
    var topicLength = stop.Body[0] * 256 + stop.Body[1];
    using var json = JsonDocument.Parse(stop.Body.AsMemory(2 + topicLength));
    var command = json.RootElement;
    Check(command.GetProperty("command").GetString() == "stop", "gateway sends stop first after connecting");
    return (command.GetProperty("sessionId").GetString()!, command.GetProperty("sequence").GetInt64());
}

async Task SendFeedback(TcpClient peer, string json, CancellationToken token, bool retained = false)
{
    var topic = Encoding.UTF8.GetBytes("sumoos/bot-2/status");
    var payload = Encoding.UTF8.GetBytes(json);
    var length = 2 + topic.Length + payload.Length;
    var stream = peer.GetStream();
    await stream.WriteAsync(new byte[] { retained ? (byte)0x31 : (byte)0x30,
        (byte)((length & 127) | 128), (byte)(length >> 7), 0, (byte)topic.Length }, token);
    await stream.WriteAsync(topic, token);
    await stream.WriteAsync(payload, token);
}

try
{
    using var peer = await listener.AcceptTcpClientAsync(feedbackToken);
    var (session, sequence) = await AcceptGateway(peer, feedbackToken);
    await WaitForFeedback(value => value.BrokerConnected && value.Feedback.State == FeedbackState.Waiting);
    await SendFeedback(peer, Status(session, sequence), feedbackToken, retained: true);
    await SendFeedback(peer, Status(session, sequence, id: "wrong"), feedbackToken);
    gatewayWithFeedback.ArmSimulation();
    await WaitForFeedback(_ => gatewayWithFeedback.Logs.Any(entry => entry.Message.StartsWith("Arm rejected:")));
    Check(!gatewayWithFeedback.Snapshot.Armed && gatewayWithFeedback.Snapshot.Feedback.State == FeedbackState.Waiting,
        "broker traffic and retained or wrong-robot feedback cannot arm");

    await SendFeedback(peer, Status(session, sequence), feedbackToken);
    await WaitForFeedback(value => value.Feedback.State == FeedbackState.Ready);
    Check(!gatewayWithFeedback.Snapshot.Armed, "an arm attempt while waiting is not replayed when feedback arrives");
    gatewayWithFeedback.ArmSimulation();
    await WaitForFeedback(value => value.Armed);
    await SendFeedback(peer, Status(session, sequence, boot: "boot-b"), feedbackToken);
    await WaitForFeedback(value => !value.Armed && value.Feedback.Report?.BootId == "boot-b");
    Check(gatewayWithFeedback.Snapshot.Feedback.State == FeedbackState.Ready, "new receiver boot disarms even if already ready");

    await SendFeedback(peer, Status(session, sequence, boot: "boot-b"), feedbackToken);
    gatewayWithFeedback.ArmSimulation();
    await WaitForFeedback(value => value.Armed);
    await WaitForFeedback(value => value.Feedback.State == FeedbackState.Stale);
    Check(!gatewayWithFeedback.Snapshot.Armed, "feedback expiry disarms with a healthy broker and input");
    peer.Dispose();
    using var replacement = await listener.AcceptTcpClientAsync(feedbackToken);
    var (newSession, newSequence) = await AcceptGateway(replacement, feedbackToken);
    await SendFeedback(replacement, Status(newSession, sequence), feedbackToken);
    await WaitForFeedback(value => value.Feedback.State == FeedbackState.NotReady);
    await SendFeedback(replacement, Status(newSession, newSequence), feedbackToken);
    await WaitForFeedback(value => value.Feedback.State == FeedbackState.Ready);
    Check(!gatewayWithFeedback.Snapshot.Armed, "broker reconnect needs a new stop acknowledgement and explicit re-arm");
    gatewayWithFeedback.ArmSimulation();
    await WaitForFeedback(value => value.Armed);
    Check(gatewayWithFeedback.Snapshot.Armed, "explicit re-arm works after recovery");
}
finally
{
    feedbackDeadline.Cancel();
    await feedbackRun.WaitAsync(TimeSpan.FromSeconds(2));
}

using var liveDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var liveGateway = new GatewaySession();
var liveRun = liveGateway.RunAsync(new AppOptions
{
    BrokerHost = "127.0.0.1", BrokerPort = port, SerialPort = "SUMOOS_CHECK_MISSING_PORT"
}, OperatingMode.LivePi, null, null, liveDeadline.Token);
try
{
    using var peer = await listener.AcceptTcpClientAsync(liveDeadline.Token);
    var (session, sequence) = await AcceptGateway(peer, liveDeadline.Token);
    await SendFeedback(peer, Status(session, sequence, simulated: false), liveDeadline.Token);
    while (liveGateway.Snapshot.Feedback.State != FeedbackState.Ready)
        await Task.Delay(15, liveDeadline.Token);
    Check(liveGateway.Snapshot is { BrokerConnected: true, SerialConnected: false, Armed: false } &&
        liveGateway.Snapshot.Feedback.Report is { Simulated: false, Left: null, Right: null },
        "Pi feedback is independent of controller USB and does not fabricate motor readings");
}
finally
{
    liveDeadline.Cancel();
    await liveRun.WaitAsync(TimeSpan.FromSeconds(2));
}
Console.WriteLine($"Total {checks} checks passed including MQTT feedback and recovery.");
