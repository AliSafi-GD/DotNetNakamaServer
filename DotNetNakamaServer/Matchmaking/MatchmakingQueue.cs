using System.Collections.Concurrent;
using DotNetNakamaServer;

public class MatchmakingQueue
{
    private readonly string _gameMode;
    private readonly GameModeConfig _config;
    private readonly ConcurrentQueue<MatchmakingTicket> _tickets = new();
    private readonly object _lockObject = new object();
    private readonly ILogger _logger;

    public MatchmakingQueue(string gameMode, GameModeConfig config, ILogger logger)
    {
        _gameMode = gameMode;
        _config = config;
        _logger = logger;
    }

    public string GameMode => _gameMode;
    public int Count => _tickets.Count;

    public void AddTicket(MatchmakingTicket ticket)
    {
        ticket.GameMode = _gameMode;
        _tickets.Enqueue(ticket);
        _logger?.LogInfo($"Added player {ticket.Player.Username} to {_gameMode} queue. Queue size: {Count}");
    }

    public bool RemoveTicket(string ticketId)
    {
        lock (_lockObject)
        {
            var tempList = new List<MatchmakingTicket>();
            bool found = false;

            // خالی کردن صف و حذف تیکت مورد نظر
            while (_tickets.TryDequeue(out var ticket))
            {
                if (ticket.TicketId == ticketId)
                {
                    found = true;
                    _logger?.LogInfo($"Removed player {ticket.Player.Username} from {_gameMode} queue");
                }
                else
                {
                    tempList.Add(ticket);
                }
            }

            // برگرداندن باقی تیکت‌ها
            foreach (var ticket in tempList)
            {
                _tickets.Enqueue(ticket);
            }

            return found;
        }
    }

    public List<MatchmakingTicket> TryCreateMatch()
    {
        lock (_lockObject)
        {
            var availableTickets = new List<MatchmakingTicket>();
            var tempQueue = new List<MatchmakingTicket>();

            // جمع‌آوری تیکت‌های معتبر
            while (_tickets.TryDequeue(out var ticket))
            {
                if (ticket.IsExpired)
                {
                    _logger?.LogWarning($"Ticket expired for player {ticket.Player.Username}");
                    continue; // حذف تیکت منقضی شده
                }

                if (ticket.Player.IsConnected)
                {
                    availableTickets.Add(ticket);
                }
                else
                {
                    _logger?.LogWarning($"Player {ticket.Player.Username} disconnected, removing from queue");
                    continue; // حذف بازیکن قطع شده
                }
            }

            // تلاش برای match
            var match = FindBestMatch(availableTickets);
            
            if (match != null && match.Count >= _config.MinPlayers)
            {
                // حذف بازیکنان match شده از لیست
                foreach (var matchedTicket in match)
                {
                    availableTickets.Remove(matchedTicket);
                }

                // برگرداندن باقی بازیکنان به صف
                foreach (var remainingTicket in availableTickets)
                {
                    _tickets.Enqueue(remainingTicket);
                }

                _logger?.LogInfo($"Created match for {match.Count} players in {_gameMode}");
                return match;
            }

            // برگرداندن همه تیکت‌ها
            foreach (var ticket in availableTickets)
            {
                _tickets.Enqueue(ticket);
            }

            return null;
        }
    }

    private List<MatchmakingTicket> FindBestMatch(List<MatchmakingTicket> tickets)
    {
        if (tickets.Count < _config.MinPlayers)
            return null;

        // سورت بر اساس زمان انتظار (FIFO اولویت)
        tickets.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));

        var bestMatch = new List<MatchmakingTicket>();
        var primaryTicket = tickets.First();
        bestMatch.Add(primaryTicket);

        // محاسبه skill range با در نظر گیری زمان انتظار
        var waitTime = DateTime.UtcNow - primaryTicket.CreatedAt;
        var relaxSteps = (int)(waitTime.TotalMilliseconds / _config.SkillRelaxTime.TotalMilliseconds);
        var currentSkillRange = _config.MaxSkillDifference + (relaxSteps * _config.SkillRelaxStep);

        // پیدا کردن بازیکنان مناسب
        foreach (var ticket in tickets.Skip(1))
        {
            if (bestMatch.Count >= _config.MaxPlayers)
                break;

            // بررسی skill difference
            var skillDiff = Math.Abs(primaryTicket.SkillLevel - ticket.SkillLevel);
            if (skillDiff > currentSkillRange)
                continue;

            // بررسی region (اگر لازم باشه)
            if (_config.RequireSameRegion && primaryTicket.Region != ticket.Region)
                continue;

            // بررسی preferences اضافی
            if (!ArePreferencesCompatible(primaryTicket, ticket))
                continue;

            bestMatch.Add(ticket);
        }

        return bestMatch.Count >= _config.MinPlayers ? bestMatch : null;
    }

    private bool ArePreferencesCompatible(MatchmakingTicket ticket1, MatchmakingTicket ticket2)
    {
        // مثال: بررسی map preference
        if (ticket1.Preferences.TryGetValue("preferredMap", out var map1) &&
            ticket2.Preferences.TryGetValue("preferredMap", out var map2))
        {
            return map1.Equals(map2);
        }

        // پیش‌فرض: سازگار
        return true;
    }

    public List<MatchmakingTicket> GetAllTickets()
    {
        return _tickets.ToList();
    }

    public void CleanExpiredTickets()
    {
        lock (_lockObject)
        {
            var validTickets = new List<MatchmakingTicket>();
            
            while (_tickets.TryDequeue(out var ticket))
            {
                if (!ticket.IsExpired && ticket.Player.IsConnected)
                {
                    validTickets.Add(ticket);
                }
            }

            foreach (var ticket in validTickets)
            {
                _tickets.Enqueue(ticket);
            }
        }
    }
}