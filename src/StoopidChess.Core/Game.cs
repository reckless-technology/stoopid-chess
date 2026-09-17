namespace StoopidChess.Core;

public enum GameStatus
{
    InProgress,
    Checkmate,
    Stalemate,
    DrawByRepetition,
    DrawByFiftyMoveRule,
    DrawByInsufficientMaterial,
}

/// <summary>
/// A game of chess: a starting position, the moves played from it, and the rules that decide when it
/// is over. Only legal moves can be made.
/// </summary>
public sealed class Game
{
    private readonly List<Move> moves = [];
    private readonly Colour startSideToMove;
    private List<Move>? legalMoves;
    private (ulong Key, int Ply, int EditCount) legalMovesFor;

    /// <summary>
    /// A game from the standard starting position.
    /// </summary>
    public Game()
        : this(Board.StartPosition())
    {
    }

    /// <summary>
    /// A game starting from the given position, which must be legal (see
    /// <see cref="MoveGenerator.IsPositionLegal"/>) and must not have any moves made on it yet;
    /// use <see cref="MakeMove"/> to replay moves. The game owns the board from here on: mutate it
    /// only through the game, or the move history will not match.
    /// </summary>
    public Game(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (!MoveGenerator.IsPositionLegal(board))
        {
            throw new ArgumentException("the position is not legal: each side needs one king and the side not to move may not be in check", nameof(board));
        }
        if (board.Ply != 0)
        {
            throw new ArgumentException("the board already has moves made on it; a game starts from a fresh position", nameof(board));
        }
        Board = board;
        StartFen = board.ToFen();
        startSideToMove = board.SideToMove;
    }

    public static Game FromFen(string fen) => new(Board.FromFen(fen));

    public Board Board { get; }

    /// <summary>
    /// FEN of the position the game started from.
    /// </summary>
    public string StartFen { get; }

    public IReadOnlyList<Move> Moves => moves;

    /// <summary>
    /// The legal moves in the current position. Cached, and recomputed if the board turns out to
    /// have been changed behind the game's back.
    /// </summary>
    public IReadOnlyList<Move> LegalMoves
    {
        get
        {
            (ulong, int, int) current = (Board.Key, Board.Ply, Board.EditCount);
            if (legalMoves is null || legalMovesFor != current)
            {
                legalMoves = MoveGenerator.GenerateLegalMoves(Board);
                legalMovesFor = current;
            }
            return legalMoves;
        }
    }

    public Colour SideToMove => Board.SideToMove;

    public bool IsInCheck => MoveGenerator.IsInCheck(Board);

    public GameStatus Status
    {
        get
        {
            if (LegalMoves.Count == 0)
            {
                return IsInCheck ? GameStatus.Checkmate : GameStatus.Stalemate;
            }
            if (Board.HalfMoveClock >= 100)
            {
                return GameStatus.DrawByFiftyMoveRule;
            }
            if (Board.RepetitionCount() >= 2)
            {
                return GameStatus.DrawByRepetition;
            }
            if (Board.IsInsufficientMaterial())
            {
                return GameStatus.DrawByInsufficientMaterial;
            }
            return GameStatus.InProgress;
        }
    }

    public bool IsOver => Status != GameStatus.InProgress;

    /// <summary>
    /// The moves in UCI notation, e.g. for a "position fen ... moves ..." command.
    /// </summary>
    public IEnumerable<string> UciMoves => moves.Select(move => move.ToUci());

    /// <summary>
    /// The legal moves from one square to another. There is more than one only for a promotion.
    /// </summary>
    public IEnumerable<Move> LegalMovesBetween(int from, int to) => LegalMoves.Where(move => move.From == from && move.To == to);

    /// <summary>
    /// Find the legal move between two squares, promoting to the given piece if it is a promotion.
    /// </summary>
    public Move? FindMove(int from, int to, PieceType promotion = PieceType.None)
    {
        foreach (Move move in LegalMoves)
        {
            if (move.From == from && move.To == to && move.Promotion == promotion)
            {
                return move;
            }
        }
        return null;
    }

    /// <summary>
    /// Find the legal move written in UCI notation ("e2e4", "e7e8q").
    /// </summary>
    public Move? FindMove(string uci)
    {
        return MoveNotation.TryParseUci(uci, out int from, out int to, out PieceType promotion) ? FindMove(from, to, promotion) : null;
    }

    /// <summary>
    /// Make a legal move.
    /// </summary>
    /// <exception cref="ArgumentException">The move is not legal in the current position.</exception>
    public void MakeMove(Move move)
    {
        if (!LegalMoves.Contains(move))
        {
            throw new ArgumentException($"{move} is not a legal move in {Board.ToFen()}", nameof(move));
        }
        Board.MakeMove(move);
        moves.Add(move);
        legalMoves = null;
    }

    /// <summary>
    /// Make the move written in UCI notation, if it is legal.
    /// </summary>
    public bool TryMakeMove(string uci)
    {
        Move? move = FindMove(uci);
        if (move is null)
        {
            return false;
        }
        MakeMove(move.Value);
        return true;
    }

    /// <summary>
    /// Take back the last move. Returns false if there is none.
    /// </summary>
    public bool UndoMove()
    {
        if (moves.Count == 0)
        {
            return false;
        }
        Board.UnmakeMove();
        moves.RemoveAt(moves.Count - 1);
        legalMoves = null;
        return true;
    }

    /// <summary>
    /// The pieces of the given colour that have been captured during the game, in order of capture.
    /// </summary>
    public IReadOnlyList<Piece> CapturedPieces(Colour colour)
    {
        var captured = new List<Piece>();
        Colour mover = startSideToMove;
        foreach (Move move in moves)
        {
            if (move.IsCapture && mover.Opponent() == colour)
            {
                captured.Add(new Piece(colour, move.Captured));
            }
            mover = mover.Opponent();
        }
        return captured;
    }
}
