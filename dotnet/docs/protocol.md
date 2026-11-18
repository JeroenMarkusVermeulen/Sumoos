# Proposed v1 controller contract

This is the initial software interface, not a claim that existing firmware supports it.
Vlad and Oliver must implement this contract or agree a change with Jeroen before integration.

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

## MQTT: laptop to ESP32

Topic: sumoos/bot-2/command (RobotId is configurable).
QoS 0, retain false, clean session, no application command backlog.
Fresh message approximately every 50 ms while connected.
Example (real timestamps/session/sequence vary):

    {"version":1,"robotId":"bot-2","sessionId":"unique-process-id","sequence":12,
     "sentAtUnixMs":1795000000000,"ttlMs":250,"command":"drive","left":12,"right":12}

command is drive or stop; stop always has zero output. Values include the configured
speed limit (30% default), so a joystick value 40 produces motor percentage 12.
Convert percentages to the Zumo motor library scale in firmware.

ESP32 MUST reject retained, malformed, wrong-version, wrong-robot, expired and
out-of-order messages. Synchronize clocks to use sentAtUnixMs; reject commands if
clock validity is unknown. Track sequence per session and require a stop before
accepting a new session. No old-session motion may be accepted after switching.
Do not repeatedly forward an old drive command to keep a Zumo watchdog alive.
No MQTT delivery acknowledgement should be interpreted as physical motor success.

## ESP32 to Zumo and independent safety

Forward only fresh validated commands via UART. Proposed existing UART format:
DRIVE,left,right or STOP, with LF terminator and 115200 8N1.
Firmware must convert the documented percentages consistently; do not silently
interpret 100% as a library-specific PWM value of 100.
Zumo must stop within 250 ms without fresh valid input, start stopped after reset,
and give ring boundary detection priority over remote driving.
Wi-Fi/MQTT disconnect and expired input must stop forwarding motion and send STOP.
A killed laptop process cannot guarantee delivery of STOP; the local watchdog is essential.
Autonomous decisions remain on the Zumo; this baseline controls the manual bot only.
