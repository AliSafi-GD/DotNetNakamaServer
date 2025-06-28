namespace DotNetNakamaServer.NetworkTransport;

public class ConnectionRequest
{
    public string Address { get; set; }
    public string ConnectionKey { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    
    public void Accept() => IsAccepted = true;
    public void Reject(string reason = "") { IsRejected = true; RejectReason = reason; }
    
    public bool IsAccepted { get; private set; }
    public bool IsRejected { get; private set; }
    public string RejectReason { get; private set; }
}