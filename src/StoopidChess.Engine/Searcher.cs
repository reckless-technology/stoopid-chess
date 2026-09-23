using System.Diagnostics;
using System.Runtime.CompilerServices;
using StoopidChess.Core;

namespace StoopidChess.Engine;

/// <summary>
/// Finds the best move by iterative deepening over a principal variation search with a quiescence
/// search at the leaves. Move ordering uses the transposition table move, then captures by most
/// valuable victim / least valuable attacker, then killer and history moves. Searches are
/// deterministic for a given position, table size and depth, so the bench node count acts as a
/// signature of the search. A Searcher is not thread safe; use one per concurrent search.
/// </summary>
public sealed class Searcher
{
    public const int MaxPly = 128;
    public const int MateScore = 32000;
    public const int MateThreshold = MateScore - MaxPly;
    public const int DrawScore = 0;
    private const int Infinity = MateScore + 1;
    private const int NodesBetweenTimeChecks = 2048;
    private const int NullMoveMinimumDepth = 2;
    private const int NullMoveReduction = 3;
    private const int ScoreDropMargin = 30;          // A fall in the root score by this much earns the move more time
    private const int ScoreDropMinimumDepth = 4;
    private const int MoveChangeMinimumDepth = 6;     // A new best move from this depth on earns more time too
    private const int InternalIterativeReductionMinimumDepth = 4;
    private const int ReverseFutilityMaxDepth = 7;
    private const int ReverseFutilityMargin = 60;      // Per ply of remaining depth
    private const int RazoringMaxDepth = 3;
    private const int RazoringMargin = 250;            // Per ply of remaining depth
    private const int LateMovePruningMaxDepth = 4;
    private const int LateMovePruningBase = 5;         // Quiet moves after the (base + depth^2)th are skipped
    private const int LmrMinimumDepth = 3;
    private const int LmrFullDepthMoves = 3;
    private const int AspirationMinimumDepth = 4;
    private const int AspirationWindow = 20;
    private const int HistoryCap = 79_999;

    private readonly TranspositionTable table;
    private readonly Evaluator evaluator;
    private readonly MoveList[] moveLists = new MoveList[MaxPly + 1];   // One per ply, reused by every node at that ply
    // The heuristic tables are flat arrays with hand-rolled indexing: multi-dimensional array
    // accesses go through a runtime helper on some shapes, which showed in the profile.
    private readonly Move[] killers = new Move[(MaxPly + 1) * 2];   // [2 * ply] and [2 * ply + 1]
    private readonly Move[] counterAtPly = new Move[MaxPly + 1];   // The countermove ordered at each ply, exempt from reductions like the killers
    private readonly Move[] counterMoves = new Move[7 * Square.Count];   // [piece, to] of the opponent's last move: the quiet reply that last refuted it
    private readonly int[] continuationHistory = new int[7 * Square.Count * 7 * Square.Count];   // [previous piece, previous to, piece, to]: cutoffs by a quiet move after that reply
    private readonly int[] history = new int[3 * Square.Count * Square.Count];   // [color, from, to]: how often a quiet move caused a cutoff
    private readonly Stopwatch stopwatch = new();

    private Position position = Position.Start();
    private SearchStatistics statistics = new();
    private CancellationToken cancellation;
    private TimeSpan? timeBudget;
    private TimeSpan? timeCap;
    private long? nodeLimit;
    private bool stopped;

    public Searcher(TranspositionTable table) : this(table, new EvaluationParameters())
    {
    }

    public Searcher(TranspositionTable table, EvaluationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(parameters);
        this.table = table;
        evaluator = new Evaluator(parameters);
    }

    /// <summary>
    /// The evaluation parameters the search plays with.
    /// </summary>
    public EvaluationParameters Parameters => evaluator.Parameters;

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
    /// <param name="onProgress">Called after each completed depth.</param>
    /// <param name="cancellationToken">Stops the search early (the UCI "stop" command); the best move so far is returned.</param>
    public SearchResult Search(Position root, SearchLimits limits, Action<SearchResult>? onProgress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(limits);

        position = root.Clone();
        statistics = new SearchStatistics();
        cancellation = cancellationToken;
        timeBudget = limits.TimeBudget(position.SideToMove);
        timeCap = limits.TimeCap(position.SideToMove);
        bool extended = false;
        nodeLimit = limits.Infinite ? null : limits.Nodes;
        stopped = false;
        Array.Clear(killers);
        Array.Clear(counterMoves);
        Array.Clear(history);
        Array.Clear(continuationHistory);
        stopwatch.Restart();

        MoveList rootMoves = position.GenerateLegalMoves();
        if (rootMoves.Count == 0)
        {
            return new SearchResult(0, position.IsInCheck ? -MateScore : DrawScore, 0, stopwatch.Elapsed, []);
        }

        int maxDepth = limits.Infinite ? MaxPly : Math.Clamp(limits.Depth ?? MaxPly, 1, MaxPly);
        Move bestMove = rootMoves[0];
        int bestScore = 0;
        int completedDepth = 0;
        List<Move> principalVariation = [bestMove];

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            (int score, Move move) = SearchWithAspiration(ref rootMoves, depth, bestScore, completedDepth);
            if (stopped)
            {
                // The iteration was cut short so its score cannot be trusted and the previous
                // iteration's move is kept, unless there is none: then the best of the root moves
                // that did complete is better than an arbitrary one.
                if (completedDepth == 0)
                {
                    principalVariation = [move];
                }
                break;
            }
            if (timeBudget is not null && timeCap is not null && !extended
                && ((completedDepth >= ScoreDropMinimumDepth && score <= bestScore - ScoreDropMargin) || (completedDepth >= MoveChangeMinimumDepth && move != bestMove)))
            {
                // The score fell or the best move changed: give this move half as much time again, within
                // the clock, to sort it out
                extended = true;
                TimeSpan longer = timeBudget.Value * 1.5;
                timeBudget = longer < timeCap.Value ? longer : timeCap.Value;
            }
            bestMove = move;
            bestScore = score;
            completedDepth = depth;
            principalVariation = ExtractPrincipalVariation(bestMove, depth);
            onProgress?.Invoke(new SearchResult(depth, score, statistics.Nodes, stopwatch.Elapsed, principalVariation));

            if (IsMateScore(score) && MateScore - Math.Abs(score) <= depth && !limits.Infinite && limits.Depth is null)
            {
                break;  // A forced mate within the searched depth is proven; deeper search cannot improve on it
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
    /// Aspiration windows: once a few iterations have settled the score, search the next one in a
    /// narrow window around it, which cuts off far more, and widen and repeat if the score escapes.
    /// </summary>
    private (int Score, Move BestMove) SearchWithAspiration(ref MoveList rootMoves, int depth, int previousScore, int completedDepth)
    {
        if (completedDepth < AspirationMinimumDepth || IsMateScore(previousScore))
        {
            return SearchRoot(ref rootMoves, depth, -Infinity, Infinity);
        }
        int window = AspirationWindow;
        int alpha = previousScore - window;
        int beta = previousScore + window;
        while (true)
        {
            (int score, Move bestMove) = SearchRoot(ref rootMoves, depth, alpha, beta);
            if (stopped || (score > alpha && score < beta))
            {
                return (score, bestMove);
            }
            window *= 2;
            if (score <= alpha)
            {
                alpha = Math.Max(previousScore - window, -Infinity);
            }
            else
            {
                beta = Math.Min(previousScore + window, Infinity);
            }
            if (window > AspirationWindow * 16)
            {
                alpha = -Infinity;
                beta = Infinity;
            }
        }
    }

    /// <summary>
    /// The root is searched separately so that the best move is always known even if the iteration
    /// is stopped part way through, and so that the previous best move is searched first.
    /// </summary>
    private (int Score, Move BestMove) SearchRoot(ref MoveList rootMoves, int depth, int alpha, int beta)
    {
        OrderMoves(ref rootMoves, 0, GetTableMove());
        Move bestMove = rootMoves[0];
        int originalAlpha = alpha;
        bool first = true;
        foreach (Move move in rootMoves)
        {
            position.MakeMove(move);
            table.Prefetch(position.Key);
            int score = SearchChild(alpha, beta, depth - 1, 1, first);
            position.UnmakeMove();
            first = false;
            if (stopped)
            {
                return (alpha, bestMove);
            }
            if (score > alpha)
            {
                alpha = score;
                bestMove = move;
                if (alpha >= beta)
                {
                    break;      // Fail high: the caller widens the window and searches again
                }
            }
        }
        NodeType type = alpha >= beta ? NodeType.LowerBound : alpha <= originalAlpha ? NodeType.UpperBound : NodeType.Exact;
        table.Store(position.Key, bestMove, ScoreToTable(alpha, 0), depth, type);
        return (alpha, bestMove);
    }

    /// <summary>
    /// Principal variation search of a child: the first move is expected to be best and gets the
    /// full window; the rest only need to prove they are not better (a null window) and are
    /// re-searched with the full window only when they are.
    /// </summary>
    private int SearchChild(int alpha, int beta, int depth, int ply, bool first, int reduction = 0)
    {
        if (first)
        {
            return -AlphaBeta(-beta, -alpha, depth, ply);
        }
        int score = -AlphaBeta(-alpha - 1, -alpha, depth - reduction, ply);
        if (reduction > 0 && score > alpha && !stopped)
        {
            score = -AlphaBeta(-alpha - 1, -alpha, depth, ply);
        }
        if (score > alpha && score < beta && !stopped)
        {
            score = -AlphaBeta(-beta, -alpha, depth, ply);
        }
        return score;
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

        if (position.RepetitionCount() >= 1 || position.IsInsufficientMaterial())
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

        bool inCheck = position.IsInCheck;
        // The fifty-move rule draws the game unless this is checkmate, so when in check it is
        // applied only once a legal move has been found
        bool fiftyMoveRule = position.HalfMoveClock >= 100;
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

        if (TryTableCutoff(alpha, beta, depth, ply, out Move tableMove, out int tableScore))
        {
            return tableScore;
        }

        // Internal iterative reduction: with no table move to order first, a deep node is searched a ply
        // shallower; if it matters, the next iteration comes back to it with a table move in hand
        if (depth >= InternalIterativeReductionMinimumDepth && tableMove.IsNone)
        {
            depth--;
        }

        bool pvNode = beta - alpha > 1;
        int staticEval = inCheck ? -Infinity : Evaluate();

        // Reverse futility pruning: in a non-PV node at low depth, a static evaluation so far above
        // beta that a few plies of play cannot bring it back down is taken as a fail-high at once.
        if (depth <= ReverseFutilityMaxDepth && !inCheck && !pvNode && beta < MateThreshold
            && staticEval - (ReverseFutilityMargin * depth) >= beta)
        {
            statistics.ReverseFutilityCutoffs++;
            return staticEval;
        }

        // Razoring: in a non-PV node at low depth, a static evaluation so far below alpha that quiet
        // play cannot recover is checked against the quiescence search, and when the captures agree
        // that alpha is out of reach the node is cut to the quiescence score.
        if (depth <= RazoringMaxDepth && !inCheck && !pvNode && alpha > -MateThreshold
            && staticEval + (RazoringMargin * depth) <= alpha)
        {
            int razorScore = Quiescence(alpha, alpha + 1, ply);
            if (stopped)
            {
                return alpha;
            }
            if (razorScore <= alpha)
            {
                statistics.RazoringCutoffs++;
                return razorScore;
            }
        }

        // Null-move pruning: if handing the opponent a free move still leaves us at or above beta, a
        // real move will almost certainly do so too, so the node is a fail-high. Skipped in check,
        // right after another null move, in pawn endgames (zugzwang) and for mate-bound windows.
        if (depth >= NullMoveMinimumDepth && !inCheck && beta < MateThreshold && !position.LastMoveWasNull
            && position.HasNonPawnMaterial(position.SideToMove) && staticEval >= beta)
        {
            int reduction = NullMoveReduction + (depth / 4);
            position.MakeNullMove();
            table.Prefetch(position.Key);
            int nullScore = -AlphaBeta(-beta, -beta + 1, depth - 1 - reduction, ply + 1);
            position.UnmakeMove();
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

        int originalAlpha = alpha;
        int bestScore = -Infinity;
        Move bestMove = Move.None;
        int moveNumber = 0;

        // The table move first, before paying for move generation: it alone cuts off a large share
        // of nodes. It is trusted without a legality check, as pawnstar trusts its table: stores
        // only ever hold moves from the storing position's legal moves, and entries are verified by
        // the full 64-bit key, so a collision is beyond unlikely.
        if (!tableMove.IsNone)
        {
            if (fiftyMoveRule)
            {
                return DrawScore;   // In check, but the table move proves a legal reply exists
            }
            moveNumber = 1;
            position.MakeMove(tableMove);
            table.Prefetch(position.Key);
            int tableMoveScore = SearchChild(alpha, beta, depth - 1, ply + 1, first: true);
            position.UnmakeMove();
            if (stopped)
            {
                return alpha;
            }
            bestScore = tableMoveScore;
            bestMove = tableMove;
            if (tableMoveScore >= beta)
            {
                RecordCutoff(tableMove, ply, depth);
                table.Store(position.Key, tableMove, ScoreToTable(tableMoveScore, ply), depth, NodeType.LowerBound);
                return tableMoveScore;
            }
            alpha = Math.Max(alpha, tableMoveScore);
        }

        ref MoveList moves = ref moveLists[ply];
        moves.Clear();
        position.GenerateLegalMoves(ref moves);
        if (moves.Count == 0)
        {
            return inCheck ? -MateScore + ply : DrawScore;
        }
        if (fiftyMoveRule)
        {
            return DrawScore;   // In check but not mated: the fifty-move rule stands
        }
        OrderMoves(ref moves, ply, tableMove);

        foreach (Move move in moves)
        {
            if (move == tableMove)
            {
                continue;       // Already searched, before anything was generated
            }
            moveNumber++;
            // Late move pruning: at low depth in a non-PV node, quiet moves ordered this late are
            // so unlikely to beat the moves already searched that they are skipped altogether
            if (depth <= LateMovePruningMaxDepth && !pvNode && !inCheck && moveNumber > LateMovePruningBase + (depth * depth) && IsReducible(move, ply))
            {
                statistics.PrunedMoves++;
                continue;
            }
            position.MakeMove(move);
            table.Prefetch(position.Key);
            // Late move reductions: quiet moves ordered this far down are searched shallower first
            int reduction = depth >= LmrMinimumDepth && moveNumber > LmrFullDepthMoves && !inCheck && IsReducible(move, ply) && !position.IsInCheck
                ? (moveNumber > LmrFullDepthMoves * 3 ? 2 : 1)
                : 0;
            int score = SearchChild(alpha, beta, depth - 1, ply + 1, first: moveNumber == 1, reduction);
            position.UnmakeMove();
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
                RecordCutoff(move, ply, depth);
                table.Store(position.Key, move, ScoreToTable(score, ply), depth, NodeType.LowerBound);
                return score;
            }
            alpha = Math.Max(alpha, score);
        }

        NodeType type = bestScore <= originalAlpha ? NodeType.UpperBound : NodeType.Exact;
        table.Store(position.Key, bestMove, ScoreToTable(bestScore, ply), depth, type);
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

        bool inCheck = position.IsInCheck;
        bool fiftyMoveRule = position.HalfMoveClock >= 100;
        if (!inCheck && (fiftyMoveRule || position.RepetitionCount() >= 1 || position.IsInsufficientMaterial()))
        {
            return DrawScore;
        }

        // Entries from the main search (and from earlier quiescence searches) settle many nodes here
        if (TryTableCutoff(alpha, beta, 0, ply, out Move tableMove, out int tableScore))
        {
            return tableScore;
        }

        int originalAlpha = alpha;
        int bestScore = -Infinity;
        Move bestMove = Move.None;
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

        ref MoveList moves = ref moveLists[ply];
        moves.Clear();
        if (inCheck)
        {
            position.GenerateLegalMoves(ref moves);
            if (moves.Count == 0)
            {
                return -MateScore + ply;
            }
            if (fiftyMoveRule)
            {
                return DrawScore;   // In check but not mated
            }
        }
        else
        {
            position.GenerateLegalCaptures(ref moves);
        }
        OrderMoves(ref moves, ply, tableMove);

        foreach (Move move in moves)
        {
            // A capture that the exchange evaluation says loses material is not worth searching
            if (!inCheck && move.IsCapture && !move.IsPromotion && StaticExchange.Evaluate(position, move) < 0)
            {
                statistics.PrunedMoves++;
                continue;
            }
            position.MakeMove(move);
            table.Prefetch(position.Key);
            int score = -Quiescence(-beta, -alpha, ply + 1);
            position.UnmakeMove();
            if (stopped)
            {
                return alpha;
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
            if (score >= beta)
            {
                statistics.BetaCutoffs++;
                table.Store(position.Key, move, ScoreToTable(score, ply), 0, NodeType.LowerBound);
                return score;
            }
            alpha = Math.Max(alpha, score);
        }

        table.Store(position.Key, bestMove, ScoreToTable(bestScore, ply), 0, bestScore <= originalAlpha ? NodeType.UpperBound : NodeType.Exact);
        return bestScore;
    }

    /// <summary>
    /// Probe the transposition table for this position. The stored move comes back for ordering
    /// whether or not the entry is deep enough to settle the node.
    /// </summary>
    private bool TryTableCutoff(int alpha, int beta, int depth, int ply, out Move tableMove, out int score)
    {
        tableMove = Move.None;
        score = 0;
        if (!table.TryGet(position.Key, out TranspositionEntry entry))
        {
            return false;
        }
        tableMove = entry.BestMove;
        if (entry.Depth < depth)
        {
            return false;
        }
        int tableScore = ScoreFromTable(entry.Score, ply);
        bool cutoff = entry.Type switch
        {
            NodeType.Exact => true,
            NodeType.LowerBound => tableScore >= beta,
            NodeType.UpperBound => tableScore <= alpha,
            _ => false,
        };
        score = tableScore;
        if (cutoff)
        {
            statistics.TranspositionCutoffs++;
        }
        return cutoff;
    }

    private int Evaluate() => evaluator.Evaluate(position);

    /// <summary>
    /// Forget what was learnt in the previous game: the evaluation cache.
    /// </summary>
    public void NewGame()
    {
        evaluator.Clear();
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
        if (statistics.Nodes % NodesBetweenTimeChecks == 0
            && (cancellation.IsCancellationRequested || (timeBudget is not null && stopwatch.Elapsed >= timeBudget.Value)))
        {
            stopped = true;
        }
    }

    private Move GetTableMove()
    {
        return table.TryGet(position.Key, out TranspositionEntry entry) ? entry.BestMove : Move.None;
    }

    /// <summary>
    /// Order moves best-first to maximise cutoffs: the table move, then captures by most valuable
    /// victim / least valuable attacker (promotions count as capturing the promoted piece), then the
    /// killer moves for this ply, then quiet moves by history. The sort is stable so that the search
    /// is deterministic.
    /// </summary>
    private void OrderMoves(ref MoveList list, int ply, Move tableMove)
    {
        Span<Move> moves = list.AsSpan();
        int count = moves.Length;
        if (count < 2)
        {
            return;
        }
        Move previous = position.LastMove is { IsNone: false } last ? last : Move.None;
        Move counter = previous.IsNone ? Move.None : counterMoves[CounterIndex(previous)];
        counterAtPly[ply] = counter;
        Span<int> scores = stackalloc int[count];
        for (int i = 0; i < count; i++)
        {
            scores[i] = ScoreMove(moves[i], ply, tableMove, counter, previous);
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

    private int ScoreMove(Move move, int ply, Move tableMove, Move counter, Move previous)
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
        if (score != 0)
        {
            return score;
        }
        if (move == killers[2 * ply])
        {
            return 90_000;
        }
        if (move == killers[(2 * ply) + 1])
        {
            return 80_000;
        }
        if (move == counter)
        {
            return 70_000;
        }
        int quiet = history[HistoryIndex(position.SideToMove, move)];
        if (!previous.IsNone)
        {
            quiet += continuationHistory[ContinuationIndex(previous, move)];
        }
        return Math.Min(quiet, HistoryCap);
    }

    /// <summary>
    /// Quiet, non-killer moves may be searched with a reduced depth when ordered late.
    /// </summary>
    private bool IsReducible(Move move, int ply)
    {
        return !move.IsCapture && !move.IsPromotion && move != killers[2 * ply] && move != killers[(2 * ply) + 1] && move != counterAtPly[ply];
    }

    /// <summary>
    /// Quiet moves that cut off deep in the tree are likely to be good elsewhere too; order them by
    /// the depth-weighted count of cutoffs they have produced in this search.
    /// </summary>
    private void RecordHistory(Move move, int depth)
    {
        ref int entry = ref history[HistoryIndex(position.SideToMove, move)];
        entry = Math.Min(entry + (depth * depth), HistoryCap);
    }

    /// <summary>
    /// The bookkeeping of a fail-high: count it, and remember a quiet cutoff move in the killer,
    /// history, countermove and continuation tables.
    /// </summary>
    private void RecordCutoff(Move move, int ply, int depth)
    {
        statistics.BetaCutoffs++;
        if (move.IsCapture)
        {
            return;
        }
        RecordKiller(ply, move);
        RecordHistory(move, depth);
        if (position.LastMove is { IsNone: false } previous)
        {
            counterMoves[CounterIndex(previous)] = move;
            ref int continuation = ref continuationHistory[ContinuationIndex(previous, move)];
            continuation = Math.Min(continuation + (depth * depth), HistoryCap);
        }
    }

    private void RecordKiller(int ply, Move move)
    {
        if (killers[2 * ply] != move)
        {
            killers[(2 * ply) + 1] = killers[2 * ply];
            killers[2 * ply] = move;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HistoryIndex(Color color, Move move) => ((((int)color * Square.Count) + move.From) * Square.Count) + move.To;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CounterIndex(Move previous) => ((int)previous.Piece * Square.Count) + previous.To;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ContinuationIndex(Move previous, Move move) =>
        (((((int)previous.Piece * Square.Count) + previous.To) * 7 + (int)move.Piece) * Square.Count) + move.To;

    private static int PieceValue(Piece type) => type switch
    {
        Piece.Pawn => 1,
        Piece.Knight => 3,
        Piece.Bishop => 3,
        Piece.Rook => 5,
        Piece.Queen => 9,
        Piece.King => 20,
        _ => 0,
    };

    /// <summary>
    /// Mate scores are stored relative to the node rather than the root, so that an entry found from
    /// a different root still gives the right distance to mate.
    /// </summary>
    internal static int ScoreToTable(int score, int ply) => score switch
    {
        >= MateThreshold => score + ply,
        <= -MateThreshold => score - ply,
        _ => score,
    };

    internal static int ScoreFromTable(int score, int ply) => score switch
    {
        >= MateThreshold => score - ply,
        <= -MateThreshold => score + ply,
        _ => score,
    };

    /// <summary>
    /// Follow the table's best moves from the root to reconstruct the expected line of play.
    /// </summary>
    private List<Move> ExtractPrincipalVariation(Move bestMove, int maxLength)
    {
        var line = new List<Move> { bestMove };
        position.MakeMove(bestMove);
        while (line.Count < maxLength && position.RepetitionCount() == 0
            && table.TryGet(position.Key, out TranspositionEntry entry) && !entry.BestMove.IsNone)
        {
            Move move = entry.BestMove;
            if (!position.GenerateLegalMoves().Contains(move))
            {
                break;
            }
            line.Add(move);
            position.MakeMove(move);
        }
        for (int i = 0; i < line.Count; i++)
        {
            position.UnmakeMove();
        }
        return line;
    }
}
