#pragma once

// --- Where commands come from ---
// Step 1 (bench test, no Wi-Fi): USB cable to the laptop, type lines in the Arduino Serial Monitor.
// Step 2 (real): the MKR bridge on the UART (pins 0/1). Comment/uncomment ONE of these.
// WARNING: on the Zumo 32U4 OLED, pins 0 and 1 are used by the display (reset / clock) per Pololu's
// user guide section 3.11. Serial1 is only free if the display is removed. Do not wire pins 0/1 with the OLED on.
#define LINK Serial
// #define LINK Serial1
const uint32_t LINK_BAUD = 115200;
// The display shares pins 0/1 with Serial1. Set this to false when LINK is Serial1 (the OLED then stays dark/garbled).
const bool USE_OLED = true;

// --- Motors ---
const int MOTOR_SCALE = 4;                        // the protocol sends -100..100 %, the Zumo library wants -400..400
const bool FLIP_LEFT_MOTOR = false;               // set true/false if a track drives the wrong way
const bool FLIP_RIGHT_MOTOR = false;
const uint16_t COMMAND_TIMEOUT_MS = 250;          // no fresh DRIVE for this long -> stop

// --- Ring edge protection (overrides remote driving) ---
// The ring is dark with a white border. White reflects more light, so a LOWER reading than the
// threshold on an outer sensor means "edge". Calibrate: read the values on black and on white
// (EDGE_DEBUG on the OLED) and put the threshold in between.
const bool EDGE_ENABLED = true;                   // false while bench-testing on a light table (it would look like "white border" all the time)
const uint16_t EDGE_THRESHOLD = 400;
const int EDGE_BACKUP_SPEED = 200;                // library units (max 400)
const uint16_t EDGE_BACKUP_MS = 250;
const bool EDGE_DEBUG = false;                    // true: the OLED shows the two outer sensor readings
