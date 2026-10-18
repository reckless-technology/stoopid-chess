using System.IO.Pipes;
using StoopidChess.Engine.Uci;

namespace StoopidChess.Tests.Play;

/// <summary>
/// A fake UCI engine on a background thread, connected to a <see cref="UciEngineClient"/> through a
/// pair of anonymous pipes exactly as the built-in engine is. It names itself and answers "uci" and
/// "isready"; every other command goes to the script, which returns the lines to reply with, or null
/// to die - the engine closes its pipes at once, as a crashed engine's process would. "quit" ends it.
/// </summary>
internal static class ScriptedEngine
{
    /// <summary>
    /// Start the engine. With <paramref name="dieAfterHandshake"/> it dies straight after its "uciok":
    /// it starts, then is gone before its first command.
    /// </summary>
    public static UciEngineClient Start(string name, Func<string, IEnumerable<string>?> script, bool dieAfterHandshake = false)
    {
        var toEngine = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var fromEngine = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
        var engineInput = new AnonymousPipeClientStream(PipeDirection.In, toEngine.ClientSafePipeHandle);
        var engineOutput = new AnonymousPipeClientStream(PipeDirection.In, fromEngine.ClientSafePipeHandle);
        var thread = new Thread(() =>
        {
            using var reader = new StreamReader(engineInput);
            using var writer = new StreamWriter(fromEngine) { AutoFlush = true };
            try
            {
                while (reader.ReadLine() is string command && command != "quit")
                {
                    IEnumerable<string>? reply = command switch
                    {
                        "uci" => [$"id name {name}", "id author Test", "uciok"],
                        "isready" => ["readyok"],
                        _ => script(command),
                    };
                    if (reply is null)
                    {
                        return;
                    }
                    foreach (string line in reply)
                    {
                        writer.WriteLine(line);
                    }
                    if (dieAfterHandshake && command == "uci")
                    {
                        return;
                    }
                }
            }
            catch (IOException)
            {
                // The client closed its end
            }
        })
        {
            IsBackground = true,
            Name = $"Scripted engine {name}",
        };
        thread.Start();
        return UciEngineClient.Connect(new StreamReader(engineOutput), new StreamWriter(toEngine) { AutoFlush = true });
    }

    /// <summary>
    /// A script that plays the given move whenever it is asked to search and ignores everything else.
    /// </summary>
    public static Func<string, IEnumerable<string>?> Plays(string move) =>
        command => command.StartsWith("go", StringComparison.Ordinal) ? [$"bestmove {move}"] : [];
}
