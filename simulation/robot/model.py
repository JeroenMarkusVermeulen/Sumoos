import time

class Robot:
    def __init__(self, robot_id):
        self.robot_id = robot_id
        self.session = ""
        self.retired = set()
        self.sequence = 0
        self.left = self.right = 0
        self.ready = False
        self.reason = "startup"
        self.last_drive = 0.0

    def stop(self, reason):
        self.left = self.right = 0
        self.ready = False
        self.reason = reason

    def tick(self, now=None):
        now = time.monotonic() if now is None else now
        if self.reason == "drive" and now - self.last_drive >= .250:
            self.stop("command timeout")

    def apply(self, p, retained=False, wall=None, mono=None):
        wall = time.time()*1000 if wall is None else wall
        mono = time.monotonic() if mono is None else mono
        self.tick(mono)
        try:
            if retained or p["version"] != 1 or p["robotId"] != self.robot_id:
                raise ValueError("wrong robot/version or retained")
            session, sequence = p["sessionId"], p["sequence"]
            if not isinstance(session,str) or not session or len(session)>100: raise ValueError("session")
            if type(sequence) is not int or sequence <= 0: raise ValueError("sequence")
            if type(p["ttlMs"]) is not int or not 0 < p["ttlMs"] <= 250: raise ValueError("expiry")
            age = wall-p["sentAtUnixMs"]
            if not 0 <= age < p["ttlMs"]: raise ValueError("stale/future")
            if session in self.retired: raise ValueError("retired session")
            if session != self.session:
                if p["command"] != "stop" or len(self.retired)>=32: raise ValueError("stop handshake required")
                if self.session: self.retired.add(self.session)
                self.session, self.sequence = session, 0
                self.stop("new session")
            if sequence <= self.sequence: raise ValueError("out of order")
            left,right=p["left"],p["right"]
            if any(type(v) is not int or not -100 <= v <= 100 for v in [left,right]): raise ValueError("motor range")
            if p["command"] == "stop":
                if left or right: raise ValueError("nonzero stop")
                self.left=self.right=0
                self.ready=True
                self.reason="stopped"
            elif p["command"] == "drive" and self.ready:
                self.left,self.right=left,right
                self.last_drive=mono
                self.reason="drive"
            else: raise ValueError("not ready")
            self.sequence=sequence
            return True
        except (KeyError,ValueError,TypeError,OverflowError):
            self.stop("rejected command")
            return False

    def status(self):
        return dict(version=1, robotId=self.robot_id, simulated=True, sessionId=self.session,
                    sequence=self.sequence, ready=self.ready, left=self.left,right=self.right,reason=self.reason)
