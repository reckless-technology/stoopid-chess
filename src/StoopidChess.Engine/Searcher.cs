using System.Diagnostics;
using System.Runtime.CompilerServices;
using StoopidChess.Core;

namespace StoopidChess.Engine;

/// <summary>
/// Finds the best move: an alpha-beta search of the game tree, deepened one ply at a time, with a
/// quiescence search at the leaves. Searches are deterministic for a given position, table size
/// and depth, so the bench node count acts as a signature of the search. A Searcher is not thread
/// safe; use one per concurrent search. docs/SEARCH.md records what each technique measured.
/// </summary>
/// <remarks>
/// The techniques, numbered as the comments in the code below number them.
///
/// Around the tree (<see cref="Search"/>):
///  A. Iterative deepening: search to depth 1, then 2, and so on, each iteration ordering the
///     next one's moves through the transposition table and leaving a best move should time run out.
///     https://www.chessprogramming.org/Iterative_Deepening
///  B. Aspiration windows: from depth 4, search in a narrow window around the last score and widen
///     it only if the score falls outside. https://www.chessprogramming.org/Aspiration_Windows
///  C. Time management: a budget from the clock (<see cref="SearchLimits.TimeAllocation"/>), half
///     as much again when the score drops or the best move changes, and no new iteration once half
///     the budget is spent. https://www.chessprogramming.org/Time_Management
///
/// In every node of the main search (<see cref="AlphaBeta"/>), in the order it applies them:
///  1. Draws by repetition and insufficient material. https://www.chessprogramming.org/Repetitions
///  2. Mate distance pruning: no mate found here can beat one already found nearer the root.
///     https://www.chessprogramming.org/Mate_Distance_Pruning
///  3. The fifty-move rule. https://www.chessprogramming.org/Fifty-move_Rule
///  4. At the horizon, the quiescence search (Q below) takes over, except in check.
///     https://www.chessprogramming.org/Quiescence_Search
///  5. Transposition table cutoff: a stored result for this position, deep enough and with a
///     bound that settles the window, is returned at once.
///     https://www.chessprogramming.org/Transposition_Table
///  6. Internal iterative reduction: a deep node with no table move is searched a ply shallower.
///     https://www.chessprogramming.org/Internal_Iterative_Reductions
///  7. Reverse futility pruning: far enough above beta at low depth, fail high at once.
///     https://www.chessprogramming.org/Reverse_Futility_Pruning
///  8. Razoring: far enough below alpha at low depth, trust the quiescence search.
///     https://www.chessprogramming.org/Razoring
///  9. Null-move pruning: if passing still beats beta, so would a real move.
///     https://www.chessprogramming.org/Null_Move_Pruning
/// 10. The table move first, before any move is generated.
///     https://www.chessprogramming.org/Transposition_Table
/// 11. Move ordering: captures by most valuable victim, least valuable attacker; then the two
///     killer moves of the ply, the countermove, and the quiet moves by their history.
///     https://www.chessprogramming.org/Move_Ordering, .../MVV-LVA, .../Killer_Heuristic,
///     .../Countermove_Heuristic, .../History_Heuristic
/// 12. Late move pruning: quiet moves ordered late at low depth are not searched at all.
///     https://www.chessprogramming.org/Futility_Pruning#MoveCountBasedPruning
/// 13. Late move reductions: quiet moves ordered late are searched shallower first.
///     https://www.chessprogramming.org/Late_Move_Reductions
/// 14. Principal variation search: the first move gets the full window, the rest a null window,
///     re-searched only if they beat it (<see cref="SearchChild"/>).
///     https://www.chessprogramming.org/Principal_Variation_Search
/// 15. A cutoff teaches the killer, history, countermove and continuation history tables.
/// 16. The result is stored in the transposition table, as an exact score or a bound.
///
/// At the leaves (<see cref="Quiescence"/>):
///  Q. Only captures and promotions (every move when in check), until the position is quiet. The
///     side to move may "stand pat" on the static evaluation instead of capturing, and captures that
///     lose material by the static exchange evaluation are skipped.
///     https://www.chessprogramming.org/Quiescence_Search, .../Static_Exchange_Evaluation
/// </remarks>
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
    private const int ScoreDropMargin = 30; // A fall in the root score by this much earns the move more time
    private const int ScoreDropMinimumDepth = 4;
    private const int MoveChangeMinimumDepth = 6; // A new best move from this depth on earns more time too
    private const int InternalIterativeReductionMinimumDepth = 4;
    private const int ReverseFutilityMaxDepth = 7;
    private const int ReverseFutilityMargin = 60; // Per ply of remaining depth
    private const int RazoringMaxDepth = 3;
    private const int RazoringMargin = 250; // Per ply of remaining depth
    private const int LateMovePruningMaxDepth = 4;
    private const int LateMovePruningBase = 5; // Quiet moves after the (base + depth^2)th are skipped
    private const int LmrMinimumDepth = 3;
    private const int LmrFullDepthMoves = 3;
    private const int AspirationMinimumDepth = 4;
    private const int AspirationWindow = 20;
    private const int HistoryCap = 69_999; // Quiet history orders below the killers and the countermove (70,000)

    private readonly TranspositionTable _table;
    private readonly Evaluator _evaluator = new();
    private readonly MoveList[] _moveLists = new MoveList[MaxPly + 1]; // One per ply, reused by every node at that ply

    // The history tables are flat arrays with hand-rolled indexing (HistoryIndex and the others
    // below): multi-dimensional array accesses go through a runtime helper on some shapes, which
    // showed in the profile.
    private readonly Killers[] _killers = new Killers[MaxPly + 1]; // The two most recent quiet cutoff moves at each ply
    private readonly Move[] _counterAtPly = new Move[MaxPly + 1]; // The countermove ordered at each ply, exempt from reductions like the killers
    private readonly Move[] _counterMoves = new Move[7 * Square.Count]; // [piece, to] of the opponent's last move: the quiet reply that last refuted it
    private readonly int[] _continuationHistory = new int[7 * Square.Count * 7 * Square.Count]; // [previous piece, previous to, piece, to]: cutoffs by a quiet move after that reply
    private readonly int[] _history = new int[2 * Square.Count * Square.Count]; // [color, from, to]: how often a quiet move caused a cutoff
    private readonly Stopwatch _stopwatch = new();

    private Position _position = Position.Start();
    private long _nodes; // Searched in this search: main search and quiescence nodes alike
    private CancellationToken _cancellation;
    private TimeSpan? _timeBudget;
    private TimeSpan? _timeCap;
    private long? _nodeLimit;
    private bool _isStopped;

    public Searcher(TranspositionTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        this._table = table;
    }

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
    /// <param name="root">The position to search, including its move history for repetition detection.</param>
    /// <param name="limits">When to stop.</param>
    /// <param name="onProgress">Called after each completed depth.</param>
    /// <param name="cancellationToken">Stops the search early (the UCI "stop" command); the best move so far is returned.</param>
    public SearchResult Search(
        Position root,
        SearchLimits limits,
        Action<SearchResult>? onProgress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(limits);

        _position = root.Clone();
        _nodes = 0;
        _cancellation = cancellationToken;
        (TimeSpan Budget, TimeSpan Cap)? allocation = limits.TimeAllocation(_position.SideToMove);
        _timeBudget = allocation?.Budget;
        _timeCap = allocation?.Cap;
        bool isExtended = false;
        _nodeLimit = limits.IsInfinite ? null : limits.Nodes;
        _isStopped = false;
        Array.Clear(_killers);
        Array.Clear(_counterMoves);
        Array.Clear(_history);
        Array.Clear(_continuationHistory);
        _stopwatch.Restart();

        MoveList rootMoves = _position.GenerateLegalMoves();
        if (rootMoves.Count == 0)
        {
            return new SearchResult(0, _position.IsInCheck ? -MateScore : DrawScore, 0, _stopwatch.Elapsed, []);
        }

        int maxDepth = limits.IsInfinite ? MaxPly : Math.Clamp(limits.Depth ?? MaxPly, 1, MaxPly);
        Move bestMove = rootMoves[0];
        int bestScore = 0;
        int completedDepth = 0;
        List<Move> principalVariation = [bestMove];

        // A. Iterative deepening
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            (int score, Move move) = SearchWithAspiration(ref rootMoves, depth, bestScore, completedDepth);
            if (_isStopped)
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
            if (
                _timeBudget is not null
                && _timeCap is not null
                && !isExtended
                && (
                    (completedDepth >= ScoreDropMinimumDepth && score <= bestScore - ScoreDropMargin)
                    || (completedDepth >= MoveChangeMinimumDepth && move != bestMove)
                )
            )
            {
                // C. Time management: the score fell or the best move changed, so give this move half
                // as much time again, within the clock, to sort it out
                isExtended = true;
                TimeSpan longer = _timeBudget.Value * 1.5;
                _timeBudget = longer < _timeCap.Value ? longer : _timeCap.Value;
            }
            bestMove = move;
            bestScore = score;
            completedDepth = depth;
            principalVariation = ExtractPrincipalVariation(bestMove, depth);
            onProgress?.Invoke(new SearchResult(depth, score, _nodes, _stopwatch.Elapsed, principalVariation));

            if (
                IsMateScore(score)
                && MateScore - Math.Abs(score) <= depth
                && !limits.IsInfinite
                && limits.Depth is null
            )
            {
                break; // A forced mate within the searched depth is proven; deeper search cannot improve on it
            }
            if (_timeBudget is not null && _stopwatch.Elapsed > _timeBudget.Value / 2)
            {
                break; // The next iteration would take longer than the time left
            }
        }

        _stopwatch.Stop();
        return new SearchResult(completedDepth, bestScore, _nodes, _stopwatch.Elapsed, principalVariation);
    }

    /// <summary>
    /// B. Aspiration windows: once a few iterations have settled the score, search the next one in a
    /// narrow window around it, which cuts off far more, and widen and repeat if the score escapes.
    /// </summary>
    private (int Score, Move BestMove) SearchWithAspiration(
        ref MoveList rootMoves,
        int depth,
        int previousScore,
        int completedDepth
    )
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
            if (_isStopped || (score > alpha && score < beta))
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
        bool isFirstMove = true;
        foreach (Move move in rootMoves)
        {
            _position.MakeMove(move);
            _table.Prefetch(_position.Key);
            int score = SearchChild(alpha, beta, depth - 1, 1, isFirstMove);
            _position.UnmakeMove();
            isFirstMove = false;
            if (_isStopped)
            {
                return (alpha, bestMove);
            }
            if (score > alpha)
            {
                alpha = score;
                bestMove = move;
                if (alpha >= beta)
                {
                    break; // Fail high: the caller widens the window and searches again
                }
            }
        }
        NodeType type =
            alpha >= beta ? NodeType.LowerBound
            : alpha <= originalAlpha ? NodeType.UpperBound
            : NodeType.Exact;
        _table.Store(_position.Key, bestMove, ScoreToTable(alpha, 0), depth, type);
        return (alpha, bestMove);
    }

    /// <summary>
    /// 14. Principal variation search of a child: the first move is expected to be best and gets the
    /// full window; the rest only need to prove they are not better (a null window) and are
    /// re-searched with the full window only when they are.
    /// </summary>
    private int SearchChild(int alpha, int beta, int depth, int ply, bool isFirstMove, int reduction = 0)
    {
        if (isFirstMove)
        {
            return -AlphaBeta(-beta, -alpha, depth, ply);
        }
        int score = -AlphaBeta(-alpha - 1, -alpha, depth - reduction, ply);
        if (reduction > 0 && score > alpha && !_isStopped)
        {
            score = -AlphaBeta(-alpha - 1, -alpha, depth, ply);
        }
        if (score > alpha && score < beta && !_isStopped)
        {
            score = -AlphaBeta(-beta, -alpha, depth, ply);
        }
        return score;
    }

    private int AlphaBeta(int alpha, int beta, int depth, int ply)
    {
        Debug.Assert(alpha < beta, "alpha must be below beta");
        _nodes++;
        CheckLimits();
        if (_isStopped)
        {
            return alpha;
        }

        // 1. Draws by repetition (once is enough inside the search: a line that repeats can repeat
        // again) and by insufficient material
        if (_position.RepetitionCount() >= 1 || _position.IsInsufficientMaterial())
        {
            return DrawScore;
        }
        if (ply >= MaxPly)
        {
            return Evaluate();
        }

        // 2. Mate distance pruning: a mate found here can be no shorter than ply
        alpha = Math.Max(alpha, -MateScore + ply);
        beta = Math.Min(beta, MateScore - ply - 1);
        if (alpha >= beta)
        {
            return alpha;
        }

        bool isInCheck = _position.IsInCheck;
        // 3. The fifty-move rule: a draw, unless the move that reached the hundredth half-move mated
        if (_position.HalfMoveClock >= 100 && (!isInCheck || HasLegalMove(ply)))
        {
            return DrawScore;
        }
        // 4. The horizon
        if (depth <= 0 && !isInCheck)
        {
            return Quiescence(alpha, beta, ply);
        }
        if (depth <= 0)
        {
            depth = 1; // In check at the horizon: search every reply rather than stand pat in quiescence
        }

        // 5. Transposition table cutoff
        if (TryTableCutoff(alpha, beta, depth, ply, out Move tableMove, out int tableScore))
        {
            return tableScore;
        }

        // 6. Internal iterative reduction: with no table move to order first, a deep node is searched a ply
        // shallower; if it matters, the next iteration comes back to it with a table move in hand
        if (depth >= InternalIterativeReductionMinimumDepth && tableMove.IsNone)
        {
            depth--;
        }

        bool isPvNode = beta - alpha > 1;
        int staticEval = isInCheck ? -Infinity : Evaluate();

        // 7. Reverse futility pruning: in a non-PV node at low depth, a static evaluation so far above
        // beta that a few plies of play cannot bring it back down is taken as a fail-high at once.
        if (
            depth <= ReverseFutilityMaxDepth
            && !isInCheck
            && !isPvNode
            && beta < MateThreshold
            && staticEval - (ReverseFutilityMargin * depth) >= beta
        )
        {
            return staticEval;
        }

        // 8. Razoring: in a non-PV node at low depth, a static evaluation so far below alpha that quiet
        // play cannot recover is checked against the quiescence search, and when the captures agree
        // that alpha is out of reach the node is cut to the quiescence score.
        if (
            depth <= RazoringMaxDepth
            && !isInCheck
            && !isPvNode
            && alpha > -MateThreshold
            && staticEval + (RazoringMargin * depth) <= alpha
        )
        {
            int razorScore = Quiescence(alpha, alpha + 1, ply);
            if (_isStopped)
            {
                return alpha;
            }
            if (razorScore <= alpha)
            {
                return razorScore;
            }
        }

        // 9. Null-move pruning: if handing the opponent a free move still leaves us at or above beta, a
        // real move will almost certainly do so too, so the node is a fail-high. Skipped in check,
        // right after another null move, in pawn endgames (zugzwang) and for mate-bound windows.
        if (
            depth >= NullMoveMinimumDepth
            && !isInCheck
            && beta < MateThreshold
            && !_position.WasLastMoveNull
            && _position.HasNonPawnMaterial(_position.SideToMove)
            && staticEval >= beta
        )
        {
            int reduction = NullMoveReduction + (depth / 4);
            _position.MakeNullMove();
            _table.Prefetch(_position.Key);
            int nullScore = -AlphaBeta(-beta, -beta + 1, depth - 1 - reduction, ply + 1);
            _position.UnmakeMove();
            if (_isStopped)
            {
                return alpha;
            }
            if (nullScore >= beta)
            {
                return nullScore >= MateThreshold ? beta : nullScore; // A mate found after a null move is not trusted
            }
        }

        int originalAlpha = alpha;
        int bestScore = -Infinity;
        Move bestMove = Move.None;
        int moveNumber = 0;

        // 10. The table move first, before paying for move generation: it alone cuts off a large share
        // of nodes. Searching it first also changes the order of everything after it, since its
        // subtree has already taught the killer and history tables by the time the rest are ordered. It is trusted without a legality check, as pawnstar trusts its table: stores
        // only ever hold moves from the storing position's legal moves, and entries are verified by
        // the full 64-bit key, so a collision is beyond unlikely.
        if (!tableMove.IsNone)
        {
            moveNumber = 1;
            _position.MakeMove(tableMove);
            _table.Prefetch(_position.Key);
            int tableMoveScore = SearchChild(alpha, beta, depth - 1, ply + 1, isFirstMove: true);
            _position.UnmakeMove();
            if (_isStopped)
            {
                return alpha;
            }
            bestScore = tableMoveScore;
            bestMove = tableMove;
            if (tableMoveScore >= beta)
            {
                RecordCutoff(tableMove, ply, depth);
                _table.Store(_position.Key, tableMove, ScoreToTable(tableMoveScore, ply), depth, NodeType.LowerBound);
                return tableMoveScore;
            }
            alpha = Math.Max(alpha, tableMoveScore);
        }

        ref MoveList moves = ref _moveLists[ply];
        moves.Clear();
        _position.GenerateLegalMoves(ref moves);
        if (moves.Count == 0)
        {
            return isInCheck ? -MateScore + ply : DrawScore;
        }
        // 11. Move ordering
        OrderMoves(ref moves, ply, tableMove);

        foreach (Move move in moves)
        {
            if (move == tableMove)
            {
                continue; // Already searched, before anything was generated
            }
            moveNumber++;
            // 12. Late move pruning: at low depth in a non-PV node, quiet moves ordered this late are
            // so unlikely to beat the moves already searched that they are skipped altogether
            if (
                depth <= LateMovePruningMaxDepth
                && !isPvNode
                && !isInCheck
                && moveNumber > LateMovePruningBase + (depth * depth)
                && IsReducible(move, ply)
            )
            {
                continue;
            }
            _position.MakeMove(move);
            _table.Prefetch(_position.Key);
            // 13. Late move reductions: quiet moves ordered this far down are searched shallower first
            int reduction =
                depth >= LmrMinimumDepth
                && moveNumber > LmrFullDepthMoves
                && !isInCheck
                && IsReducible(move, ply)
                && !_position.IsInCheck
                    ? (moveNumber > LmrFullDepthMoves * 3 ? 2 : 1)
                    : 0;
            int score = SearchChild(alpha, beta, depth - 1, ply + 1, isFirstMove: moveNumber == 1, reduction); // 14. PVS
            _position.UnmakeMove();
            if (_isStopped)
            {
                return alpha; // Do not store results from an incomplete search
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
            }
            if (score >= beta)
            {
                // 15. A cutoff: the move refutes the opponent's last move, so remember it; 16. the
                // score is a lower bound
                RecordCutoff(move, ply, depth);
                _table.Store(_position.Key, move, ScoreToTable(score, ply), depth, NodeType.LowerBound);
                return score;
            }
            alpha = Math.Max(alpha, score);
        }

        // 16. No cutoff: an exact score if some move raised alpha, otherwise only an upper bound
        NodeType type = bestScore <= originalAlpha ? NodeType.UpperBound : NodeType.Exact;
        _table.Store(_position.Key, bestMove, ScoreToTable(bestScore, ply), depth, type);
        return bestScore;
    }

    /// <summary>
    /// Q. The quiescence search: captures and promotions only, until the position is quiet, so that
    /// the static evaluation is never taken in the middle of an exchange (the "horizon effect": a
    /// fixed-depth search that stopped after QxP would score a pawn up without seeing PxQ). When in
    /// check every move is considered, so that mates are seen.
    /// </summary>
    private int Quiescence(int alpha, int beta, int ply)
    {
        _nodes++;
        CheckLimits();
        if (_isStopped)
        {
            return alpha;
        }
        if (ply >= MaxPly)
        {
            return Evaluate();
        }

        bool isInCheck = _position.IsInCheck;
        // The fifty-move rule, as in the main search
        if (_position.HalfMoveClock >= 100 && (!isInCheck || HasLegalMove(ply)))
        {
            return DrawScore;
        }
        if (!isInCheck && (_position.RepetitionCount() >= 1 || _position.IsInsufficientMaterial()))
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
        if (!isInCheck)
        {
            // Stand pat: the side to move need not capture at all
            bestScore = Evaluate();
            if (bestScore >= beta)
            {
                return bestScore;
            }
            alpha = Math.Max(alpha, bestScore);
        }

        ref MoveList moves = ref _moveLists[ply];
        moves.Clear();
        if (isInCheck)
        {
            _position.GenerateLegalMoves(ref moves);
            if (moves.Count == 0)
            {
                return -MateScore + ply;
            }
        }
        else
        {
            _position.GenerateLegalCaptures(ref moves);
        }
        OrderMoves(ref moves, ply, tableMove);

        foreach (Move move in moves)
        {
            // A capture that the exchange evaluation says loses material is not worth searching
            if (!isInCheck && move.IsCapture && !move.IsPromotion && StaticExchange.Evaluate(_position, move) < 0)
            {
                continue;
            }
            _position.MakeMove(move);
            _table.Prefetch(_position.Key);
            int score = -Quiescence(-beta, -alpha, ply + 1);
            _position.UnmakeMove();
            if (_isStopped)
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
                _table.Store(_position.Key, move, ScoreToTable(score, ply), 0, NodeType.LowerBound);
                return score;
            }
            alpha = Math.Max(alpha, score);
        }

        _table.Store(
            _position.Key,
            bestMove,
            ScoreToTable(bestScore, ply),
            0,
            bestScore <= originalAlpha ? NodeType.UpperBound : NodeType.Exact
        );
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
        if (!_table.TryGet(_position.Key, out TranspositionEntry entry))
        {
            return false;
        }
        tableMove = entry.BestMove;
        if (entry.Depth < depth)
        {
            return false;
        }
        int tableScore = ScoreFromTable(entry.Score, ply);
        bool isCutoff = entry.Type switch
        {
            NodeType.Exact => true,
            NodeType.LowerBound => tableScore >= beta,
            NodeType.UpperBound => tableScore <= alpha,
            _ => false,
        };
        score = tableScore;
        return isCutoff;
    }

    private int Evaluate() => _evaluator.Evaluate(_position);

    /// <summary>
    /// The static evaluation of a position by this searcher's evaluator, for the UCI "eval" command.
    /// </summary>
    public int Evaluate(Position position) => _evaluator.Evaluate(position);

    /// <summary>
    /// Does the side to move have a legal move? Generated into the ply's move list, which the node
    /// fills again before it uses it.
    /// </summary>
    private bool HasLegalMove(int ply)
    {
        ref MoveList moves = ref _moveLists[ply];
        moves.Clear();
        _position.GenerateLegalMoves(ref moves);
        return moves.Count > 0;
    }

    /// <summary>
    /// Forget what was learnt in the previous game: the evaluation cache.
    /// </summary>
    public void NewGame()
    {
        _evaluator.Clear();
    }

    private void CheckLimits()
    {
        if (_isStopped)
        {
            return;
        }
        if (_nodeLimit is not null && _nodes >= _nodeLimit.Value)
        {
            _isStopped = true;
            return;
        }
        if (
            _nodes % NodesBetweenTimeChecks == 0
            && (
                _cancellation.IsCancellationRequested
                || (_timeBudget is not null && _stopwatch.Elapsed >= _timeBudget.Value)
            )
        )
        {
            _isStopped = true;
        }
    }

    private Move GetTableMove()
    {
        return _table.TryGet(_position.Key, out TranspositionEntry entry) ? entry.BestMove : Move.None;
    }

    /// <summary>
    /// 11. Order moves best-first to maximise cutoffs, since alpha-beta prunes most when the best move
    /// comes first: the table move, then captures by most valuable
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
        Move previous = _position.LastMove is { IsNone: false } last ? last : Move.None;
        Move counter = previous.IsNone ? Move.None : _counterMoves[CounterIndex(previous)];
        _counterAtPly[ply] = counter;
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
        if (move == _killers[ply].First)
        {
            return 90_000;
        }
        if (move == _killers[ply].Second)
        {
            return 80_000;
        }
        if (move == counter)
        {
            return 70_000;
        }
        int quiet = _history[HistoryIndex(_position.SideToMove, move)];
        if (!previous.IsNone)
        {
            quiet += _continuationHistory[ContinuationIndex(previous, move)];
        }
        return Math.Min(quiet, HistoryCap);
    }

    /// <summary>
    /// Quiet, non-killer moves may be searched with a reduced depth when ordered late.
    /// </summary>
    private bool IsReducible(Move move, int ply)
    {
        return !move.IsCapture
            && !move.IsPromotion
            && move != _killers[ply].First
            && move != _killers[ply].Second
            && move != _counterAtPly[ply];
    }

    /// <summary>
    /// Quiet moves that cut off deep in the tree are likely to be good elsewhere too; order them by
    /// the depth-weighted count of cutoffs they have produced in this search.
    /// </summary>
    private void RecordHistory(Move move, int depth)
    {
        ref int entry = ref _history[HistoryIndex(_position.SideToMove, move)];
        entry = Math.Min(entry + (depth * depth), HistoryCap);
    }

    /// <summary>
    /// The bookkeeping of a fail-high: remember a quiet cutoff move in the killer, history,
    /// countermove and continuation tables.
    /// </summary>
    private void RecordCutoff(Move move, int ply, int depth)
    {
        if (move.IsCapture)
        {
            return;
        }
        RecordKiller(ply, move);
        RecordHistory(move, depth);
        if (_position.LastMove is { IsNone: false } previous)
        {
            _counterMoves[CounterIndex(previous)] = move;
            ref int continuation = ref _continuationHistory[ContinuationIndex(previous, move)];
            continuation = Math.Min(continuation + (depth * depth), HistoryCap);
        }
    }

    private void RecordKiller(int ply, Move move)
    {
        if (_killers[ply].First != move)
        {
            _killers[ply] = new Killers(move, _killers[ply].First);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HistoryIndex(Color color, Move move) =>
        ((((int)color * Square.Count) + move.From) * Square.Count) + move.To;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CounterIndex(Move previous) => ((int)previous.Piece * Square.Count) + previous.To;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ContinuationIndex(Move previous, Move move) =>
        (((((int)previous.Piece * Square.Count) + previous.To) * 7 + (int)move.Piece) * Square.Count) + move.To;

    private static int PieceValue(Piece type) =>
        type switch
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
    internal static int ScoreToTable(int score, int ply) =>
        score switch
        {
            >= MateThreshold => score + ply,
            <= -MateThreshold => score - ply,
            _ => score,
        };

    internal static int ScoreFromTable(int score, int ply) =>
        score switch
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
        _position.MakeMove(bestMove);
        while (
            line.Count < maxLength
            && _position.RepetitionCount() == 0
            && _table.TryGet(_position.Key, out TranspositionEntry entry)
            && !entry.BestMove.IsNone
        )
        {
            Move move = entry.BestMove;
            if (!_position.GenerateLegalMoves().Contains(move))
            {
                break;
            }
            line.Add(move);
            _position.MakeMove(move);
        }
        for (int i = 0; i < line.Count; i++)
        {
            _position.UnmakeMove();
        }
        return line;
    }

    /// <summary>
    /// The killer moves of one ply: the last quiet move to cause a cutoff there, and the one before.
    /// </summary>
    private readonly record struct Killers(Move First, Move Second);
}
