using System.Diagnostics;
using System.Globalization;
using StoopidChess.Core;
using StoopidChess.Engine;
using StoopidChess.Engine.Uci;

// The Stoopid Chess engine as a UCI console program.
//
//   stoopid-chess              run the UCI protocol on stdin/stdout (for a chess GUI)
//   stoopid-chess bench [d]    fixed-depth search signature and speed
//   stoopid-chess perft [n]    move generation check against the standard positions
//                              (depths with at most n million nodes; default 10)

return args switch
{
    [] => RunUci(),
    ["bench"] => RunBench(Bench.DefaultDepth),
    ["bench", string depth] when ParseArgument(depth) is int d => RunBench(d),
    ["perft"] => RunPerft(10),
    ["perft", string budget] when ParseArgument(budget) is int n => RunPerft(n),
    _ => PrintUsage(),
};

static int RunUci()
{
    using var session = new UciSession(Console.In, Console.Out);
    session.Run();
    return 0;
}

static int RunBench(int depth)
{
    Console.WriteLine($"{UciSession.EngineName} {UciSession.EngineVersion} bench");
    Bench.Run(depth, Console.Out);
    return 0;
}

static int RunPerft(int maxMillionNodes)
{
    long budget = maxMillionNodes * 1_000_000L;
    var stopwatch = Stopwatch.StartNew();
    long totalNodes = 0;
    int failures = 0;
    foreach (PerftPosition entry in Perft.StandardPositions)
    {
        var position = Position.FromFen(entry.Fen);
        foreach ((int depth, long expected) in entry.Depths)
        {
            if (expected > budget)
            {
                break;
            }
            long actual = Perft.Count(position, depth);
            totalNodes += actual;
            if (actual != expected)
            {
                failures++;
                Console.WriteLine($"FAIL {entry.Fen} depth {depth}: expected {expected} got {actual}");
            }
        }
    }
    stopwatch.Stop();
    Console.WriteLine($"perft: {Perft.StandardPositions.Count} positions, {totalNodes} nodes, {SearchResult.Rate(totalNodes, stopwatch.Elapsed)} nps, {(long)stopwatch.Elapsed.TotalMilliseconds} ms");
    Console.WriteLine(failures == 0 ? "perft PASS" : $"perft FAIL ({failures} mismatches)");
    return failures == 0 ? 0 : 1;
}

static int PrintUsage()
{
    Console.Error.WriteLine("usage: stoopid-chess [bench [depth] | perft [max million nodes]]");
    Console.Error.WriteLine("       depth and max million nodes must be positive whole numbers");
    return 2;
}

/// <summary>
/// A positive whole number, or null when the text is not one.
/// </summary>
static int? ParseArgument(string text)
{
    return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0 ? value : null;
}
