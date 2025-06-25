using System.Text.Json;

namespace DotNetNakamaServer;

public class MatchDispatcher : IMatchDispatcher
{
    private readonly MatchInstance _match;
    private readonly INetworkTransport _transport;
    private readonly ILogger _logger;

    public MatchDispatcher(MatchInstance match, INetworkTransport transport, ILogger logger)
    {
        _match = match;
        _transport = transport;
        _logger = logger;
    }

    public async Task BroadcastMessage(int opCode, byte[] data, List<PlayerPresence> recipients = null)
    {
        try
        {
            var targets = recipients ?? _match.State?.Presences;
            if (targets == null || !targets.Any()) return;

            var message = new NetworkMessage
            {
                OpCode = opCode,
                Data = data,
                DeliveryMode = DeliveryMode.ReliableOrdered
            };

            var connectedPeers = targets.Where(p => p.IsConnected)
                                       .Select(p => p.NetworkPeer)
                                       .Where(p => p != null);

            if (connectedPeers.Any())
            {
                await _transport.BroadcastAsync(message, connectedPeers);
                _logger?.LogInfo($"Broadcasted message {opCode} to {connectedPeers.Count()} players in match {_match.Id}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to broadcast message in match {_match.Id}: {ex.Message}");
        }
    }

    public async Task SendToPlayer(PlayerPresence player, int opCode, byte[] data)
    {
        try
        {
            if (!player.IsConnected) return;

            var message = new NetworkMessage
            {
                OpCode = opCode,
                Data = data,
                DeliveryMode = DeliveryMode.ReliableOrdered
            };

            await _transport.SendToAsync(player.NetworkPeer, message);
            _logger?.LogInfo($"Sent message {opCode} to player {player.UserId} in match {_match.Id}");
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to send message to player {player.UserId}: {ex.Message}");
        }
    }

    public async Task KickPlayer(PlayerPresence player, string reason = "")
    {
        try
        {
            _logger?.LogInfo($"Kicking player {player.UserId} from match {_match.Id}. Reason: {reason}");
            
            // ارسال پیام kick قبل از قطع ارتباط
            var kickData = JsonSerializer.SerializeToUtf8Bytes(new { reason = reason });
            await SendToPlayer(player, 999, kickData); // OpCode 999 = Kick
            
            // کمی انتظار برای ارسال پیام
            await Task.Delay(100);
            
            // قطع ارتباط
            player.NetworkPeer?.Disconnect(DisconnectReason.Kicked);
            
            // حذف از بازی
            await _match.RemovePlayer(player);
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to kick player {player.UserId}: {ex.Message}");
        }
    }

    public async Task UpdateMatchLabel(string label)
    {
        try
        {
            if (_match.State != null)
            {
                _match.State.Label = label;
                _logger?.LogInfo($"Updated label for match {_match.Id}: {label}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to update match label: {ex.Message}");
        }
    }
}