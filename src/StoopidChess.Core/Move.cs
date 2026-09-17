namespace StoopidChess.Core;

/// <summary>
/// The special handling a move needs when it is made on the board.
/// </summary>
public enum MoveKind : byte
{
    Normal = 0,
    DoublePawnPush = 1,
    EnPassant = 2,
    CastleKingSide = 3,
    CastleQueenSide = 4,
    Promotion = 5,
}

/// <summary>
/// A move, including enough information about the position it was made in to unmake it.
/// The default value is <see cref="None"/>.
/// </summary>
/// <param name="From">Source square (0x88).</param>
/// <param name="To">Destination square (0x88). For castling this is the king's destination.</param>
/// <param name="Piece">The moving piece type; never <see cref="PieceType.None"/> for a real move.</param>
/// <param name="Captured">The captured piece type, or <see cref="PieceType.None"/>. A pawn for en passant.</param>
/// <param name="Kind">Special-move classification.</param>
/// <param name="Promotion">The piece a pawn promotes to, or <see cref="PieceType.None"/>.</param>
public readonly record struct Move(int From, int To, PieceType Piece, PieceType Captured, MoveKind Kind, PieceType Promotion)
{
    public Move(int from, int to, PieceType piece, PieceType captured = PieceType.None, MoveKind kind = MoveKind.Normal)
        : this(from, to, piece, captured, kind, PieceType.None)
    {
    }

    public static readonly Move None;

    public bool IsNone => Piece == PieceType.None;

    public bool IsCapture => Captured != PieceType.None;

    public bool IsCastling => Kind is MoveKind.CastleKingSide or MoveKind.CastleQueenSide;

    public bool IsPromotion => Kind == MoveKind.Promotion;

    /// <summary>
    /// UCI long algebraic notation: "e2e4", "e7e8q". Castling is written as the king move ("e1g1").
    /// </summary>
    public string ToUci()
    {
        string text = Squares.Name(From) + Squares.Name(To);
        return IsPromotion ? text + char.ToLowerInvariant(Promotion.ToChar()) : text;
    }

    public override string ToString() => IsNone ? "0000" : ToUci();
}
