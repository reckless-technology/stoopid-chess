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
/// A move, including enough information about the position it was made in to unmake it, packed
/// into 32 bits so that move lists and transposition table entries stay small. The default value is
/// <see cref="None"/>.
/// </summary>
public readonly record struct Move
{
    // Bit layout: from 0-5, to 6-11, piece 12-14, captured 15-17, kind 18-20, promotion 21-23
    private const int ToShift = 6;
    private const int PieceShift = 12;
    private const int CapturedShift = 15;
    private const int KindShift = 18;
    private const int PromotionShift = 21;
    private const uint SquareMask = 0x3F;
    private const uint ThreeBits = 0x7;

    private readonly uint bits;

    /// <param name="from">Source square.</param>
    /// <param name="to">Destination square. For castling this is the king's destination.</param>
    /// <param name="piece">The moving piece type; never <see cref="PieceType.None"/> for a real move.</param>
    /// <param name="captured">The captured piece type, or <see cref="PieceType.None"/>. A pawn for en passant.</param>
    /// <param name="kind">Special-move classification.</param>
    /// <param name="promotion">The piece a pawn promotes to, or <see cref="PieceType.None"/>.</param>
    public Move(Square from, Square to, PieceType piece, PieceType captured = PieceType.None, MoveKind kind = MoveKind.Normal, PieceType promotion = PieceType.None)
    {
        bits = ((uint)from.Index & SquareMask)
             | (((uint)to.Index & SquareMask) << ToShift)
             | (((uint)piece & ThreeBits) << PieceShift)
             | (((uint)captured & ThreeBits) << CapturedShift)
             | (((uint)kind & ThreeBits) << KindShift)
             | (((uint)promotion & ThreeBits) << PromotionShift);
    }

    private Move(uint bits)
    {
        this.bits = bits;
    }

    public static readonly Move None;

    public Square From => Square.Unchecked((int)(bits & SquareMask));

    public Square To => Square.Unchecked((int)((bits >> ToShift) & SquareMask));

    public PieceType Piece => (PieceType)((bits >> PieceShift) & ThreeBits);

    public PieceType Captured => (PieceType)((bits >> CapturedShift) & ThreeBits);

    public MoveKind Kind => (MoveKind)((bits >> KindShift) & ThreeBits);

    public PieceType Promotion => (PieceType)((bits >> PromotionShift) & ThreeBits);

    public bool IsNone => Piece == PieceType.None;

    public bool IsCapture => Captured != PieceType.None;

    public bool IsPromotion => Kind == MoveKind.Promotion;

    /// <summary>
    /// The packed representation, for storing moves compactly.
    /// </summary>
    public uint ToBits() => bits;

    public static Move FromBits(uint bits) => new(bits);

    /// <summary>
    /// UCI long algebraic notation: "e2e4", "e7e8q". Castling is written as the king move ("e1g1").
    /// </summary>
    public string ToUci()
    {
        string text = From.Name + To.Name;
        return IsPromotion ? text + char.ToLowerInvariant(Promotion.ToChar()) : text;
    }

    public override string ToString() => IsNone ? "0000" : ToUci();
}
