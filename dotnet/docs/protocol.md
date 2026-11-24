# Sumoos v1 communication protocol

The checked-in MKR handheld, Pi bridge, MKR bridge and Zumo 32U4 use this command
contract. Agree changes with the controller, gateway and robot owners before updating firmware.

## USB serial: Arduino to laptop

115200 baud, 8N1, ASCII, newline terminated (LF or CRLF), maximum 64 characters per line.
Send fresh joystick input every 50 ms; do not buffer/replay old input.
Left/right are signed percentages -100..100. Positive means forward.

    DRIVE,0,0
    ARM
    DRIVE,40,40
    DRIVE,-30,30
    STOP

Send ARM only on a deliberate button press, never continuously or automatically.
ARM requires a neutral report less than 250 ms old. A non-neutral joystick cannot arm.
Invalid input disarms. Input loss for 250 ms disarms; driving does not rearm.
STOP and boundary protection must override movement. Run only one gateway per robot.

## MQTT commands: laptop to Pi or MKR bridge

Topic: sumoos/bot-2/command (RobotId is configurable).
QoS 0, retain false, clean session, no application command backlog.
Fresh message approximately every 50 ms while connected.
Example (real timestamps/session/sequence vary):

    {"version":1,"robotId":"bot-2","sessionId":"unique-process-id","sequence":12,
     "sentAtUnixMs":1795000000000,"ttlMs":250,"command":"drive","left":12,"right":12}

command is drive or stop; stop always has zero output. Values include the configured
speed limit (30% default), so a joystick value 40 produces motor percentage 12.
Convert percentages to the Zumo motor library scale in firmware.

The bridge must reject retained, malformed, wrong-version, wrong-robot, expired and
out-of-order messages. Synchronize clocks to use sentAtUnixMs; reject commands if
clock validity is unknown. Track sequence per session and require a stop before
accepting a new session. No old-session motion may be accepted after switching.
Forward each accepted drive once. The Zumo watchdog depends on receiving fresh drive commands.

## Pi or MKR bridge to Zumo and independent safety

Forward fresh validated commands to the Zumo 32U4 over USB serial (Pi) or UART
(MKR bridge). Send `DRIVE,left,right` or `STOP` with LF at 115200 baud, 8N1.
Firmware must convert the documented percentages consistently; do not silently
interpret 100% as a library-specific PWM value of 100.
Zumo must stop within 250 ms without fresh valid input, start stopped after reset,
and give ring boundary detection priority over remote driving.
Wi-Fi/MQTT disconnect and expired input must stop forwarding motion and send STOP.
A killed laptop process cannot guarantee delivery of STOP; the local watchdog is essential.
Autonomous decisions remain on the Zumo; this baseline controls the manual bot only.

## MQTT status: Docker simulator and Raspberry Pi bridge

Publish version 1 JSON on `sumoos/{robotId}/status` about every 50 ms using QoS 0
with retain disabled. The Pi example below acknowledges STOP and has no motor telemetry.

```json
{"version":1,"robotId":"bot-2","simulated":false,"bootId":"bridge-generation-id",
 "sessionId":"gateway-session","sequence":17,"ready":true,"left":null,"right":null,"reason":"stopped"}
```

| Field | Meaning |
| --- | --- |
| `version`, `robotId`, `simulated` | Schema version `1`, selected robot ID, and `true` for the Docker robot or `false` for live hardware. |
| `bootId` | Nonempty receiver generation ID, up to 100 characters. Generate a new ID at receiver startup, on broker reconnect and when the Pi detects loss of the Zumo USB link. |
| `sessionId`, `sequence` | Last validated command session and sequence at the bridge or simulator. Before a command is accepted, send an empty session ID and sequence `0`. Session IDs are at most 100 characters. |
| `ready` | The receiver accepted a STOP handshake and can accept a fresh drive in that session. The Pi also requires a successful USB STOP write. A write confirms the bridge path as far as the USB driver. |
| `left`, `right` | Reported signed motor percentages from −100 to 100, or `null`/omitted if unavailable. Docker reports simulated values. Pi sends `null` until the Zumo can report motor values. |
| `reason` | Receiver state or stop reason, up to 160 characters without control characters, or `null`/omitted if unavailable. Zumo-local stop or boundary events require firmware telemetry. |

Docker and Pi modes listen for a valid report on the selected robot's topic. They
require `ready: true` for the current gateway session and confirmation of a command
from the current broker connection before accepting ARM. The gateway calculates
feedback age from local monotonic receipt time. A report at least 750 ms old, a
receiver generation change, a not-ready report or a lost connection disarms the
gateway. After recovery the operator must provide fresh neutral input and arm again.

The MKR bridge currently accepts commands without publishing status. In the explicit
MKR command-only mode, feedback stays unavailable. Physical motion and the independent
250 ms Zumo watchdog must be verified on the hardware in either live mode.
