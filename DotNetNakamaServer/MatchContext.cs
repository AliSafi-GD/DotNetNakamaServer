namespace DotNetNakamaServer;

public class MatchContext
{
    public string MatchId { get; set; }
    public string NodeId { get; set; }
    public ILogger Logger { get; set; }
    public IMatchDispatcher Dispatcher { get; set; }
    public Dictionary<string, string> Environment { get; set; } = new();
}