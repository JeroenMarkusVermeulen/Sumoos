import sys, unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/"robot"))
from model import Robot
class Tests(unittest.TestCase):
    def packet(self,seq=1,command="stop",**kw):
        return dict(version=1,robotId="bot-2",sessionId="a",sequence=seq,sentAtUnixMs=1000,ttlMs=250,command=command,left=0,right=0,**kw)
    def test_handshake_and_timeout(self):
        r=Robot("bot-2")
        self.assertFalse(r.apply(self.packet(command="drive"),wall=1000,mono=1))
        self.assertTrue(r.apply(self.packet(),wall=1000,mono=1))
        p=self.packet(2,"drive");p["left"]=30
        self.assertTrue(r.apply(p,wall=1000,mono=1))
        self.assertEqual(r.left,30)
        r.tick(1.251);self.assertEqual(r.left,0);self.assertFalse(r.ready)
        self.assertFalse(r.apply(self.packet(3,"drive"),wall=1000,mono=1.3))
    def test_rejection(self):
        for change in [{"robotId":"other"},{"version":2},{"sequence":0},{"left":101},{"sentAtUnixMs":700},{"sentAtUnixMs":1001}]:
            r=Robot("bot-2");p=self.packet();p.update(change)
            self.assertFalse(r.apply(p,wall=1000,mono=1));self.assertEqual(r.left,0)
        self.assertFalse(Robot("bot-2").apply(self.packet(),retained=True,wall=1000,mono=1))
    def test_duplicate_and_retired(self):
        r=Robot("bot-2");p=self.packet()
        self.assertTrue(r.apply(p,wall=1000,mono=1))
        self.assertFalse(r.apply(p,wall=1000,mono=1))
        p["sessionId"]="b";self.assertTrue(r.apply(p,wall=1000,mono=1))
        p["sessionId"]="a";p["sequence"]=10
        self.assertFalse(r.apply(p,wall=1000,mono=1))
if __name__=="__main__":unittest.main()
