using DotNetNakamaServer;

class Program
{
    static async Task Main(string[] args)
    {
        // راه‌اندازی transport و match engine
        var transport = NetworkTransportFactory.Create("litenetlib");
        var logger = new ConsoleLogger();
        var matchEngine = new MatchEngine(transport, logger);

        // ثبت handlers
        matchEngine.RegisterHandler("tictactoe", new TicTacToeHandler());
        matchEngine.RegisterHandler("chess", new ChessHandler());

        // راه‌اندازی matchmaking
        matchEngine.SetupMatchmaking();

        // شروع سرور
        var config = new NetworkConfig { Port = 9050 };
        await matchEngine.StartAsync(config);

        Console.WriteLine("🚀 Game Server with Matchmaking started!");
        Console.WriteLine("📊 Commands: 's' = stats, 'q' = quit");

        // حلقه مدیریت
        while (true)
        {
            var key = Console.ReadKey(true);

            if (key.KeyChar == 'q')
                break;

            if (key.KeyChar == 's')
            {
                ShowStats(matchEngine);
            }
        }

        await matchEngine.StopAsync();
        Console.WriteLine("Server stopped.");
    }

    static void ShowStats(MatchEngine engine)
    {
        var stats = engine.GetMatchmakingStats();
        var matches = engine.GetMatchList();

        Console.WriteLine("\n" + "=".PadRight(50, '='));
        Console.WriteLine("📊 MATCHMAKING STATS");
        Console.WriteLine("=".PadRight(50, '='));
        Console.WriteLine($"Players in Queue: {stats.TotalPlayersInQueue}");
        Console.WriteLine($"Total Matches: {stats.TotalMatches}");
        Console.WriteLine($"Successful: {stats.SuccessfulMatches}");
        Console.WriteLine($"Failed: {stats.FailedMatches}");
        Console.WriteLine($"Average Wait Time: {stats.AverageWaitTime:F1}s");
        
        Console.WriteLine("\n📋 QUEUES BY GAME MODE:");
        foreach (var queue in stats.QueuesByGameMode)
        {
            Console.WriteLine($"  {queue.Key}: {queue.Value} players");
        }

        Console.WriteLine("\n🎮 ACTIVE MATCHES:");
        foreach (var match in matches)
        {
            Console.WriteLine($"  {match.MatchId}: {match.HandlerName} ({match.PlayerCount} players)");
        }
        Console.WriteLine("=".PadRight(50, '=') + "\n");
    }
}