using System.Collections.Concurrent;
using Sumoos.Core;
using Sumoos.Controller.Configuration;
using Sumoos.Controller.Communication;
using Sumoos.Controller.Input;

namespace Sumoos.Controller.Services;

// Single background loop owns hardware/state. UI reads immutable snapshots and posts intent.
// Simulation drive intent has a 150 ms lease, so a frozen UI cannot sustain motion.
public sealed class GatewaySession
{
    private GatewaySnapshot snapshot = GatewaySnapshot.Idle;
    public GatewaySnapshot Snapshot => Volatile.Read(ref snapshot);
    public ConcurrentQueue<LogEntry> Logs { get; } = new();
    private readonly object gate = new();
    private (int Left, int Right, long At) simulationInput;
    private int stop;
    private int arm;
    private int running;
    public void FeedSimulation(int left, int right)
    {
        lock (gate) simulationInput = (left, right, Environment.TickCount64);
    }
    public void ArmSimulation() => Interlocked.Exchange(ref arm, 1);
    public void RequestStop()
    {
        Interlocked.Exchange(ref arm, 0);
        Interlocked.Exchange(ref stop, 1);
    }
    private void Log(string level, string message)
    {
        Logs.Enqueue(new(DateTimeOffset.Now, level, message));
        while (Logs.Count > 500) Logs.TryDequeue(out _);
    }
    public async Task RunAsync(AppOptions options, bool simulation, string? username, string? password, CancellationToken token)
    {
        options.Validate();
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("Already running.");
        var state = new ControlState();
        using var mqtt = simulation ? null : new RobotPublisher(options, username, password);
        ArduinoInput? serial = null;
        long sent = 0, errors = 0, nextConnect = 0;
        long? lastInputAt = null;
        string lastInput = "No input yet", previousReason = "", payload = "";
        Log("INFO", simulation ? "Simulation started. No network or hardware is used." : $"Live gateway started for {options.RobotId}.");
        void PublishSnapshot(bool active = true)
        {
            var (l, r) = state.Output();
            Volatile.Write(ref snapshot, new(active, simulation, mqtt?.Connected == true, serial is not null,
                state.Armed, state.Reason, l * options.MaxSpeedPercent / 100, r * options.MaxSpeedPercent / 100,
                lastInput, lastInputAt is { } at ? Environment.TickCount64 - at : null, sent, errors, payload));
            if (previousReason != state.Reason) { Log(state.Armed ? "INFO" : "STATE", state.Reason); previousReason = state.Reason; }
        }
        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = Environment.TickCount64;
                state.Tick(now);
                var stopRequested = Interlocked.Exchange(ref stop, 0) != 0;
                if (stopRequested) { state.Stop("Dashboard stop"); Interlocked.Exchange(ref arm, 0); serial?.Clear(); }
                if (!simulation && (mqtt is { Connected: false } || serial is null))
                {
                    state.Stop("Disconnected; re-arm required");
                    if (now >= nextConnect)
                    {
                        try
                        {
                            PublishSnapshot();
                            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                            deadline.CancelAfter(2000);
                            if (!mqtt!.Connected) await mqtt.ConnectAsync(deadline.Token);
                            serial?.Dispose(); serial = null;
                            var opening = new ArduinoInput(options.SerialPort, options.BaudRate);
                            try { opening.Open(); serial = opening; } catch { opening.Dispose(); throw; }
                            state.Stop("Connected; waiting for neutral + Arduino ARM");
                            await mqtt.SendAsync(false, 0, 0, deadline.Token);
                            payload = mqtt.LastPayload; sent++;
                            Log("INFO", "Broker and Arduino connected. Motion remains disarmed.");
                        }
                        catch (Exception ex) when (!token.IsCancellationRequested)
                        {
                            serial?.Dispose(); serial = null; errors++;
                            Log("ERROR", $"Connect failed ({ex.GetType().Name}). Check host, credentials and USB port. Retry in 2s.");
                            nextConnect = Environment.TickCount64 + 2000;
                        }
                    }
                    PublishSnapshot();
                    await Task.Delay(50, token);
                    continue;
                }
                try
                {
                    now = Environment.TickCount64;
                    if (simulation)
                    {
                        (int Left, int Right, long At) intent;
                        lock (gate) intent = simulationInput;
                        if (!stopRequested && now - intent.At < 150)
                        {
                            lastInput = $"DRIVE,{intent.Left},{intent.Right}";
                            lastInputAt = intent.At;
                            state.Apply(lastInput, intent.At);
                        }
                        if (Interlocked.Exchange(ref arm, 0) != 0 && !stopRequested)
                        {
                            if (!state.Apply("ARM", now)) Log("WARN", "Arm rejected: provide fresh neutral input first.");
                        }
                    }
                    else if (serial is not null && !stopRequested)
                    {
                        foreach (var line in serial.Read())
                        {
                            lastInput = line; lastInputAt = now;
                            if (!state.Apply(line, now)) Log("WARN", "Arduino input rejected; disarmed.");
                        }
                    }
                    state.Tick(now);
                    var (left, right) = state.Output();
                    left = left * options.MaxSpeedPercent / 100;
                    right = right * options.MaxSpeedPercent / 100;
                    if (mqtt is not null)
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(150);
                        await mqtt.SendAsync(state.Armed, left, right, deadline.Token);
                        payload = mqtt.LastPayload; sent++;
                    }
                    else
                    {
                        payload = System.Text.Json.JsonSerializer.Serialize(new { simulation = true,
                            command = state.Armed ? "drive" : "stop", left, right });
                        sent++;
                    }
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    state.Stop("Communication fault"); serial?.Dispose(); serial = null; errors++;
                    Log("ERROR", $"Communication failed ({ex.GetType().Name}); disarmed.");
                }
                PublishSnapshot();
                await Task.Delay(50, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            state.Stop("Session stopped");
            serial?.Dispose(); serial = null;
            if (mqtt is { Connected: true })
            {
                using var deadline = new CancellationTokenSource(500);
                try { await mqtt.SendAsync(false, 0, 0, deadline.Token); sent++; payload = mqtt.LastPayload; }
                catch { Log("WARN", "Final stop not delivered. Robot watchdog must stop motors."); }
            }
            mqtt?.Dispose();
            PublishSnapshot(false);
            Log("INFO", "Session stopped. Reconnect starts disarmed.");
            Volatile.Write(ref running, 0);
        }
    }
}
