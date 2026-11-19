using System.Collections.ObjectModel;
using System.ComponentModel;
using Sumoos.Controller.Services;
namespace Sumoos.Dashboard.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public GatewaySnapshot State { get; private set; } = GatewaySnapshot.Idle;
    public ObservableCollection<LogEntry> Logs { get; } = new();
    public string Mode => State.Running ? (State.Simulation ? "SIMULATION" : "LIVE HARDWARE") : "OFFLINE";
    public string Broker => State.Simulation ? "Not used" : State.BrokerConnected ? "Connected" : "Disconnected";
    public string Controller => State.Simulation ? "Simulated input" : State.SerialConnected ? "USB connected" : "USB disconnected";
    public string Armed => State.Armed ? "ARMED" : "DISARMED";
    public string InputAge => State.InputAgeMs is { } age ? $"{age} ms" : "No input";
    public string Left => $"{State.Left:+0;-0;0}%";
    public string Right => $"{State.Right:+0;-0;0}%";
    public int LeftMagnitude => Math.Abs(State.Left);
    public int RightMagnitude => Math.Abs(State.Right);
    public string CountLabel => State.Simulation ? "SIMULATED FRAMES" : "MQTT WRITES";
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
