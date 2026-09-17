using StoopidChess.Core;

namespace StoopidChess.Engine;

/// <summary>
/// The outcome of a search, or of one completed iteration of it: the expected line of play, the
/// score of the first move, and the work done to find it.
/// </summary>
public sealed record SearchResult(int Depth, int Score, long Nodes, TimeSpan Elapsed, IReadOnlyList<Move> PrincipalVariation)
{
    /// <summary>
    /// The move to play; <see cref="Move.None"/> when the position has no legal moves.
    /// </summary>
    public Move BestMove => PrincipalVariation.Count == 0 ? Move.None : PrincipalVariation[0];

    public bool IsMateScore => Searcher.IsMateScore(Score);

    /// <summary>
    /// Full moves until mate from the side to move's point of view: positive if we mate, negative if
    /// we are mated. Only meaningful when <see cref="IsMateScore"/>.
    /// </summary>
    public int MateIn => Searcher.MateIn(Score);

    public long NodesPerSecond => Elapsed.TotalSeconds > 0 ? (long)(Nodes / Elapsed.TotalSeconds) : 0;
}

/// <summary>
/// Counters describing the work done by a search, for diagnostics and the bench command.
/// </summary>
public sealed class SearchStatistics
{
    public long Nodes { get; internal set; }
    public long QuiescenceNodes { get; internal set; }
    public long TranspositionCutoffs { get; internal set; }
    public long BetaCutoffs { get; internal set; }

    public override string ToString()
    {
        return $"nodes {Nodes} qnodes {QuiescenceNodes} ttcuts {TranspositionCutoffs} betacuts {BetaCutoffs}";
    }
}
