namespace DotNetNakamaServer;

public class MatchState
{
    public string MatchId { get; set; }
    public string Label { get; set; } = "";
    public int TickRate { get; set; } = 10;
    public long TickCount { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public bool IsTerminated { get; set; } = false;
    
    // لیست بازیکنان
    public List<PlayerPresence> Presences { get; set; } = new();
    
    // داده‌های اختصاصی بازی
    public Dictionary<string, object> GameData { get; set; } = new();
}