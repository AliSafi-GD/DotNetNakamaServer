using System.Collections.Concurrent;
using System.Text.Json;
using DotNetNakamaServer;

public class MatchmakingService
{
    private readonly MatchEngine _matchEngine;
    private readonly ILogger _logger;
    private readonly Dictionary<string, MatchmakingQueue> _queues = new();
    private readonly Dictionary<string, GameModeConfig> _gameModeConfigs = new();
    private readonly Timer _matchmakingTimer;
    private readonly Timer _cleanupTimer;
    private readonly ConcurrentDictionary<string, MatchmakingTicket> _playerTickets = new();

    // آمار
    private int _totalMatches = 0;
    private int _successfulMatches = 0;
    private int _failedMatches = 0;
    private readonly List<double> _waitTimes = new();

    public MatchmakingService(MatchEngine matchEngine, ILogger logger)
    {
        _matchEngine = matchEngine;
        _logger = logger;

        // تایمر matchmaking هر 2 ثانیه
        _matchmakingTimer = new Timer(ProcessMatchmaking, null, 
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        // تایمر cleanup هر 30 ثانیه
        _cleanupTimer = new Timer(CleanupExpiredTickets, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        // پیکربندی پیش‌فرض
        SetupDefaultConfigs();
    }

    private void SetupDefaultConfigs()
    {
        // Tic-Tac-Toe
        RegisterGameMode("tictactoe", new GameModeConfig
        {
            GameMode = "tictactoe",
            MinPlayers = 2,
            MaxPlayers = 2,
            MaxSkillDifference = 300,
            MatchTimeout = TimeSpan.FromSeconds(30),
            RequireSameRegion = false
        });

        // Chess
        RegisterGameMode("chess", new GameModeConfig
        {
            GameMode = "chess",
            MinPlayers = 2,
            MaxPlayers = 2,
            MaxSkillDifference = 200,
            MatchTimeout = TimeSpan.FromMinutes(1),
            RequireSameRegion = false
        });

        // 4-Player Game
        RegisterGameMode("fourplayer", new GameModeConfig
        {
            GameMode = "fourplayer",
            MinPlayers = 4,
            MaxPlayers = 4,
            MaxSkillDifference = 400,
            MatchTimeout = TimeSpan.FromMinutes(2),
            RequireSameRegion = true
        });

        // Battle Royale
        RegisterGameMode("battleroyale", new GameModeConfig
        {
            GameMode = "battleroyale",
            MinPlayers = 10,
            MaxPlayers = 100,
            MaxSkillDifference = 500,
            MatchTimeout = TimeSpan.FromMinutes(3),
            RequireSameRegion = true
        });
    }

    public void RegisterGameMode(string gameMode, GameModeConfig config)
    {
        _gameModeConfigs[gameMode] = config;
        _queues[gameMode] = new MatchmakingQueue(gameMode, config, _logger);
        _logger?.LogInfo($"Registered game mode: {gameMode}");
    }

    public async Task<string> JoinQueue(PlayerPresence player, string gameMode, 
        int skillLevel = 1000, string region = "default", 
        Dictionary<string, object> preferences = null)
    {
        try
        {
            if (!_queues.TryGetValue(gameMode, out var queue))
            {
                throw new ArgumentException($"Game mode '{gameMode}' not found");
            }

            // بررسی اینکه بازیکن قبلاً در صف نباشه
            if (_playerTickets.TryGetValue(player.SessionId, out var existingTicket))
            {
                await LeaveQueue(player.SessionId);
            }

            // ایجاد تیکت جدید
            var ticket = new MatchmakingTicket
            {
                Player = player,
                GameMode = gameMode,
                SkillLevel = skillLevel,
                Region = region,
                Preferences = preferences ?? new Dictionary<string, object>()
            };

            // اضافه کردن به صف
            queue.AddTicket(ticket);
            _playerTickets[player.SessionId] = ticket;

            // اطلاع‌رسانی به بازیکن
            await NotifyPlayer(player, "QUEUE_JOINED", new { 
                ticketId = ticket.TicketId,
                gameMode = gameMode,
                estimatedWaitTime = GetEstimatedWaitTime(gameMode)
            });

            _logger?.LogInfo($"Player {player.Username} joined {gameMode} queue");
            return ticket.TicketId;
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to join queue: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> LeaveQueue(string sessionId)
    {
        try
        {
            if (!_playerTickets.TryRemove(sessionId, out var ticket))
                return false;

            if (_queues.TryGetValue(ticket.GameMode, out var queue))
            {
                queue.RemoveTicket(ticket.TicketId);
                
                await NotifyPlayer(ticket.Player, "QUEUE_LEFT", new { 
                    reason = "player_requested" 
                });
                
                _logger?.LogInfo($"Player {ticket.Player.Username} left {ticket.GameMode} queue");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to leave queue: {ex.Message}");
            return false;
        }
    }

    private async void ProcessMatchmaking(object state)
    {
        try
        {
            foreach (var queue in _queues.Values)
            {
                var match = queue.TryCreateMatch();
                if (match != null)
                {
                    _ = Task.Run(() => CreateAndStartMatch(queue.GameMode, match));
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error in matchmaking process: {ex.Message}");
        }
    }

    private async Task CreateAndStartMatch(string gameMode, List<MatchmakingTicket> tickets)
    {
        string matchId = null;
        
        try
        {
            _totalMatches++;

            // ایجاد match
            var config = _gameModeConfigs[gameMode];
            matchId = await _matchEngine.CreateMatchAsync(gameMode, config.HandlerParameters);

            if (string.IsNullOrEmpty(matchId))
            {
                throw new InvalidOperationException("Failed to create match");
            }

            // اضافه کردن بازیکنان
            var joinTasks = new List<Task>();
            foreach (var ticket in tickets)
            {
                joinTasks.Add(JoinPlayerToMatch(ticket, matchId));
            }

            await Task.WhenAll(joinTasks);

            // محاسبه آمار
            var averageWaitTime = tickets.Average(t => 
                (DateTime.UtcNow - t.CreatedAt).TotalSeconds);
            _waitTimes.Add(averageWaitTime);

            _successfulMatches++;
            _logger?.LogInfo($"Successfully created match {matchId} for {tickets.Count} players in {gameMode}");

            // اطلاع‌رسانی موفقیت
            foreach (var ticket in tickets)
            {
                await NotifyPlayer(ticket.Player, "MATCH_FOUND", new {
                    matchId = matchId,
                    gameMode = gameMode,
                    waitTime = (DateTime.UtcNow - ticket.CreatedAt).TotalSeconds
                });

                // حذف از tracking
                _playerTickets.TryRemove(ticket.Player.SessionId, out _);
            }
        }
        catch (Exception ex)
        {
            _failedMatches++;
            _logger?.LogError($"Failed to create match for {gameMode}: {ex.Message}");

            // برگرداندن بازیکنان به صف
            if (_queues.TryGetValue(gameMode, out var queue))
            {
                foreach (var ticket in tickets)
                {
                    ticket.LastUpdate = DateTime.UtcNow;
                    queue.AddTicket(ticket);
                    
                    await NotifyPlayer(ticket.Player, "MATCH_FAILED", new {
                        error = "Failed to create match, returned to queue"
                    });
                }
            }

            // پاک کردن match ناموفق
            if (!string.IsNullOrEmpty(matchId))
            {
                try
                {
                    await _matchEngine.TerminateMatchAsync(matchId, 0);
                }
                catch { /* ignore cleanup errors */ }
            }
        }
    }

    private async Task JoinPlayerToMatch(MatchmakingTicket ticket, string matchId)
    {
        try
        {
            var success = await _matchEngine.JoinMatchAsync(matchId, ticket.Player.SessionId);
            if (!success)
            {
                throw new InvalidOperationException($"Failed to join player {ticket.Player.Username} to match {matchId}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to join player to match: {ex.Message}");
            throw;
        }
    }

    private async Task NotifyPlayer(PlayerPresence player, string eventType, object data)
    {
        try
        {
            var notification = new {
                eventType = eventType,
                data = data,
                timestamp = DateTime.UtcNow
            };

            var jsonData = JsonSerializer.SerializeToUtf8Bytes(notification);
            await player.NetworkPeer.SendAsync(new NetworkMessage
            {
                OpCode = 300, // Matchmaking notification
                Data = jsonData,
                DeliveryMode = DeliveryMode.ReliableOrdered
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Failed to notify player {player.Username}: {ex.Message}");
        }
    }

    private double GetEstimatedWaitTime(string gameMode)
    {
        if (_queues.TryGetValue(gameMode, out var queue) && 
            _gameModeConfigs.TryGetValue(gameMode, out var config))
        {
            var playersInQueue = queue.Count;
            var playersNeeded = config.MinPlayers;
            
            if (playersInQueue == 0)
                return 60; // 1 minute default
                
            // تخمین ساده بر اساس تعداد بازیکنان
            var estimatedSeconds = Math.Max(5, (playersNeeded - playersInQueue + 1) * 15);
            return Math.Min(estimatedSeconds, 300); // حداکثر 5 دقیقه
        }
        
        return 60;
    }

    private void CleanupExpiredTickets(object state)
    {
        try
        {
            foreach (var queue in _queues.Values)
            {
                queue.CleanExpiredTickets();
            }

            // پاک کردن expired tickets از tracking
            var expiredSessions = _playerTickets
                .Where(kvp => kvp.Value.IsExpired || !kvp.Value.Player.IsConnected)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var sessionId in expiredSessions)
            {
                _playerTickets.TryRemove(sessionId, out _);
            }

            if (expiredSessions.Count > 0)
            {
                _logger?.LogInfo($"Cleaned up {expiredSessions.Count} expired tickets");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error in cleanup: {ex.Message}");
        }
    }

    public MatchmakingStats GetStats()
    {
        return new MatchmakingStats
        {
            TotalPlayersInQueue = _queues.Values.Sum(q => q.Count),
            TotalMatches = _totalMatches,
            SuccessfulMatches = _successfulMatches,
            FailedMatches = _failedMatches,
            AverageWaitTime = _waitTimes.Count > 0 ? _waitTimes.Average() : 0,
            QueuesByGameMode = _queues.ToDictionary(
                kvp => kvp.Key, 
                kvp => kvp.Value.Count
            )
        };
    }

    public List<MatchmakingTicket> GetQueueStatus(string gameMode)
    {
        if (_queues.TryGetValue(gameMode, out var queue))
        {
            return queue.GetAllTickets();
        }
        return new List<MatchmakingTicket>();
    }

    public void Dispose()
    {
        _matchmakingTimer?.Dispose();
        _cleanupTimer?.Dispose();
    }
}