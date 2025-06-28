namespace DotNetNakamaServer.NetworkTransport;

public static class NetworkTransportFactory
{
    private static readonly Dictionary<string, Func<INetworkTransport>> _transportFactories = new()
    {
        ["litenetlib"] = () => new LiteNetLibTransport(),
        //["tcp"] = () => new TcpTransport(),
        //["websocket"] = () => new WebSocketTransport(),
        // اضافه کردن transport های جدید
    };

    public static INetworkTransport Create(string transportType)
    {
        if (_transportFactories.TryGetValue(transportType.ToLowerInvariant(), out var factory))
        {
            return factory();
        }
        
        throw new NotSupportedException($"Transport type '{transportType}' is not supported");
    }

    public static void RegisterTransport(string name, Func<INetworkTransport> factory)
    {
        _transportFactories[name.ToLowerInvariant()] = factory;
    }

    public static IEnumerable<string> GetSupportedTransports()
    {
        return _transportFactories.Keys;
    }
}