using System.Diagnostics;
using System.Globalization;
using StoopidChess.Core;

namespace StoopidChess.Engine;

/// <summary>
/// A fixed-depth search over a fixed set of positions. Because the search is deterministic, the total
/// node count is a signature of the search and evaluation: any change to either changes it, which the
/// tests check for. The nodes-per-second figure is a speed benchmark.
/// </summary>
public static class Bench
{
    public const int DefaultDepth = 6;
    public const int TableSizeInMegabytes = 16;

    public static readonly IReadOnlyList<string> Positions =
    [
        Board.StartFen,
        "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
        "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1",
        "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1",
        "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8",
        "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10",
        "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3",
        "r2q1rk1/ppp2ppp/2n1bn2/2b1p3/3pP3/1BP1B1N1/PP3PPP/RN1QR1K1 w - - 0 11",
        "8/8/1p1r1k2/p1pPN1p1/P3P1P1/1P1R1K2/8/8 b - - 0 1",
        "6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1",
        "2r3k1/pp2Rppp/8/3q4/8/1PN5/P4PPP/3Q2K1 w - - 0 1",
        "8/k7/3p4/p2P1p2/P2P1P2/8/8/K7 w - - 0 1",
    ];

    /// <summary>
    /// Search every position to the given depth and return the total node count and the time taken.
    /// Progress and a summary are written to <paramref name="log"/> if given.
    /// </summary>
    public static (long Nodes, TimeSpan Elapsed) Run(int depth = DefaultDepth, TextWriter? log = null)
    {
        var searcher = new Searcher(new TranspositionTable(TableSizeInMegabytes));
        var stopwatch = Stopwatch.StartNew();
        long totalNodes = 0;
        for (int i = 0; i < Positions.Count; i++)
        {
            searcher.Table.Clear();
            SearchResult result = searcher.Search(Board.FromFen(Positions[i]), SearchLimits.ForDepth(depth));
            totalNodes += result.Nodes;
            log?.WriteLine(string.Format(CultureInfo.InvariantCulture, "position {0,2}: bestmove {1,-5} score {2,6} nodes {3,10}", i + 1, result.BestMove, result.Score, result.Nodes));
        }
        stopwatch.Stop();
        long nps = stopwatch.Elapsed.TotalSeconds > 0 ? (long)(totalNodes / stopwatch.Elapsed.TotalSeconds) : 0;
        log?.WriteLine(string.Format(CultureInfo.InvariantCulture, "bench depth {0}: {1} nodes {2} nps {3} ms", depth, totalNodes, nps, (long)stopwatch.Elapsed.TotalMilliseconds));
        return (totalNodes, stopwatch.Elapsed);
    }
}
