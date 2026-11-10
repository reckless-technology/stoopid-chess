using System.Diagnostics;
using StoopidChess.Core;
using StoopidChess.Engine;
using StoopidChess.Play;

namespace StoopidChess.Tests.Play;

/// <summary>
/// An engine that dies between two commands: the command that meets its death fails as the client documents
/// (<see cref="InvalidOperationException"/>, the engine has exited), and disposing the client afterwards neither throws
/// nor holds on to the pipes or the process - for an engine on pipes in this process (<see cref="DeafEngine"/>) and for
/// a real engine process killed under the client.
/// </summary>
public class UciEngineClientDeadEngineTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public void ACommandToAnEngineThatHasStoppedListeningFailsAndDisposingThenReleasesEverything()
    {
        using DeafEngine engine = DeafEngine.Silent();
        engine.StopListening();
        // The write fails, and the command stays in the client's writer, unsent
        Assert.Throws<InvalidOperationException>(engine.Client.NewGame);
        Assert.True(engine.Client.HasExited);
        Assert.Throws<InvalidOperationException>(() => engine.Client.SetOption("Hash", "16"));
        engine.CloseOutput();
        engine.Client.Dispose();                     // Threw IOException ("Pipe is broken") flushing that command
        Assert.True(engine.IsCommandPipeReleased);
        Assert.True(engine.IsReplyPipeReleased);
        Assert.Throws<ObjectDisposedException>(engine.Client.NewGame);
        engine.Client.Dispose();                     // Idempotent
    }

    [Fact]
    public void DisposingAnEngineThatHasStoppedListeningReleasesEverything()
    {
        using DeafEngine engine = DeafEngine.Silent();
        engine.StopListening();
        engine.CloseOutput();
        engine.Client.Dispose();                     // "quit" cannot be written; nor may its flush throw
        Assert.True(engine.IsCommandPipeReleased);
        Assert.True(engine.IsReplyPipeReleased);
    }

    [Fact]
    public async Task AKilledEngineProcessFailsTheNextCommandAndIsReleasedOnDispose()
    {
        string executable = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "stoopid-chess.exe" : "stoopid-chess");
        Assert.True(File.Exists(executable), $"no engine executable at {executable}");
        var client = UciEngineClient.StartProcess(executable);
        try
        {
            await client.InitialiseAsync(new CancellationTokenSource(Timeout).Token);
            int id = client.ProcessId ?? throw new InvalidOperationException("an external engine has a process");

            // The reader thread held in the first "info" line, so it cannot see the engine die before the next command
            using var holdReader = new ManualResetEventSlim();
            using var readerHeld = new ManualResetEventSlim();
            client.InfoReceived += (_, _) =>
            {
                readerHeld.Set();
                holdReader.Wait(Timeout);
            };
            client.SetPosition(Position.StartFen, []);
            Task<string> search = client.GoAsync(SearchLimits.ForDepth(30));
            Assert.True(readerHeld.Wait(Timeout), "the engine reported nothing");
            using (var engine = Process.GetProcessById(id))
            {
                engine.Kill(entireProcessTree: true);
                Assert.True(engine.WaitForExit(Timeout));
            }
            Assert.False(client.HasExited);
            Assert.Throws<InvalidOperationException>(client.NewGame);
            Assert.True(client.HasExited);
            holdReader.Set();
            await Assert.ThrowsAsync<InvalidOperationException>(() => search.WaitAsync(Timeout));
        }
        finally
        {
            client.Dispose();                        // Threw IOException flushing the unsent "ucinewgame"
        }
        Assert.Throws<ObjectDisposedException>(client.NewGame);
    }
}
