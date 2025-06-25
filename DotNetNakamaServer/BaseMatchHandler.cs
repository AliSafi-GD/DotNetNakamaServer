namespace DotNetNakamaServer;

public abstract class BaseMatchHandler : IMatchHandler
{
    public abstract string HandlerName { get; }

    public virtual async Task<MatchState> MatchInit(MatchContext context, Dictionary<string, object> parameters)
    {
        var state = new MatchState
        {
            MatchId = context.MatchId,
            Label = HandlerName,
            TickRate = 10
        };
        
        context.Logger?.LogInfo($"Match {context.MatchId} initialized with handler {HandlerName}");
        return state;
    }

    public abstract Task<MatchState> MatchLoop(MatchContext context, MatchState state, List<MatchMessage> messages);

    public virtual async Task<bool> MatchJoinAttempt(MatchContext context, MatchState state, PlayerPresence presence)
    {
        // پیش‌فرض: همه می‌توانند ملحق شوند
        return true;
    }

    public virtual async Task<MatchState> MatchJoin(MatchContext context, MatchState state, List<PlayerPresence> newPresences)
    {
        foreach (var presence in newPresences)
        {
            context.Logger?.LogInfo($"Player {presence.Username} ({presence.UserId}) joined match {context.MatchId}");
            state.Presences.Add(presence);
        }
        
        state.LastActivity = DateTime.UtcNow;
        return state;
    }

    public virtual async Task<MatchState> MatchLeave(MatchContext context, MatchState state, List<PlayerPresence> leftPresences)
    {
        foreach (var presence in leftPresences)
        {
            context.Logger?.LogInfo($"Player {presence.Username} ({presence.UserId}) left match {context.MatchId}");
            state.Presences.RemoveAll(p => p.SessionId == presence.SessionId);
        }
        
        state.LastActivity = DateTime.UtcNow;
        return state;
    }

    public virtual async Task<MatchState> MatchTerminate(MatchContext context, MatchState state, int graceSeconds)
    {
        context.Logger?.LogInfo($"Match {context.MatchId} terminating with {graceSeconds}s grace period");
        state.IsTerminated = true;
        return null; // null = terminate match
    }

    public virtual async Task<(MatchState, string)> MatchSignal(MatchContext context, MatchState state, string signal)
    {
        // پیش‌فرض: ignore signals
        return (state, "");
    }
}