using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

public class BenchTests
{
    /// <summary>
    /// The node count of the fixed-depth bench is a signature of the search and evaluation. Any
    /// change to either changes this number; when that is intended, run "stoopid-chess bench 5" and
    /// update the constant here and in CLAUDE.md.
    /// </summary>
    public const long Depth5Signature = 63478;

    [Fact]
    public void SignatureIsUnchanged()
    {
        (long nodes, _) = Bench.Run(5);
        Assert.Equal(Depth5Signature, nodes);
    }

    [Fact]
    public void LogsOneLinePerPositionAndASummary()
    {
        var log = new StringWriter();
        Bench.Run(1, log);
        string[] lines = log.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(Bench.Positions.Count + 1, lines.Length);
        Assert.StartsWith("bench depth 1: ", lines[^1], StringComparison.Ordinal);
    }
}
