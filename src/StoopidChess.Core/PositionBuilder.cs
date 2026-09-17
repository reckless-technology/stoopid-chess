namespace StoopidChess.Core;

/// <summary>
/// Assembles a position piece by piece, for position editors and tests. A <see cref="Board"/> can
/// only be changed by making and unmaking moves; anything else goes through a builder, which
/// produces a fresh board once the position is legal. Castling rights are kept only where the king
/// and rook still stand on their home squares.
/// </summary>
public sealed class PositionBuilder
{
    private readonly Piece[] squares = new Piece[128];
    private Colour sideToMove = Colour.White;

    /// <summary>
    /// An empty board, white to move, no castling rights.
    /// </summary>
    public PositionBuilder()
    {
        Array.Fill(squares, Piece.None);
    }

    /// <summary>
    /// A builder holding the pieces, side to move and castling rights of an existing position.
    /// </summary>
    public static PositionBuilder From(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var builder = new PositionBuilder { sideToMove = board.SideToMove, CastlingRights = board.CastlingRights };
        foreach (int square in Squares.All)
        {
            builder.squares[square] = board[square];
        }
        return builder;
    }

    public Piece this[int square]
    {
        get
        {
            RequireOnBoard(square);
            return squares[square];
        }
        set
        {
            RequireOnBoard(square);
            bool valid = value.IsNone
                ? value.Colour == Colour.None
                : value.Colour is Colour.White or Colour.Black && value.Type is >= PieceType.Pawn and <= PieceType.King;
            if (!valid)
            {
                throw new ArgumentException($"'{value.Colour} {value.Type}' is not a valid piece", nameof(value));
            }
            squares[square] = value;
        }
    }

    public Colour SideToMove
    {
        get => sideToMove;
        set => sideToMove = value is Colour.White or Colour.Black ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Requested castling rights; <see cref="Build"/> drops any whose king or rook has moved.
    /// </summary>
    public CastlingRights CastlingRights { get; set; }

    public int Count(Colour colour, PieceType type)
    {
        int count = 0;
        foreach (int square in Squares.All)
        {
            if (squares[square] == new Piece(colour, type))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Can play proceed from this position? See <see cref="MoveGenerator.IsPositionLegal"/>.
    /// </summary>
    public bool IsLegal => TryBuild(out _);

    /// <summary>
    /// Build the board, or return false if the position is not legal.
    /// </summary>
    public bool TryBuild(out Board board)
    {
        Board candidate = Assemble();
        if (MoveGenerator.IsPositionLegal(candidate))
        {
            board = candidate;
            return true;
        }
        board = null!;
        return false;
    }

    /// <summary>
    /// Build the board.
    /// </summary>
    /// <exception cref="InvalidOperationException">The position is not legal.</exception>
    public Board Build()
    {
        return TryBuild(out Board board) ? board : throw new InvalidOperationException("the position is not legal: each side needs exactly one king, no pawns on the back ranks, and the side not to move may not be in check");
    }

    private Board Assemble()
    {
        var board = new Board();
        foreach (int square in Squares.All)
        {
            if (!squares[square].IsNone)
            {
                board.PutPiece(square, squares[square]);
            }
        }
        board.SwitchSideToMove(sideToMove);
        board.SetCastlingRights(EffectiveCastlingRights());
        return board;
    }

    private CastlingRights EffectiveCastlingRights()
    {
        CastlingRights rights = CastlingRights;
        if (squares[Squares.E1] != Piece.WhiteKing)
        {
            rights &= ~CastlingRights.White;
        }
        if (squares[Squares.H1] != Piece.WhiteRook)
        {
            rights &= ~CastlingRights.WhiteKingSide;
        }
        if (squares[Squares.A1] != Piece.WhiteRook)
        {
            rights &= ~CastlingRights.WhiteQueenSide;
        }
        if (squares[Squares.E8] != Piece.BlackKing)
        {
            rights &= ~CastlingRights.Black;
        }
        if (squares[Squares.H8] != Piece.BlackRook)
        {
            rights &= ~CastlingRights.BlackKingSide;
        }
        if (squares[Squares.A8] != Piece.BlackRook)
        {
            rights &= ~CastlingRights.BlackQueenSide;
        }
        return rights;
    }

    private static void RequireOnBoard(int square)
    {
        if (!Squares.IsOnBoard(square))
        {
            throw new ArgumentOutOfRangeException(nameof(square));
        }
    }
}
