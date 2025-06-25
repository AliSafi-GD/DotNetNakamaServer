using LiteNetLib;
using LiteNetLib.Utils;

namespace DotNetNakamaServer;

public class LiteNetLibTransport : INetworkTransport
{
    public string Name => "LiteNetLib";
    public bool IsRunning => _netManager?.IsRunning ?? false;

    private NetManager _netManager;
    private EventBasedNetListener _listener;
    private readonly Dictionary<int, LiteNetLibPeer> _peers = new();
    private NetworkConfig _config;

    // رویدادها
    public event Action<INetworkPeer> PeerConnected;
    public event Action<INetworkPeer, DisconnectReason> PeerDisconnected;
    public event Action<INetworkPeer, NetworkMessage> MessageReceived;
    public event Action<ConnectionRequest> ConnectionRequest;

    public async Task<bool> StartAsync(NetworkConfig config)
    {
        try
        {
            _config = config;
            _listener = new EventBasedNetListener();
            _netManager = new NetManager(_listener)
            {
                AutoRecycle = true,
                IPv6Enabled = false
            };

            // تنظیمات اضافی از config
            if (config.TransportSpecific.TryGetValue("IPv6Enabled", out var ipv6))
                _netManager.IPv6Enabled = (bool)ipv6;

            if (config.TransportSpecific.TryGetValue("DisconnectTimeout", out var timeout))
                _netManager.DisconnectTimeout = (int)timeout;

            // راه‌اندازی رویدادها
            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnNetworkReceive;

            _netManager.Start(config.Port);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start LiteNetLib: {ex.Message}");
            return false;
        }
    }

    public async Task StopAsync()
    {
        _netManager?.Stop();
        _peers.Clear();
    }

    public void Poll()
    {
        _netManager?.PollEvents();
    }

    public async Task<INetworkPeer> ConnectAsync(string address, int port, object connectionData = null)
    {
        var peer = _netManager.Connect(address, port, _config.ConnectionKey);
        if (peer != null)
        {
            var wrapper = new LiteNetLibPeer(peer);
            _peers[peer.Id] = wrapper;
            return wrapper;
        }
        return null;
    }

    public void DisconnectPeer(INetworkPeer peer, DisconnectReason reason)
    {
        if (peer is LiteNetLibPeer lnlPeer)
        {
            lnlPeer.NativePeer.Disconnect();
        }
    }

    public async Task SendToAsync(INetworkPeer peer, NetworkMessage message)
    {
        if (peer is LiteNetLibPeer lnlPeer)
        {
            var writer = new NetDataWriter();
            writer.Put(message.OpCode);
            writer.Put(message.Data?.Length ?? 0);
            if (message.Data != null)
                writer.Put(message.Data);

            var deliveryMethod = ConvertDeliveryMode(message.DeliveryMode);
            lnlPeer.NativePeer.Send(writer, message.Channel, deliveryMethod);
        }
    }

    public async Task BroadcastAsync(NetworkMessage message, IEnumerable<INetworkPeer> peers = null)
    {
        var targets = peers?.Cast<LiteNetLibPeer>() ?? _peers.Values;
        
        var writer = new NetDataWriter();
        writer.Put(message.OpCode);
        writer.Put(message.Data?.Length ?? 0);
        if (message.Data != null)
            writer.Put(message.Data);

        var deliveryMethod = ConvertDeliveryMode(message.DeliveryMode);
        
        foreach (var peer in targets)
        {
            if (peer.IsConnected)
                peer.NativePeer.Send(writer, message.Channel, deliveryMethod);
        }
    }

    public IReadOnlyList<INetworkPeer> GetConnectedPeers()
    {
        return _peers.Values.Where(p => p.IsConnected).Cast<INetworkPeer>().ToList();
    }

    public INetworkPeer GetPeer(string peerId)
    {
        if (int.TryParse(peerId, out var id) && _peers.TryGetValue(id, out var peer))
            return peer;
        return null;
    }

    // Event handlers
    private void OnConnectionRequest(LiteNetLib.ConnectionRequest request)
    {
        var req = new ConnectionRequest
        {
            Address = request.RemoteEndPoint.Address.ToString(),
            ConnectionKey = request.Data.GetString()
        };
        
        ConnectionRequest?.Invoke(req);
        
        if (req.IsAccepted)
            request.Accept();
        else if (req.IsRejected)
            request.Reject();
    }

    private void OnPeerConnected(NetPeer peer)
    {
        var wrapper = new LiteNetLibPeer(peer);
        _peers[peer.Id] = wrapper;
        PeerConnected?.Invoke(wrapper);
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (_peers.TryGetValue(peer.Id, out var wrapper))
        {
            _peers.Remove(peer.Id);
            var reason = ConvertDisconnectInfo(disconnectInfo);
            PeerDisconnected?.Invoke(wrapper, reason);
        }
    }

    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        if (_peers.TryGetValue(peer.Id, out var wrapper))
        {
            var opCode = reader.GetInt();
            var dataLength = reader.GetInt();
            var data = dataLength > 0 ? reader.GetBytesWithLength() : null;

            var message = new NetworkMessage
            {
                OpCode = opCode,
                Data = data,
                Channel = channel,
                DeliveryMode = ConvertDeliveryMethod(deliveryMethod)
            };

            MessageReceived?.Invoke(wrapper, message);
        }
        reader.Recycle();
    }

    // Helper methods
    private DeliveryMethod ConvertDeliveryMode(DeliveryMode mode)
    {
        return mode switch
        {
            DeliveryMode.Unreliable => DeliveryMethod.Unreliable,
            DeliveryMode.ReliableOrdered => DeliveryMethod.ReliableOrdered,
            DeliveryMode.ReliableUnordered => DeliveryMethod.ReliableUnordered,
            DeliveryMode.UnreliableSequenced => DeliveryMethod.Sequenced,
            _ => DeliveryMethod.ReliableOrdered
        };
    }

    private DeliveryMode ConvertDeliveryMethod(DeliveryMethod method)
    {
        return method switch
        {
            DeliveryMethod.Unreliable => DeliveryMode.Unreliable,
            DeliveryMethod.ReliableOrdered => DeliveryMode.ReliableOrdered,
            DeliveryMethod.ReliableUnordered => DeliveryMode.ReliableUnordered,
            DeliveryMethod.Sequenced => DeliveryMode.UnreliableSequenced,
            _ => DeliveryMode.ReliableOrdered
        };
    }

    private DisconnectReason ConvertDisconnectInfo(DisconnectInfo info)
    {
        return info.Reason switch
        {
            LiteNetLib.DisconnectReason.DisconnectPeerCalled => DisconnectReason.Disconnect,
            LiteNetLib.DisconnectReason.Timeout => DisconnectReason.Timeout,
            LiteNetLib.DisconnectReason.ConnectionFailed => DisconnectReason.ConnectionLost,
            LiteNetLib.DisconnectReason.RemoteConnectionClose => DisconnectReason.RemoteConnectionClose,
            _ => DisconnectReason.Disconnect
        };
    }

    public void Dispose()
    {
        _netManager?.Stop();
        _netManager = null;
    }
}