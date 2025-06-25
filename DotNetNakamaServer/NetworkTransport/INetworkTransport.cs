namespace DotNetNakamaServer;

public interface INetworkTransport : IDisposable
{
    string Name { get; }
    bool IsRunning { get; }
    
    // رویدادها
    event Action<INetworkPeer> PeerConnected;
    event Action<INetworkPeer, DisconnectReason> PeerDisconnected;
    event Action<INetworkPeer, NetworkMessage> MessageReceived;
    event Action<ConnectionRequest> ConnectionRequest;

    // متدهای کنترل
    Task<bool> StartAsync(NetworkConfig config);
    Task StopAsync();
    void Poll();
    
    // مدیریت اتصالات
    Task<INetworkPeer> ConnectAsync(string address, int port, object connectionData = null);
    void DisconnectPeer(INetworkPeer peer, DisconnectReason reason = DisconnectReason.Disconnect);
    
    // ارسال پیام
    Task SendToAsync(INetworkPeer peer, NetworkMessage message);
    Task BroadcastAsync(NetworkMessage message, IEnumerable<INetworkPeer> peers = null);
    
    // مدیریت peers
    IReadOnlyList<INetworkPeer> GetConnectedPeers();
    INetworkPeer GetPeer(string peerId);
}