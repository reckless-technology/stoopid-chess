using StoopidChess.Core;
using StoopidChess.Engine.Uci;

namespace StoopidChess.Tests.Engine;

/// <summary>
/// Drives the UCI protocol handler directly through in-memory readers and writers.
/// </summary>
public class UciSessionTests
{
    private static (UciSession Session, StringWriter Output) NewSession(string input = "")
    {
        var output = new StringWriter();
        return (new UciSession(new StringReader(input), output), output);
    }

    private static string[] Lines(StringWriter output)
    {
        return output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void IdentifiesItself()
    {
        (UciSession session, StringWriter output) = NewSession();
        using (session)
        {
            Assert.True(session.HandleCommand("uci"));
        }
        string[] lines = Lines(output);
        Assert.StartsWith("id name Stoopid Chess", lines[0], StringComparison.Ordinal);
        Assert.Equal("id author Jonny Reckless", lines[1]);
        Assert.Contains(lines, l => l.StartsWith("option name Hash type spin", StringComparison.Ordinal));
        Assert.Equal("uciok", lines[^1]);
    }

    [Fact]
    public void AnswersIsReady()
    {
        (UciSession session, StringWriter output) = NewSession();
        using (session)
        {
            session.HandleCommand("isready");
        }
        Assert.Equal(["readyok"], Lines(output));
    }

    [Fact]
    public void SetsPositionFromStartposAndMoves()
    {
        (UciSession session, _) = NewSession();
        using (session)
        {
            session.HandleCommand("position startpos moves e2e4 e7e5 g1f3");
            Assert.Equal("rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2", session.Position.ToFen());
            Assert.Equal(3, session.Position.Ply);
        }
    }

    [Fact]
    public void SetsPositionFromFen()
    {
        (UciSession session, _) = NewSession();
        using (session)
        {
            session.HandleCommand("position fen 4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2 moves e5d6 e8d8");
            Assert.Equal("3k4/8/3P4/8/8/8/8/4K3 w - - 1 3", session.Position.ToFen());
            // In this position the en passant capture is illegal: it would expose the king along the rank
            session.HandleCommand("position fen 8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1 moves e2e4 f4e3");
            Assert.Equal("8/2p5/3p4/KP5r/1R2Pp1k/8/6P1/8 b - e3 0 1", session.Position.ToFen());
        }
    }

    [Fact]
    public void IgnoresIllegalMovesAndBadFen()
    {
        (UciSession session, StringWriter output) = NewSession();
        using (session)
        {
            session.HandleCommand("position startpos moves e2e4 e2e4 e7e5");
            Assert.Equal(1, session.Position.Ply);
            session.HandleCommand("position fen not a fen at all");
            Assert.Equal(1, session.Position.Ply);
        }
        Assert.Contains(Lines(output), l => l.Contains("illegal move 'e2e4'", StringComparison.Ordinal));
        Assert.Contains(Lines(output), l => l.Contains("invalid fen", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsIllegalPositionsAndKeepsRunning()
    {
        (UciSession session, StringWriter output) = NewSession();
        using (session)
        {
            session.HandleCommand("position startpos moves e2e4");
            // Black's king is in check with white to move: the black king could be captured
            session.HandleCommand("position fen k6R/8/8/8/8/8/8/K7 w - - 0 1 moves h8a8 a1a2");
            Assert.Equal(1, session.Position.Ply);
            session.HandleCommand("perft 2");
            session.HandleCommand("eval");
            session.HandleCommand("d");
            Assert.True(session.HandleCommand("isready"));
        }
        string[] lines = Lines(output);
        Assert.Contains(lines, l => l.StartsWith("info string illegal position", StringComparison.Ordinal));
        Assert.Equal("readyok", lines[^1]);
    }

    [Fact]
    public void AcceptsAnyWhitespaceBetweenTokens()
    {
        (UciSession session, _) = NewSession();
        using (session)
        {
            session.HandleCommand("position\tstartpos \t moves\te2e4  e7e5\r");
            Assert.Equal(2, session.Position.Ply);
        }
    }

    [Fact]
    public void DegenerateGoLimitsStillProduceAMove()
    {
        (UciSession session, StringWriter output) = NewSession();
        using (session)
        {
            session.HandleCommand("position startpos");
            foreach (string go in new[] { "go wtime 0 btime 0 movestogo 0", "go depth -5", "go movetime 0", "go nodes 0", "go depth 0 nodes -1 movestogo -3" })
            {
                session.HandleCommand(go);
                session.HandleCommand("stop");
            }
            session.HandleCommand("perft 0");
            session.HandleCommand("bench 0");
        }
        string[] bestMoves = [.. Lines(output).Where(l => l.StartsWith("bestmove ", StringComparison.Ordinal))];
        Assert.Equal(5, bestMoves.Length);
        Assert.All(bestMoves, l => Assert.NotEqual("bestmove 0000", l));
        Assert.DoesNotContain(Lines(output), l => l.Contains("search failed", StringComparison.Ordinal));
        Assert.Contains(Lines(output), l => l.StartsWith("nodes 20 ", StringComparison.Ordinal));
        Assert.Contains(Lines(output), l => l.StartsWith("bench depth 1:", StringComparison.Ordinal));
    }

    [Fact]
    public void GoProducesInfoAndALegalBestMove()
    {
        (UciSession session, StringWriter output) = NewSession();
        using (session)
        {
            session.HandleCommand("position startpos moves e2e4");
            session.HandleCommand("go depth 3");
            session.HandleCommand("stop");        // Waits for the search to finish
            Assert.False(session.IsSearching);
        }
        string[] lines = Lines(output);
        Assert.Contains(lines, l => l.StartsWith("info depth 1 score cp ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("info depth 3 score cp ", StringComparison.Ordinal) && l.Contains(" pv ", StringComparison.Ordinal));
        string best = lines[^1];
        Assert.StartsWith("bestmove ", best, StringComparison.Ordinal);
        var position = Position.Start();
        position.MakeMove(BoardTestHelpers.Move(position, "e2e4"));
        Assert.Contains(position.GenerateLegalMoves(), m => m.ToUci() == best.Split(' ')[1]);
    }

    [Fact]
    public void ReportsMateScores()
    {
        (UciSession session, StringWriter output) = NewSession();
        using (session)
        {
            session.HandleCommand("position fen 6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1");
            session.HandleCommand("go depth 3");
            session.HandleCommand("quit");
        }
        string[] lines = Lines(output);
        Assert.Contains(lines, l => l.Contains(" score mate 1 ", StringComparison.Ordinal));
        Assert.Equal("bestmove d1d8", lines[^1]);
    }

    [Fact]
    public void InfiniteSearchStopsOnStop()
    {
        (UciSession session, StringWriter output) = NewSession();
        using (session)
        {
            session.HandleCommand("position startpos");
            session.HandleCommand("go infinite");
            Thread.Sleep(100);
            Assert.True(session.IsSearching);
            session.HandleCommand("stop");
            Assert.False(session.IsSearching);
        }
        Assert.StartsWith("bestmove ", Lines(output)[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void RunProcessesAScriptUntilQuit()
    {
        (UciSession session, StringWriter output) = NewSession(string.Join(Environment.NewLine, "uci", "isready", "position startpos", "go depth 2", "quit", "isready"));
        using (session)
        {
            session.Run();
        }
        string[] lines = Lines(output);
        Assert.Contains("uciok", lines);
        Assert.Single(lines, "readyok");     // The isready after quit is never read
        Assert.Contains(lines, l => l.StartsWith("bestmove ", StringComparison.Ordinal));
    }

    [Fact]
    public void ANewPositionOrOptionDuringASearchFinishesItFirst()
    {
        (UciSession session, StringWriter output) = NewSession();
        using (session)
        {
            session.HandleCommand("position startpos");
            session.HandleCommand("go infinite");
            session.HandleCommand("setoption name Hash value 2");
            Assert.False(session.IsSearching);
            session.HandleCommand("go infinite");
            session.HandleCommand("position fen 6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1");
            Assert.False(session.IsSearching);
            session.HandleCommand("go depth 2");
            session.HandleCommand("quit");
        }
        string[] lines = Lines(output);
        int[] bestMoveLines = [.. lines.Select((line, index) => (line, index)).Where(t => t.line.StartsWith("bestmove ", StringComparison.Ordinal)).Select(t => t.index)];
        Assert.Equal(3, bestMoveLines.Length);
        Assert.Equal("bestmove d1d8", lines[bestMoveLines[2]]);
        // Every bestmove precedes the info lines of the next search, so after the second bestmove
        // only the mate position's lines appear
        Assert.All(lines.Skip(bestMoveLines[1] + 1).Where(l => l.StartsWith("info depth", StringComparison.Ordinal)), l => Assert.Contains("d1d8", l, StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsHashOptionAndNewGame()
    {
        (UciSession session, _) = NewSession();
        using (session)
        {
            Assert.True(session.HandleCommand("setoption name Hash value 4"));
            Assert.True(session.HandleCommand("setoption name Nonsense value 4"));
            Assert.True(session.HandleCommand("setoption name Hash"));
            Assert.True(session.HandleCommand("ucinewgame"));
        }
    }

    [Fact]
    public void DisplaysBoardEvaluationAndPerft()
    {
        (UciSession session, StringWriter output) = NewSession();
        using (session)
        {
            session.HandleCommand("position startpos");
            session.HandleCommand("d");
            session.HandleCommand("eval");
            session.HandleCommand("perft 2");
        }
        string[] lines = Lines(output);
        Assert.Contains("fen " + Position.StartFen, lines);
        Assert.Contains(lines, l => l.StartsWith("eval ", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("nodes 400 ", StringComparison.Ordinal));
        Assert.Contains("e2e4: 20", lines);
    }

    [Fact]
    public void IgnoresUnknownCommandsAndBlankLines()
    {
        (UciSession session, StringWriter output) = NewSession();
        using (session)
        {
            Assert.True(session.HandleCommand(""));
            Assert.True(session.HandleCommand("   "));
            Assert.True(session.HandleCommand("xyzzy"));
            Assert.False(session.HandleCommand("quit"));
        }
        Assert.Empty(Lines(output));
    }
}
