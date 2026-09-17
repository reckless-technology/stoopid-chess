using StoopidChess.Core;

namespace StoopidChess.Engine;

/// <summary>
/// The outcome of one completed iteration of iterative deepening, reported as the search progresses.
/// </summary>
public sealed record SearchProgress(int Depth, int Score, long Nodes, TimeSpan Elapsed, IReadOnlyList<Move> PrincipalVariation)
{
    public bool IsMateScore => Searcher.IsMateScore(Score);

    /// <summary>
    /// Full moves until mate from the side to move's point of view: positive if we mate, negative if
    /// we are mated. Only meaningful when <see cref="IsMateScore"/>.
    /// </summary>
    public int MateIn => Searcher.MateIn(Score);

    public long NodesPerSecond => Elapsed.TotalSeconds > 0 ? (long)(Nodes / Elapsed.TotalSeconds) : 0;
}

/// <summary>
/// The result of a search: the move to play and how it was found.
/// </summary>
public sealed record SearchResult(Move BestMove, int Score, int Depth, long Nodes, TimeSpan Elapsed, IReadOnlyList<Move> PrincipalVariation)
{
    public bool IsMateScore => Searcher.IsMateScore(Score);

    public int MateIn => Searcher.MateIn(Score);
}

/// <summary>
/// Counters describing the work done by a search, for diagnostics and the bench command.
/// </summary>
public sealed class SearchStatistics
{
    public long Nodes { get; internal set; }
    public long QuiescenceNodes { get; internal set; }
    public long Evaluations { get; internal set; }
    public long TranspositionHits { get; internal set; }
    public long TranspositionCutoffs { get; internal set; }
    public long BetaCutoffs { get; internal set; }
    public long KillerMoveHits { get; internal set; }
    public long PawnHashHits { get; internal set; }

    public override string ToString()
    {
        return $"nodes {Nodes} qnodes {QuiescenceNodes} evals {Evaluations} tthits {TranspositionHits} " +
               $"ttcuts {TranspositionCutoffs} betacuts {BetaCutoffs} killers {KillerMoveHits} pawnhits {PawnHashHits}";
    }
}
