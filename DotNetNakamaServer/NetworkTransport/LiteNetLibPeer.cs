using LiteNetLib;
using LiteNetLib.Utils;

namespace DotNetNakamaServer;

public class LiteNetLibPeer : INetworkPeer
{
    public NetPeer NativePeer { get; }
    
    public string Id => NativePeer.Id.ToString();
    public string Address => NativePeer.Address.ToString();
    public int Port => NativePeer.Port;
    public bool IsConnected => NativePeer.ConnectionState == ConnectionState.Connected;
    public DateTime ConnectedAt { get; }
    public Dictionary<string, object> Metadata { get; } = new();

    public LiteNetLibPeer(NetPeer nativePeer)
    {
        NativePeer = nativePeer;
        ConnectedAt = DateTime.UtcNow;
    }

    public async Task SendAsync(NetworkMessage message)
    {
        var writer = new NetDataWriter();
        writer.Put(message.OpCode);
        writer.Put(message.Data?.Length ?? 0);
        if (message.Data != null)
            writer.Put(message.Data);

        var deliveryMethod = message.DeliveryMode switch
        {
            DeliveryMode.Unreliable => DeliveryMethod.Unreliable,
            DeliveryMode.ReliableOrdered => DeliveryMethod.ReliableOrdered,
            DeliveryMode.ReliableUnordered => DeliveryMethod.ReliableUnordered,
            DeliveryMode.UnreliableSequenced => DeliveryMethod.Sequenced,
            _ => DeliveryMethod.ReliableOrdered
        };

        NativePeer.Send(writer,message.Channel, deliveryMethod);
    }

    public void Disconnect(DisconnectReason reason = DisconnectReason.Disconnect)
    {
        NativePeer.Disconnect();
    }
}