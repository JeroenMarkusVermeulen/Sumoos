import json, os, threading, time, uuid
import paho.mqtt.client as mqtt
from model import Robot

robot=Robot(os.getenv("ROBOT_ID","bot-2"))
lock=threading.Lock()
boot=uuid.uuid4().hex
client=mqtt.Client(mqtt.CallbackAPIVersion.VERSION2,client_id="sumoos-simulator-"+boot)
client.username_pw_set(os.environ["SIM_MQTT_USERNAME"],os.environ["SIM_MQTT_PASSWORD"])
topic="sumoos/"+robot.robot_id
def connected(c,u,flags,reason,properties):
    global boot
    with lock:
        boot = uuid.uuid4().hex
        robot.stop("broker connected; stop handshake required")
    if not reason.is_failure: c.subscribe(topic+"/command",qos=0)
def disconnected(c,u,flags,reason,properties):
    with lock: robot.stop("broker disconnected")
def received(c,u,message):
    with lock:
        try: robot.apply(json.loads(message.payload),retained=message.retain)
        except (ValueError,UnicodeDecodeError): robot.stop("invalid JSON")
client.on_connect=connected
client.on_disconnect=disconnected
client.on_message=received
client.reconnect_delay_set(1,3)
client.connect_async(os.getenv("BROKER_HOST","broker"),int(os.getenv("BROKER_PORT","1883")),keepalive=10)
client.loop_start()
try:
    while True:
        with lock:
            robot.tick()
            payload=robot.status(boot, simulated=True)
        if client.is_connected(): client.publish(topic+"/status",json.dumps(payload),qos=0,retain=False)
        time.sleep(.05)
finally:
    with lock: robot.stop("shutdown")
    client.disconnect()
    client.loop_stop()
