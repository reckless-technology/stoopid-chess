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
    private Board board = Board.StartPosition();
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
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>
    /// The current position, as set by the last "position" command.
    /// </summary>
    public Board Board => board;

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
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return true;
        }
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
            case "setoption":
                SetOption(tokens);
                break;
            case "ucinewgame":
                StopSearch();
                table.Clear();
                break;
            case "position":
                StopSearch();
                SetPosition(tokens);
                break;
            case "go":
                Go(tokens);
                break;
            case "stop":
                StopSearch();
                break;
            case "quit":
                StopSearch();
                return false;
            case "d":
                StopSearch();
                WriteLine(board.ToString());
                WriteLine($"fen {board.ToFen()}");
                WriteLine($"key {board.Key:X16}");
                break;
            case "eval":
                StopSearch();
                WriteLine($"eval {new Evaluator().Evaluate(board)} cp (side to move)");
                break;
            case "perft":
                StopSearch();
                RunPerft(tokens.Length > 1 ? ParseInt(tokens[1]) ?? 1 : 1);
                break;
            case "bench":
                StopSearch();
                Bench.Run(tokens.Length > 1 ? ParseInt(tokens[1]) ?? Bench.DefaultDepth : Bench.DefaultDepth, output);
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

    private void SetPosition(string[] tokens)
    {
        int movesIndex = Array.IndexOf(tokens, "moves");
        int fenEnd = movesIndex < 0 ? tokens.Length : movesIndex;
        Board newBoard;
        if (tokens.Length > 1 && tokens[1] == "startpos")
        {
            newBoard = Board.StartPosition();
        }
        else if (tokens.Length > 2 && tokens[1] == "fen")
        {
            string fen = string.Join(' ', tokens, 2, fenEnd - 2);
            if (!Fen.TryParse(fen, out newBoard))
            {
                WriteLine($"info string invalid fen '{fen}'");
                return;
            }
        }
        else
        {
            return;
        }
        if (movesIndex >= 0)
        {
            for (int i = movesIndex + 1; i < tokens.Length; i++)
            {
                Move? move = FindLegalMove(newBoard, tokens[i]);
                if (move is null)
                {
                    WriteLine($"info string illegal move '{tokens[i]}' ignored");
                    break;
                }
                newBoard.MakeMove(move.Value);
            }
        }
        board = newBoard;
    }

    private static Move? FindLegalMove(Board board, string uci)
    {
        if (!MoveNotation.TryParseUci(uci, out int from, out int to, out PieceType promotion))
        {
            return null;
        }
        foreach (Move move in MoveGenerator.GenerateLegalMoves(board))
        {
            if (move.From == from && move.To == to && move.Promotion == promotion)
            {
                return move;
            }
        }
        return null;
    }

    private void Go(string[] tokens)
    {
        StopSearch();
        SearchLimits limits = ParseGo(tokens);
        Board position = board.Clone();
        searchCancellation = new CancellationTokenSource();
        CancellationToken token = searchCancellation.Token;
        searchTask = Task.Run(() =>
        {
            try
            {
                SearchResult result = searcher.Search(position, limits, WriteProgress, token);
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
    /// Parse the arguments of a "go" command.
    /// </summary>
    public static SearchLimits ParseGo(string[] tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var limits = new SearchLimits();
        for (int i = 1; i < tokens.Length; i++)
        {
            string? next = i + 1 < tokens.Length ? tokens[i + 1] : null;
            switch (tokens[i])
            {
                case "infinite":
                    limits = limits with { Infinite = true };
                    break;
                case "depth" when ParseInt(next) is int depth:
                    limits = limits with { Depth = depth };
                    i++;
                    break;
                case "movetime" when ParseInt(next) is int moveTime:
                    limits = limits with { MoveTime = TimeSpan.FromMilliseconds(moveTime) };
                    i++;
                    break;
                case "nodes" when ParseLong(next) is long nodes:
                    limits = limits with { Nodes = nodes };
                    i++;
                    break;
                case "wtime" when ParseInt(next) is int wtime:
                    limits = limits with { WhiteTime = TimeSpan.FromMilliseconds(wtime) };
                    i++;
                    break;
                case "btime" when ParseInt(next) is int btime:
                    limits = limits with { BlackTime = TimeSpan.FromMilliseconds(btime) };
                    i++;
                    break;
                case "winc" when ParseInt(next) is int winc:
                    limits = limits with { WhiteIncrement = TimeSpan.FromMilliseconds(winc) };
                    i++;
                    break;
                case "binc" when ParseInt(next) is int binc:
                    limits = limits with { BlackIncrement = TimeSpan.FromMilliseconds(binc) };
                    i++;
                    break;
                case "movestogo" when ParseInt(next) is int movesToGo:
                    limits = limits with { MovesToGo = movesToGo };
                    i++;
                    break;
                default:
                    break;  // ponder, searchmoves and anything unknown are ignored
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

    private void WriteProgress(SearchProgress progress)
    {
        string score = progress.IsMateScore ? $"mate {progress.MateIn}" : $"cp {progress.Score}";
        string pv = string.Join(' ', progress.PrincipalVariation);
        WriteLine($"info depth {progress.Depth} score {score} nodes {progress.Nodes} nps {progress.NodesPerSecond} time {(long)progress.Elapsed.TotalMilliseconds} pv {pv}");
    }

    private void RunPerft(int depth)
    {
        var stopwatch = Stopwatch.StartNew();
        long total = 0;
        foreach ((Move move, long nodes) in Perft.Divide(board, depth))
        {
            WriteLine($"{move}: {nodes}");
            total += nodes;
        }
        stopwatch.Stop();
        long nps = stopwatch.Elapsed.TotalSeconds > 0 ? (long)(total / stopwatch.Elapsed.TotalSeconds) : 0;
        WriteLine($"nodes {total} time {(long)stopwatch.Elapsed.TotalMilliseconds} nps {nps}");
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
