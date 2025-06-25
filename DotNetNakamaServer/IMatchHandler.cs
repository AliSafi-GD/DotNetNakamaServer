namespace DotNetNakamaServer;

public interface IMatchHandler
{
    string HandlerName { get; }
    Task<MatchState> MatchInit(MatchContext context, Dictionary<string, object> parameters);
    Task<MatchState> MatchLoop(MatchContext context, MatchState state, List<MatchMessage> messages);
    Task<bool> MatchJoinAttempt(MatchContext context, MatchState state, PlayerPresence presence);
    Task<MatchState> MatchJoin(MatchContext context, MatchState state, List<PlayerPresence> newPresences);
    Task<MatchState> MatchLeave(MatchContext context, MatchState state, List<PlayerPresence> leftPresences);
    Task<MatchState> MatchTerminate(MatchContext context, MatchState state, int graceSeconds);
    Task<(MatchState, string)> MatchSignal(MatchContext context, MatchState state, string signal);
}