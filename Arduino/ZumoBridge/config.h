#pragma once
// Copy this file to config.h and fill in your own values. config.h is git-ignored (no passwords in git).

// --- Wi-Fi (the same network the laptop is on) ---
const char WIFI_SSID[] = "your-wifi-name";
const char WIFI_PASS[] = "your-wifi-password";   // "" for an open network

// --- MQTT broker: the REAL hardware broker from infrastructure/compose.yaml (port 1883) ---
const char MQTT_HOST[] = "192.168.0.10";          // the laptop's LAN IP, not localhost
const uint16_t MQTT_PORT = 1883;
const char MQTT_USER[] = "";
const char MQTT_PASS[] = "";
const char ROBOT_ID[] = "bot-2";                  // must equal RobotId in the gateway's appsettings.json

// --- Link to the Zumo (Serial1 = MKR pins 13 RX / 14 TX) ---
const uint32_t ZUMO_BAUD = 115200;

// --- Safety ---
const uint16_t COMMAND_TIMEOUT_MS = 250;          // no fresh valid drive for this long -> STOP
// protocol.md: reject expired commands. This needs the laptop and this board to agree on the time.
const bool CHECK_EXPIRY = true;
const int16_t CLOCK_SKEW_MS = 100;                // allowed clock difference laptop <-> board (set CHECK_EXPIRY=false to bench-test)
