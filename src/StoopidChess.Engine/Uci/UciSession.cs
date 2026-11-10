using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using StoopidChess.Core;

namespace StoopidChess.Engine.Uci;

/// <summary>
/// The engine side of the Universal Chess Interface (https://www.chessprogramming.org/UCI): reads
/// commands from a <see cref="TextReader"/> and writes responses to a <see cref="TextWriter"/>, so
/// it can be driven by a console, a pipe or a test. Searches run on a background task so that
/// "stop" and "isready" are answered while thinking.
///
/// Besides the standard commands (uci, isready, setoption, ucinewgame, position, go, stop, quit) it
/// accepts the customary extras: "d" prints the board, "eval" the static evaluation, "perft N" the
/// divided perft of the current position, and "bench [depth]" the fixed-depth node signature.
/// </summary>
public sealed class UciSession : IDisposable
{
    public const string EngineName = "Stoopid Chess";
    public const string EngineAuthor = "Jonny Reckless";

    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly Lock outputLock = new();
    private readonly TranspositionTable table = new();
    private readonly Searcher searcher;
    private readonly Evaluator evaluator = new();
    private Position position = Position.Start();
    private Task? searchTask;
    private CancellationTokenSource? searchCancellation;
    private bool disposed;

    public UciSession(TextReader input, TextWriter output)
    {
        this.input = input ?? throw new ArgumentNullException(nameof(input));
        this.output = output ?? throw new ArgumentNullException(nameof(output));
        searcher = new Searcher(table);
    }

    public static string EngineVersion { get; } =
        typeof(UciSession).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(UciSession).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>
    /// The current position, as set by the last "position" command.
    /// </summary>
    public Position Position => position;

    public bool IsSearching => searchTask is { IsCompleted: false };

    /// <summary>
    /// Process commands until "quit" or the end of the input.
    /// </summary>
    public void Run()
    {
        while (input.ReadLine() is string line)
        {
            if (!HandleCommand(line))
            {
                break;
            }
        }
        StopSearch();
    }

    /// <summary>
    /// Process a single command line. Returns false when the session should end.
    /// </summary>
    public bool HandleCommand(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return true;
        }
        // Every command except the asynchronous ones ("isready", "stop") first ends any running search
        switch (tokens[0])
        {
            case "uci":
                WriteLine($"id name {EngineName} {EngineVersion}");
                WriteLine($"id author {EngineAuthor}");
                WriteLine($"option name Hash type spin default {TranspositionTable.DefaultSizeInMegabytes} min {TranspositionTable.MinimumSizeInMegabytes} max {TranspositionTable.MaximumSizeInMegabytes}");
                WriteLine("uciok");
                break;
            case "isready":
                WriteLine("readyok");
                break;
            case "stop":
                StopSearch();
                break;
            case "setoption":
                SetOption(tokens);
                break;
            case "ucinewgame":
                StopSearch();
                table.Clear();
                searcher.NewGame();
                break;
            case "position":
                StopSearch();
                SetPosition(tokens);
                break;
            case "go":
                StopSearch();
                Go(ParseGo(tokens));
                break;
            case "quit":
                StopSearch();
                return false;
            case "d":
                StopSearch();
                WriteLine(position.ToString());
                WriteLine($"fen {position.ToFen()}");
                WriteLine($"key {position.Key:X16}");
                break;
            case "eval":
                StopSearch();
                WriteLine($"eval {evaluator.Evaluate(position)} cp (side to move)");
                break;
            case "perft":
                StopSearch();
                RunPerft(Math.Max(1, ParseInt(tokens.ElementAtOrDefault(1)) ?? 1));
                break;
            case "bench":
                StopSearch();
                Bench.Run(Math.Max(1, ParseInt(tokens.ElementAtOrDefault(1)) ?? Bench.DefaultDepth), output);
                break;
            default:
                // UCI says unknown commands and tokens are ignored
                break;
        }
        return true;
    }

    private void SetOption(string[] tokens)
    {
        // setoption name <name words> value <value words>
        int valueIndex = Array.IndexOf(tokens, "value");
        if (tokens.Length < 3 || tokens[1] != "name" || valueIndex < 0 || valueIndex == tokens.Length - 1)
        {
            return;
        }
        string name = string.Join(' ', tokens, 2, valueIndex - 2);
        string value = string.Join(' ', tokens, valueIndex + 1, tokens.Length - valueIndex - 1);
        if (name.Equals("Hash", StringComparison.OrdinalIgnoreCase) && ParseInt(value) is int megabytes)
        {
            StopSearch();
            table.Resize(megabytes);
        }
    }

    /// <summary>
    /// position (startpos | fen &lt;fen&gt;) [moves &lt;move&gt;...]. The position is left unchanged
    /// if the FEN is invalid; the moves are replayed up to the first illegal one.
    /// </summary>
    private void SetPosition(string[] tokens)
    {
        int movesIndex = Array.IndexOf(tokens, "moves");
        int fenEnd = movesIndex < 0 ? tokens.Length : movesIndex;
        Position start;
        if (tokens.Length > 1 && tokens[1] == "startpos")
        {
            start = Position.Start();
        }
        else if (tokens.Length > 2 && tokens[1] == "fen")
        {
            string fen = string.Join(' ', tokens, 2, fenEnd - 2);
            if (Fen.TryParse(fen) is not Position parsed)
            {
                WriteLine($"info string invalid fen '{fen}'");
                return;
            }
            if (!parsed.IsLegal)
            {
                WriteLine($"info string illegal position '{fen}'");
                return;
            }
            start = parsed;
        }
        else
        {
            return;
        }
        var game = new Game(start);
        foreach (string uci in tokens.Skip(movesIndex < 0 ? tokens.Length : movesIndex + 1))
        {
            if (!game.TryMakeMove(uci))
            {
                WriteLine($"info string illegal move '{uci}' ignored");
                break;
            }
        }
        position = game.Position;
    }

    private void Go(SearchLimits limits)
    {
        Position root = position.Clone();
        searchCancellation = new CancellationTokenSource();
        CancellationToken token = searchCancellation.Token;
        searchTask = Task.Run(() =>
        {
            try
            {
                SearchResult result = searcher.Search(root, limits, WriteProgress, token);
                WriteLine($"bestmove {result.BestMove}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The protocol requires an answer to every "go", and the session must survive
                WriteLine($"info string search failed: {ex.Message}");
                WriteLine("bestmove 0000");
            }
        });
    }

    /// <summary>
    /// Parse the arguments of a "go" command. Unknown words (ponder, searchmoves) are ignored.
    /// </summary>
    public static SearchLimits ParseGo(string[] tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var limits = new SearchLimits();
        for (int i = 1; i < tokens.Length; i++)
        {
            if (tokens[i] == "infinite")
            {
                limits = limits with { Infinite = true };
                continue;
            }
            string? argument = tokens.ElementAtOrDefault(i + 1);
            long? value = ParseLong(argument);
            SearchLimits? updated = (tokens[i], value) switch
            {
                ("depth", long depth) => limits with { Depth = (int)Math.Clamp(depth, int.MinValue, int.MaxValue) },
                ("movetime", long moveTime) => limits with { MoveTime = TimeSpan.FromMilliseconds(moveTime) },
                ("nodes", long nodes) => limits with { Nodes = nodes },
                ("wtime", long wtime) => limits with { WhiteTime = TimeSpan.FromMilliseconds(wtime) },
                ("btime", long btime) => limits with { BlackTime = TimeSpan.FromMilliseconds(btime) },
                ("winc", long winc) => limits with { WhiteIncrement = TimeSpan.FromMilliseconds(winc) },
                ("binc", long binc) => limits with { BlackIncrement = TimeSpan.FromMilliseconds(binc) },
                ("movestogo", long movesToGo) => limits with { MovesToGo = (int)Math.Clamp(movesToGo, int.MinValue, int.MaxValue) },
                _ => null,
            };
            if (updated is not null)
            {
                limits = updated;
                i++;
            }
        }
        return limits;
    }

    /// <summary>
    /// Cancel any running search and wait for its "bestmove".
    /// </summary>
    private void StopSearch()
    {
        if (searchTask is null)
        {
            return;
        }
        searchCancellation?.Cancel();
        searchTask.Wait();
        searchTask = null;
        searchCancellation?.Dispose();
        searchCancellation = null;
    }

    private void WriteProgress(SearchResult progress)
    {
        string score = progress.IsMateScore ? $"mate {progress.MateIn}" : $"cp {progress.Score}";
        string pv = string.Join(' ', progress.PrincipalVariation);
        WriteLine($"info depth {progress.Depth} score {score} nodes {progress.Nodes} nps {progress.NodesPerSecond} time {(long)progress.Elapsed.TotalMilliseconds} pv {pv}");
    }

    private void RunPerft(int depth)
    {
        var stopwatch = Stopwatch.StartNew();
        long total = 0;
        foreach ((Move move, long nodes) in Perft.Divide(position, depth))
        {
            WriteLine($"{move}: {nodes}");
            total += nodes;
        }
        stopwatch.Stop();
        WriteLine($"nodes {total} time {(long)stopwatch.Elapsed.TotalMilliseconds} nps {SearchResult.Rate(total, stopwatch.Elapsed)}");
    }

    private void WriteLine(string text)
    {
        lock (outputLock)
        {
            output.WriteLine(text);
            output.Flush();
        }
    }

    private static int? ParseInt(string? text)
    {
        return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : null;
    }

    private static long? ParseLong(string? text)
    {
        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value) ? value : null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        StopSearch();
    }
}
