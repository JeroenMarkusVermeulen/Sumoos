namespace Sumoos.Controller.Configuration;
public sealed record AppOptions
{
    public string BrokerHost { get; init; } = "localhost";
    public int BrokerPort { get; init; } = 1883;
    public string RobotId { get; init; } = "bot-2";
    public string SerialPort { get; init; } = "COM3";
    public int BaudRate { get; init; } = 115200;
    public int MaxSpeedPercent { get; init; } = 30;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BrokerHost) || BrokerPort is < 1 or > 65535 ||
            BaudRate <= 0 || string.IsNullOrWhiteSpace(SerialPort) || MaxSpeedPercent is < 1 or > 100 ||
            string.IsNullOrEmpty(RobotId) || RobotId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Invalid appsettings.json.");
    }
}
