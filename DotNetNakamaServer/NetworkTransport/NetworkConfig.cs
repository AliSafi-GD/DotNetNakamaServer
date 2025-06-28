namespace DotNetNakamaServer.NetworkTransport;

public class NetworkConfig
{
    public string Address { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 9050;
    public string ConnectionKey { get; set; } = "";
    public int MaxConnections { get; set; } = 100;
    public Dictionary<string, object> TransportSpecific { get; set; } = new();
}