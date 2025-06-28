namespace DotNetNakamaServer.Matchmaking;

public class MatchmakingStats
{
    public int TotalPlayersInQueue { get; set; }
    public int TotalMatches { get; set; }
    public double AverageWaitTime { get; set; }
    public int SuccessfulMatches { get; set; }
    public int FailedMatches { get; set; }
    public Dictionary<string, int> QueuesByGameMode { get; set; } = new();
}