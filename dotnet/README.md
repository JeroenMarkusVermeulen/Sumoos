# Sumoos .NET controller gateway

Windows desktop dashboard and optional console gateway for Jeroen's communication component:
Arduino MKR handheld -> USB serial -> this app -> Docker MQTT -> Pi or MKR bridge -> Zumo 32U4.
The business website remains separate in ../website. This app runs on the laptop, not Vercel.

## Layout

- src/Sumoos.Core: motor input validation, arming and timeout rules (no hardware dependencies).
- src/Sumoos.Dashboard: WPF interface, styles and display view model.
- src/Sumoos.Controller: optional console entry point, Configuration, Input, Communication and Services adapters.
- tests/Sumoos.Checks: executable safety checks; failures return a nonzero exit code.
- docs/protocol.md: interface contract for Vlad and Oliver.
- Sumoos.sln: open in Visual Studio.

## Open the dashboard (Windows)

From this dotnet folder:

    dotnet run --project src/Sumoos.Dashboard

Or open Sumoos.sln in Visual Studio, set Sumoos.Dashboard as the startup project and press F5.
VS Code can launch it from its PowerShell terminal with the same command.

Offline simulation is selected by default. Click Start session, then Arm simulation.
Hold a directional button to drive; release it to return to neutral.
Pause input deliberately to verify the 250 ms timeout. Resume input and explicitly
re-arm afterward. Switching away from the window requests a stop.

The dashboard provides:
- Broker/Arduino connection state, arming reason and input age.
- Signed outgoing motor percentages and the configured speed limit.
- Last controller line, latest JSON payload, frame/write count and error count.
- Timestamped event log (latest 500 entries), clear and text export.
- Broker host/port, USB port discovery, robot ID, credentials and speed configuration.
- Start/disconnect and a prominent stop button.

### Operating modes

| Dashboard selection | Controller | Bridge and feedback |
| --- | --- | --- |
| **Simulation** | On-screen pad | Offline. No broker or robot feedback. |
| **Simulation** + **Use Docker MQTT simulator** | On-screen pad | Docker MQTT simulator with simulated feedback required. |
| **Simulation** cleared, **Raspberry Pi (feedback required)** | MKR handheld over USB | Pi to Zumo 32U4 over USB. Valid Pi feedback required. |
| **Simulation** cleared, **MKR (command only)** | MKR handheld over USB | MKR bridge to Zumo 32U4 over UART. Feedback unavailable. |

Disconnect before changing modes, bridge, robot ID or other connection settings.
In live mode, select the handheld's COM port and close Serial Monitor before starting.
Start the [hardware broker](../README.md#start-the-broker) and follow the
[Pi setup](../Pi/README.md) or [MKR bridge sketch](../Arduino/ZumoBridge/ZumoBridge.ino).
Set the dashboard broker to `localhost:1883` if Docker is on this laptop and use the
laptop's LAN IP on the bridge. Match the robot ID on both ends. The handheld's neutral
input and ARM action control live arming.

The dashboard pre-fills hardware credentials from `MQTT_USERNAME`
and `MQTT_PASSWORD` in a `.env` file in the repository or a parent directory; if a key is
missing, its corresponding `SUMOOS_MQTT_USERNAME` or `SUMOOS_MQTT_PASSWORD` environment
variable is used instead. The session uses the displayed connection settings.
Edit `appsettings.json` for persistent endpoint defaults.

Check **Controller USB**, **MQTT broker** and **Robot feedback** separately. In Pi and
Docker modes **Waiting for robot** means the broker is up and the stop handshake is
pending. **Input and packet** shows feedback age, robot ID, last accepted sequence,
reported motor values and the receiver's reason. The command panel shows what the
gateway is sending. The Pi reports bridge readiness and leaves motor values unavailable
until the Zumo firmware provides telemetry. Use one gateway per robot.

Use **Export log** to save timestamped events when diagnosing a failure. A busy
COM port, broker connection failure, rejected credentials and stale feedback have
separate messages. Keep local credential files out of exported diagnostics.

## Optional console: start without hardware

Install the .NET 8 SDK (the SDK available for verification on this machine).
This baseline targets net8.0; upgrade to a supported .NET LTS before production use.

From this dotnet folder:

    dotnet build Sumoos.sln
    dotnet run --project tests/Sumoos.Checks
    dotnet run --project src/Sumoos.Controller -- --simulate

Simulation uses no serial port or broker. Enter DRIVE,0,0, then ARM.
Commands must keep arriving within 250 ms to keep the robot armed. Typing manually is
normally too slow for continuous driving; the automated checks exercise motion and timeout.
Enter status, stop or quit. Ctrl+C attempts a final stop.
Simulation output reports intended motor commands, not measured robot motion.

## Run with Arduino and MQTT

1. Start the broker from ../infrastructure using its existing compose.yaml.
   Its mosquitto.conf requires a passwords file. Create that using mosquitto_passwd
   as described in your broker setup; do not commit it.
2. Set the broker username/password in your local shell (not in git):

       $env:SUMOOS_MQTT_USERNAME = "your-broker-user"
       $env:SUMOOS_MQTT_PASSWORD = "<your-local-password>"

3. Find the controller port:

       dotnet run --project src/Sumoos.Controller -- --ports

4. Edit src/Sumoos.Controller/appsettings.json: SerialPort (e.g. COM3), BrokerHost,
   BrokerPort and RobotId. For a broker running on this laptop, localhost is correct;
   the bridge uses the laptop's LAN IP. Default speed limit is 30%.
5. Flash the appropriate checked-in sketches and close Arduino Serial Monitor.
6. Run with feedback for the Pi. Omit `--pi-feedback` for the MKR command-only bridge.

       dotnet run --project src/Sumoos.Controller -- --pi-feedback

7. Start neutral, press the Arduino ARM button once, then drive. Send STOP from the
   controller or type stop. A new neutral report and ARM are needed after a fault.

Port/broker failures retry every two seconds and disarm the controller.
The console reports local state and broker connectivity. Pi mode also gates arming on
fresh feedback from the bridge.

## Hardware acceptance

Lift tracks and use low speed initially. Verify correct motor direction, neutral arming,
manual stop, invalid input, unplugged controller, broker shutdown, Wi-Fi loss,
bridge and Zumo resets, and reconnection without motion. Verify boundary override on the Zumo.
The gateway's stop is best effort. Robot-side timeout and boundary protection are mandatory.
Never expose the plaintext MQTT port to the public internet; this baseline is for a trusted LAN.

## Verification

The .NET checks use sample controller frames and a local TCP broker to exercise command
framing, status validation, feedback expiry, receiver reset, broker reconnect and explicit
re-arming. CI also runs the WPF smoke test and VS Code design-time XAML check. The
[Docker integration tests](../simulation/README.md#automated-checks) cover real MQTT with
the simulated robot. End-to-end USB, Wi-Fi and mechanical verification needs the actual hardware.

## VS Code: missing InitializeComponent or named controls

The dashboard's Directory.Build.targets runs WPF markup generation for language-server
design-time compilation outside Visual Studio. This provides the actual generated partial
classes to C# IntelliSense; it does not suppress diagnostics, add fake controls, or change XAML.

After pulling this fix, open the repository folder (not just an individual .cs file), run
`dotnet build dotnet/Sumoos.sln` from the repository root, then use
**Ctrl+Shift+P → Developer: Reload Window**. If opening only the dotnet folder, use
`dotnet build Sumoos.sln` instead. Wait for C# project loading to finish.

Run the interface as before: `dotnet run --project dotnet/src/Sumoos.Dashboard`
from the repository root.

## Docker-connected simulation

The separate [simulation guide](../simulation/README.md) runs an authenticated MQTT broker
and simulated robot. Select **Simulation** and **Use Docker MQTT simulator**. The dashboard
defaults to `127.0.0.1:1884` and fills in `SIM_MQTT_USERNAME` and `SIM_MQTT_PASSWORD`
from the root `.env` or the corresponding environment variables. **Input and packet**
shows returned robot status.
Missing feedback (750 ms), receiver reset or broker loss disarms the gateway; fresh neutral
input and explicit re-arming are required. Offline simulation and live Arduino mode remain available.
