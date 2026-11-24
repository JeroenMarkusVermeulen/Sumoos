using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Sumoos.Controller.Configuration;
using Sumoos.Controller.Services;
using Sumoos.Dashboard.ViewModels;

namespace Sumoos.Dashboard;

public partial class MainWindow : Window
{
    private readonly DashboardViewModel model = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private GatewaySession? session;
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private Task? stopping;
    private int heldLeft, heldRight;
    private bool closing, closingRequested, releaseCapture;
    private (string Host, string Port, string Username, string Password) hardwareBroker, simulationBroker;

    public MainWindow()
    {
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        MinHeight = Math.Min(740, area.Height);
        MinWidth = Math.Min(MinWidth, area.Width);
        Height = Math.Min(Height, area.Height);
        Width = Math.Min(Width, area.Width);
        DataContext = model;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            var options = File.Exists(path) ? JsonSerializer.Deserialize<AppOptions>(File.ReadAllText(path)) : null;
            if (options is not null)
            {
                HostBox.Text = options.BrokerHost; PortBox.Text = options.BrokerPort.ToString();
                RobotBox.Text = options.RobotId; SerialBox.Text = options.SerialPort;
                BaudBox.Text = options.BaudRate.ToString(); SpeedSlider.Value = options.MaxSpeedPercent;
            }
        }
        catch (Exception ex) { AddLog("WARN", $"Settings could not be loaded ({ex.GetType().Name}); using defaults."); }
        LoadCredentials();
        BrokerSimulationBox.Checked += BrokerSimulationChanged;
        BrokerSimulationBox.Unchecked += BrokerSimulationChanged;
        SimulationBox.Checked += ModeSelectionChanged;
        SimulationBox.Unchecked += ModeSelectionChanged;
        PiBridgeOption.Checked += ModeSelectionChanged;
        MkrBridgeOption.Checked += ModeSelectionChanged;
        UpdateModeSelection();
        AddLog("INFO", "Ready. Start simulation or configure the Arduino and broker.");
        timer.Tick += (_, _) => Refresh();
        timer.Start();
    }
    private void LoadCredentials()
    {
        var credentials = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                {
                    var path = Path.Combine(directory.FullName, ".env");
                    if (!File.Exists(path)) continue;
                    foreach (var line in File.ReadLines(path))
                    {
                        var entry = line.Trim();
                        if (entry.Length == 0 || entry.StartsWith('#')) continue;
                        if (entry.StartsWith("export ", StringComparison.Ordinal)) entry = entry[7..].TrimStart();
                        var separator = entry.IndexOf('=');
                        if (separator <= 0) continue;
                        var key = entry[..separator].Trim();
                        if (key is not ("MQTT_USERNAME" or "MQTT_PASSWORD" or "SIM_MQTT_USERNAME" or "SIM_MQTT_PASSWORD")) continue;
                        var value = entry[(separator + 1)..].Trim();
                        if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
                            value = value[1..^1];
                        else
                        {
                            var comment = value.IndexOf(" #", StringComparison.Ordinal);
                            if (comment >= 0) value = value[..comment].TrimEnd();
                        }
                        credentials[key] = value;
                    }
                    break;
                }
            }
        }
        catch (Exception ex) { AddLog("WARN", $"MQTT credentials could not be loaded ({ex.GetType().Name})."); }

        UserBox.Text = credentials.GetValueOrDefault("MQTT_USERNAME")
            ?? Environment.GetEnvironmentVariable("SUMOOS_MQTT_USERNAME") ?? "";
        PasswordInput.Password = credentials.GetValueOrDefault("MQTT_PASSWORD")
            ?? Environment.GetEnvironmentVariable("SUMOOS_MQTT_PASSWORD") ?? "";

        var simulationUsername = Environment.GetEnvironmentVariable("SIM_MQTT_USERNAME")
            ?? credentials.GetValueOrDefault("SIM_MQTT_USERNAME");
        var simulationPassword = Environment.GetEnvironmentVariable("SIM_MQTT_PASSWORD")
            ?? credentials.GetValueOrDefault("SIM_MQTT_PASSWORD") ?? "";
        simulationBroker = ("127.0.0.1", "1884", string.IsNullOrEmpty(simulationUsername) ? "sumoos" : simulationUsername, simulationPassword);
    }

    private void BrokerSimulationChanged(object sender, RoutedEventArgs e)
    {
        var current = (HostBox.Text, PortBox.Text, UserBox.Text, PasswordInput.Password);
        var useSimulation = BrokerSimulationBox.IsChecked == true;
        var next = useSimulation ? simulationBroker : hardwareBroker;
        if (useSimulation) hardwareBroker = current;
        else simulationBroker = current;

        HostBox.Text = next.Host;
        PortBox.Text = next.Port;
        UserBox.Text = next.Username;
        PasswordInput.Password = next.Password;
        UpdateModeSelection();
    }

    private OperatingMode SelectedMode => SimulationBox.IsChecked == true
        ? BrokerSimulationBox.IsChecked == true ? OperatingMode.DockerSimulation : OperatingMode.OfflineSimulation
        : MkrBridgeOption.IsChecked == true ? OperatingMode.LiveMkr : OperatingMode.LivePi;

    private void ModeSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (SimulationBox.IsChecked != true && BrokerSimulationBox.IsChecked == true)
            BrokerSimulationBox.IsChecked = false;
        UpdateModeSelection();
    }

    private void UpdateModeSelection()
    {
        var simulation = SimulationBox.IsChecked == true;
        BrokerSimulationBox.IsEnabled = simulation;
        LiveBridgePanel.Visibility = simulation ? Visibility.Collapsed : Visibility.Visible;
        model.SelectMode(SelectedMode);
    }
    private void AddLog(string level, string message) => model.Add(new(DateTimeOffset.Now, level, message));
    private void Refresh()
    {
        if (session is null) return;
        if (session.Snapshot is { Running: true, Simulation: true } && PauseInput.IsChecked != true && IsActive)
            session.FeedSimulation(heldLeft, heldRight);
        model.Update(session.Snapshot);
        while (session.Logs.TryDequeue(out var entry)) model.Add(entry);
    }
    private async void ConnectClicked(object sender, RoutedEventArgs e)
    {
        if (worker is not null) { await StopSession(); return; }
        try
        {
            var options = new AppOptions {
                BrokerHost = HostBox.Text.Trim(), BrokerPort = int.Parse(PortBox.Text),
                RobotId = RobotBox.Text.Trim(), SerialPort = SerialBox.Text.Trim(),
                BaudRate = int.Parse(BaudBox.Text), MaxSpeedPercent = (int)SpeedSlider.Value
            };
            options.Validate();
            var mode = SelectedMode;
            var simulation = mode is OperatingMode.OfflineSimulation or OperatingMode.DockerSimulation;
            var networkSimulation = mode == OperatingMode.DockerSimulation;
            session = new GatewaySession();
            cancellation = new CancellationTokenSource();
            heldLeft = heldRight = 0; PauseInput.IsChecked = false;
            session.FeedSimulation(0, 0);
            var user = networkSimulation || !string.IsNullOrEmpty(UserBox.Text) ? UserBox.Text : null;
            var password = networkSimulation || !string.IsNullOrEmpty(PasswordInput.Password) ? PasswordInput.Password : null;
            var token = cancellation.Token;
            var currentSession = session;
            worker = Task.Run(() => currentSession.RunAsync(options, mode, user, password, token));
            SettingsPanel.IsEnabled = false; SimControls.IsEnabled = simulation;
            ConnectButton.Content = "Disconnect";
            await Task.Delay(100);
            Refresh();
        }
        catch (Exception ex) { AddLog("ERROR", $"Session not started: {ex.Message}"); await StopSession(); }
    }
    private async Task StopSession()
    {
        if (worker is null) return;
        ConnectButton.IsEnabled = false;
        heldLeft = heldRight = 0; session?.RequestStop(); cancellation?.Cancel();
        stopping ??= FinishStopSession(worker);
        try { await stopping.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException)
        {
            AddLog("WARN", "Disconnect is waiting for device cleanup. Robot watchdog must stop motors.");
        }
    }

    private async Task FinishStopSession(Task runningWorker)
    {
        // Defer cleanup so StopSession can publish the shared task even if the worker already exited.
        await Task.Yield();
        try { await runningWorker; }
        catch (Exception ex) { AddLog("ERROR", $"Session ended ({ex.GetType().Name})."); }
        finally
        {
            Refresh(); worker = null; stopping = null; cancellation?.Dispose(); cancellation = null;
            SimControls.IsEnabled = false; SettingsPanel.IsEnabled = true;
            ConnectButton.Content = "Start session"; ConnectButton.IsEnabled = true;
        }
    }
    private void StopClicked(object sender, RoutedEventArgs e)
    {
        heldLeft = heldRight = 0;
        session?.RequestStop();
        AddLog("WARN", "Dashboard stop requested. Motion must be explicitly re-armed.");
    }
    private void ArmClicked(object sender, RoutedEventArgs e)
    {
        if (PauseInput.IsChecked == true) { AddLog("WARN", "Resume simulation input before arming."); return; }
        session?.ArmSimulation();
    }
    private void DriveDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button || session?.Snapshot is not { Running: true, Simulation: true, Armed: true }) return;
        var parts = ((string)button.Tag).Split(',');
        heldLeft = int.Parse(parts[0]); heldRight = int.Parse(parts[1]);
        button.CaptureMouse();
        e.Handled = true;
    }
    private void DriveUp(object sender, MouseButtonEventArgs e)
    {
        heldLeft = heldRight = 0;
        session?.FeedSimulation(0, 0);
        releaseCapture = true;
        if (sender is Button button) button.ReleaseMouseCapture();
        releaseCapture = false;
        e.Handled = true;
    }
    private void DriveLost(object sender, MouseEventArgs e)
    {
        heldLeft = heldRight = 0;
        if (!releaseCapture) session?.RequestStop();
    }
    private void WindowDeactivated(object? sender, EventArgs e)
    {
        heldLeft = heldRight = 0;
        session?.RequestStop();
    }
    private void RefreshPorts(object sender, RoutedEventArgs e)
    {
        try
        {
            var ports = Sumoos.Controller.Input.ArduinoInput.GetPortNames().Order().ToArray();
            PortsText.Text = ports.Length == 0 ? "No serial ports detected." : string.Join(", ", ports);
            if (ports.Length == 1) SerialBox.Text = ports[0];
        }
        catch (Exception ex) { AddLog("ERROR", $"Port discovery failed ({ex.GetType().Name})."); }
    }
    private void ClearLog(object sender, RoutedEventArgs e) => model.Logs.Clear();
    private void ExportLog(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Text log (*.txt)|*.txt", FileName = $"sumoos-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllLines(dialog.FileName, model.Logs.Reverse().Select(x => $"{x.Time:O} [{x.Level}] {x.Message}"), Encoding.UTF8);
            AddLog("INFO", "Diagnostic log exported.");
        }
        catch (Exception ex) { AddLog("ERROR", $"Export failed ({ex.GetType().Name})."); }
    }
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        if (closingRequested) return;
        closingRequested = true;
        await StopSession();
        timer.Stop(); closing = true;
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
