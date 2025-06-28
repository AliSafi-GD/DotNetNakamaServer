namespace DotNetNakamaServer.NetworkTransport;

public enum DeliveryMode
{
    Unreliable,
    ReliableOrdered,
    ReliableUnordered,
    UnreliableSequenced
}