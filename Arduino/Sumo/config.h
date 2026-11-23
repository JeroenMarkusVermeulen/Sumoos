#pragma once

// --- USB serial to the laptop (must match dotnet/docs/protocol.md and the gateway's appsettings.json) ---
const uint32_t SERIAL_BAUD = 115200;

// --- Carrier ---
const bool USE_CASE = true;                       // false if the Carrier is used without the case
const uint8_t STOP_PAD = 0;                       // TOUCH0..TOUCH4 (labelled 00..04 on the board)
const uint8_t ARM_PAD = 4;
const uint8_t ROBOT_LABEL = 2;                    // only shown on the screen ("Bot 2")

// --- Driving ---
const int8_t MAX_SPEED = 100;                     // DRIVE values are -MAX_SPEED..MAX_SPEED (the gateway applies its own speed limit on top)
const uint16_t DRIVE_INTERVAL_MS = 50;            // the gateway needs fresh input every 50 ms (it disarms after 250 ms)

// --- Tilt control (the Carrier's accelerometer, values in g) ---
const float TILT_DEADZONE = 0.12f;                // below this tilt = no movement
const float TILT_FULL = 0.55f;                    // at this tilt = full speed
const bool TILT_FORWARD_IS_Y = false;             // false: tilting along X is forward/back, true: along Y
const bool TILT_INVERT_FORWARD = false;           // flip if "tilt forward" drives backwards
const bool TILT_INVERT_TURN = false;              // flip if "tilt right" turns left
