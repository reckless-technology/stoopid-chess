namespace StoopidChess.Core;

/// <summary>
/// Zobrist hash keys for positions. See https://www.chessprogramming.org/Zobrist_Hashing
/// The keys come from a fixed-seed 64-bit linear congruential generator, so they are identical on
/// every run and every platform, which keeps searches reproducible.
/// </summary>
public static class Zobrist
{
    private const ulong Seed = 0x72B32F6D517CD491;
    private const ulong Multiplier = 0x27BB2EE687B0B0FD;
    private const ulong Increment = 0x00000000B504F32D;

    private const int PieceTypes = 7;
    private const int Colours = 3;

    private static readonly ulong[] Pieces = new ulong[Colours * PieceTypes * Square.Count];
    private static readonly ulong[] CastlingKeys = new ulong[16];
    private static readonly ulong[] EnPassantKeys = new ulong[Square.Count];

    static Zobrist()
    {
        ulong state = Seed;
        ulong Next()
        {
            state = unchecked((Multiplier * state) + Increment);
            return state;
        }

        SideToMove = Next();
        for (int i = 0; i < Pieces.Length; i++)
        {
            Pieces[i] = Next();
        }
        // No castling rights contributes nothing, so a bare board with no rights has key zero
        for (int i = 1; i < CastlingKeys.Length; i++)
        {
            CastlingKeys[i] = Next();
        }
        for (int i = 0; i < EnPassantKeys.Length; i++)
        {
            EnPassantKeys[i] = Next();
        }
    }

    /// <summary>
    /// XORed into the key when it is black's turn to move.
    /// </summary>
    public static ulong SideToMove { get; }

    public static ulong Piece(Colour colour, PieceType type, Square square)
    {
        return Pieces[((((int)colour * PieceTypes) + (int)type) * Square.Count) + square];
    }

    public static ulong Castling(CastlingRights rights) => CastlingKeys[(int)rights];

    /// <summary>
    /// The key for an en passant square; zero for <see cref="Square.None"/>.
    /// </summary>
    public static ulong EnPassant(Square square) => square.IsValid ? EnPassantKeys[square] : 0;
}
