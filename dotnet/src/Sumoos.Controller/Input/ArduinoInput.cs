using System.IO.Ports;
using System.Text;
namespace Sumoos.Controller.Input;

public sealed class ArduinoInput(string name, int baud) : IDisposable
{
    private readonly SerialPort port = new(name, baud);
    private readonly StringBuilder pending = new();
    public void Open() { port.Open(); port.DiscardInBuffer(); }
    public void Clear() { pending.Clear(); port.DiscardInBuffer(); }
    public IReadOnlyList<string> Read()
    {
        if (port.BytesToRead > 1024) throw new IOException("Controller backlog.");
        var text = port.ReadExisting();
        var lines = new List<string>();
        foreach (var c in text)
        {
            if (c == '\n') { lines.Add(pending.ToString().TrimEnd('\r')); pending.Clear(); }
            else if (pending.Length < 64) pending.Append(c);
            else throw new IOException("Controller line too long.");
        }
        return lines;
    }
    public void Dispose() => port.Dispose();
}
