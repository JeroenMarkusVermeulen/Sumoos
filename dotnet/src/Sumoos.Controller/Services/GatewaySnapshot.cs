namespace Sumoos.Controller.Services;

public sealed record GatewaySnapshot(
    bool Running, bool Simulation, bool BrokerConnected, bool SerialConnected,
    bool Armed, string Reason, int Left, int Right, string LastInput,
    long? InputAgeMs, long Sent, long Errors, string Payload)
{
    public static GatewaySnapshot Idle { get; } = new(false, true, false, false, false,
        "Ready to start", 0, 0, "No input yet", null, 0, 0, "");
}
public sealed record LogEntry(DateTimeOffset Time, string Level, string Message);
