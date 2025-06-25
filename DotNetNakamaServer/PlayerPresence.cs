namespace DotNetNakamaServer;

public class PlayerPresence
{
    public string UserId { get; set; }
    public string SessionId { get; set; }
    public string Username { get; set; }
    public INetworkPeer NetworkPeer { get; set; }
    public DateTime JoinTime { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    // راحتی استفاده
    public bool IsConnected => NetworkPeer?.IsConnected ?? false;
}