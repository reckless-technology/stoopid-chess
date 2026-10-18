using System.Reflection;

namespace StoopidChess.Tests.Uci;

/// <summary>
/// The engine's console entry point (stoopid-chess): its command line, the UCI loop on standard input
/// and output, bench and perft.
/// </summary>
[Collection(ConsoleCapture.Name)]
public class UciProgramTests
{
    private static readonly MethodInfo EntryPoint = Assembly.Load("stoopid-chess").EntryPoint!;

    private static (int ExitCode, string Output, string Error) Run(string input, params string[] args) =>
        ConsoleCapture.Run(() => (int)EntryPoint.Invoke(null, [args])!, input);

    [Fact]
    public void WithNoArgumentsItSpeaksUciOnTheConsole()
    {
        (int exitCode, string output, _) = Run("uci\nisready\nquit\n");
        Assert.Equal(0, exitCode);
        Assert.Contains("uciok", output, StringComparison.Ordinal);
        Assert.Contains("readyok", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bench depth 2:", "bench", "2")]
    [InlineData("bench depth 6:", "bench")]          // The default depth, CI's signature
    public void BenchPrintsItsSignature(string signature, params string[] args)
    {
        (int exitCode, string output, _) = Run("", args);
        Assert.Equal(0, exitCode);
        Assert.StartsWith("Stoopid Chess", output, StringComparison.Ordinal);
        Assert.Contains(signature, output, StringComparison.Ordinal);
    }

    [Fact]
    public void PerftPassesTheStandardPositions()
    {
        (int exitCode, string output, _) = Run("", "perft", "1");
        Assert.Equal(0, exitCode);
        Assert.Contains("perft: 128 positions", output, StringComparison.Ordinal);
        Assert.Contains("perft PASS", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bench", "0")]
    [InlineData("bench", "x")]
    [InlineData("perft", "-3")]
    [InlineData("help")]
    public void AnythingElsePrintsTheUsage(params string[] args)
    {
        (int exitCode, string output, string error) = Run("", args);
        Assert.Equal(2, exitCode);
        Assert.Empty(output);
        Assert.StartsWith("usage: stoopid-chess", error, StringComparison.Ordinal);
    }
}
