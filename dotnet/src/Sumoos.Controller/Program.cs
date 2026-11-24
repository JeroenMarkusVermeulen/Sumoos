using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Sumoos.Core;
using Sumoos.Controller.Configuration;
using Sumoos.Controller.Communication;
using Sumoos.Controller.Input;

var simulation = args.Contains("--simulate");
var piFeedback = !simulation && args.Contains("--pi-feedback");
var settings = JsonSerializer.Deserialize<AppOptions>(
    await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "appsettings.json")))
    ?? throw new InvalidOperationException("Missing settings.");
settings.Validate();
if (args.Contains("--ports")) { Console.WriteLine(string.Join(Environment.NewLine, System.IO.Ports.SerialPort.GetPortNames())); return; }
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
var input = Channel.CreateBounded<string>(16);
_ = Task.Run(async () => { while (Console.ReadLine() is { } line) await input.Writer.WriteAsync(line); input.Writer.TryComplete(); });
var state = new ControlState();
var clock = Stopwatch.StartNew();
using var mqtt = simulation ? null : new RobotPublisher(settings, receiveStatus: piFeedback, simulated: false);
ArduinoInput? serial = null;
long nextConnect = 0;
string previous = "";
var previousFeedback = new FeedbackSnapshot(FeedbackState.Waiting);
Console.WriteLine($"Sumoos | {(simulation ? "OFFLINE SIMULATION" : piFeedback ? "LIVE USB -> PI (feedback required)" : "LIVE USB -> MQTT (command only)")} | speed limit {settings.MaxSpeedPercent}%");
Console.WriteLine("status | stop | quit. Simulation: DRIVE,0,0 then ARM then fresh DRIVE,l,r every 50ms.");
try
{
    while (!cancel.IsCancellationRequested)
    {
        var now = clock.ElapsedMilliseconds;
        if (!simulation && (mqtt is { Connected: false } || serial is null) && input.Reader.TryRead(out var consoleLine))
        {
            if (consoleLine == "quit") break;
            if (consoleLine == "stop") { state.Stop("console stop"); serial?.Clear(); }
            if (consoleLine == "status") Console.WriteLine($"Armed={state.Armed}; broker={mqtt?.Connected}; reason={state.Reason}");
        }
        if (!simulation && (mqtt is { Connected: false } || serial is null))
        {
            state.Stop("disconnected");
            if (now < nextConnect) { await Task.Delay(50, cancel.Token); continue; }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
                timeout.CancelAfter(2000);
                if (!mqtt!.Connected)
                {
                    await mqtt.ConnectAsync(timeout.Token);
                    previousFeedback = new(FeedbackState.Waiting);
                }
                serial?.Dispose(); serial = new ArduinoInput(settings.SerialPort, settings.BaudRate);
                serial.Open(); state.Stop("connected; neutral + ARM required");
                await mqtt.SendAsync(false, 0, 0, timeout.Token);
            }
            catch (BrokerCredentialsException)
            { Console.WriteLine("Broker rejected MQTT credentials."); nextConnect = clock.ElapsedMilliseconds + 2000; continue; }
            catch (Exception ex) when (!cancel.IsCancellationRequested)
            { serial?.Dispose(); serial = null; Console.WriteLine($"Connection failed ({ex.GetType().Name}). Check broker credentials and serial port."); nextConnect = clock.ElapsedMilliseconds + 2000; continue; }
        }
        now = clock.ElapsedMilliseconds;
        state.Tick(now);
        try
        {
            var feedback = piFeedback ? mqtt!.Feedback(Environment.TickCount64) : null;
            if (serial is not null)
            {
                if (feedback is not null && (feedback.State != FeedbackState.Ready ||
                    previousFeedback.State != FeedbackState.Ready || previousFeedback.FaultCount != feedback.FaultCount))
                {
                    state.Stop(feedback.State == FeedbackState.Ready ? "robot ready; explicit arm required" : "waiting for robot feedback");
                    serial.Clear();
                }
                else
                {
                    foreach (var line in serial.Read())
                    {
                        var accepted = state.Apply(line, now);
                        if (line == "STOP" || (!accepted && line != "ARM")) break;
                    }
                }
            }
            if (feedback is not null) previousFeedback = feedback;
            while (input.Reader.TryRead(out var line))
            {
                if (line == "quit") { cancel.Cancel(); break; }
                if (line == "status") Console.WriteLine($"Armed={state.Armed}; broker={mqtt?.Connected}; serial={serial is not null}; reason={state.Reason}");
                else if (line == "stop") { state.Stop("console stop"); serial?.Clear(); }
                else if (simulation && !state.Apply(line, now)) Console.WriteLine("Rejected input");
            }
            if (cancel.IsCancellationRequested) break;
            var (left, right) = state.Output();
            left = left * settings.MaxSpeedPercent / 100; right = right * settings.MaxSpeedPercent / 100;
            if (mqtt is not null)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
                timeout.CancelAfter(150);
                await mqtt.SendAsync(state.Armed, left, right, timeout.Token);
            }
            var status = $"{state.Reason}: L={left}, R={right}";
            if (previous != status) { Console.WriteLine(status); previous = status; }
        }
        catch (Exception ex) when (!cancel.IsCancellationRequested)
        { state.Stop("communication error"); serial?.Dispose(); serial = null; Console.WriteLine($"Communication failed ({ex.GetType().Name}); disarmed."); }
        await Task.Delay(50, cancel.Token);
    }
}
catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
finally
{
    state.Stop("shutdown"); serial?.Dispose();
    if (mqtt is { Connected: true })
    {
        using var timeout = new CancellationTokenSource(500);
        try { await mqtt.SendAsync(false, 0, 0, timeout.Token); } catch { Console.WriteLine("Stop not delivered. Robot watchdog must stop motors."); }
    }
    Console.WriteLine("Stopped.");
}
