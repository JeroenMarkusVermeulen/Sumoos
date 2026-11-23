/*
 * Sumoos robot bridge (second Arduino MKR WiFi 1010, mounted on the manually controlled Zumo 32U4)
 *
 *   gateway on the laptop --MQTT/Wi-Fi--> THIS BOARD --UART text--> Zumo 32U4 (Arduino/Zumo32U4)
 *
 * It subscribes to  sumoos/<ROBOT_ID>/command  (JSON from the gateway, see dotnet/docs/protocol.md),
 * validates every message like simulation/robot/model.py does, and forwards ONLY fresh valid commands
 * to the Zumo as plain lines:   DRIVE,<left>,<right>   or   STOP     (115200 8N1, LF, -100..100).
 * It never repeats an old drive command: if no fresh valid one arrives for COMMAND_TIMEOUT_MS it
 * sends STOP. The Zumo also has its own 250 ms watchdog.
 *
 * Wiring (power off while wiring). The MKR is 3.3 V and NOT 5 V tolerant, the Zumo 32U4 is 5 V:
 *   Zumo TX (pin 1)  --[ divider 10k / 20k: 5 V -> 3.3 V ]--> MKR pin 13 (RX)
 *   MKR pin 14 (TX)  ---------------------------------------> Zumo RX (pin 0)   (3.3 V is read as HIGH by a 5 V AVR)
 *   GND              ---------------------------------------> GND
 * Power the MKR from the Zumo's 5 V rail (VIN/5V pin) or its own USB while testing, never both at once.
 * WARNING: on the Zumo 32U4 OLED, Zumo pins 0 and 1 are used by the OLED display (Pololu user guide,
 * section 3.11), so this exact wiring only works with the display removed. Check the guide first.
 *
 * USB Serial (115200) prints why a command was accepted/rejected; it is not used for commands.
 *
 * Checks (each failure: forward STOP and need a new "stop" command first):
 *   not retained, version 1, robotId matches, sessionId text, sequence > 0, ttlMs 1..250,
 *   not stale (needs the clock), session not retired, a NEW session must start with "stop",
 *   sequence strictly increasing, left/right integers -100..100, stop has zero output,
 *   drive only after an accepted stop.
 */

#define ARDUINOJSON_USE_LONG_LONG 1
#include <WiFiNINA.h>
#include <WiFiUdp.h>
#include <ArduinoMqttClient.h>
#include <ArduinoJson.h>
#include "config.h"

WiFiClient net;
MqttClient mqtt(net);
WiFiUDP ntpUdp;

String topicCommand;

/* ---------- state mirrors simulation/robot/model.py ---------- */

String session;
const uint8_t RETIRED_MAX = 8;
String retired[RETIRED_MAX];
uint8_t retiredCount = 0, retiredNext = 0;
int64_t lastSequence = 0;
bool ready = false;                    // true after an accepted "stop"; drive needs it
bool forwardingDrive = false;          // the Zumo is currently running on our last DRIVE
uint32_t lastDriveAt = 0;

bool isRetired(const String &s) {
  for (uint8_t i = 0; i < retiredCount; ++i)
    if (retired[i] == s) return true;
  return false;
}

void retire(const String &s) {
  retired[retiredNext] = s;
  retiredNext = (retiredNext + 1) % RETIRED_MAX;
  if (retiredCount < RETIRED_MAX) ++retiredCount;
}

/* ---------- time (NTP with milliseconds, for the expiry check) ---------- */

bool clockValid = false;
int64_t clockBaseMs = 0;               // Unix ms at clockBaseMillis
uint32_t clockBaseMillis = 0;
uint32_t lastSyncTry = 0, lastSyncOk = 0;
const char *const NTP_SERVERS[] = {"pool.ntp.org", "time.google.com", "dk.pool.ntp.org"};
uint8_t ntpIdx = 0;

int64_t nowUnixMs() { return clockBaseMs + (uint32_t)(millis() - clockBaseMillis); }

bool ntpSync() {
  uint8_t pkt[48] = {0};
  pkt[0] = 0xE3;                       // unsynced, version 4, client
  ntpUdp.begin(2390);
  const char *host = NTP_SERVERS[ntpIdx];
  ntpIdx = (ntpIdx + 1) % (sizeof(NTP_SERVERS) / sizeof(NTP_SERVERS[0]));
  if (!ntpUdp.beginPacket(host, 123)) { ntpUdp.stop(); return false; }
  const uint32_t t0 = millis();
  ntpUdp.write(pkt, sizeof(pkt));
  ntpUdp.endPacket();
  while (millis() - t0 < 1500) {
    if (ntpUdp.parsePacket() >= 48) {
      const uint32_t t1 = millis();
      ntpUdp.read(pkt, sizeof(pkt));
      ntpUdp.stop();
      const uint32_t secs = ((uint32_t)pkt[40] << 24) | ((uint32_t)pkt[41] << 16) | ((uint32_t)pkt[42] << 8) | pkt[43];
      const uint32_t frac = ((uint32_t)pkt[44] << 24) | ((uint32_t)pkt[45] << 16) | ((uint32_t)pkt[46] << 8) | pkt[47];
      if (secs <= 2208988800UL) return false;
      const int64_t serverMs = (int64_t)(secs - 2208988800UL) * 1000 + (((uint64_t)frac * 1000) >> 32);
      clockBaseMs = serverMs + (t1 - t0) / 2;        // assume the reply took half the round trip
      clockBaseMillis = t1;
      return true;
    }
  }
  ntpUdp.stop();
  return false;
}

// resync when unknown (every 5 s) or old (every 5 min), but never while the robot is driving
void updateClock() {
  const uint32_t now = millis();
  const bool idle = !forwardingDrive && now - lastDriveAt > 2000;
  if (clockValid && !(idle && now - lastSyncOk > 300000UL)) return;
  if (now - lastSyncTry < 5000 && lastSyncTry) return;
  lastSyncTry = now;
  if (ntpSync()) {
    clockValid = true;
    lastSyncOk = millis();
    Serial.println("Clock synced");
  } else if (!clockValid) {
    Serial.println("Clock sync failed (UDP 123 blocked?)");
  }
}

/* ---------- link to the Zumo ---------- */

void sendZumo(const char *line) {
  Serial1.print(line);
  Serial1.print('\n');
}

void stopZumo() {
  sendZumo("STOP");
  forwardingDrive = false;
}

// a rejected or lost command: stop now and wait for a new "stop" before any drive
bool reject(const char *why) {
  Serial.print("rejected: ");
  Serial.println(why);
  ready = false;
  stopZumo();
  return false;
}

/* ---------- command handling ---------- */

bool handleCommand(const char *body, bool retainedMsg) {
  JsonDocument doc;
  if (deserializeJson(doc, body)) return reject("invalid JSON");
  if (retainedMsg) return reject("retained");

  JsonVariantConst v = doc["version"];
  if (!v.is<int>() || v.as<int>() != 1) return reject("version");
  if (!doc["robotId"].is<const char *>() || strcmp(doc["robotId"].as<const char *>(), ROBOT_ID)) return reject("robot");

  if (!doc["sessionId"].is<const char *>()) return reject("session");
  const String sid = doc["sessionId"].as<const char *>();
  if (!sid.length() || sid.length() > 100) return reject("session");

  if (!doc["sequence"].is<int64_t>()) return reject("sequence");
  const int64_t seq = doc["sequence"].as<int64_t>();
  if (seq <= 0) return reject("sequence");

  if (!doc["ttlMs"].is<int>()) return reject("ttl");
  const int ttl = doc["ttlMs"].as<int>();
  if (ttl <= 0 || ttl > 250) return reject("ttl");

  if (CHECK_EXPIRY) {
    if (!clockValid) return reject("clock unknown");
    if (!doc["sentAtUnixMs"].is<int64_t>()) return reject("sentAt");
    const int64_t age = nowUnixMs() - doc["sentAtUnixMs"].as<int64_t>();
    if (age < -CLOCK_SKEW_MS || age >= ttl + CLOCK_SKEW_MS) {
      Serial.print("age ms: ");
      Serial.println((long)age);
      return reject("stale or from the future");
    }
  }

  if (!doc["command"].is<const char *>()) return reject("command");
  const char *cmd = doc["command"].as<const char *>();
  const bool isStop = !strcmp(cmd, "stop");
  if (!isStop && strcmp(cmd, "drive")) return reject("command");

  if (isRetired(sid)) return reject("retired session");
  if (sid != session) {
    if (!isStop) return reject("stop handshake required");   // a new session must start with stop
    if (session.length()) retire(session);
    session = sid;
    lastSequence = 0;
    ready = false;
    stopZumo();
    Serial.println("new session");
  }
  if (seq <= lastSequence) return reject("out of order");

  if (!doc["left"].is<int>() || !doc["right"].is<int>()) return reject("motor type");
  const int left = doc["left"].as<int>(), right = doc["right"].as<int>();
  if (left < -100 || left > 100 || right < -100 || right > 100) return reject("motor range");

  if (isStop) {
    if (left || right) return reject("nonzero stop");
    ready = true;
    stopZumo();
  } else if (ready) {
    char line[24];
    snprintf(line, sizeof(line), "DRIVE,%d,%d", left, right);
    sendZumo(line);
    forwardingDrive = true;
    lastDriveAt = millis();
  } else {
    return reject("not ready");
  }
  lastSequence = seq;
  return true;
}

void onMqttMessage(int size) {
  static char body[384];
  const bool retainedMsg = mqtt.messageRetain();
  if (size <= 0 || size >= (int)sizeof(body)) {
    while (mqtt.available()) mqtt.read();
    reject("size");
    return;
  }
  int n = 0;
  while (mqtt.available() && n < (int)sizeof(body) - 1) body[n++] = (char)mqtt.read();
  body[n] = 0;
  handleCommand(body, retainedMsg);
}

/* ---------- network ---------- */

uint32_t lastWifiAttempt = 0, lastMqttAttempt = 0;
bool wasConnected = false;

bool ensureWifi() {
  if (WiFi.status() == WL_CONNECTED) return true;
  if (millis() - lastWifiAttempt >= 10000 || !lastWifiAttempt) {
    lastWifiAttempt = millis();
    Serial.println("Connecting to WiFi...");
    if (WIFI_PASS[0]) WiFi.begin(WIFI_SSID, WIFI_PASS);
    else WiFi.begin(WIFI_SSID);
  }
  return false;
}

bool ensureMqtt() {
  if (mqtt.connected()) return true;
  if (millis() - lastMqttAttempt < 3000 && lastMqttAttempt) return false;
  lastMqttAttempt = millis();

  byte mac[6];
  WiFi.macAddress(mac);
  char id[24];
  snprintf(id, sizeof(id), "sumoos-bot-%02X%02X%02X", mac[2], mac[1], mac[0]);
  mqtt.setId(id);
  mqtt.setKeepAliveInterval(5000);
  if (MQTT_USER[0]) mqtt.setUsernamePassword(MQTT_USER, MQTT_PASS);

  Serial.print("Connecting to MQTT... ");
  if (!mqtt.connect(MQTT_HOST, MQTT_PORT)) {
    Serial.print("failed, error ");
    Serial.println(mqtt.connectError());
    return false;
  }
  Serial.println("connected");
  mqtt.onMessage(onMqttMessage);
  mqtt.subscribe(topicCommand.c_str(), 0);          // QoS 0, as the gateway publishes
  ready = false;                                    // after (re)connecting a new stop handshake is needed
  stopZumo();
  wasConnected = true;
  return true;
}

// Wi-Fi or MQTT dropped after we were connected: stop and require a new stop command
void checkConnectionLost() {
  if (!wasConnected || (WiFi.status() == WL_CONNECTED && mqtt.connected())) return;
  wasConnected = false;
  Serial.println("Connection lost");
  ready = false;
  stopZumo();
}

/* ---------- main ---------- */

void setup() {
  Serial.begin(115200);                  // debug only, do not wait for the monitor
  Serial1.begin(ZUMO_BAUD);
  topicCommand = String("sumoos/") + ROBOT_ID + "/command";
  stopZumo();
  Serial.print("Bridge for ");
  Serial.println(ROBOT_ID);
}

void loop() {
  // watchdog: never keep a stale drive alive
  if (forwardingDrive && millis() - lastDriveAt >= COMMAND_TIMEOUT_MS) {
    Serial.println("command timeout");
    ready = false;
    stopZumo();
  }
  checkConnectionLost();
  if (!ensureWifi()) return;
  if (CHECK_EXPIRY) updateClock();
  if (!ensureMqtt()) return;
  mqtt.poll();
}
