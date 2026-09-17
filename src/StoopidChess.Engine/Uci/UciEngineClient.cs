using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using StoopidChess.Core;

namespace StoopidChess.Engine.Uci;

/// <summary>
/// The GUI side of the Universal Chess Interface: drives any UCI engine, either an external
/// executable (<see cref="StartProcess"/>) or the built-in engine running on a background thread in
/// this process (<see cref="StartInProcess"/>). Both are spoken to over exactly the same text
/// protocol, so the GUI has no dependency on the engine's internals.
/// </summary>
public sealed class UciEngineClient : IDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    private readonly TextReader reader;
    private readonly TextWriter writer;
    private readonly Process? process;
    private readonly Action? shutdown;
    private readonly Thread readerThread;
    private readonly Lock sync = new();
    private TaskCompletionSource<bool>? uciOk;
    private TaskCompletionSource<bool>? readyOk;
    private TaskCompletionSource<string>? bestMove;
    private bool disposed;

    private UciEngineClient(TextReader reader, TextWriter writer, Process? process, Action? shutdown)
    {
        this.reader = reader;
        this.writer = writer;
        this.process = process;
        this.shutdown = shutdown;
        readerThread = new Thread(ReadLoop) { IsBackground = true, Name = "UCI engine reader" };
        readerThread.Start();
    }

    /// <summary>
    /// The engine's name as reported by "id name", once <see cref="InitialiseAsync"/> has completed.
    /// </summary>
    public string? Name { get; private set; }

    public string? Author { get; private set; }

    /// <summary>
    /// True once the engine has closed its output: it has quit, crashed or been disposed. Commands
    /// sent after that throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public bool HasExited => Volatile.Read(ref exited);

    private bool exited;

    /// <summary>
    /// Raised on the reader thread for every "info" line the engine sends.
    /// </summary>
    public event EventHandler<string>? InfoReceived;

    /// <summary>
    /// Launch an external UCI engine executable.
    /// </summary>
    public static UciEngineClient StartProcess(string fileName)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(fileName)),
        };
        Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"could not start '{fileName}'");
        process.StandardInput.AutoFlush = true;
        return new UciEngineClient(process.StandardOutput, process.StandardInput, process, null);
    }

    /// <summary>
    /// Run the built-in engine on a background thread, connected through a pair of anonymous pipes.
    /// </summary>
    public static UciEngineClient StartInProcess()
    {
        var toEngine = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var fromEngine = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var engineInput = new AnonymousPipeClientStream(PipeDirection.In, toEngine.ClientSafePipeHandle);
        var engineOutput = new AnonymousPipeClientStream(PipeDirection.In, fromEngine.ClientSafePipeHandle);

        var engineThread = new Thread(() =>
        {
            using var session = new UciSession(new StreamReader(engineInput), new StreamWriter(fromEngine) { AutoFlush = true });
            try
            {
                session.Run();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or AggregateException)
            {
                // The pipe closed or the session failed; closing our ends tells the client the engine has gone
            }
            finally
            {
                fromEngine.Dispose();
                engineInput.Dispose();
            }
        })
        {
            IsBackground = true,
            Name = "UCI engine",
        };
        engineThread.Start();

        return new UciEngineClient(
            new StreamReader(engineOutput),
            new StreamWriter(toEngine) { AutoFlush = true },
            process: null,
            shutdown: () => engineThread.Join(ShutdownTimeout));
    }

    /// <summary>
    /// Send "uci" and wait for "uciok".
    /// </summary>
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync)
        {
            uciOk = completion;
        }
        Send("uci");
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Send "isready" and wait for "readyok".
    /// </summary>
    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync)
        {
            readyOk = completion;
        }
        Send("isready");
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void NewGame() => Send("ucinewgame");

    public void SetOption(string name, string value) => Send($"setoption name {name} value {value}");

    /// <summary>
    /// Set the position to search: a starting FEN and the moves played from it.
    /// </summary>
    public void SetPosition(string startFen, IEnumerable<string> uciMoves)
    {
        ArgumentNullException.ThrowIfNull(uciMoves);
        string position = startFen == Board.StartFen ? "startpos" : $"fen {startFen}";
        string moves = string.Join(' ', uciMoves);
        Send(moves.Length == 0 ? $"position {position}" : $"position {position} moves {moves}");
    }

    /// <summary>
    /// Start a search and return the engine's best move in UCI notation ("0000" if it has none).
    /// Cancelling the token sends "stop"; the engine then still answers with its best move so far.
    /// </summary>
    public Task<string> GoAsync(SearchLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (HasExited)
        {
            throw new InvalidOperationException("the engine has exited");
        }
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (sync)
        {
            if (bestMove is { Task.IsCompleted: false })
            {
                throw new InvalidOperationException("a search is already in progress");
            }
            bestMove = completion;
        }
        Send("go " + FormatGo(limits));
        if (cancellationToken.CanBeCanceled)
        {
            CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                try
                {
                    Stop();
                }
                catch (InvalidOperationException)
                {
                    // The engine has already gone; the pending task is failed by the reader
                }
            });
            completion.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        }
        return completion.Task;
    }

    public void Stop() => Send("stop");

    /// <summary>
    /// The argument text of a "go" command for the given limits.
    /// </summary>
    public static string FormatGo(SearchLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.Infinite)
        {
            return "infinite";
        }
        var parts = new List<string>();
        Add("depth", limits.Depth);
        Add("movetime", Milliseconds(limits.MoveTime));
        Add("nodes", limits.Nodes);
        Add("wtime", Milliseconds(limits.WhiteTime));
        Add("btime", Milliseconds(limits.BlackTime));
        Add("winc", Milliseconds(limits.WhiteIncrement));
        Add("binc", Milliseconds(limits.BlackIncrement));
        Add("movestogo", limits.MovesToGo);
        return parts.Count == 0 ? "infinite" : string.Join(' ', parts);

        void Add(string name, long? value)
        {
            if (value is not null)
            {
                parts.Add(name);
                parts.Add(value.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        static long? Milliseconds(TimeSpan? time) => time is null ? null : (long)time.Value.TotalMilliseconds;
    }

    private void Send(string command)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (HasExited)
        {
            throw new InvalidOperationException("the engine has exited");
        }
        try
        {
            lock (writer)
            {
                writer.WriteLine(command);
                writer.Flush();
            }
        }
        catch (IOException ex)
        {
            Volatile.Write(ref exited, true);
            throw new InvalidOperationException("the engine has exited", ex);
        }
    }

    private void ReadLoop()
    {
        try
        {
            while (reader.ReadLine() is string line)
            {
                Dispatch(line);
            }
        }
        catch (IOException)
        {
            // The engine has gone away; fall through and fail anything still waiting
        }
        catch (ObjectDisposedException)
        {
        }
        Volatile.Write(ref exited, true);
        var gone = new InvalidOperationException("the engine has exited");
        lock (sync)
        {
            uciOk?.TrySetException(gone);
            readyOk?.TrySetException(gone);
            bestMove?.TrySetException(gone);
        }
    }

    private void Dispatch(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return;
        }
        switch (tokens[0])
        {
            case "uciok":
                lock (sync)
                {
                    uciOk?.TrySetResult(true);
                }
                break;
            case "readyok":
                lock (sync)
                {
                    readyOk?.TrySetResult(true);
                }
                break;
            case "bestmove":
                lock (sync)
                {
                    bestMove?.TrySetResult(tokens.Length > 1 ? tokens[1] : "0000");
                }
                break;
            case "id" when tokens.Length > 2 && tokens[1] == "name":
                Name = string.Join(' ', tokens, 2, tokens.Length - 2);
                break;
            case "id" when tokens.Length > 2 && tokens[1] == "author":
                Author = string.Join(' ', tokens, 2, tokens.Length - 2);
                break;
            case "info":
                InfoReceived?.Invoke(this, line);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Ask the engine to quit and release the connection. An external engine that does not exit
    /// promptly is killed.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        try
        {
            Send("quit");
        }
        catch (InvalidOperationException)
        {
            // Already gone
        }
        disposed = true;
        if (process is not null)
        {
            if (!process.WaitForExit((int)ShutdownTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }
            process.Dispose();
        }
        shutdown?.Invoke();
        readerThread.Join(ShutdownTimeout);
        writer.Dispose();
        reader.Dispose();
    }
}
