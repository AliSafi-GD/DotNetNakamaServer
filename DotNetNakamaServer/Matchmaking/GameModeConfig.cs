namespace DotNetNakamaServer.Matchmaking;

public class GameModeConfig
{
    public string GameMode { get; set; }
    public int MinPlayers { get; set; } = 2;
    public int MaxPlayers { get; set; } = 2;
    public int MaxSkillDifference { get; set; } = 200; // تفاوت skill مجاز
    public TimeSpan MatchTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan SkillRelaxTime { get; set; } = TimeSpan.FromMinutes(1); // هر دقیقه skill requirement کم شه
    public int SkillRelaxStep { get; set; } = 50; // هر بار چقدر کم شه
    public bool RequireSameRegion { get; set; } = true;
    public Dictionary<string, object> HandlerParameters { get; set; } = new();
}