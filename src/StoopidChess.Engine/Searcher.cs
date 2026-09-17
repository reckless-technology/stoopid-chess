using System.Diagnostics;
using StoopidChess.Core;

namespace StoopidChess.Engine;

/// <summary>
/// Finds the best move by iterative deepening over a negamax alpha-beta search with a quiescence
/// search at the leaves. Move ordering uses the transposition table move, then captures by most
/// valuable victim / least valuable attacker, then killer moves. Searches are deterministic for a
/// given position, table size and depth, so the bench node count acts as a signature of the search.
/// A Searcher is not thread safe; use one per concurrent search.
/// </summary>
public sealed class Searcher
{
    public const int MaxPly = 128;
    public const int MateScore = 32000;
    public const int MateThreshold = MateScore - MaxPly;
    public const int DrawScore = 0;
    private const int Infinity = MateScore + 1;
    private const int NodesBetweenTimeChecks = 2048;
    private const int NullMoveMinimumDepth = 3;
    private const int NullMoveReduction = 2;
    private const int LmrMinimumDepth = 3;
    private const int LmrFullDepthMoves = 3;

    private readonly TranspositionTable table;
    private readonly Evaluator evaluator = new();
    private readonly List<Move>[] moveLists = new List<Move>[MaxPly + 1];
    private readonly Move[,] killers = new Move[MaxPly + 1, 2];
    private readonly int[,,] history = new int[3, 128, 128];   // [colour, from, to]: how often a quiet move caused a cutoff
    private readonly Stopwatch stopwatch = new();

    private Board board = Board.StartPosition();
    private SearchStatistics statistics = new();
    private CancellationToken cancellation;
    private TimeSpan? timeBudget;
    private long? nodeLimit;
    private bool stopped;

    public Searcher(TranspositionTable table)
    {
        this.table = table ?? throw new ArgumentNullException(nameof(table));
        for (int i = 0; i < moveLists.Length; i++)
        {
            moveLists[i] = new List<Move>(64);
        }
    }

    public TranspositionTable Table => table;

    /// <summary>
    /// Statistics of the most recent search.
    /// </summary>
    public SearchStatistics Statistics => statistics;

    public static bool IsMateScore(int score) => Math.Abs(score) >= MateThreshold;

    /// <summary>
    /// Full moves to mate for a mate score: positive when the side to move mates.
    /// </summary>
    public static int MateIn(int score)
    {
        int plies = MateScore - Math.Abs(score);
        int moves = (plies + 1) / 2;
        return score > 0 ? moves : -moves;
    }

    /// <summary>
    /// Search the position. The caller's board is copied and left untouched.
    /// </summary>
    /// <param name="position">The position to search, including its move history for repetition detection.</param>
    /// <param name="limits">When to stop.</param>
    /// <param name="cancellationToken">Stops the search early (the UCI "stop" command); the best move so far is returned.</param>
    /// <param name="onProgress">Called after each completed depth.</param>
    public SearchResult Search(Board position, SearchLimits limits, Action<SearchResult>? onProgress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(limits);

        board = position.Clone();
        board.NormalisePieceOrder();    // So that the search depends only on the position, not on how it was reached
        statistics = new SearchStatistics();
        cancellation = cancellationToken;
        timeBudget = limits.TimeBudget(board.SideToMove);
        nodeLimit = limits.Infinite ? null : limits.Nodes;
        stopped = false;
        Array.Clear(killers);
        Array.Clear(history);
        stopwatch.Restart();

        List<Move> rootMoves = MoveGenerator.GenerateLegalMoves(board);
        if (rootMoves.Count == 0)
        {
            return new SearchResult(0, MoveGenerator.IsInCheck(board) ? -MateScore : DrawScore, 0, stopwatch.Elapsed, []);
        }

        int maxDepth = limits.Infinite ? MaxPly : Math.Clamp(limits.Depth ?? MaxPly, 1, MaxPly);
        Move bestMove = rootMoves[0];
        int bestScore = 0;
        int completedDepth = 0;
        List<Move> principalVariation = [bestMove];

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            int score = SearchRoot(rootMoves, depth, out Move move);
            if (stopped)
            {
                // The iteration was cut short so its score cannot be trusted and the previous
                // iteration's move is kept, unless there is none: then the best of the root moves
                // that did complete is better than an arbitrary one.
                if (completedDepth == 0)
                {
                    bestMove = move;
                    principalVariation = [move];
                }
                break;
            }
            bestMove = move;
            bestScore = score;
            completedDepth = depth;
            principalVariation = ExtractPrincipalVariation(bestMove, depth);
            onProgress?.Invoke(new SearchResult(depth, score, statistics.Nodes, stopwatch.Elapsed, principalVariation));

            if (IsMateScore(score) && !limits.Infinite && limits.Depth is null)
            {
                break;  // A forced mate has been found; deeper search cannot improve on it
            }
            if (timeBudget is not null && stopwatch.Elapsed > timeBudget.Value / 2)
            {
                break;  // The next iteration would take longer than the time left
            }
        }

        stopwatch.Stop();
        return new SearchResult(completedDepth, bestScore, statistics.Nodes, stopwatch.Elapsed, principalVariation);
    }

    /// <summary>
    /// The root is searched separately so that the best move is always known even if the iteration
    /// is stopped part way through, and so that the previous best move is searched first.
    /// </summary>
    private int SearchRoot(List<Move> rootMoves, int depth, out Move bestMove)
    {
        OrderMoves(rootMoves, 0, GetTableMove());
        bestMove = rootMoves[0];
        int alpha = -Infinity;
        const int beta = Infinity;
        foreach (Move move in rootMoves)
        {
            board.MakeMove(move);
            int score = -AlphaBeta(-beta, -alpha, depth - 1, 1);
            board.UnmakeMove();
            if (stopped)
            {
                return alpha;
            }
            if (score > alpha)
            {
                alpha = score;
                bestMove = move;
            }
        }
        table.Store(board.Key, board.SecondaryKey, bestMove, ScoreToTable(alpha, 0), depth, NodeType.Exact);
        return alpha;
    }

    private int AlphaBeta(int alpha, int beta, int depth, int ply)
    {
        Debug.Assert(alpha < beta, "alpha must be below beta");
        statistics.Nodes++;
        CheckLimits();
        if (stopped)
        {
            return alpha;
        }

        if (board.RepetitionCount() >= 1 || board.IsInsufficientMaterial())
        {
            return DrawScore;
        }
        if (ply >= MaxPly)
        {
            return Evaluate();
        }

        // Mate distance pruning: a mate found here can be no shorter than ply
        alpha = Math.Max(alpha, -MateScore + ply);
        beta = Math.Min(beta, MateScore - ply - 1);
        if (alpha >= beta)
        {
            return alpha;
        }

        bool inCheck = MoveGenerator.IsInCheck(board);
        // The fifty-move rule draws the game unless this is checkmate, so when in check it is
        // applied only once a legal move has been found
        bool fiftyMoveRule = board.HalfMoveClock >= 100;
        if (fiftyMoveRule && !inCheck)
        {
            return DrawScore;
        }
        if (depth <= 0 && !inCheck)
        {
            return Quiescence(alpha, beta, ply);
        }
        if (depth <= 0)
        {
            depth = 1;      // Check extension: never evaluate a position statically while in check
        }

        Move tableMove = Move.None;
        if (table.TryGet(board.Key, board.SecondaryKey, out TranspositionEntry entry))
        {
            tableMove = entry.BestMove;
            if (entry.Depth >= depth)
            {
                int tableScore = ScoreFromTable(entry.Score, ply);
                if (entry.Type == NodeType.Exact
                    || (entry.Type == NodeType.LowerBound && tableScore >= beta)
                    || (entry.Type == NodeType.UpperBound && tableScore <= alpha))
                {
                    statistics.TranspositionCutoffs++;
                    return tableScore;
                }
            }
        }

        // Null-move pruning: if handing the opponent a free move still leaves us at or above beta, a
        // real move will almost certainly do so too, so the node is a fail-high. Skipped in check,
        // right after another null move, in pawn endgames (zugzwang) and for mate-bound windows.
        if (depth >= NullMoveMinimumDepth && !inCheck && beta < MateThreshold && !board.LastMoveWasNull
            && board.HasNonPawnMaterial(board.SideToMove) && Evaluate() >= beta)
        {
            int reduction = NullMoveReduction + (depth / 6);
            board.MakeNullMove();
            int nullScore = -AlphaBeta(-beta, -beta + 1, depth - 1 - reduction, ply + 1);
            board.UnmakeMove();
            if (stopped)
            {
                return alpha;
            }
            if (nullScore >= beta)
            {
                statistics.NullMoveCutoffs++;
                return nullScore >= MateThreshold ? beta : nullScore;   // A mate found after a null move is not trusted
            }
        }

        List<Move> moves = moveLists[ply];
        moves.Clear();
        MoveGenerator.GeneratePseudoLegalMoves(board, moves);
        OrderMoves(moves, ply, tableMove);

        int originalAlpha = alpha;
        int bestScore = -Infinity;
        Move bestMove = Move.None;
        int legalMoves = 0;
        Colour us = board.SideToMove;

        foreach (Move move in moves)
        {
            board.MakeMove(move);
            if (MoveGenerator.IsAttacked(board, board.KingSquare(us), board.SideToMove))
            {
                board.UnmakeMove();
                continue;
            }
            legalMoves++;
            if (fiftyMoveRule)
            {
                board.UnmakeMove();
                return DrawScore;   // In check but not mated: the fifty-move rule stands
            }
            int score;
            if (depth >= LmrMinimumDepth && legalMoves > LmrFullDepthMoves && !inCheck && IsReducible(move, ply) && !MoveGenerator.IsInCheck(board))
            {
                // Late move reductions: a quiet move ordered this far down rarely raises alpha, so search
                // it shallower with a null window first and only pay for the full search if it does
                int reduction = legalMoves > LmrFullDepthMoves * 3 ? 2 : 1;
                score = -AlphaBeta(-alpha - 1, -alpha, depth - 1 - reduction, ply + 1);
                if (score > alpha && !stopped)
                {
                    score = -AlphaBeta(-beta, -alpha, depth - 1, ply + 1);
                }
            }
            else
            {
                score = -AlphaBeta(-beta, -alpha, depth - 1, ply + 1);
            }
            board.UnmakeMove();
            if (stopped)
            {
                return alpha;   // Do not store results from an incomplete search
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
            if (score >= beta)
            {
                statistics.BetaCutoffs++;
                if (!move.IsCapture)
                {
                    RecordKiller(ply, move);
                    RecordHistory(move, depth);
                }
                table.Store(board.Key, board.SecondaryKey, move, ScoreToTable(score, ply), depth, NodeType.LowerBound);
                return score;
            }
            if (score > alpha)
            {
                alpha = score;
            }
        }

        if (legalMoves == 0)
        {
            return inCheck ? -MateScore + ply : DrawScore;
        }

        NodeType type = bestScore <= originalAlpha ? NodeType.UpperBound : NodeType.Exact;
        table.Store(board.Key, board.SecondaryKey, bestMove, ScoreToTable(bestScore, ply), depth, type);
        return bestScore;
    }

    /// <summary>
    /// Search captures and promotions until the position is quiet, to avoid evaluating in the middle
    /// of an exchange. When in check every move is considered so that mates are seen.
    /// </summary>
    private int Quiescence(int alpha, int beta, int ply)
    {
        statistics.Nodes++;
        statistics.QuiescenceNodes++;
        CheckLimits();
        if (stopped)
        {
            return alpha;
        }
        if (ply >= MaxPly)
        {
            return Evaluate();
        }

        bool inCheck = MoveGenerator.IsInCheck(board);
        bool fiftyMoveRule = board.HalfMoveClock >= 100;
        if (!inCheck && (fiftyMoveRule || board.RepetitionCount() >= 1 || board.IsInsufficientMaterial()))
        {
            return DrawScore;
        }
        int bestScore = -Infinity;
        if (!inCheck)
        {
            // Stand pat: the side to move need not capture at all
            bestScore = Evaluate();
            if (bestScore >= beta)
            {
                return bestScore;
            }
            alpha = Math.Max(alpha, bestScore);
        }

        List<Move> moves = moveLists[ply];
        moves.Clear();
        if (inCheck)
        {
            MoveGenerator.GeneratePseudoLegalMoves(board, moves);
        }
        else
        {
            MoveGenerator.GenerateCaptures(board, moves);
        }
        OrderMoves(moves, ply, Move.None);

        int legalMoves = 0;
        Colour us = board.SideToMove;
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            if (MoveGenerator.IsAttacked(board, board.KingSquare(us), board.SideToMove))
            {
                board.UnmakeMove();
                continue;
            }
            legalMoves++;
            if (fiftyMoveRule)
            {
                board.UnmakeMove();
                return DrawScore;
            }
            int score = -Quiescence(-beta, -alpha, ply + 1);
            board.UnmakeMove();
            if (stopped)
            {
                return alpha;
            }
            bestScore = Math.Max(bestScore, score);
            if (score >= beta)
            {
                statistics.BetaCutoffs++;
                return score;
            }
            alpha = Math.Max(alpha, score);
        }

        if (inCheck && legalMoves == 0)
        {
            return -MateScore + ply;
        }
        return bestScore;
    }

    private int Evaluate()
    {
        return evaluator.Evaluate(board);
    }

    private void CheckLimits()
    {
        if (stopped)
        {
            return;
        }
        if (nodeLimit is not null && statistics.Nodes >= nodeLimit.Value)
        {
            stopped = true;
            return;
        }
        if (statistics.Nodes % NodesBetweenTimeChecks == 0)
        {
            if (cancellation.IsCancellationRequested || (timeBudget is not null && stopwatch.Elapsed >= timeBudget.Value))
            {
                stopped = true;
            }
        }
    }

    private Move GetTableMove()
    {
        return table.TryGet(board.Key, board.SecondaryKey, out TranspositionEntry entry) ? entry.BestMove : Move.None;
    }

    /// <summary>
    /// Order moves best-first to maximise cutoffs: the table move, then captures by most valuable
    /// victim / least valuable attacker (promotions count as capturing the promoted piece), then the
    /// killer moves for this ply, then the rest in generation order. The sort is stable so that the
    /// search is deterministic.
    /// </summary>
    private void OrderMoves(List<Move> moves, int ply, Move tableMove)
    {
        int count = moves.Count;
        if (count < 2)
        {
            return;
        }
        Span<int> scores = count <= 256 ? stackalloc int[count] : new int[count];
        for (int i = 0; i < count; i++)
        {
            scores[i] = ScoreMove(moves[i], ply, tableMove);
        }
        // Insertion sort, descending by score; stable and fast for the short lists chess produces
        for (int i = 1; i < count; i++)
        {
            Move move = moves[i];
            int score = scores[i];
            int j = i - 1;
            while (j >= 0 && scores[j] < score)
            {
                moves[j + 1] = moves[j];
                scores[j + 1] = scores[j];
                j--;
            }
            moves[j + 1] = move;
            scores[j + 1] = score;
        }
    }

    private int ScoreMove(Move move, int ply, Move tableMove)
    {
        if (move == tableMove)
        {
            return 1_000_000;
        }
        int score = 0;
        if (move.IsCapture)
        {
            score += 100_000 + (PieceValue(move.Captured) * 10) - PieceValue(move.Piece);
        }
        if (move.IsPromotion)
        {
            score += 100_000 + PieceValue(move.Promotion);
        }
        if (score == 0)
        {
            if (move == killers[ply, 0])
            {
                return 90_000;
            }
            if (move == killers[ply, 1])
            {
                return 80_000;
            }
            return Math.Min(history[(int)board.SideToMove, move.From, move.To], HistoryCap);
        }
        return score;
    }

    private const int HistoryCap = 79_999;

    /// <summary>
    /// Quiet, non-killer moves may be searched with a reduced depth when ordered late.
    /// </summary>
    private bool IsReducible(Move move, int ply)
    {
        return !move.IsCapture && !move.IsPromotion && move != killers[ply, 0] && move != killers[ply, 1];
    }

    /// <summary>
    /// Quiet moves that cut off deep in the tree are likely to be good elsewhere too; order them by
    /// the depth-weighted count of cutoffs they have produced in this search.
    /// </summary>
    private void RecordHistory(Move move, int depth)
    {
        ref int entry = ref history[(int)board.SideToMove, move.From, move.To];
        entry = Math.Min(entry + (depth * depth), HistoryCap);
    }

    private void RecordKiller(int ply, Move move)
    {
        if (killers[ply, 0] != move)
        {
            killers[ply, 1] = killers[ply, 0];
            killers[ply, 0] = move;
        }
    }

    private static int PieceValue(PieceType type) => type switch
    {
        PieceType.Pawn => 1,
        PieceType.Knight => 3,
        PieceType.Bishop => 3,
        PieceType.Rook => 5,
        PieceType.Queen => 9,
        PieceType.King => 20,
        _ => 0,
    };

    /// <summary>
    /// Mate scores are stored relative to the node rather than the root, so that an entry found from
    /// a different root still gives the right distance to mate.
    /// </summary>
    internal static int ScoreToTable(int score, int ply)
    {
        if (score >= MateThreshold)
        {
            return score + ply;
        }
        if (score <= -MateThreshold)
        {
            return score - ply;
        }
        return score;
    }

    internal static int ScoreFromTable(int score, int ply)
    {
        if (score >= MateThreshold)
        {
            return score - ply;
        }
        if (score <= -MateThreshold)
        {
            return score + ply;
        }
        return score;
    }

    /// <summary>
    /// Follow the table's best moves from the root to reconstruct the expected line of play.
    /// </summary>
    private List<Move> ExtractPrincipalVariation(Move bestMove, int maxLength)
    {
        var line = new List<Move> { bestMove };
        board.MakeMove(bestMove);
        while (line.Count < maxLength && board.RepetitionCount() == 0
            && table.TryGet(board.Key, board.SecondaryKey, out TranspositionEntry entry) && !entry.BestMove.IsNone)
        {
            Move move = entry.BestMove;
            if (!MoveGenerator.GenerateLegalMoves(board).Contains(move))
            {
                break;
            }
            line.Add(move);
            board.MakeMove(move);
        }
        for (int i = 0; i < line.Count; i++)
        {
            board.UnmakeMove();
        }
        return line;
    }
}
