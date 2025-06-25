namespace DotNetNakamaServer;

public interface IMatchDispatcher
{
    Task BroadcastMessage(int opCode, byte[] data, List<PlayerPresence> recipients = null);
    Task SendToPlayer(PlayerPresence player, int opCode, byte[] data);
    Task KickPlayer(PlayerPresence player, string reason = "");
    Task UpdateMatchLabel(string label);
}