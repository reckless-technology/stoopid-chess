namespace StoopidChess.Core;

// Position, part 3 of 3: editing. Part 1 (Position.cs) holds the board and makes and unmakes
// moves; part 2 (Position.MoveGeneration.cs) generates the legal moves.
public sealed partial class Position
{
    // Position editing, for position editors and tests. A position is edited before any move has
    // been made on it (an edited position starts a fresh game); the bitboards and keys stay
    // consistent throughout, and castling rights survive only where the king and rook still stand
    // on their home squares.

    /// <summary>
    /// An empty board, white to move, no castling rights.
    /// </summary>
    public static Position Empty() => new();

    /// <summary>
    /// A fresh copy of a position's pieces, side to move and castling rights, without its move
    /// history, ready for editing.
    /// </summary>
    public static Position Setup(Position source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var position = new Position();
        foreach (Square square in Square.All)
        {
            (Color color, Piece type) = source[square];
            if (type != Piece.None)
            {
                position.PutPiece(square, color, type);
            }
        }
        position.SwitchSideToMove(source.SideToMove);
        position.ApplyCastlingRights(source.CastlingRights & position.HomeCastlingRights());
        position.UpdateCheckers();
        return position;
    }

    /// <summary>
    /// Put a piece on a square, replacing whatever stood there; a type of <see cref="Piece.None"/>
    /// (whatever the color, as in the default pair) empties the square.
    /// </summary>
    public void Place(Square square, (Color Color, Piece Type) piece)
    {
        RequireEditable();
        if (!square.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(square));
        }
        bool valid = piece.Type == Piece.None
            || (piece.Color is Color.White or Color.Black && piece.Type is >= Piece.Pawn and <= Piece.King);
        if (!valid)
        {
            throw new ArgumentException($"'{piece.Color} {piece.Type}' is not a valid piece", nameof(piece));
        }
        (Color oldColor, Piece oldType) = this[square];
        if (oldType != Piece.None)
        {
            RemovePiece(square, oldColor, oldType);
        }
        if (piece.Type != Piece.None)
        {
            PutPiece(square, piece.Color, piece.Type);
        }
        SetEnPassantSquare(Square.None);
        ApplyCastlingRights(CastlingRights & HomeCastlingRights());
        UpdateCheckers();
    }

    /// <summary>
    /// Empty a square.
    /// </summary>
    public void Clear(Square square) => Place(square, default);

    /// <summary>
    /// Give the move to a side.
    /// </summary>
    public void SetSideToMove(Color color)
    {
        RequireEditable();
        if (color is not (Color.White or Color.Black))
        {
            throw new ArgumentOutOfRangeException(nameof(color));
        }
        SwitchSideToMove(color);
        UpdateCheckers();
    }

    /// <summary>
    /// Grant castling rights; any whose king or rook is not on its home square is dropped.
    /// </summary>
    public void SetCastlingRights(CastlingRights rights)
    {
        RequireEditable();
        ApplyCastlingRights(rights & HomeCastlingRights());
    }

    // Castling rights survive only where the king or rook still stands on its home square
    private static readonly (Square Square, Color Color, Piece Type)[] CastlingHomes =
    [
        (Square.E1, Color.White, Piece.King), (Square.H1, Color.White, Piece.Rook), (Square.A1, Color.White, Piece.Rook),
        (Square.E8, Color.Black, Piece.King), (Square.H8, Color.Black, Piece.Rook), (Square.A8, Color.Black, Piece.Rook),
    ];

    private CastlingRights HomeCastlingRights()
    {
        CastlingRights rights = CastlingRights.All;
        foreach ((Square square, Color color, Piece type) in CastlingHomes)
        {
            if (this[square] != (color, type))
            {
                rights &= ~CastlingRightsExtensions.LostBySquare(square);
            }
        }
        return rights;
    }

    private void RequireEditable()
    {
        if (history.Count != 0)
        {
            throw new InvalidOperationException("a position can only be edited before moves are made on it; start from Position.Setup");
        }
    }
}
