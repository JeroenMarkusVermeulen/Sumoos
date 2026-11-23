/*
 * Sumoos controller (Arduino MKR WiFi 1010 + MKR IoT Carrier), USB serial version
 *
 * The board is only a joystick for the manually controlled robot. It talks over USB serial to the
 * .NET gateway on the laptop (dotnet/src/Sumoos.Controller), which publishes to MQTT. The board
 * does NOT use Wi-Fi or MQTT. The protocol is dotnet/docs/protocol.md.
 *
 * Serial: 115200 baud, 8N1, ASCII, one command per line ending with LF, max 64 characters.
 *   DRIVE,<left>,<right>   every 50 ms, ALWAYS (also when stopped or neutral). -100..100, positive = forward.
 *   ARM                    once, only on a deliberate touch of the ARM pad, and only when the tilt is neutral.
 *   STOP                   once, when the STOP pad is touched.
 * The gateway disarms by itself if DRIVE stops for 250 ms. Never print anything else to Serial:
 * the gateway treats every line as a command and an unknown line disarms the robot.
 *
 * Controls
 *   Tilt the Carrier   forward/back = speed, left/right = turn
 *   Touch pad 00       STOP
 *   Touch pad 04       ARM. The pose you hold the board in at that moment becomes "neutral"
 *                      (zero), so hold it comfortably and still when you touch it.
 *   Touching several pads at once is ignored (neighbouring pads often light up together).
 *
 * The gateway sends nothing back, so the screen shows what this board did ("ARM SENT"), not
 * whether the robot is really armed. Check the gateway/dashboard for the real state.
 *
 * Calibrating the tilt: open the Serial Monitor and read the DRIVE lines while tilting
 * (with the gateway closed, otherwise it owns the port). Flip the TILT_* switches in config.h
 * until forward tilt gives positive values and right tilt gives left > right.
 */

#include <Arduino_MKRIoTCarrier.h>
#include "config.h"

MKRIoTCarrier carrier;

/* ---------- state ---------- */

bool armSent = false;                   // we sent ARM and have not sent STOP since
const char *screenNote = "";
int8_t driveLeft = 0, driveRight = 0;   // last values sent

/* ---------- tilt -> left/right motor values ---------- */

float lastAx = 0, lastAy = 0;
float zeroAx = 0, zeroAy = 0;           // the pose held when ARM was touched = "no movement"
float smoothFwd = 0, smoothTurn = 0;

// -1..1 with a dead zone in the middle
float shapeTilt(float g) {
  const float a = fabsf(g);
  if (a <= TILT_DEADZONE) return 0;
  float v = (a - TILT_DEADZONE) / (TILT_FULL - TILT_DEADZONE);
  if (v > 1) v = 1;
  return g < 0 ? -v : v;
}

void readTilt(float &fwd, float &turn) {
  float x, y, z;
  if (carrier.IMUmodule.accelerationAvailable()) {
    carrier.IMUmodule.readAcceleration(x, y, z);
    lastAx = x;
    lastAy = y;
  }
  float f = TILT_FORWARD_IS_Y ? lastAy - zeroAy : lastAx - zeroAx;
  float t = TILT_FORWARD_IS_Y ? lastAx - zeroAx : lastAy - zeroAy;
  if (TILT_INVERT_FORWARD) f = -f;
  if (TILT_INVERT_TURN) t = -t;
  fwd = shapeTilt(f);
  turn = shapeTilt(t);
}

// tank mixing: forward +/- turn, scaled so neither side exceeds full speed
void computeDrive(int8_t &left, int8_t &right) {
  float fwd, turn;
  readTilt(fwd, turn);
  smoothFwd += 0.4f * (fwd - smoothFwd);            // light smoothing against hand shake
  smoothTurn += 0.4f * (turn - smoothTurn);
  if (fabsf(smoothFwd) < 0.02f) smoothFwd = 0;      // let the smoothing settle on exactly 0 (ARM needs neutral)
  if (fabsf(smoothTurn) < 0.02f) smoothTurn = 0;
  float l = smoothFwd + smoothTurn;
  float r = smoothFwd - smoothTurn;
  const float m = max(fabsf(l), fabsf(r));
  if (m > 1) { l /= m; r /= m; }
  left = (int8_t)lroundf(l * MAX_SPEED);
  right = (int8_t)lroundf(r * MAX_SPEED);
}

/* ---------- serial output ---------- */

void sendLine(const char *line) {
  Serial.print(line);
  Serial.print('\n');
}

uint32_t lastDriveAt = 0;

void sendDrive(int8_t l, int8_t r) {
  char line[24];
  snprintf(line, sizeof(line), "DRIVE,%d,%d", l, r);
  sendLine(line);
  driveLeft = l;
  driveRight = r;
  lastDriveAt = millis();
}

void updateDrive() {
  if (millis() - lastDriveAt < DRIVE_INTERVAL_MS) return;
  lastDriveAt = millis();
  int8_t l, r;
  computeDrive(l, r);
  sendDrive(l, r);
}

/* ---------- feedback: LEDs, screen (no sound) ---------- */

uint32_t ledOffAt = 0;

void setLeds(uint8_t r, uint8_t g, uint8_t b) {
  for (uint8_t i = 0; i < 5; ++i) carrier.leds.setPixelColor(i, r, g, b);
  carrier.leds.show();
}

void flashLeds(uint8_t r, uint8_t g, uint8_t b, uint16_t ms) {
  setLeds(r, g, b);
  ledOffAt = millis() + ms;
}

void restoreLeds() {
  if (armSent) setLeds(0, 40, 0);
  else setLeds(0, 0, 0);
}

void updateLeds() {
  if (ledOffAt && (int32_t)(millis() - ledOffAt) >= 0) {
    ledOffAt = 0;
    restoreLeds();
  }
}

// speed numbers sit in their own area so they can be redrawn without flicker
void drawSpeeds() {
  carrier.display.fillRect(0, 175, 240, 40, 0x0000);
  carrier.display.setTextColor(0x07FF);
  carrier.display.setTextSize(3);
  carrier.display.setCursor(20, 180);
  carrier.display.print("L");
  carrier.display.print((int)driveLeft);
  carrier.display.print(" R");
  carrier.display.print((int)driveRight);
}

// the parts that never change: only drawn once at boot (a full-screen redraw takes ~0.2 s)
void drawStatic() {
  carrier.display.fillScreen(0x0000);
  carrier.display.setTextColor(0xFFFF);
  carrier.display.setTextSize(3);
  carrier.display.setCursor(20, 40);
  carrier.display.print("Bot ");
  carrier.display.print(ROBOT_LABEL);
}

// only the state and note areas, in small steps: the gateway disarms after 250 ms without DRIVE,
// so the DRIVE stream is kept alive between the steps
void drawState() {
  carrier.display.fillRect(0, 85, 240, 45, 0x0000);
  carrier.display.setTextColor(armSent ? 0x07E0 : 0xF800);
  carrier.display.setTextSize(4);
  carrier.display.setCursor(20, 90);
  carrier.display.print(armSent ? "ARM SENT" : "STOPPED");
  updateDrive();
  carrier.display.fillRect(0, 135, 240, 30, 0x0000);
  if (screenNote[0]) {
    carrier.display.setTextColor(0xFFFF);
    carrier.display.setTextSize(2);
    carrier.display.setCursor(20, 140);
    carrier.display.print(screenNote);
  }
  updateDrive();
  drawSpeeds();
}

void drawScreen() {
  drawStatic();
  drawState();
}

uint32_t lastSpeedDraw = 0;

void updateSpeedDisplay() {
  if (millis() - lastSpeedDraw < 250) return;      // the screen is slow, keep it off the 50 ms loop
  lastSpeedDraw = millis();
  drawSpeeds();
}

/* ---------- touch pads ---------- */

const uint32_t DEBOUNCE_MS = 35;
uint8_t rawMask = 0, stableMask = 0;
uint32_t changedAt = 0, lastPadPoll = 0;

void onPadsPressed(uint8_t pressed) {
  const uint8_t stopMask = 1 << STOP_PAD, armMask = 1 << ARM_PAD;
  const uint8_t used = pressed & (stopMask | armMask);
  // several pads at once is almost always a ghost touch from a neighbouring pad: ignore it
  if (used != stopMask && used != armMask) return;

  if (used == stopMask) {
    armSent = false;
    screenNote = "";
    sendLine("STOP");
    flashLeds(255, 0, 0, 300);
  } else {
    // the gateway refuses ARM unless the last DRIVE was a fresh 0,0. So the pose held right now becomes
    // "neutral" (no need to hold the board perfectly flat), then 0,0 and ARM go out back to back.
    zeroAx = lastAx;
    zeroAy = lastAy;
    smoothFwd = smoothTurn = 0;
    sendDrive(0, 0);
    armSent = true;
    screenNote = "";
    sendLine("ARM");
    flashLeds(0, 255, 0, 300);
  }
  drawState();                                      // not drawScreen(): a full redraw would stall DRIVE for ~0.2 s
}

void readPads() {
  if (millis() - lastPadPoll < 5) return;
  lastPadPoll = millis();
  if (!carrier.Buttons.update()) return;
  uint8_t mask = 0;
  for (uint8_t i = 0; i < 5; ++i)
    if (carrier.Buttons.getTouch((touchButtons)i)) mask |= 1 << i;
  if (mask != rawMask) { rawMask = mask; changedAt = millis(); }
  if (rawMask != stableMask && millis() - changedAt >= DEBOUNCE_MS) {
    const uint8_t pressed = rawMask & ~stableMask;
    stableMask = rawMask;
    if (pressed) onPadsPressed(pressed);
  }
}

/* ---------- main ---------- */

void setup() {
  Serial.begin(SERIAL_BAUD);          // do not wait for the Serial Monitor: the board must also run on its own
  if (USE_CASE) carrier.withCase();
  else carrier.noCase();
  if (!carrier.begin()) {
    while (true);                     // nothing may be printed here, see the note about Serial above
  }
  carrier.display.setRotation(0);     // library default is 2; 0 turns the screen 180 degrees
  drawScreen();
}

void loop() {
  readPads();
  updateDrive();
  updateLeds();
  updateSpeedDisplay();
}
