using System.IO.Ports;
using System.Text;

namespace Sumoos.Controller.Input;

public sealed class ArduinoInput(string name, int baud) : IDisposable
{
    private readonly SerialPort port = new(name, baud);
    private readonly ControllerFrameReader frames = new();

    public static string[] GetPortNames() => SerialPort.GetPortNames();

    public void Open()
    {
        port.Open();
        Clear();
    }

    public void Clear()
    {
        frames.Reset();
        port.DiscardInBuffer();
    }

    public IReadOnlyList<string> Read()
    {
        if (port.BytesToRead > 1024)
        {
            Clear();
            throw new InvalidDataException("Controller input backlog discarded.");
        }
        return frames.Append(port.ReadExisting());
    }

    public void Dispose() => port.Dispose();
}

public sealed class ControllerFrameReader
{
    private readonly StringBuilder pending = new();
    private bool discard;

    public IReadOnlyList<string> Append(string text)
    {
        var lines = new List<string>();
        var overflow = false;
        foreach (var c in text)
        {
            if (c == '\n')
            {
                if (!discard)
                {
                    var line = pending.ToString();
                    if (line.EndsWith('\r')) line = line[..^1];
                    if (line.Length > 64) overflow = true;
                    else lines.Add(line);
                }
                discard = false;
                pending.Clear();
            }
            else if (!discard)
            {
                if (pending.Length < 65) pending.Append(c); // allow a trailing CR
                else
                {
                    pending.Clear();
                    discard = true;
                    overflow = true;
                }
            }
        }
        if (overflow)
        {
            pending.Clear();
            discard = discard || (text.Length > 0 && text[^1] != '\n');
            throw new InvalidDataException("Oversized controller frame discarded.");
        }
        return lines;
    }

    public void Reset()
    {
        pending.Clear();
        discard = false;
    }
}
