using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using DotNetNakamaServer;

public class MatchmakingTicket
{
    public string TicketId { get; set; } = Guid.NewGuid().ToString();
    public PlayerPresence Player { get; set; }
    public string GameMode { get; set; }
    public int SkillLevel { get; set; } = 1000; // ELO-like system
    public string Region { get; set; } = "default";
    public Dictionary<string, object> Preferences { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    public TimeSpan MaxWaitTime { get; set; } = TimeSpan.FromMinutes(5);
    public bool IsExpired => DateTime.UtcNow - CreatedAt > MaxWaitTime;
}

// تنظیمات matchmaking برای هر نوع بازی

// نتیجه matchmaking

// آمار matchmaking