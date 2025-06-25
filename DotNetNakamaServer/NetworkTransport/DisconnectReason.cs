namespace DotNetNakamaServer;

public enum DisconnectReason
{
    Disconnect,
    Timeout,
    ConnectionLost,
    RemoteConnectionClose,
    Kicked
}