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

    public long NodesPerSecond => Rate(Nodes, Elapsed);

    /// <summary>
    /// Nodes per second, zero when no time has passed.
    /// </summary>
    public static long Rate(long nodes, TimeSpan elapsed) => elapsed > TimeSpan.Zero ? (long)(nodes / elapsed.TotalSeconds) : 0;
}
