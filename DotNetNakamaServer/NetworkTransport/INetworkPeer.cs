namespace DotNetNakamaServer.NetworkTransport;

public interface INetworkPeer
{
    string Id { get; }
    string Address { get; }
    int Port { get; }
    bool IsConnected { get; }
    DateTime ConnectedAt { get; }
    Dictionary<string, object> Metadata { get; }
    
    Task SendAsync(NetworkMessage message);
    void Disconnect(DisconnectReason reason = DisconnectReason.Disconnect);
}