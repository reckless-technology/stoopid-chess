using System.Collections.Concurrent;
using System.Text;
using StoopidChess.Engine.Uci;

namespace StoopidChess.Tests.Play;

/// <summary>
/// A fake UCI engine that answers inside the call that sends it a command: the client's reader thread has
/// dispatched every line of the reply before the client's write returns, so a reply is already in when the
/// caller reaches its await - the fastest an engine can possibly be, and a race a real engine wins only now
/// and then. It names itself and answers "uci" and "isready"; every other command goes to the script, whose
/// lines are the reply. Lines can also be sent later, unasked, with <see cref="Send"/>.
/// </summary>
internal sealed class InstantEngine
{
    private readonly BlockingCollection<string> lines = [];
    private readonly object sync = new();     // A monitor, not a Lock: the waits below need Monitor.Wait
    private int sent;           // Lines put on the wire
    private int dispatched;     // Lines the client's reader thread has finished handling

    private InstantEngine(string name, Func<string, IEnumerable<string>> script)
    {
        Client = UciEngineClient.Connect(new Reader(this), new Writer(this, name, script));
    }

    public UciEngineClient Client { get; }

    public static InstantEngine Start(string name, Func<string, IEnumerable<string>> script) => new(name, script);

    /// <summary>
    /// Send lines to the client and return once its reader thread has handled them.
    /// </summary>
    public void Send(params IEnumerable<string> reply)
    {
        int target;
        lock (sync)
        {
            foreach (string line in reply)
            {
                lines.Add(line);
                sent++;
            }
            target = sent;
        }
        lock (sync)
        {
            while (dispatched < target)
            {
                Assert.True(Monitor.Wait(sync, TimeSpan.FromSeconds(30)), "the client never read the reply");
            }
        }
    }

    /// <summary>
    /// The client's side of the engine's output. Asking for the next line means the last one has been
    /// handled.
    /// </summary>
    private sealed class Reader(InstantEngine engine) : TextReader
    {
        private bool started;

        public override string? ReadLine()
        {
            lock (engine.sync)
            {
                if (started)
                {
                    engine.dispatched++;
                    Monitor.PulseAll(engine.sync);
                }
                started = true;
            }
            return engine.lines.TryTake(out string? line, Timeout.Infinite) ? line : null;
        }
    }

    /// <summary>
    /// The client's side of the engine's input: each command is answered before WriteLine returns.
    /// </summary>
    private sealed class Writer(InstantEngine engine, string name, Func<string, IEnumerable<string>> script) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => throw new NotSupportedException("commands are written whole");

        public override void WriteLine(string? value)
        {
            string command = value ?? "";
            if (command == "quit")
            {
                engine.lines.CompleteAdding();
                return;
            }
            engine.Send(command switch
            {
                "uci" => [$"id name {name}", "id author Test", "uciok"],
                "isready" => ["readyok"],
                _ => script(command),
            });
        }
    }
}
