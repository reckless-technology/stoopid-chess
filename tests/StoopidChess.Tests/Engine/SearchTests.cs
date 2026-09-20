using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

public class SearchTests
{
    private static Searcher NewSearcher() => new(new TranspositionTable(8));

    [Theory]
    [InlineData("6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1", "d1d8")]                     // Back rank mate
    [InlineData("r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4", "h5f7")]  // Scholar's mate
    [InlineData("6k1/4Rppp/8/8/8/8/5PPP/6K1 w - - 0 1", "e7e8")]                      // Rook to the back rank
    [InlineData("k7/8/1K6/8/8/8/8/7R w - - 0 1", "h1h8")]                              // Rook and king mate
    public void FindsMateInOne(string fen, string mate)
    {
        SearchResult result = NewSearcher().Search(Position.FromFen(fen), SearchLimits.ForDepth(3));
        Assert.Equal(mate, result.BestMove.ToUci());
        Assert.True(result.IsMateScore);
        Assert.Equal(1, result.MateIn);
    }

    [Fact]
    public void FindsMateInTwo()
    {
        // Nf6+ gxf6 Bxf7#
        SearchResult result = NewSearcher().Search(Position.FromFen("r2qkb1r/pp2nppp/3p4/2pNN1B1/2BnP3/3P4/PPP2PPP/R2bK2R w KQkq - 1 1"), SearchLimits.ForDepth(4));
        Assert.True(result.IsMateScore);
        Assert.Equal(2, result.MateIn);
        Assert.Equal("d5f6", result.BestMove.ToUci());
    }

    [Fact]
    public void ReportsBeingMated()
    {
        // Black to move has only Kb8, after which Rg8 is mate
        SearchResult result = NewSearcher().Search(Position.FromFen("k7/8/1K6/8/8/8/8/6R1 b - - 0 1"), SearchLimits.ForDepth(4));
        Assert.True(result.IsMateScore);
        Assert.Equal(-1, result.MateIn);
        Assert.Equal("a8b8", result.BestMove.ToUci());
    }

    [Fact]
    public void ReturnsNoMoveWhenGameIsOver()
    {
        SearchResult mated = NewSearcher().Search(Position.FromFen("7k/6Q1/6K1/8/8/8/8/8 b - - 0 1"), SearchLimits.ForDepth(3));
        Assert.True(mated.BestMove.IsNone);
        Assert.Equal(-Searcher.MateScore, mated.Score);
        SearchResult stalemated = NewSearcher().Search(Position.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1"), SearchLimits.ForDepth(3));
        Assert.True(stalemated.BestMove.IsNone);
        Assert.Equal(Searcher.DrawScore, stalemated.Score);
    }

    [Fact]
    public void AvoidsStalematingWhenWinning()
    {
        // White has a queen against a bare king; Qf7 would be stalemate
        SearchResult result = NewSearcher().Search(Position.FromFen("7k/8/6K1/8/8/8/8/5Q2 w - - 0 1"), SearchLimits.ForDepth(4));
        Assert.NotEqual("f1f7", result.BestMove.ToUci());
        Assert.True(result.Score > 0);
    }

    [Fact]
    public void PrefersADrawByRepetitionWhenLosing()
    {
        // White has a rook against a queen. The kings have shuffled a1-b1-a1 and a8-b8-a8 once, so
        // Kb1 now recreates an earlier position, which the search treats as a draw: better than the
        // material deficit of every other move.
        var position = Position.FromFen("k7/8/8/8/8/8/6q1/K6R w - - 0 1");
        foreach (string uci in new[] { "a1b1", "a8b8", "b1a1", "b8a8" })
        {
            position.MakeMove(BoardTestHelpers.Move(position, uci));
        }
        Assert.Equal(1, position.RepetitionCount());
        SearchResult result = NewSearcher().Search(position, SearchLimits.ForDepth(3));
        Assert.Equal("a1b1", result.BestMove.ToUci());
        Assert.Equal(Searcher.DrawScore, result.Score);
    }

    [Fact]
    public void InsufficientMaterialIsScoredAsADraw()
    {
        // A lone knight cannot mate; add a pawn and the position is worth something again
        Assert.Equal(Searcher.DrawScore, NewSearcher().Search(Position.FromFen("7k/8/8/8/8/8/8/K6N w - - 0 1"), SearchLimits.ForDepth(3)).Score);
        Assert.True(NewSearcher().Search(Position.FromFen("7k/8/8/8/8/8/P7/K6N w - - 0 1"), SearchLimits.ForDepth(3)).Score > 0);
    }

    [Fact]
    public void FiftyMoveRuleDoesNotHideAMate()
    {
        // The hundredth half-move is checkmate, which takes precedence over the fifty-move rule
        SearchResult result = NewSearcher().Search(Position.FromFen("6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 99 1"), SearchLimits.ForDepth(3));
        Assert.Equal("d1d8", result.BestMove.ToUci());
        Assert.True(result.IsMateScore);
        // Without the mate (and without pawn moves to reset the clock) every line is a draw
        SearchResult drawn = NewSearcher().Search(Position.FromFen("6k1/8/8/8/8/8/8/R5K1 w - - 99 1"), SearchLimits.ForDepth(3));
        Assert.Equal(Searcher.DrawScore, drawn.Score);
    }

    [Fact]
    public void TableScoresAreRelativeToTheNode()
    {
        // A mate in 5 plies found at ply 3 is a mate in 2 plies from that node; read back at ply 7 it is a mate in 9
        int score = Searcher.MateScore - 5;
        int stored = Searcher.ScoreToTable(score, 3);
        Assert.Equal(Searcher.MateScore - 2, stored);
        Assert.Equal(score, Searcher.ScoreFromTable(stored, 3));
        Assert.Equal(Searcher.MateScore - 9, Searcher.ScoreFromTable(stored, 7));
        Assert.Equal(-(Searcher.MateScore - 2), Searcher.ScoreToTable(-(Searcher.MateScore - 5), 3));
        Assert.Equal(123, Searcher.ScoreFromTable(Searcher.ScoreToTable(123, 9), 4));
    }

    [Fact]
    public void AStoppedFirstIterationStillReturnsTheBestCompletedRootMove()
    {
        // With a node budget too small to finish depth 1, the mating move is found early enough
        // (captures and checks are ordered first) that it must still be the answer
        SearchResult result = NewSearcher().Search(Position.FromFen("6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1"), new SearchLimits { Nodes = 12 });
        Assert.False(result.BestMove.IsNone);
        Assert.Equal(0, result.Depth);
    }

    [Fact]
    public void SearchIsDeterministic()
    {
        var position = Position.FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
        SearchResult first = NewSearcher().Search(position, SearchLimits.ForDepth(4));
        SearchResult second = NewSearcher().Search(position, SearchLimits.ForDepth(4));
        Assert.Equal(first.BestMove, second.BestMove);
        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Nodes, second.Nodes);
        Assert.Equal(first.PrincipalVariation, second.PrincipalVariation);
    }

    [Fact]
    public void SearchDependsOnlyOnThePositionNotOnHowItWasReached()
    {
        var viaOneOrder = Position.Start();
        foreach (string uci in new[] { "e2e4", "e7e6", "d2d4", "d7d5", "b1c3", "f8b4", "e4e5", "c7c5" })
        {
            viaOneOrder.MakeMove(BoardTestHelpers.Move(viaOneOrder, uci));
        }
        var viaOtherOrder = Position.Start();
        foreach (string uci in new[] { "d2d4", "e7e6", "b1c3", "d7d5", "e2e4", "f8b4", "e4e5", "c7c5" })
        {
            viaOtherOrder.MakeMove(BoardTestHelpers.Move(viaOtherOrder, uci));
        }
        SearchResult first = NewSearcher().Search(viaOneOrder, SearchLimits.ForDepth(5));
        SearchResult second = NewSearcher().Search(viaOtherOrder, SearchLimits.ForDepth(5));
        Assert.Equal(first.Nodes, second.Nodes);
        Assert.Equal(first.BestMove, second.BestMove);
        Assert.Equal(first.Score, second.Score);
    }

    [Fact]
    public void AStoppedFirstIterationReportsAConsistentPrincipalVariation()
    {
        for (int nodes = 1; nodes <= 40; nodes += 13)
        {
            SearchResult result = NewSearcher().Search(Position.FromFen("r1bqkbnr/pppp1ppp/2n5/4p3/3PP3/5N2/PPP2PPP/RNBQKB1R b KQkq d3 0 3"), new SearchLimits { Nodes = nodes });
            Assert.Equal(result.BestMove, result.PrincipalVariation[0]);
        }
    }

    [Fact]
    public void LeavesTheCallersBoardUntouched()
    {
        var position = Position.Start();
        position.MakeMove(BoardTestHelpers.Move(position, "e2e4"));
        string fen = position.ToFen();
        NewSearcher().Search(position, SearchLimits.ForDepth(3));
        Assert.Equal(fen, position.ToFen());
        Assert.Equal(1, position.Ply);
    }

    [Fact]
    public void PrincipalVariationIsLegal()
    {
        var position = Position.FromFen("r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3");
        SearchResult result = NewSearcher().Search(position, SearchLimits.ForDepth(4));
        Assert.Equal(result.BestMove, result.PrincipalVariation[0]);
        Position replay = position.Clone();
        foreach (Move move in result.PrincipalVariation)
        {
            Assert.Contains(move, replay.GenerateLegalMoves());
            replay.MakeMove(move);
        }
    }

    [Fact]
    public void HonoursDepthLimitAndReportsProgress()
    {
        var depths = new List<int>();
        SearchResult result = NewSearcher().Search(Position.Start(), SearchLimits.ForDepth(3), onProgress: p => depths.Add(p.Depth));
        Assert.Equal([1, 2, 3], depths);
        Assert.Equal(3, result.Depth);
        Assert.True(result.Nodes > 0);
    }

    [Fact]
    public void HonoursNodeLimit()
    {
        SearchResult result = NewSearcher().Search(Position.Start(), new SearchLimits { Nodes = 5000, Depth = 20 });
        Assert.InRange(result.Nodes, 1, 5000 + 1);
        Assert.False(result.BestMove.IsNone);
        Assert.True(result.Depth < 20);
    }

    [Fact]
    public void HonoursMoveTime()
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        SearchResult result = NewSearcher().Search(Position.Start(), SearchLimits.ForMoveTime(TimeSpan.FromMilliseconds(150)));
        started.Stop();
        Assert.False(result.BestMove.IsNone);
        Assert.InRange(started.ElapsedMilliseconds, 0, 1500);
    }

    [Fact]
    public async Task CanBeCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        Searcher searcher = NewSearcher();
        Task<SearchResult> search = Task.Run(() => searcher.Search(Position.Start(), new SearchLimits { Infinite = true }, cancellationToken: cancellation.Token));
        await Task.Delay(100);
        Assert.False(search.IsCompleted);
        cancellation.Cancel();
        SearchResult result = await search.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(result.BestMove.IsNone);
    }

    [Fact]
    public void FindsMateInTwoAtDepthThree()
    {
        // Nf6+ gxf6 Bxf7#: three plies, with the mated side's lack of moves seen by the check extension
        SearchResult result = NewSearcher().Search(Position.FromFen("r2qkb1r/pp2nppp/3p4/2pNN1B1/2BnP3/3P4/PPP2PPP/R2bK2R w KQkq - 1 1"), SearchLimits.ForDepth(3));
        Assert.True(result.IsMateScore, $"score was {result.Score}");
        Assert.Equal(2, result.MateIn);
        Assert.Equal("d5f6", result.BestMove.ToUci());
    }

    [Fact]
    public void EitherBackRankMateIsFound()
    {
        SearchResult result = NewSearcher().Search(Position.FromFen("6k1/5ppp/8/8/8/8/5PPP/3RR1K1 w - - 0 1"), SearchLimits.ForDepth(2));
        Assert.True(result.IsMateScore, $"score was {result.Score}");
        Assert.Contains(result.BestMove.ToUci(), new[] { "d1d8", "e1e8" });
    }

    [Theory]
    [InlineData(Searcher.MateScore - 1, 1)]
    [InlineData(Searcher.MateScore - 2, 1)]
    [InlineData(Searcher.MateScore - 3, 2)]
    [InlineData(-(Searcher.MateScore - 2), -1)]
    [InlineData(-(Searcher.MateScore - 4), -2)]
    public void ConvertsMateScoresToMoves(int score, int mateIn)
    {
        Assert.True(Searcher.IsMateScore(score));
        Assert.Equal(mateIn, Searcher.MateIn(score));
        Assert.False(Searcher.IsMateScore(100));
    }
}

internal static class BoardTestHelpers
{
    public static Move Move(Position position, string uci)
    {
        return position.GenerateLegalMoves().Single(m => m.ToUci() == uci);
    }
}
