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
    ["bench", ..] => RunBench(args.Length > 1 ? ParseArgument(args[1]) : Bench.DefaultDepth),
    ["perft", ..] => RunPerft(args.Length > 1 ? ParseArgument(args[1]) : 10),
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
    foreach (PerftPosition position in Perft.StandardPositions)
    {
        Board board = Board.FromFen(position.Fen);
        foreach ((int depth, long expected) in position.Depths)
        {
            if (expected > budget)
            {
                break;
            }
            long actual = Perft.Count(board, depth);
            totalNodes += actual;
            if (actual != expected)
            {
                failures++;
                Console.WriteLine($"FAIL {position.Fen} depth {depth}: expected {expected} got {actual}");
            }
        }
    }
    stopwatch.Stop();
    long nps = stopwatch.Elapsed.TotalSeconds > 0 ? (long)(totalNodes / stopwatch.Elapsed.TotalSeconds) : 0;
    Console.WriteLine($"perft: {Perft.StandardPositions.Count} positions, {totalNodes} nodes, {nps} nps, {(long)stopwatch.Elapsed.TotalMilliseconds} ms");
    Console.WriteLine(failures == 0 ? "perft PASS" : $"perft FAIL ({failures} mismatches)");
    return failures == 0 ? 0 : 1;
}

static int PrintUsage()
{
    Console.Error.WriteLine("usage: stoopid-chess [bench [depth] | perft [max million nodes]]");
    return 2;
}

static int ParseArgument(string text)
{
    return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0
        ? value
        : throw new ArgumentException($"'{text}' is not a positive number");
}
