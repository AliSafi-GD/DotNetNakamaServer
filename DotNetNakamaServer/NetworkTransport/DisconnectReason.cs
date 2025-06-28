namespace DotNetNakamaServer.NetworkTransport;

public enum DisconnectReason
{
    Disconnect,
    Timeout,
    ConnectionLost,
    RemoteConnectionClose,
    Kicked
}