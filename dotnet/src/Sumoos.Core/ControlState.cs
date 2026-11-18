namespace Sumoos.Core;

// Called from one loop with monotonic Stopwatch milliseconds.
public sealed class ControlState
{
    public bool Armed { get; private set; }
    public int Left { get; private set; }
    public int Right { get; private set; }
    public string Reason { get; private set; } = "startup";
    private long lastDrive;
    private bool hasDrive;
    public void Stop(string reason) { Armed = false; Left = Right = 0; hasDrive = false; Reason = reason; }
    public void Tick(long now) { if (Armed && now - lastDrive >= 250) Stop("controller timeout"); }
    public bool Apply(string line, long now)
    {
        Tick(now);
        if (line == "STOP") { Stop("stop requested"); return true; }
        if (line == "ARM")
        {
            if (!hasDrive || now - lastDrive >= 250 || Left != 0 || Right != 0) return false;
            Armed = true; Reason = "armed"; return true;
        }
        var p = line.Split(',');
        if (p.Length != 3 || p[0] != "DRIVE" ||
            !int.TryParse(p[1], out var l) || !int.TryParse(p[2], out var r) ||
            Math.Abs((long)l) > 100 || Math.Abs((long)r) > 100)
        { Stop("invalid input"); return false; }
        Left = l; Right = r; lastDrive = now; hasDrive = true; return true;
    }
    public (int Left, int Right) Output() => Armed ? (Left, Right) : (0, 0);
}
