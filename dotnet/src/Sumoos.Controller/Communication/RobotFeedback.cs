using System.Text.Json;

namespace Sumoos.Controller.Communication;

public sealed record RobotFeedback(
    string RobotId, bool Simulated, string BootId, string SessionId, long Sequence,
    bool Ready, int? Left, int? Right, string? Reason)
{
    public static RobotFeedback? Parse(string json, string expectedRobotId, bool simulated)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Integer(root, "version") != 1 ||
                ReadText(root, "robotId") != expectedRobotId || Boolean(root, "simulated") != simulated ||
                ReadText(root, "bootId") is not { Length: > 0 and <= 100 } bootId ||
                ReadText(root, "sessionId") is not { Length: <= 100 } sessionId ||
                Integer(root, "sequence") is not { } sequence || sequence < 0 ||
                Boolean(root, "ready") is not { } ready ||
                !Motor(root, "left", out var left) || !Motor(root, "right", out var right))
                return null;

            if ((sessionId.Length == 0 && sequence != 0) || (ready && sequence == 0)) return null;
            if (root.TryGetProperty("reason", out var reasonValue) &&
                reasonValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) return null;
            var reason = ReadText(root, "reason");
            if (reason?.Length > 160 || reason?.Any(char.IsControl) == true) return null;
            return new(expectedRobotId, simulated, bootId, sessionId, sequence, ready, left, right, reason);
        }
        catch (JsonException) { return null; }
    }

    private static string? ReadText(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Integer(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number) ? number : null;

    private static bool? Boolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        } : null;

    private static bool Motor(JsonElement root, string name, out int? motor)
    {
        motor = null;
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return true;
        if (Integer(root, name) is not { } number || number is < -100 or > 100) return false;
        motor = (int)number;
        return true;
    }
}

public enum FeedbackState { NotUsed, Unavailable, Waiting, Stale, NotReady, Ready }

public sealed record FeedbackSnapshot(
    FeedbackState State, RobotFeedback? Report = null, long? AgeMs = null, long FaultCount = 0);

// One tracker per MQTT connection. Remember faults even if a newer frame arrives before the gateway polls.
public sealed class RobotFeedbackTracker(string robotId, bool simulated, string sessionId, long minimumSequence)
{
    private const int TimeoutMs = 750;
    private readonly object gate = new();
    private RobotFeedback? latest;
    private long receivedAt;
    private long faultCount;

    public void Receive(string json, long now, long lastSentSequence)
    {
        if (RobotFeedback.Parse(json, robotId, simulated) is not { } report) return;
        if (report.SessionId == sessionId && report.Sequence > lastSentSequence) return;

        lock (gate)
        {
            if (latest is not null)
            {
                if (latest.BootId == report.BootId && latest.SessionId == report.SessionId &&
                    report.Sequence < latest.Sequence) return;
                if (latest.BootId != report.BootId ||
                    IsReady(latest) && (!IsReady(report) || now - receivedAt >= TimeoutMs))
                    faultCount++;
            }
            latest = report;
            receivedAt = now;
        }
    }

    public FeedbackSnapshot Read(long now)
    {
        lock (gate)
        {
            if (latest is null) return new(FeedbackState.Waiting);
            var age = Math.Max(0, now - receivedAt);
            var state = age >= TimeoutMs ? FeedbackState.Stale
                : IsReady(latest) ? FeedbackState.Ready : FeedbackState.NotReady;
            return new(state, latest, age, faultCount);
        }
    }

    private bool IsReady(RobotFeedback report) =>
        report.Ready && report.SessionId == sessionId && report.Sequence >= minimumSequence;
}
