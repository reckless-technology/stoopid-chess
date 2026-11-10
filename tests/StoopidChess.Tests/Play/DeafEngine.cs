using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;
using StoopidChess.Play;

namespace StoopidChess.Tests.Play;

/// <summary>
/// A fake engine that dies in two steps, each when the test says: first it stops listening (the read end of its
/// input pipe closes, as a dead process's does, so the client's next write fails), then its output closes (the client's
/// reader sees the end). Between the two the client cannot yet know the engine is gone - the moment a real engine's
/// death and the GUI's next command can meet. Connected to a <see cref="UciEngineClient"/> through anonymous pipes,
/// exactly as the built-in engine is. <see cref="StartAnswering"/> also runs a thread that answers the handshake
/// first ("uci", "isready") and then stops listening by itself, closing its output <paramref name="outputDelay"/> later.
/// </summary>
internal sealed class DeafEngine : IDisposable
{
    private readonly AnonymousPipeServerStream toEngine = new(PipeDirection.Out, HandleInheritability.None);
    private readonly AnonymousPipeServerStream fromEngine = new(PipeDirection.Out, HandleInheritability.None);
    private readonly AnonymousPipeClientStream engineInput;
    private readonly AnonymousPipeClientStream engineOutput;
    private readonly ManualResetEventSlim deaf = new();
    private readonly SafePipeHandle commandPipe;
    private readonly SafePipeHandle replyPipe;

    private DeafEngine()
    {
        engineInput = new AnonymousPipeClientStream(PipeDirection.In, toEngine.ClientSafePipeHandle);
        engineOutput = new AnonymousPipeClientStream(PipeDirection.In, fromEngine.ClientSafePipeHandle);
        commandPipe = toEngine.SafePipeHandle;
        replyPipe = engineOutput.SafePipeHandle;
        Client = UciEngineClient.Connect(new StreamReader(engineOutput), new StreamWriter(toEngine) { AutoFlush = true });
    }

    /// <summary>The client connected to the engine.</summary>
    public UciEngineClient Client { get; }

    /// <summary>Whether the client's end of its command pipe has been released.</summary>
    public bool IsCommandPipeReleased => commandPipe.IsClosed;

    /// <summary>Whether the client's end of its reply pipe has been released.</summary>
    public bool IsReplyPipeReleased => replyPipe.IsClosed;

    /// <summary>
    /// An engine that does nothing until the test makes it stop listening and close its output.
    /// </summary>
    public static DeafEngine Silent() => new();

    /// <summary>
    /// An engine that answers the handshake as <paramref name="name"/>, then stops listening, and closes its output
    /// <paramref name="outputDelay"/> after that.
    /// </summary>
    public static DeafEngine StartAnswering(string name, TimeSpan outputDelay)
    {
        var engine = new DeafEngine();
        var thread = new Thread(() =>
        {
            var reader = new StreamReader(engine.engineInput);
            var writer = new StreamWriter(engine.fromEngine) { AutoFlush = true };
            try
            {
                while (reader.ReadLine() is string command)
                {
                    if (command == "uci")
                    {
                        writer.WriteLine($"id name {name}");
                        writer.WriteLine("uciok");
                        break;
                    }
                }
                engine.StopListening();
                Thread.Sleep(outputDelay);
                engine.CloseOutput();
            }
            catch (IOException)
            {
                // The client closed its end first
            }
        })
        {
            IsBackground = true,
            Name = $"Deaf engine {name}",
        };
        thread.Start();
        return engine;
    }

    /// <summary>
    /// Wait until the engine has stopped listening.
    /// </summary>
    public void WaitUntilDeaf(TimeSpan timeout) => Assert.True(deaf.Wait(timeout), "the engine never stopped listening");

    /// <summary>
    /// The engine stops reading its input: the read end of the command pipe closes, and the client's next write fails.
    /// </summary>
    public void StopListening()
    {
        engineInput.Dispose();
        deaf.Set();
    }

    /// <summary>
    /// The engine's output closes: the client's reader sees the end.
    /// </summary>
    public void CloseOutput() => fromEngine.Dispose();

    public void Dispose()
    {
        engineInput.Dispose();
        fromEngine.Dispose();
        Client.Dispose();
        toEngine.Dispose();
        engineOutput.Dispose();
        deaf.Dispose();
    }
}
