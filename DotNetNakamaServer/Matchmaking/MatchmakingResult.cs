public class MatchmakingResult
{
    public bool Success { get; set; }
    public string MatchId { get; set; }
    public List<MatchmakingTicket> MatchedTickets { get; set; } = new();
    public string Error { get; set; }
    public DateTime MatchCreatedAt { get; set; } = DateTime.UtcNow;
}