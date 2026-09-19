namespace StoopidChess.Core;

/// <summary>
/// Assembles a position piece by piece, for position editors and tests. A <see cref="Board"/> can
/// only be changed by making and unmaking moves; anything else goes through a builder, which
/// produces a fresh board once the position is legal. Castling rights are kept only where the king
/// and rook still stand on their home squares. A piece is a (colour, type) pair; the default pair
/// is an empty square.
/// </summary>
public sealed class PositionBuilder
{
    private readonly (Colour Colour, PieceType Type)[] squares = new (Colour, PieceType)[Square.Count];
    private Colour sideToMove = Colour.White;

    /// <summary>
    /// An empty board, white to move, no castling rights.
    /// </summary>
    public PositionBuilder()
    {
    }

    /// <summary>
    /// A builder holding the pieces, side to move and castling rights of an existing position.
    /// </summary>
    public static PositionBuilder From(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var builder = new PositionBuilder { sideToMove = board.SideToMove, CastlingRights = board.CastlingRights };
        foreach (Square square in Square.All)
        {
            builder.squares[square] = board[square];
        }
        return builder;
    }

    public (Colour Colour, PieceType Type) this[Square square]
    {
        get
        {
            RequireOnBoard(square);
            return squares[square];
        }
        set
        {
            RequireOnBoard(square);
            bool valid = value.Type == PieceType.None
                ? value.Colour == Colour.None
                : value.Colour is Colour.White or Colour.Black && value.Type is >= PieceType.Pawn and <= PieceType.King;
            if (!valid)
            {
                throw new ArgumentException($"'{value.Colour} {value.Type}' is not a valid piece", nameof(value));
            }
            squares[square] = value;
        }
    }

    /// <summary>
    /// Empty a square.
    /// </summary>
    public void Clear(Square square) => this[square] = default;

    public Colour SideToMove
    {
        get => sideToMove;
        set => sideToMove = value is Colour.White or Colour.Black ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>
    /// Requested castling rights; <see cref="Build"/> drops any whose king or rook has moved.
    /// </summary>
    public CastlingRights CastlingRights { get; set; }

    /// <summary>
    /// Can play proceed from this position? See <see cref="MoveGenerator.IsPositionLegal"/>.
    /// </summary>
    public bool IsLegal => TryBuild() is not null;

    /// <summary>
    /// Build the board, or return null if the position is not legal.
    /// </summary>
    public Board? TryBuild()
    {
        Board candidate = Assemble();
        return MoveGenerator.IsPositionLegal(candidate) ? candidate : null;
    }

    /// <summary>
    /// Build the board.
    /// </summary>
    /// <exception cref="InvalidOperationException">The position is not legal.</exception>
    public Board Build()
    {
        return TryBuild() ?? throw new InvalidOperationException("the position is not legal: each side needs exactly one king, no pawns on the back ranks, and the side not to move may not be in check");
    }

    private Board Assemble()
    {
        var board = new Board();
        foreach (Square square in Square.All)
        {
            (Colour colour, PieceType type) = squares[square];
            if (type != PieceType.None)
            {
                board.PutPiece(square, colour, type);
            }
        }
        board.SwitchSideToMove(sideToMove);
        board.SetCastlingRights(EffectiveCastlingRights());
        return board;
    }

    // Castling rights survive only where the king or rook still stands on its home square
    private static readonly (Square Square, Colour Colour, PieceType Type)[] CastlingHomes =
    [
        (Square.E1, Colour.White, PieceType.King), (Square.H1, Colour.White, PieceType.Rook), (Square.A1, Colour.White, PieceType.Rook),
        (Square.E8, Colour.Black, PieceType.King), (Square.H8, Colour.Black, PieceType.Rook), (Square.A8, Colour.Black, PieceType.Rook),
    ];

    private CastlingRights EffectiveCastlingRights()
    {
        CastlingRights rights = CastlingRights;
        foreach ((Square square, Colour colour, PieceType type) in CastlingHomes)
        {
            if (squares[square] != (colour, type))
            {
                rights &= ~CastlingRightsExtensions.LostBySquare(square);
            }
        }
        return rights;
    }

    private static void RequireOnBoard(Square square)
    {
        if (!square.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(square));
        }
    }
}
