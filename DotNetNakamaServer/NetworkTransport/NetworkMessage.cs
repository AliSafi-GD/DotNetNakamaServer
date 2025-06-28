namespace DotNetNakamaServer.NetworkTransport;

public class NetworkMessage
{
    public int OpCode { get; set; }
    public byte[] Data { get; set; }
    public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.ReliableOrdered;
    public byte Channel { get; set; } = 0;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}