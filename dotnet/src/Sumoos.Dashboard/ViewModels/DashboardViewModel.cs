using System.Collections.ObjectModel;
using System.ComponentModel;
using Sumoos.Controller.Communication;
using Sumoos.Controller.Services;
namespace Sumoos.Dashboard.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public GatewaySnapshot State { get; private set; } = GatewaySnapshot.Idle;
    public ObservableCollection<LogEntry> Logs { get; } = new();
    private OperatingMode selectedMode;
    private OperatingMode CurrentMode => State.Running ? State.Mode : selectedMode;
    private bool Live => CurrentMode is OperatingMode.LivePi or OperatingMode.LiveMkr;
    public string Mode => CurrentMode switch
    {
        OperatingMode.OfflineSimulation => "OFFLINE SIMULATION",
        OperatingMode.DockerSimulation => "DOCKER MQTT SIMULATION",
        _ => "LIVE HARDWARE"
    };
    public string ModeSetup => CurrentMode switch
    {
        OperatingMode.OfflineSimulation => "On-screen controller. Runs locally without a broker or bridge.",
        OperatingMode.DockerSimulation => "On-screen controller and Docker MQTT robot simulator.",
        OperatingMode.LivePi => "MKR handheld over USB. Raspberry Pi to Zumo 32U4 over USB.",
        _ => "MKR handheld over USB. MKR Wi-Fi bridge to Zumo 32U4 over UART."
    };
    public string ConnectionPath => CurrentMode switch
    {
        OperatingMode.OfflineSimulation => "ON-SCREEN PAD  →  LOCAL SIMULATION",
        OperatingMode.DockerSimulation => "ON-SCREEN PAD  →  MQTT  →  DOCKER SIMULATOR",
        OperatingMode.LivePi => "MKR USB  →  .NET GATEWAY  →  MQTT  →  PI BRIDGE  →  ZUMO USB",
        _ => "MKR USB  →  .NET GATEWAY  →  MQTT  →  MKR BRIDGE  →  ZUMO UART"
    };
    public string Broker => CurrentMode == OperatingMode.OfflineSimulation ? "Not used"
        : State.BrokerConnected ? "Connected" : "Disconnected";
    public string Controller => !Live ? "Not used" : State.SerialConnected ? "Connected" : "Disconnected";
    public string FeedbackSource => CurrentMode switch
    {
        OperatingMode.DockerSimulation => "Simulated robot feedback",
        OperatingMode.LivePi => "Raspberry Pi bridge feedback",
        _ => "Robot feedback"
    };
    public string RobotState => CurrentMode switch
    {
        OperatingMode.OfflineSimulation => "Not used",
        OperatingMode.LiveMkr => "Unavailable",
        _ when !State.BrokerConnected => "Disconnected",
        _ => State.Feedback.State switch
        {
            FeedbackState.Ready => "Robot ready",
            FeedbackState.Stale => "Feedback stale",
            FeedbackState.NotReady => "Robot not ready",
            _ => "Waiting for robot"
        }
    };
    public string FeedbackDetails
    {
        get
        {
            var report = State.Feedback.Report;
            var age = State.Feedback.AgeMs is { } milliseconds ? $"{milliseconds} ms" : "Unavailable";
            var sequence = report is { Sequence: > 0 } ? report.Sequence.ToString() : "Unavailable";
            return $"{RobotState} · Age {age}\nRobot ID {report?.RobotId ?? "Unavailable"} · Last accepted sequence {sequence}\n"
                + $"Reported left {Motor(report?.Left)} · right {Motor(report?.Right)}\nStop reason {report?.Reason ?? "Unavailable"}";
        }
    }
    private static string Motor(int? value) => value is { } percent ? $"{percent:+0;-0;0}%" : "Unavailable";
    public string Armed => State.Armed ? "ARMED" : "DISARMED";
    public string InputAge => State.Running && State.InputAgeMs is { } age ? $"Last input {age} ms ago" : "No input";
    public string Left => $"{State.Left:+0;-0;0}%";
    public string Right => $"{State.Right:+0;-0;0}%";
    public int LeftMagnitude => Math.Abs(State.Left);
    public int RightMagnitude => Math.Abs(State.Right);
    public string CountLabel => CurrentMode == OperatingMode.OfflineSimulation ? "SIMULATED FRAMES" : "MQTT WRITES";
    public void SelectMode(OperatingMode mode)
    {
        selectedMode = mode;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    public void Update(GatewaySnapshot state)
    {
        State = state;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    public void Add(LogEntry entry)
    {
        Logs.Insert(0, entry);
        if (Logs.Count > 500) Logs.RemoveAt(Logs.Count - 1);
    }
}
