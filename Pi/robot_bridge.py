#!/usr/bin/env python3
"""Sumoos robot bridge for a Raspberry Pi mounted on the manually controlled Zumo 32U4.

    gateway on the laptop --MQTT/Wi-Fi--> THIS PROGRAM --USB serial--> Zumo 32U4 (Arduino/Zumo32U4)

It subscribes to sumoos/<ROBOT_ID>/command and validates every message with the SAME rules as the
Docker simulator (simulation/robot/model.py): not retained, version 1, robot id, session handshake,
increasing sequence, ttl/expiry, motor range. Only fresh valid commands are forwarded to the Zumo as
    DRIVE,<left>,<right>     or     STOP       (115200 8N1, LF terminated, -100..100)
An old drive command is never repeated: when no fresh valid one arrives for 250 ms (the model's
watchdog), this program sends STOP. The Zumo firmware has its own 250 ms watchdog as well.
Losing the broker, the Wi-Fi or the USB cable stops the robot and needs a new "stop" handshake.

Settings come from environment variables or from bridge.env next to this file (see bridge.env.example).
"""
import json
import os
import sys
import threading
import time
from pathlib import Path

import paho.mqtt.client as mqtt
import serial

# reuse the simulator's validation model, so the real bridge and the tested simulator cannot drift apart
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "simulation" / "robot"))
from model import Robot  # noqa: E402


def load_env_file(path):
    if not path.exists():
        return
    for raw in path.read_text(encoding="utf-8").splitlines():
        raw = raw.strip()
        if not raw or raw.startswith("#") or "=" not in raw:
            continue
        key, value = raw.split("=", 1)
        os.environ.setdefault(key.strip(), value.strip())


load_env_file(Path(__file__).with_name("bridge.env"))

BROKER_HOST = os.environ.get("BROKER_HOST", "")
BROKER_PORT = int(os.environ.get("BROKER_PORT", "1883"))
MQTT_USERNAME = os.environ.get("MQTT_USERNAME", "")
MQTT_PASSWORD = os.environ.get("MQTT_PASSWORD", "")
ROBOT_ID = os.environ.get("ROBOT_ID", "bot-2")
SERIAL_PORT = os.environ.get("SERIAL_PORT", "/dev/ttyACM0")
SERIAL_BAUD = 115200
IGNORE_EXPIRY = os.environ.get("IGNORE_EXPIRY", "0") == "1"
CLOCK_OFFSET_MS = float(os.environ.get("CLOCK_OFFSET_MS", "0"))

if not BROKER_HOST:
    sys.exit("Set BROKER_HOST (the laptop's LAN IP) in bridge.env, see bridge.env.example")

robot = Robot(ROBOT_ID)
lock = threading.Lock()
last_sent_drive = False   # the Zumo is running on a DRIVE we forwarded
last_log = 0.0


def log(message):
    print(time.strftime("%H:%M:%S"), message, flush=True)


class ZumoLink:
    """USB serial to the Zumo. Reconnects by itself; every failure means 'not sent'."""

    def __init__(self, path, baud):
        self.path, self.baud = path, baud
        self.port = None
        self.last_try = 0.0

    def ensure(self):
        if self.port is not None:
            return True
        if time.monotonic() - self.last_try < 2:
            return False
        self.last_try = time.monotonic()
        try:
            self.port = serial.Serial(self.path, self.baud, timeout=0, write_timeout=0.1)
            log(f"Zumo connected on {self.path}")
            return True
        except (serial.SerialException, OSError):
            return False

    def send(self, line):
        if not self.ensure():
            return False
        try:
            self.port.write((line + "\n").encode("ascii"))
            return True
        except (serial.SerialException, OSError):
            log("Zumo link lost")
            try:
                self.port.close()
            except OSError:
                pass
            self.port = None
            return False


link = ZumoLink(SERIAL_PORT, SERIAL_BAUD)


def forward():
    """Send the robot's current state to the Zumo. Call with the lock held."""
    global last_sent_drive
    driving = robot.reason == "drive"
    line = f"DRIVE,{robot.left},{robot.right}" if driving else "STOP"
    sent = link.send(line)
    if driving and not sent:
        robot.stop("zumo link down")          # cannot reach the Zumo: do not stay 'driving'
        driving = False
    last_sent_drive = driving


def on_connect(client, userdata, flags, reason, properties):
    with lock:
        robot.stop("broker connected; stop handshake required")
        forward()
    if reason.is_failure:
        log(f"Broker refused the connection: {reason}")
        return
    client.subscribe(f"sumoos/{ROBOT_ID}/command", qos=0)
    log(f"Connected to broker {BROKER_HOST}:{BROKER_PORT}, listening on sumoos/{ROBOT_ID}/command")


def on_disconnect(client, userdata, flags, reason, properties):
    with lock:
        robot.stop("broker disconnected")
        forward()
    log("Broker disconnected: robot stopped")


def on_message(client, userdata, message):
    global last_log
    with lock:
        try:
            payload = json.loads(message.payload)
            wall = time.time() * 1000 + CLOCK_OFFSET_MS
            if IGNORE_EXPIRY and isinstance(payload, dict) and isinstance(payload.get("sentAtUnixMs"), (int, float)):
                wall = payload["sentAtUnixMs"]     # age = 0: bench test only
            if not robot.apply(payload, retained=message.retain, wall=wall):
                if time.monotonic() - last_log > 1:
                    last_log = time.monotonic()
                    sent = payload.get("sentAtUnixMs") if isinstance(payload, dict) else None
                    age = f"{wall - sent:.0f} ms" if isinstance(sent, (int, float)) else "n/a"
                    log(f"rejected a command (age {age}); a new stop is needed")
        except (ValueError, UnicodeDecodeError):
            robot.stop("invalid JSON")
        forward()


def main():
    log(f"Sumoos bridge for {ROBOT_ID}; expiry check {'OFF (bench test)' if IGNORE_EXPIRY else 'on'}")
    client = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id=f"sumoos-pi-{ROBOT_ID}-{os.getpid()}")
    if MQTT_USERNAME:
        client.username_pw_set(MQTT_USERNAME, MQTT_PASSWORD)
    client.on_connect = on_connect
    client.on_disconnect = on_disconnect
    client.on_message = on_message
    client.reconnect_delay_set(1, 3)
    client.connect_async(BROKER_HOST, BROKER_PORT, keepalive=10)
    client.loop_start()
    try:
        while True:
            with lock:
                robot.tick()                       # 250 ms without a fresh drive -> stop
                if last_sent_drive and robot.reason != "drive":
                    forward()                      # tell the Zumo to STOP now
            link.ensure()
            time.sleep(0.02)
    except KeyboardInterrupt:
        pass
    finally:
        with lock:
            robot.stop("shutdown")
            forward()
        client.disconnect()
        client.loop_stop()


if __name__ == "__main__":
    main()
