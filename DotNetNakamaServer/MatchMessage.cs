namespace DotNetNakamaServer;

public class MatchMessage
{
    public int OpCode { get; set; }
    public byte[] Data { get; set; }
    public PlayerPresence Sender { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}