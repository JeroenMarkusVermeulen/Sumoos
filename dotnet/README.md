# Sumoos .NET controller gateway

Basic console application for Jeroen's communication component:
Arduino handheld controller -> USB serial -> this app -> Docker MQTT -> ESP32 -> Zumo.
The business website remains separate in ../website. This app runs on the laptop, not Vercel.

## Layout

- src/Sumoos.Core: motor input validation, arming and timeout rules (no hardware dependencies).
- src/Sumoos.Controller: console entry point, Configuration, Input and Communication adapters.
- tests/Sumoos.Checks: executable safety checks; failures return a nonzero exit code.
- docs/protocol.md: interface contract for Vlad and Oliver.
- Sumoos.sln: open in Visual Studio.

## Start without hardware

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
received a command. No robot telemetry, mode switching, GUI, database or match scoring yet.

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
They do not measure radio delivery or motor stopping distance.
Communication/RobotPublisher is a deliberately narrow MQTT 3.1.1 publisher, not a
general MQTT library. It supports broker username/password, clean sessions and QoS 0.
No TLS, subscriptions or offline storage; use only on the trusted project LAN.

Local package downloads were unavailable during development. The complete source was
compiled/tested in a temporary verification copy using the cached Windows serial
assembly. The committed project uses normal NuGet PackageReference for System.IO.Ports.
GitHub Actions performs the standard restore/build/checks on a clean Windows runner.
