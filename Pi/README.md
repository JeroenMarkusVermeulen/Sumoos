# Raspberry Pi robot bridge

Runs on a Raspberry Pi (with Wi-Fi) mounted on the manually controlled Zumo 32U4. No soldering and no
changes to the Zumo: the Pi connects to the Zumo's micro-USB port with a normal USB **data** cable.

    gateway (laptop) -> MQTT broker -> Wi-Fi -> Raspberry Pi -> USB -> Zumo 32U4

`robot_bridge.py` validates every command with the same model as the Docker simulator
(`../simulation/robot/model.py`) and forwards only fresh valid ones as `DRIVE,l,r` / `STOP`.
Losing the broker, Wi-Fi or USB cable stops the robot; it needs a new `stop` handshake afterwards.
The bridge publishes [v1 robot status](../dotnet/docs/protocol.md#mqtt-status-docker-simulator-and-raspberry-pi-bridge)
on `sumoos/{ROBOT_ID}/status`. Select **Raspberry Pi (feedback required)** in the dashboard's live
mode. Readiness requires a validated STOP and a completed USB write. The Zumo sketch does not
report motor values, boundary events or its boot ID, so a reset that leaves USB writable can go
undetected by the bridge. Verify USB reset behavior and the Zumo watchdog during bench testing.

## Pi Zero / Zero W: which micro-USB port is which

The Zero has two micro-USB ports next to the mini-HDMI: the **middle one (USB)** is the data/OTG port for the
Zumo, and it needs a micro-USB OTG adapter (micro-USB plug -> USB-A socket) plus the USB-A <-> micro-USB data
cable to the Zumo. The **outer one (PWR IN)** is only for the 5 V power bank. Do not swap them. The Zero W has
2.4 GHz Wi-Fi only (no 5 GHz). Put foam or tape under it: the soldered pins on its underside must not touch the Zumo.

## Pi 3 / 4 / 5

They have full-size USB-A ports: use a USB-A <-> micro-USB **data** cable straight to the Zumo, no OTG adapter.
A Pi 4 needs a 5 V / 3 A USB-C supply (a power bank with USB-C output); a Pi 5 needs 5 V / 5 A (USB-C PD).

## Zumo firmware

Flash `../Arduino/Zumo32U4/Zumo32U4.ino` with `LINK` = `Serial` and `USE_OLED` = `true` (the defaults in
`config.h`): the commands arrive over the USB port, so the OLED and pins 0/1 stay untouched.

## Set up the Pi (once)

1. Flash Raspberry Pi OS Lite with Raspberry Pi Imager; set Wi-Fi (the same network as the laptop),
   a user name and enable SSH in the Imager's settings.
2. Over SSH:

       sudo apt update && sudo apt install -y git python3-pip python3-venv
       git clone https://github.com/JeroenMarkusVermeulen/Sumoos.git
       cd Sumoos/Pi
       python3 -m venv .venv && . .venv/bin/activate
       pip install -r requirements.txt
       cp bridge.env.example bridge.env && nano bridge.env

   Fill in `BROKER_HOST` (the laptop's LAN IP), `MQTT_USERNAME` / `MQTT_PASSWORD` of the real hardware
   broker (`../infrastructure`, port 1883) and `ROBOT_ID` (must equal RobotId in the gateway).
3. Plug the Zumo into the Pi (USB data cable) and check the port: `ls /dev/ttyACM*` (normally
   `/dev/ttyACM0`; set `SERIAL_PORT` if different). The default Pi user is already in the `dialout` group.

## Run

    cd ~/Sumoos/Pi && . .venv/bin/activate && python3 robot_bridge.py

Expect `Connected to broker ...`, then start the live session on the laptop. The first STOP command
opens the Zumo port if available. Expect `Zumo connected on /dev/ttyACM0` and **Robot ready** in the
dashboard once the bridge reports readiness. Keep the tracks lifted for the first test and use a low speed limit.

## Troubleshooting

- `rejected a command (age ... ms)`: the clocks of laptop and Pi differ. Make sure both sync time
  (`timedatectl` on the Pi). If the age is a stable offset, set `CLOCK_OFFSET_MS` to compensate. For a quick
  bench test only, `IGNORE_EXPIRY=1` switches the expiry check off.
- Nothing arrives: the laptop firewall must allow TCP 1883, and school Wi-Fi may block device-to-device traffic.
- `Zumo link lost` repeats: bad or charge-only cable, or the Serial Monitor of the Arduino IDE has the port open.
