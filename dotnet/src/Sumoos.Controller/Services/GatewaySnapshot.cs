using Sumoos.Controller.Communication;

namespace Sumoos.Controller.Services;

public sealed record GatewaySnapshot(
    bool Running, OperatingMode Mode, bool BrokerConnected, bool SerialConnected,
    bool Armed, string Reason, int Left, int Right, string LastInput,
    long? InputAgeMs, long Sent, long Errors, string Payload)
{
    public bool Simulation => Mode is OperatingMode.OfflineSimulation or OperatingMode.DockerSimulation;
    public bool NetworkSimulation => Mode == OperatingMode.DockerSimulation;
    public FeedbackSnapshot Feedback { get; init; } = new(FeedbackState.NotUsed);
    public static GatewaySnapshot Idle { get; } = new(false, OperatingMode.OfflineSimulation, false, false, false,
        "Ready to start", 0, 0, "No input yet", null, 0, 0, "");
}

public sealed record LogEntry(DateTimeOffset Time, string Level, string Message);
