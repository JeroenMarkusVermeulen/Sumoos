/*
 * Sumoos manual robot firmware: Pololu Zumo 32U4 OLED
 *
 * Receives text lines (115200 8N1, LF terminated, max 64 characters):
 *   DRIVE,<left>,<right>   -100..100 percent, positive = forward
 *   STOP
 * from the laptop gateway directly over USB (bench test) or via the MKR bridge (Arduino/ZumoBridge)
 * on the UART. Choose with LINK in config.h. dotnet/docs/protocol.md describes the contract.
 *
 * Safety (all local on the Zumo, independent of Wi-Fi):
 *   - starts stopped after power-on/reset
 *   - stops if no valid DRIVE arrives for COMMAND_TIMEOUT_MS (250 ms)
 *   - any malformed or out-of-range line stops the robot
 *   - the ring edge (white border seen by an outer line sensor) overrides remote driving: the robot
 *     backs away for EDGE_BACKUP_MS; DRIVE lines are ignored during that time
 *
 * Do not print anything to LINK: the other side treats every line it receives as a message.
 *
 * Bench test over USB (tracks lifted off the table!): set LINK to Serial, open the Serial Monitor
 * at 115200 with "Newline" line ending and type e.g.  DRIVE,30,30  then within 250 ms again, ...
 * A single typed line only runs the motors for 250 ms, that is the watchdog working.
 * Use the gateway's dashboard/console to send a continuous stream.
 */

#include <Wire.h>
#include <Zumo32U4.h>
#include "config.h"

Zumo32U4Motors motors;
Zumo32U4LineSensors lineSensors;
Zumo32U4OLED display;

enum State : uint8_t { STOPPED, DRIVING, EDGE_BACKUP };
State state = STOPPED;
int driveLeft = 0, driveRight = 0;
uint32_t lastDriveAt = 0, edgeUntil = 0;
uint16_t edgeLeftValue = 0, edgeRightValue = 0;

/* ---------- motors ---------- */

void setMotors(int left, int right) {              // left/right in protocol percent
  motors.setSpeeds(left * MOTOR_SCALE, right * MOTOR_SCALE);
}

void stopRobot() {
  motors.setSpeeds(0, 0);
  driveLeft = driveRight = 0;
  state = STOPPED;
}

/* ---------- edge protection ---------- */

bool readEdge() {
  uint16_t v[5];
  lineSensors.read(v);                             // [0] = leftmost, [4] = rightmost
  edgeLeftValue = v[0];
  edgeRightValue = v[4];
  return v[0] < EDGE_THRESHOLD || v[4] < EDGE_THRESHOLD;
}

/* ---------- command parsing ---------- */

char line[65];
uint8_t lineLen = 0;
bool overflow = false;

// returns false for anything that is not exactly DRIVE,<int>,<int> or STOP with values in range
bool handleLine(const char *s) {
  if (!strcmp(s, "STOP")) {
    stopRobot();
    return true;
  }
  if (strncmp(s, "DRIVE,", 6)) return false;
  char *end;
  const char *p = s + 6;
  const long l = strtol(p, &end, 10);
  if (end == p || *end != ',') return false;
  p = end + 1;
  const long r = strtol(p, &end, 10);
  if (end == p || *end != '\0') return false;
  if (l < -100 || l > 100 || r < -100 || r > 100) return false;

  lastDriveAt = millis();
  if (state == EDGE_BACKUP) return true;           // the edge override wins over remote driving
  driveLeft = (int)l;
  driveRight = (int)r;
  state = DRIVING;
  setMotors(driveLeft, driveRight);
  return true;
}

void readLink() {
  while (LINK.available()) {
    const char c = (char)LINK.read();
    if (c == '\r') continue;
    if (c == '\n') {
      line[lineLen] = 0;
      if (overflow || !lineLen || !handleLine(line)) stopRobot();   // invalid input stops
      lineLen = 0;
      overflow = false;
    } else if (lineLen < sizeof(line) - 1) {
      line[lineLen++] = c;
    } else {
      overflow = true;                             // too long: drop the rest, the line is invalid
    }
  }
}

/* ---------- OLED ---------- */

uint32_t lastDisplayAt = 0;

void updateDisplay() {
  if (!USE_OLED) return;                           // pins 0/1 are the UART now, the display must stay silent
  if (millis() - lastDisplayAt < 200) return;      // the OLED is slow, keep it off the fast loop
  lastDisplayAt = millis();
  char l0[22], l1[22];
  const char *name = state == STOPPED ? "STOP" : state == DRIVING ? "DRIVE" : "EDGE!";
  snprintf(l0, sizeof(l0), "%-10s", name);
  if (EDGE_DEBUG) snprintf(l1, sizeof(l1), "%4u %4u    ", edgeLeftValue, edgeRightValue);
  else snprintf(l1, sizeof(l1), "L%-4d R%-4d", driveLeft, driveRight);
  display.gotoXY(0, 0);
  display.print(l0);
  display.gotoXY(0, 1);
  display.print(l1);
}

/* ---------- main ---------- */

void setup() {
  LINK.begin(LINK_BAUD);                           // do not wait for the Serial Monitor
  motors.flipLeftMotor(FLIP_LEFT_MOTOR);
  motors.flipRightMotor(FLIP_RIGHT_MOTOR);
  lineSensors.initFiveSensors();
  if (USE_OLED) {
    display.setLayout21x8();
    display.clear();
  }
  stopRobot();
}

void loop() {
  readLink();

  const uint32_t now = millis();

  // ring edge: highest priority
  if (EDGE_ENABLED && state != EDGE_BACKUP && readEdge()) {
    motors.setSpeeds(-EDGE_BACKUP_SPEED, -EDGE_BACKUP_SPEED);
    edgeUntil = now + EDGE_BACKUP_MS;
    state = EDGE_BACKUP;
  } else if (state == EDGE_BACKUP && (int32_t)(now - edgeUntil) >= 0) {
    stopRobot();                                   // back away done; wait for a fresh DRIVE
  }

  // watchdog: no fresh DRIVE -> stop
  if (state == DRIVING && now - lastDriveAt >= COMMAND_TIMEOUT_MS) stopRobot();

  updateDisplay();
}
