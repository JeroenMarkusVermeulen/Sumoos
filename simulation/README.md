# Docker MQTT robot simulation

Everything for the hardware-free robot lives here. The real dashboard remains in
../dotnet; no Arduino, ESP32 or Zumo is required. This simulates command handling
and motor percentages, not physical movement, traction, sensors or match outcomes.

## Folder guide

- compose.yaml / mosquitto.conf: isolated authenticated broker, host port 1884.
- robot/: Python MQTT robot and its validation/watchdog model.
- tests/test_model.py: malformed, expired, retained, duplicate and retired-session checks.
- tests/Sumoos.Integration/: real dashboard gateway + MQTT + Docker failure tests.

## Start (PowerShell, repository root)

Open or create `.env` in the repository root. Keep any existing settings and add
these entries if they are missing. Choose a local test password for the simulation.
The file is ignored by Git.

    SIM_MQTT_USERNAME=sumoos
    SIM_MQTT_PASSWORD=<your-local-test-password>

    docker compose --env-file .env -f simulation/compose.yaml up -d --build
    dotnet run --project dotnet/src/Sumoos.Dashboard

In the dashboard:
1. Select Simulation and Use Docker MQTT simulator.
2. Confirm host `127.0.0.1`, MQTT port `1884` and robot ID `bot-2`.
3. The dashboard fills in the simulation credentials from the root `.env` or
   the `SIM_MQTT_USERNAME` and `SIM_MQTT_PASSWORD` environment variables.
4. Start session and wait for robot feedback (Input and packet tab).
5. Arm simulation, then hold the direction buttons. With the default 30% limit,
   forward input 50 produces 15% on each simulated track.
6. Robot feedback includes actual simulated left/right values, accepted sequence,
   stop reason, session ID and a simulated=true marker.

The old offline simulation remains available by leaving Use Docker MQTT simulator
unchecked. Live Arduino mode still works with Simulation unchecked.

This separate broker binds only localhost:1884, avoiding the existing hardware
broker on port 1883. These test credentials are local, not Docker account credentials.
The robot container may retry for a few seconds while the broker starts.

## Deliberate faults

    docker compose --env-file .env -f simulation/compose.yaml stop robot
    docker compose --env-file .env -f simulation/compose.yaml start robot
    docker compose --env-file .env -f simulation/compose.yaml stop broker
    docker compose --env-file .env -f simulation/compose.yaml start broker

The gateway disarms when feedback is absent for 750 ms or the receiver reports it
needs a stop handshake. The robot independently stops after 250 ms without fresh
drive commands. A stopped/restarted broker or robot must not resume motion.
Wait for fresh feedback, release the pad to neutral and explicitly re-arm.

Pause input tests the controller heartbeat loss. Switching away from the dashboard
also requests a stop. The automated integration test injects faults while driving,
without relying on a human keeping the dashboard focused.

## Automated checks

Model checks require only Python 3:

    python -m unittest discover -s simulation/tests -p test_model.py

With the containers running, export the SAME credentials for the .NET integration
client (Compose reads .env; .NET reads environment variables):

    $env:SIM_MQTT_USERNAME = "sumoos"
    $env:SIM_MQTT_PASSWORD = "<your-local-test-password>"
    $env:SUMOOS_MQTT_USERNAME = $env:SIM_MQTT_USERNAME
    $env:SUMOOS_MQTT_PASSWORD = $env:SIM_MQTT_PASSWORD
    dotnet run --project simulation/tests/Sumoos.Integration

Run from repository root. Close dashboard sessions before this test: use one
gateway per robot. The test restarts the simulation robot and broker intentionally.
It checks feedback-driven motion, input timeout, robot restart and broker restart,
and proves that motion does not restart automatically. CI runs this on Linux Docker.
Windows CI separately builds and smoke-tests the WPF dashboard.

## Protocol

Commands use ../dotnet/docs/protocol.md and sumoos/bot-2/command.
Feedback is non-retained QoS0 on sumoos/bot-2/status, every 50 ms:

    {"version":1,"robotId":"bot-2","simulated":true,"bootId":"unique-boot",
     "sessionId":"gateway-session","sequence":12,"ready":true,
     "left":15,"right":15,"reason":"drive"}

A new connection/session must first send STOP; drive alone cannot initialise or
recover the receiver. Session IDs and sequence numbers reject duplicates/old
sessions. Expiry uses Unix timestamps, so host/container clocks must be aligned.
The model's watchdog uses a monotonic clock. Incoming retained motion is rejected.
Use the local broker; do not expose it to the internet.

## Stop and remove test containers

    docker compose --env-file .env -f simulation/compose.yaml down

Keep ../infrastructure for the real hardware broker. No production services are
modified by these commands.
