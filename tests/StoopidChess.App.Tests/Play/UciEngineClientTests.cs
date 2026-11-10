using StoopidChess.Core;
using StoopidChess.Engine;
using StoopidChess.Engine.Uci;
using StoopidChess.Play;

namespace StoopidChess.Tests.Play;

/// <summary>
/// Client and engine talking UCI to each other over pipes, exactly as the GUI uses them.
/// </summary>
public class UciEngineClientTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task InitialisesAndReportsName()
    {
        using var client = UciEngineClient.StartInProcess();
        await client.InitialiseAsync(new CancellationTokenSource(s_timeout).Token);
        Assert.StartsWith("Stoopid Chess", client.Name, StringComparison.Ordinal);
        Assert.Equal("Jonny Reckless", client.Author);
        await client.WaitUntilReadyAsync(new CancellationTokenSource(s_timeout).Token);
    }

    [Fact]
    public async Task PlaysAMoveFromAPosition()
    {
        using var client = UciEngineClient.StartInProcess();
        await client.InitialiseAsync(new CancellationTokenSource(s_timeout).Token);
        var infos = new List<string>();
        client.InfoReceived += (_, line) =>
        {
            lock (infos)
            {
                infos.Add(line);
            }
        };

        var game = new Game();
        game.TryMakeMove("e2e4");
        client.NewGame();
        client.SetPosition(game.StartFen, game.UciMoves);
        string best = await client.GoAsync(SearchLimits.ForDepth(3)).WaitAsync(s_timeout);

        Assert.True(game.TryMakeMove(best), $"engine played illegal move {best}");
        lock (infos)
        {
            Assert.Contains(infos, l => l.StartsWith("info depth 3 ", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task FindsMateFromAFenPosition()
    {
        using var client = UciEngineClient.StartInProcess();
        await client.InitialiseAsync(new CancellationTokenSource(s_timeout).Token);
        var game = Game.FromFen("6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1");
        client.SetPosition(game.StartFen, game.UciMoves);
        string best = await client.GoAsync(SearchLimits.ForDepth(3)).WaitAsync(s_timeout);
        Assert.Equal("d1d8", best);
    }

    [Fact]
    public async Task CancellingAGoStopsTheSearch()
    {
        using var client = UciEngineClient.StartInProcess();
        await client.InitialiseAsync(new CancellationTokenSource(s_timeout).Token);
        client.SetPosition(Position.StartFen, []);
        using var cancellation = new CancellationTokenSource();
        Task<string> search = client.GoAsync(new SearchLimits { Infinite = true }, cancellation.Token);
        await Task.Delay(100);
        Assert.False(search.IsCompleted);
        cancellation.Cancel();
        string best = await search.WaitAsync(s_timeout);
        Assert.NotEqual("0000", best);
    }

    [Fact]
    public async Task RefusesOverlappingSearches()
    {
        using var client = UciEngineClient.StartInProcess();
        await client.InitialiseAsync(new CancellationTokenSource(s_timeout).Token);
        client.SetPosition(Position.StartFen, []);
        Task<string> search = client.GoAsync(new SearchLimits { Infinite = true });
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = client.GoAsync(SearchLimits.ForDepth(1));
        });
        client.Stop();
        await search.WaitAsync(s_timeout);
    }

    [Fact]
    public async Task DisposeShutsTheEngineDown()
    {
        var client = UciEngineClient.StartInProcess();
        await client.InitialiseAsync(new CancellationTokenSource(s_timeout).Token);
        Assert.False(client.HasExited);
        client.Dispose();
        client.Dispose(); // Idempotent
        Assert.True(client.HasExited);
        Assert.Throws<ObjectDisposedException>(() => client.NewGame());
    }

    [Fact]
    public async Task AFaultyInfoHandlerDoesNotBreakTheClient()
    {
        using var client = UciEngineClient.StartInProcess();
        await client.InitialiseAsync(new CancellationTokenSource(s_timeout).Token);
        client.InfoReceived += (_, _) => throw new FormatException("handler bug");
        client.SetPosition(Position.StartFen, []);
        string best = await client.GoAsync(SearchLimits.ForDepth(2)).WaitAsync(s_timeout);
        Assert.NotEqual("0000", best);
        Assert.False(client.HasExited);
    }

    [Fact]
    public void SetPositionUsesStartposForTheStandardStart()
    {
        // Format check through the session parser: startpos and fen forms both reproduce the position
        using var session = new UciSession(new StringReader(""), new StringWriter());
        session.HandleCommand("position startpos moves d2d4 d7d5");
        string viaStartpos = session.Position.ToFen();
        session.HandleCommand($"position fen {Position.StartFen} moves d2d4 d7d5");
        Assert.Equal(viaStartpos, session.Position.ToFen());
    }

    [Fact]
    public void GoCommandRoundTripsThroughFormatAndParse()
    {
        var limits = new SearchLimits
        {
            Depth = 7,
            MoveTime = TimeSpan.FromMilliseconds(500),
            Nodes = 123456,
            WhiteTime = TimeSpan.FromMilliseconds(60000),
            BlackTime = TimeSpan.FromMilliseconds(59000),
            WhiteIncrement = TimeSpan.FromMilliseconds(1000),
            BlackIncrement = TimeSpan.FromMilliseconds(2000),
            MovesToGo = 40,
        };
        string text = UciEngineClient.FormatGo(limits);
        SearchLimits parsed = UciSession.ParseGo(("go " + text).Split(' '));
        Assert.Equal(limits, parsed);
        Assert.Equal("infinite", UciEngineClient.FormatGo(new SearchLimits { Infinite = true }));
        Assert.Equal("infinite", UciEngineClient.FormatGo(new SearchLimits()));
        Assert.True(UciSession.ParseGo(["go", "infinite", "ponder"]).Infinite);
        Assert.Equal(new SearchLimits(), UciSession.ParseGo(["go", "depth", "x"]));
    }
}
