# Sumoos .NET controller gateway

Windows desktop dashboard and optional console gateway for Jeroen's communication component:
Arduino handheld controller -> USB serial -> this app -> Docker MQTT -> ESP32 -> Zumo.
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

Simulation is selected by default. Click Start session, then Arm simulation.
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

For live operation, disconnect first, deselect Simulation, set the Arduino port and broker
settings, then start. The on-screen pad is disabled in live mode: only the physical
Arduino controller can arm and drive. The dashboard pre-fills credentials from `MQTT_USERNAME`
and `MQTT_PASSWORD` in a `.env` file in the repository or a parent directory; if a key is
missing, its corresponding `SUMOOS_MQTT_USERNAME` or `SUMOOS_MQTT_PASSWORD` environment
variable is used instead. Passwords are not saved or included in logs. Settings apply to the
current session; edit appsettings.json for persistent defaults.

Live hardware still requires compatible Arduino/ESP32 firmware and a robot-side watchdog.
These indicators are gateway state and outgoing intent, not robot telemetry.
Do not run the dashboard and console gateway simultaneously for the same robot.

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
   the ESP32 instead uses the laptop's LAN IP. Default speed limit is 30%.
5. Flash Arduino firmware implementing docs/protocol.md. Close Arduino Serial Monitor
   so this app can open the port. The firmware is a separate team task.
6. Run:

       dotnet run --project src/Sumoos.Controller

7. Start neutral, press the Arduino ARM button once, then drive. Send STOP from the
   controller or type stop. A new neutral report and ARM are needed after a fault.

Port/broker failures retry every two seconds and disarm the controller.
The console reports local state and broker connectivity; it does not claim that a robot
received a command. The console remains available for terminal use. No robot telemetry, mode switching, database or match scoring yet.

## Hardware acceptance

Lift tracks and use low speed initially. Verify correct motor direction, neutral arming,
manual stop, invalid input, unplugged controller, broker shutdown, Wi-Fi loss,
ESP32 reset and reconnection without motion. Verify boundary override on the Zumo.
The gateway's stop is best effort. Robot-side timeout and boundary protection are mandatory.
Never expose the plaintext MQTT port to the public internet; this baseline is for a trusted LAN.

## Verification

Run the checks above. Real Arduino/ESP32/Zumo end-to-end testing needs the hardware.
The checks exercise state logic plus real TCP MQTT CONNECT/PUBLISH against a local
fake broker, including topic, sequence, session, expiry and non-retained QoS 0 flags.
The dashboard-session checks cover arming, speed scaling, stop precedence, heartbeat
loss, explicit re-arming and cancellation. The CI smoke test loads and lays out the
actual WPF window and verifies clean shutdown, catching runtime XAML binding failures.
They do not measure radio delivery or motor stopping distance.
Communication/RobotPublisher is a deliberately narrow MQTT 3.1.1 publisher, not a
general MQTT library. It supports broker username/password, clean sessions and QoS 0.
No TLS or offline storage; status subscription is enabled in Docker simulation only; use only on the trusted project LAN.

Local package downloads were unavailable during development. The complete source was
compiled/tested in a temporary verification copy using the cached Windows serial
assembly. The committed project uses normal NuGet PackageReference for System.IO.Ports.
GitHub Actions performs the standard restore/build/checks on a clean Windows runner.

## VS Code: missing InitializeComponent or named controls

The dashboard's Directory.Build.targets runs WPF markup generation for language-server
design-time compilation outside Visual Studio. This provides the actual generated partial
classes to C# IntelliSense; it does not suppress diagnostics, add fake controls, or change XAML.

After pulling this fix, open the repository folder (not just an individual .cs file), run
`dotnet build dotnet/Sumoos.sln` from the repository root, then use
**Ctrl+Shift+P → Developer: Reload Window**. If opening only the dotnet folder, use
`dotnet build Sumoos.sln` instead. Wait for C# project loading to finish.

Run the interface as before: `dotnet run --project dotnet/src/Sumoos.Dashboard`
from the repository root. UI layout and robot behavior are unchanged.

## Docker-connected simulation

The separate [simulation guide](../simulation/README.md) runs a real authenticated MQTT broker
and simulated robot. Select both Simulation and Use Docker MQTT simulator, set port 1884,
and enter the credentials from simulation/.env. Input and packet shows returned robot status.
Missing feedback (750 ms), receiver reset or broker loss disarms the gateway; fresh neutral
input and explicit re-arming are required. Offline simulation and live Arduino mode remain available.
