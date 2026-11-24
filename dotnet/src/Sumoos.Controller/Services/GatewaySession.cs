using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using Sumoos.Core;
using Sumoos.Controller.Configuration;
using Sumoos.Controller.Communication;
using Sumoos.Controller.Input;

namespace Sumoos.Controller.Services;

// The background loop owns motion state. UI reads snapshots and posts intent.
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

    public Task RunAsync(AppOptions options, bool simulation, string? username, string? password,
        CancellationToken token, bool brokerSimulation = false) =>
        RunAsync(options, simulation
            ? brokerSimulation ? OperatingMode.DockerSimulation : OperatingMode.OfflineSimulation
            : OperatingMode.LiveMkr, username, password, token);

    public async Task RunAsync(AppOptions options, OperatingMode mode, string? username, string? password,
        CancellationToken token)
    {
        options.Validate();
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("Already running.");

        var simulation = mode is OperatingMode.OfflineSimulation or OperatingMode.DockerSimulation;
        var requireFeedback = mode is OperatingMode.DockerSimulation or OperatingMode.LivePi;
        var state = new ControlState();
        using var mqtt = mode == OperatingMode.OfflineSimulation ? null
            : new RobotPublisher(options, username, password, requireFeedback, simulation);
        ArduinoInput? serial = null;
        Task<ArduinoInput>? openingSerial = null;
        long sent = 0, errors = 0, nextBroker = 0, nextSerial = 0, feedbackFaults = 0;
        long? lastInputAt = null;
        string lastInput = "No input yet", previousReason = "", payload = "";
        var wasConnected = false;
        var previousFeedback = requireFeedback ? FeedbackState.Waiting
            : simulation ? FeedbackState.NotUsed : FeedbackState.Unavailable;
        var feedback = new FeedbackSnapshot(previousFeedback);
        Log("INFO", $"Started {mode switch {
            OperatingMode.OfflineSimulation => "offline simulation",
            OperatingMode.DockerSimulation => "Docker MQTT simulation",
            OperatingMode.LivePi => $"live hardware with Raspberry Pi feedback for {options.RobotId}",
            _ => $"live hardware with MKR bridge (command only) for {options.RobotId}"
        }}. Motion starts disarmed.");

        void PublishSnapshot(bool active = true)
        {
            var (left, right) = state.Output();
            Volatile.Write(ref snapshot, new(active, mode, mqtt?.Connected == true, serial is not null,
                state.Armed, state.Reason, left * options.MaxSpeedPercent / 100, right * options.MaxSpeedPercent / 100,
                lastInput, lastInputAt is { } at ? Environment.TickCount64 - at : null, sent, errors, payload)
                { Feedback = feedback });
            if (previousReason == state.Reason) return;
            Log(state.Armed ? "INFO" : "STATE", state.Reason);
            previousReason = state.Reason;
        }

        bool ApplyInput(string line, long now)
        {
            if (state.Apply(line, now)) return true;
            Log("WARN", line == "ARM" ? "Arm rejected: provide fresh neutral input first."
                : "Invalid controller frame; disarmed.");
            return false;
        }

        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = Environment.TickCount64;
                var stopRequested = Interlocked.Exchange(ref stop, 0) != 0;
                var armRequested = Interlocked.Exchange(ref arm, 0) != 0;
                state.Tick(now);
                if (stopRequested) state.Stop("Dashboard stop");

                if (!simulation && serial is null)
                {
                    if (openingSerial is null && now >= nextSerial)
                        openingSerial = Task.Run(() => OpenController(options), token);
                    if (openingSerial is { IsCompleted: true })
                    {
                        try
                        {
                            serial = await openingSerial;
                            state.Stop("Controller connected; neutral and explicit arm required");
                            Log("INFO", $"USB controller connected on {options.SerialPort}.");
                        }
                        catch (Exception ex) when (!token.IsCancellationRequested)
                        {
                            var detail = ex is UnauthorizedAccessException ? "busy or access denied" : "unavailable";
                            Log("ERROR", $"USB controller port {options.SerialPort} is {detail}. Retrying in 2s.");
                            nextSerial = Environment.TickCount64 + 2000;
                            errors++;
                        }
                        openingSerial = null;
                    }
                }

                if (mqtt is { Connected: false })
                {
                    if (wasConnected) Log("WARN", "MQTT broker connection lost; disarmed.");
                    wasConnected = false;
                    feedbackFaults = 0;
                    if (requireFeedback) feedback = new(FeedbackState.Waiting);
                    previousFeedback = feedback.State;
                    state.Stop("MQTT broker disconnected; re-arm required");
                    PublishSnapshot();

                    if (now >= nextBroker)
                    {
                        try
                        {
                            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                            deadline.CancelAfter(2000);
                            await mqtt.ConnectAsync(deadline.Token);
                            wasConnected = mqtt.Connected;
                            if (!wasConnected) throw new IOException("Broker closed the connection.");
                            armRequested = false;
                            stopRequested = true;
                            if (requireFeedback)
                                Log("INFO", "MQTT broker connected. Waiting for robot feedback.");
                            else
                                Log("INFO", "MQTT broker connected. Robot feedback is unavailable for this bridge.");
                        }
                        catch (Exception ex) when (!token.IsCancellationRequested)
                        {
                            var detail = ex switch
                            {
                                BrokerCredentialsException => "Broker rejected MQTT credentials.",
                                OperationCanceledException => "Connection or subscription timed out.",
                                SocketException socket => $"Socket error: {socket.SocketErrorCode}.",
                                _ => "Check broker availability and MQTT settings."
                            };
                            Log("ERROR", $"MQTT connection to {options.BrokerHost}:{options.BrokerPort} failed. {detail} Retrying in 2s.");
                            nextBroker = Environment.TickCount64 + 2000;
                            errors++;
                        }
                    }
                }

                now = Environment.TickCount64;
                if (requireFeedback && mqtt is { Connected: true })
                {
                    feedback = mqtt.Feedback(now);
                    var reset = feedbackFaults != feedback.FaultCount;
                    feedbackFaults = feedback.FaultCount;
                    if (reset || feedback.State != previousFeedback)
                    {
                        if (reset) Log("WARN", "Robot receiver reset or feedback was interrupted; disarmed.");
                        Log(feedback.State == FeedbackState.Ready ? "INFO" : "WARN", feedback.State switch
                        {
                            FeedbackState.Ready => "Robot ready. Fresh neutral input and explicit arm are required after a fault.",
                            FeedbackState.Stale => "Robot feedback is stale; disarmed.",
                            FeedbackState.NotReady => "Robot has not acknowledged a stop for this session; disarmed.",
                            _ => "Robot feedback is missing; waiting for robot."
                        });
                        armRequested = false;
                    }
                    if (reset || feedback.State != FeedbackState.Ready || previousFeedback != FeedbackState.Ready)
                    {
                        state.Stop(feedback.State == FeedbackState.Ready
                            ? "Robot ready; explicit arm required" : "Waiting for valid robot feedback; disarmed");
                        stopRequested = true; // discard queued controller commands at the recovery boundary
                    }
                    previousFeedback = feedback.State;
                }

                var canDrive = (mqtt is null || mqtt.Connected) &&
                    (!requireFeedback || feedback.State == FeedbackState.Ready) && (simulation || serial is not null);
                if (!canDrive && state.Armed) state.Stop("Connection unavailable; re-arm required");

                if (simulation)
                {
                    (int Left, int Right, long At) intent;
                    lock (gate) intent = simulationInput;
                    if (canDrive && !stopRequested && now - intent.At < 150)
                    {
                        lastInput = $"DRIVE,{intent.Left},{intent.Right}";
                        lastInputAt = intent.At;
                        state.Apply(lastInput, intent.At);
                    }
                    if (armRequested)
                    {
                        if (canDrive && !stopRequested) ApplyInput("ARM", now);
                        else Log("WARN", "Arm rejected: robot feedback is unavailable or stop is active.");
                    }
                }
                else if (serial is not null)
                {
                    try
                    {
                        if (stopRequested || !canDrive) serial.Clear();
                        else
                        {
                            foreach (var line in serial.Read())
                            {
                                lastInput = line;
                                lastInputAt = now;
                                var accepted = ApplyInput(line, now);
                                if (line == "STOP" || (!accepted && line != "ARM")) break;
                            }
                        }
                    }
                    catch (InvalidDataException ex)
                    {
                        state.Stop("Invalid controller frame; disarmed");
                        Log("WARN", ex.Message);
                        errors++;
                    }
                    catch (Exception) when (!token.IsCancellationRequested)
                    {
                        state.Stop("USB controller connection lost; re-arm required");
                        CloseController(serial);
                        serial = null;
                        nextSerial = now + 2000;
                        Log("ERROR", $"USB controller connection lost on {options.SerialPort}.");
                        errors++;
                    }
                }
                else if (mqtt is { Connected: true } && !requireFeedback)
                {
                    state.Stop("Waiting for USB controller; disarmed");
                }

                state.Tick(Environment.TickCount64);
                if (Volatile.Read(ref stop) != 0) state.Stop("Dashboard stop");
                var (left, right) = state.Output();
                left = left * options.MaxSpeedPercent / 100;
                right = right * options.MaxSpeedPercent / 100;
                if (mqtt is { Connected: true })
                {
                    try
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        deadline.CancelAfter(150);
                        await mqtt.SendAsync(state.Armed, left, right, deadline.Token);
                        payload = mqtt.LastPayload;
                        sent++;
                    }
                    catch (Exception) when (!token.IsCancellationRequested)
                    {
                        state.Stop("MQTT write failed; re-arm required");
                        Log("ERROR", "MQTT command write failed; disarmed.");
                        errors++;
                    }
                }
                else if (mqtt is null)
                {
                    payload = JsonSerializer.Serialize(new { simulation = true,
                        command = state.Armed ? "drive" : "stop", left, right });
                    sent++;
                }

                PublishSnapshot();
                await Task.Delay(50, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            state.Stop("Session stopped");
            if (mqtt is { Connected: true })
            {
                using var deadline = new CancellationTokenSource(500);
                try { await mqtt.SendAsync(false, 0, 0, deadline.Token); sent++; payload = mqtt.LastPayload; }
                catch { Log("WARN", "Final stop write failed. Robot watchdog must stop motors."); }
            }
            mqtt?.Dispose();
            if (serial is not null) CloseController(serial);
            if (openingSerial is not null) _ = ClosePendingControllerAsync(openingSerial);
            serial = null;
            feedback = new(requireFeedback ? FeedbackState.Waiting : previousFeedback);
            PublishSnapshot(false);
            Log("INFO", "Session stopped. Reconnect starts disarmed.");
            Volatile.Write(ref running, 0);
        }
    }

    private static ArduinoInput OpenController(AppOptions options)
    {
        var port = new ArduinoInput(options.SerialPort, options.BaudRate);
        try { port.Open(); return port; }
        catch { port.Dispose(); throw; }
    }

    private static void CloseController(ArduinoInput port) => _ = Task.Run(() =>
    {
        try { port.Dispose(); }
        catch (IOException) { } // The device may already have been removed.
    });

    private static async Task ClosePendingControllerAsync(Task<ArduinoInput> opening)
    {
        try { CloseController(await opening); }
        catch (Exception) { } // An abandoned attempt either failed or was cancelled before opening.
    }
}
