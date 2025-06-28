using System.Collections.Concurrent;
using System.Text.Json;
using DotNetNakamaServer.Matchmaking;
using DotNetNakamaServer.NetworkTransport;

namespace DotNetNakamaServer;

public partial class MatchEngine
{
    private readonly INetworkTransport _transport;
    private readonly ILogger _logger;
    private MatchmakingService _matchmakingService;
    
    public void SetupMatchmaking()
    {
        _matchmakingService = new MatchmakingService(this, _logger);
        
        // ثبت game mode های پشتیبانی شده
        _matchmakingService.RegisterGameMode("tictactoe", new GameModeConfig
        {
            GameMode = "tictactoe",
            MinPlayers = 2,
            MaxPlayers = 2,
            MaxSkillDifference = 300
        });
    }

    // متد جدید برای handle کردن پیام‌های matchmaking
    private async Task HandleFindMatch(PlayerPresence presence, NetworkMessage message)
    {
        try
        {
            var request = JsonSerializer.Deserialize<FindMatchRequest>(message.Data);
            
            var ticketId = await _matchmakingService.JoinQueue(
                presence, 
                request.GameMode,
                request.SkillLevel,
                request.Region,
                request.Preferences
            );

            var response = new FindMatchResponse 
            { 
                Success = true, 
                TicketId = ticketId 
            };
            
            var responseData = JsonSerializer.SerializeToUtf8Bytes(response);
            await _transport.SendToAsync(presence.NetworkPeer, new NetworkMessage
            {
                OpCode = 310, // Find match response
                Data = responseData
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to handle find match: {ex.Message}");
            
            var errorResponse = new FindMatchResponse 
            { 
                Success = false, 
                Error = ex.Message 
            };
            
            var errorData = JsonSerializer.SerializeToUtf8Bytes(errorResponse);
            await _transport.SendToAsync(presence.NetworkPeer, new NetworkMessage
            {
                OpCode = 310,
                Data = errorData
            });
        }
    }

    private async Task HandleCancelMatchmaking(PlayerPresence presence, NetworkMessage message)
    {
        try
        {
            var success = await _matchmakingService.LeaveQueue(presence.SessionId);
            
            var response = new CancelMatchmakingResponse { Success = success };
            var responseData = JsonSerializer.SerializeToUtf8Bytes(response);
            
            await _transport.SendToAsync(presence.NetworkPeer, new NetworkMessage
            {
                OpCode = 311, // Cancel matchmaking response
                Data = responseData
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to cancel matchmaking: {ex.Message}");
        }
    }

    public async Task<string> TerminateMatchAsync(string matchId, int graceSeconds)
    {
        if (_matches.TryGetValue(matchId, out var match))
        {
            await match.TerminateMatch(graceSeconds);
            return matchId;
        }
        return null;
    }

    public MatchmakingStats GetMatchmakingStats()
    {
        return _matchmakingService?.GetStats() ?? new MatchmakingStats();
    }
    
    // نگهداری بازی‌ها و handlers
    private readonly ConcurrentDictionary<string, MatchInstance> _matches = new();
    private readonly Dictionary<string, IMatchHandler> _handlers = new();
    
    // نگهداری presences
    private readonly ConcurrentDictionary<string, PlayerPresence> _playerSessions = new();
    
    // cleanup timer
    private readonly Timer _cleanupTimer;
    private bool _isRunning = false;

    public MatchEngine(INetworkTransport transport, ILogger logger)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _logger = logger ?? new ConsoleLogger();
        
        // راه‌اندازی رویدادهای transport
        _transport.PeerConnected += OnPeerConnected;
        _transport.PeerDisconnected += OnPeerDisconnected;
        _transport.MessageReceived += OnMessageReceived;
        _transport.ConnectionRequest += OnConnectionRequest;
        
        // تایمر تمیزکاری هر 30 ثانیه
        _cleanupTimer = new Timer(CleanupExpiredMatches, null, 
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }


    #region Public Methods

    public void RegisterHandler(string handlerName, IMatchHandler handler)
    {
        if (string.IsNullOrEmpty(handlerName))
            throw new ArgumentException("Handler name cannot be null or empty", nameof(handlerName));
        
        _handlers[handlerName] = handler;
        _logger.LogInfo($"Registered match handler: {handlerName}");
    }

    public async Task<bool> StartAsync(NetworkConfig config)
    {
        try
        {
            var result = await _transport.StartAsync(config);
            if (result)
            {
                _isRunning = true;
                
                // شروع polling loop
                _ = Task.Run(PollLoop);
                
                _logger.LogInfo($"MatchEngine started with {_transport.Name} transport on port {config.Port}");
            }
            else
            {
                _logger.LogError("Failed to start transport");
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to start MatchEngine: {ex.Message}");
            return false;
        }
    }

    public async Task StopAsync()
    {
        try
        {
            _isRunning = false;
            _logger.LogInfo("Stopping MatchEngine...");
            
            // خاتمه همه بازی‌ها
            var terminationTasks = _matches.Values.Select(m => m.TerminateMatch(5));
            await Task.WhenAll(terminationTasks);
            
            // پاک کردن
            _matches.Clear();
            _playerSessions.Clear();
            
            // توقف transport
            await _transport.StopAsync();
            
            // توقف cleanup timer
            _cleanupTimer?.Dispose();
            
            _logger.LogInfo("MatchEngine stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error stopping MatchEngine: {ex.Message}");
        }
    }

    public async Task<string> CreateMatchAsync(string handlerName, Dictionary<string, object> parameters = null)
    {
        try
        {
            if (!_handlers.TryGetValue(handlerName, out var handler))
            {
                throw new ArgumentException($"No handler registered for: {handlerName}");
            }

            var matchId = Guid.NewGuid().ToString();
            
            // ایجاد context
            var context = new MatchContext
            {
                MatchId = matchId,
                NodeId = Environment.MachineName,
                Logger = _logger
            };

            // ایجاد match instance
            var match = new MatchInstance(matchId, handlerName, handler, context);
            
            // تنظیم dispatcher
            context.Dispatcher = new MatchDispatcher(match, _transport, _logger);
            
            // initialize کردن
            var success = await match.InitializeAsync(parameters ?? new Dictionary<string, object>());
            if (!success)
            {
                match.Dispose();
                throw new InvalidOperationException("Failed to initialize match");
            }

            // اضافه کردن به collection
            _matches[matchId] = match;
            
            _logger.LogInfo($"Created match {matchId} with handler {handlerName}");
            return matchId;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to create match: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> JoinMatchAsync(string matchId, string sessionId)
    {
        try
        {
            if (!_matches.TryGetValue(matchId, out var match))
            {
                _logger.LogWarning($"Match {matchId} not found for join request");
                return false;
            }

            if (!_playerSessions.TryGetValue(sessionId, out var presence))
            {
                _logger.LogWarning($"Player session {sessionId} not found for join request");
                return false;
            }

            var success = await match.TryJoinPlayer(presence);
            if (success)
            {
                _logger.LogInfo($"Player {presence.UserId} successfully joined match {matchId}");
            }
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to join match: {ex.Message}");
            return false;
        }
    }

    public async Task LeaveMatchAsync(string matchId, string sessionId)
    {
        try
        {
            if (!_matches.TryGetValue(matchId, out var match))
                return;

            if (!_playerSessions.TryGetValue(sessionId, out var presence))
                return;

            await match.RemovePlayer(presence);
            _logger.LogInfo($"Player {presence.UserId} left match {matchId}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to leave match: {ex.Message}");
        }
    }

    public List<MatchInfo> GetMatchList()
    {
        return _matches.Values.Select(m => new MatchInfo
        {
            MatchId = m.Id,
            HandlerName = m.HandlerName,
            Label = m.State?.Label ?? "",
            PlayerCount = m.State?.Presences?.Count ?? 0,
            CreatedAt = m.State?.CreatedAt ?? DateTime.MinValue
        }).ToList();
    }

    #endregion

    #region Event Handlers

    private void OnConnectionRequest(ConnectionRequest request)
    {
        try
        {
            // بررسی connection key
            if (request.ConnectionKey == "GameKey") // یا منطق پیچیده‌تر
            {
                request.Accept();
                _logger.LogInfo($"Accepted connection from {request.Address}");
            }
            else
            {
                request.Reject("Invalid connection key");
                _logger.LogWarning($"Rejected connection from {request.Address}: Invalid key");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error handling connection request: {ex.Message}");
            request.Reject("Internal server error");
        }
    }

    private void OnPeerConnected(INetworkPeer peer)
    {
        try
        {
            var presence = new PlayerPresence
            {
                SessionId = peer.Id,
                UserId = $"user_{peer.Id}", // موقتی، باید از authentication بیاد
                Username = $"Player_{peer.Id}",
                NetworkPeer = peer
            };

            _playerSessions[peer.Id] = presence;
            _logger.LogInfo($"Player connected: {peer.Id} from {peer.Address}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error handling peer connected: {ex.Message}");
        }
    }

    private void OnPeerDisconnected(INetworkPeer peer, DisconnectReason reason)
    {
        try
        {
            _logger.LogInfo($"Player disconnected: {peer.Id}, reason: {reason}");

            if (_playerSessions.TryRemove(peer.Id, out var presence))
            {
                // حذف از همه بازی‌ها
                var removalTasks = _matches.Values.Select(match => match.RemovePlayer(presence));
                _ = Task.WhenAll(removalTasks);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error handling peer disconnected: {ex.Message}");
        }
    }

    private void OnMessageReceived(INetworkPeer peer, NetworkMessage message)
    {
        try
        {
            if (!_playerSessions.TryGetValue(peer.Id, out var presence))
                return;

            switch (message.OpCode)
            {
                case 100: // Create match
                    _ = Task.Run(() => HandleCreateMatch(presence, message));
                    break;
                    
                case 101: // Join match
                    _ = Task.Run(() => HandleJoinMatch(presence, message));
                    break;
                    
                case 102: // Leave match
                    _ = Task.Run(() => HandleLeaveMatch(presence, message));
                    break;
                    
                case 200: // Game message
                    HandleGameMessage(presence, message);
                    break;
                    
                default:
                    _logger.LogWarning($"Unknown message opcode: {message.OpCode} from {peer.Id}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error handling message from {peer.Id}: {ex.Message}");
        }
    }

    #endregion

    #region Message Handlers

    private async Task HandleCreateMatch(PlayerPresence presence, NetworkMessage message)
    {
        try
        {
            var data = JsonSerializer.Deserialize<CreateMatchRequest>(message.Data);
            var matchId = await CreateMatchAsync(data.HandlerName, data.Parameters);
            
            // پاسخ
            var response = new CreateMatchResponse { MatchId = matchId, Success = true };
            var responseData = JsonSerializer.SerializeToUtf8Bytes(response);
            await _transport.SendToAsync(presence.NetworkPeer, new NetworkMessage
            {
                OpCode = 110, // Create match response
                Data = responseData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to handle create match: {ex.Message}");
            
            var errorResponse = new CreateMatchResponse { Success = false, Error = ex.Message };
            var errorData = JsonSerializer.SerializeToUtf8Bytes(errorResponse);
            await _transport.SendToAsync(presence.NetworkPeer, new NetworkMessage
            {
                OpCode = 110,
                Data = errorData
            });
        }
    }

    private async Task HandleJoinMatch(PlayerPresence presence, NetworkMessage message)
    {
        try
        {
            var data = JsonSerializer.Deserialize<JoinMatchRequest>(message.Data);
            var success = await JoinMatchAsync(data.MatchId, presence.SessionId);
            
            var response = new JoinMatchResponse { Success = success };
            var responseData = JsonSerializer.SerializeToUtf8Bytes(response);
            await _transport.SendToAsync(presence.NetworkPeer, new NetworkMessage
            {
                OpCode = 111, // Join match response
                Data = responseData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to handle join match: {ex.Message}");
        }
    }

    private async Task HandleLeaveMatch(PlayerPresence presence, NetworkMessage message)
    {
        try
        {
            var data = JsonSerializer.Deserialize<LeaveMatchRequest>(message.Data);
            await LeaveMatchAsync(data.MatchId, presence.SessionId);
            
            var response = new LeaveMatchResponse { Success = true };
            var responseData = JsonSerializer.SerializeToUtf8Bytes(response);
            await _transport.SendToAsync(presence.NetworkPeer, new NetworkMessage
            {
                OpCode = 112, // Leave match response
                Data = responseData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to handle leave match: {ex.Message}");
        }
    }

    private void HandleGameMessage(PlayerPresence presence, NetworkMessage message)
    {
        try
        {
            // پیدا کردن بازی‌هایی که بازیکن توشون هست
            var playerMatches = _matches.Values.Where(m => 
                m.State?.Presences?.Any(p => p.SessionId == presence.SessionId) == true);

            foreach (var match in playerMatches)
            {
                var matchMessage = new MatchMessage
                {
                    OpCode = message.OpCode,
                    Data = message.Data,
                    Sender = presence,
                    Timestamp = message.Timestamp
                };
                
                match.QueueMessage(matchMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to handle game message: {ex.Message}");
        }
    }

    #endregion

    #region Private Methods

    private async Task PollLoop()
    {
        while (_isRunning)
        {
            try
            {
                _transport.Poll();
                await Task.Delay(15); // 15ms delay
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in poll loop: {ex.Message}");
                await Task.Delay(1000); // انتظار بیشتر در صورت خطا
            }
        }
    }

    private void CleanupExpiredMatches(object state)
    {
        try
        {
            var expiredMatches = _matches.Values.Where(m => 
                m.State?.IsTerminated == true || 
                DateTime.UtcNow - m.State?.LastActivity > TimeSpan.FromMinutes(30)
            ).ToList();

            foreach (var match in expiredMatches)
            {
                _ = Task.Run(() => CleanupMatch(match));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error in cleanup: {ex.Message}");
        }
    }

    private async Task CleanupMatch(MatchInstance match)
    {
        try
        {
            await match.TerminateMatch(0);
            _matches.TryRemove(match.Id, out _);
            match.Dispose();
            
            _logger.LogInfo($"Cleaned up expired match: {match.Id}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error cleaning up match {match.Id}: {ex.Message}");
        }
    }

    #endregion

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _transport?.Dispose();
    }
}

#region DTOs

public class CreateMatchRequest
{
    public string HandlerName { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class CreateMatchResponse
{
    public bool Success { get; set; }
    public string MatchId { get; set; }
    public string Error { get; set; }
}

public class JoinMatchRequest
{
    public string MatchId { get; set; }
}

public class JoinMatchResponse
{
    public bool Success { get; set; }
    public string Error { get; set; }
}

public class LeaveMatchRequest
{
    public string MatchId { get; set; }
}

public class LeaveMatchResponse
{
    public bool Success { get; set; }
}

public class MatchInfo
{
    public string MatchId { get; set; }
    public string HandlerName { get; set; }
    public string Label { get; set; }
    public int PlayerCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

#endregion

#region DTOs برای Matchmaking

public class FindMatchRequest
{
    public string GameMode { get; set; }
    public int SkillLevel { get; set; } = 1000;
    public string Region { get; set; } = "default";
    public Dictionary<string, object> Preferences { get; set; } = new();
}

public class FindMatchResponse
{
    public bool Success { get; set; }
    public string TicketId { get; set; }
    public string Error { get; set; }
}

public class CancelMatchmakingResponse
{
    public bool Success { get; set; }
}

#endregion