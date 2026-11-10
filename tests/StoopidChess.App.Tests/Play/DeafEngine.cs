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
    private readonly AnonymousPipeServerStream _toEngine = new(PipeDirection.Out, HandleInheritability.None);
    private readonly AnonymousPipeServerStream _fromEngine = new(PipeDirection.Out, HandleInheritability.None);
    private readonly AnonymousPipeClientStream _engineInput;
    private readonly AnonymousPipeClientStream _engineOutput;
    private readonly ManualResetEventSlim _deaf = new();
    private readonly SafePipeHandle _commandPipe;
    private readonly SafePipeHandle _replyPipe;

    private DeafEngine()
    {
        _engineInput = new AnonymousPipeClientStream(PipeDirection.In, _toEngine.ClientSafePipeHandle);
        _engineOutput = new AnonymousPipeClientStream(PipeDirection.In, _fromEngine.ClientSafePipeHandle);
        _commandPipe = _toEngine.SafePipeHandle;
        _replyPipe = _engineOutput.SafePipeHandle;
        Client = UciEngineClient.Connect(
            new StreamReader(_engineOutput),
            new StreamWriter(_toEngine) { AutoFlush = true }
        );
    }

    /// <summary>The client connected to the engine.</summary>
    public UciEngineClient Client { get; }

    /// <summary>Whether the client's end of its command pipe has been released.</summary>
    public bool IsCommandPipeReleased => _commandPipe.IsClosed;

    /// <summary>Whether the client's end of its reply pipe has been released.</summary>
    public bool IsReplyPipeReleased => _replyPipe.IsClosed;

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
            var reader = new StreamReader(engine._engineInput);
            var writer = new StreamWriter(engine._fromEngine) { AutoFlush = true };
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
    public void WaitUntilDeaf(TimeSpan timeout) =>
        Assert.True(_deaf.Wait(timeout), "the engine never stopped listening");

    /// <summary>
    /// The engine stops reading its input: the read end of the command pipe closes, and the client's next write fails.
    /// </summary>
    public void StopListening()
    {
        _engineInput.Dispose();
        _deaf.Set();
    }

    /// <summary>
    /// The engine's output closes: the client's reader sees the end.
    /// </summary>
    public void CloseOutput() => _fromEngine.Dispose();

    public void Dispose()
    {
        _engineInput.Dispose();
        _fromEngine.Dispose();
        Client.Dispose();
        _toEngine.Dispose();
        _engineOutput.Dispose();
        _deaf.Dispose();
    }
}
