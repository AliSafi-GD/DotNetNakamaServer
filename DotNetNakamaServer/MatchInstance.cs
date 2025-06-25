using System.Collections.Concurrent;

namespace DotNetNakamaServer;

public class MatchInstance
{
    public string Id { get; private set; }
    public string HandlerName { get; private set; }
    public IMatchHandler Handler { get; private set; }
    public MatchContext Context { get; private set; }
    public MatchState State { get; set; }
    
    // پیام‌های در انتظار پردازش
    public ConcurrentQueue<MatchMessage> PendingMessages { get; } = new();
    
    // کنترل loop
    private Timer _tickTimer;
    private readonly object _lockObject = new object();
    private bool _isProcessing = false;
    
    public MatchInstance(string id, string handlerName, IMatchHandler handler, MatchContext context)
    {
        Id = id;
        HandlerName = handlerName;
        Handler = handler;
        Context = context;
    }

    public async Task<bool> InitializeAsync(Dictionary<string, object> parameters)
    {
        try
        {
            State = await Handler.MatchInit(Context, parameters);
            if (State == null)
            {
                Context.Logger?.LogError($"MatchInit returned null for match {Id}");
                return false;
            }

            State.MatchId = Id;
            StartTickLoop();
            
            Context.Logger?.LogInfo($"Match {Id} initialized successfully");
            return true;
        }
        catch (Exception ex)
        {
            Context.Logger?.LogError($"Failed to initialize match {Id}: {ex.Message}");
            return false;
        }
    }

    private void StartTickLoop()
    {
        if (State.TickRate <= 0) State.TickRate = 1;
        if (State.TickRate > 60) State.TickRate = 60;
        
        var interval = TimeSpan.FromMilliseconds(1000.0 / State.TickRate);
        _tickTimer = new Timer(ProcessTick, null, interval, interval);
        
        Context.Logger?.LogInfo($"Started tick loop for match {Id} at {State.TickRate} TPS");
    }

    private async void ProcessTick(object state)
    {
        if (_isProcessing || State?.IsTerminated == true) return;
        
        lock (_lockObject)
        {
            if (_isProcessing) return;
            _isProcessing = true;
        }

        try
        {
            await ProcessMatchLoop();
        }
        catch (Exception ex)
        {
            Context.Logger?.LogError($"Error in match loop {Id}: {ex.Message}");
            await TerminateMatch(0);
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private async Task ProcessMatchLoop()
    {
        if (State == null || State.IsTerminated) return;

        // جمع‌آوری پیام‌های انتظار
        var messages = new List<MatchMessage>();
        while (PendingMessages.TryDequeue(out var message))
        {
            messages.Add(message);
        }

        // اجرای match loop
        var newState = await Handler.MatchLoop(Context, State, messages);
        
        if (newState == null)
        {
            // خاتمه بازی
            await TerminateMatch(0);
            return;
        }

        // به‌روزرسانی state
        State = newState;
        State.TickCount++;
        State.LastActivity = DateTime.UtcNow;
    }

    public async Task<bool> TryJoinPlayer(PlayerPresence presence)
    {
        try
        {
            // بررسی امکان ورود
            var canJoin = await Handler.MatchJoinAttempt(Context, State, presence);
            if (!canJoin)
            {
                Context.Logger?.LogWarning($"Join attempt rejected for player {presence.UserId} in match {Id}");
                return false;
            }

            // اضافه کردن بازیکن
            var newState = await Handler.MatchJoin(Context, State, new List<PlayerPresence> { presence });
            if (newState != null)
            {
                State = newState;
                Context.Logger?.LogInfo($"Player {presence.UserId} successfully joined match {Id}");
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Context.Logger?.LogError($"Failed to join player {presence.UserId} to match {Id}: {ex.Message}");
            return false;
        }
    }

    public async Task RemovePlayer(PlayerPresence presence)
    {
        try
        {
            var newState = await Handler.MatchLeave(Context, State, new List<PlayerPresence> { presence });
            if (newState != null)
            {
                State = newState;
            }
        }
        catch (Exception ex)
        {
            Context.Logger?.LogError($"Error removing player {presence.UserId} from match {Id}: {ex.Message}");
        }
    }

    public void QueueMessage(MatchMessage message)
    {
        if (State?.IsTerminated != true)
        {
            PendingMessages.Enqueue(message);
        }
    }

    public async Task TerminateMatch(int graceSeconds)
    {
        try
        {
            Context.Logger?.LogInfo($"Terminating match {Id}");
            
            _tickTimer?.Dispose();
            _tickTimer = null;

            if (State != null && !State.IsTerminated)
            {
                await Handler.MatchTerminate(Context, State, graceSeconds);
                State.IsTerminated = true;
            }

            // قطع ارتباط همه بازیکنان
            foreach (var presence in State?.Presences ?? new List<PlayerPresence>())
            {
                try
                {
                    presence.NetworkPeer?.Disconnect(DisconnectReason.Kicked);
                }
                catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            Context.Logger?.LogError($"Error terminating match {Id}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _tickTimer?.Dispose();
    }
}