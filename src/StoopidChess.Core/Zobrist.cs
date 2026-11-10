namespace StoopidChess.Core;

/// <summary>
/// Zobrist hash keys for positions. Every (color, piece, square) has a random 64-bit number, and
/// so do black to move, each set of castling rights and each en passant square; a position's key
/// is the XOR of the numbers for everything in it. Because XOR undoes itself, a move updates the
/// key by XORing out what left and XORing in what arrived (a knight from g1 to f3 is two XORs),
/// and two positions with the same pieces, side, rights and en passant square have the same key
/// however they were reached. The transposition table, the evaluation caches and repetition
/// detection all compare keys instead of boards. Different positions can share a key, but with 64
/// random bits that is rare enough to ignore. See https://www.chessprogramming.org/Zobrist_Hashing
///
/// The numbers come from a fixed-seed 64-bit linear congruential generator, so they are identical
/// on every run and every platform, which keeps searches reproducible.
/// </summary>
public static class Zobrist
{
    private const ulong Seed = 0x72B32F6D517CD491;
    private const ulong Multiplier = 0x27BB2EE687B0B0FD;
    private const ulong Increment = 0x00000000B504F32D;

    private const int PieceTypes = 7;
    private const int Colors = 2;

    private static readonly ulong[] s_pieces = new ulong[Colors * PieceTypes * Square.Count];
    private static readonly ulong[] s_castlingKeys = new ulong[16];
    private static readonly ulong[] s_enPassantKeys = new ulong[Square.Count];

    static Zobrist()
    {
        ulong state = Seed;
        ulong Next()
        {
            state = unchecked((Multiplier * state) + Increment);
            return state;
        }

        SideToMove = Next();
        for (int i = 0; i < s_pieces.Length; i++)
        {
            s_pieces[i] = Next();
        }
        // No castling rights contributes nothing, so a bare board with no rights has key zero
        for (int i = 1; i < s_castlingKeys.Length; i++)
        {
            s_castlingKeys[i] = Next();
        }
        for (int i = 0; i < s_enPassantKeys.Length; i++)
        {
            s_enPassantKeys[i] = Next();
        }
    }

    /// <summary>
    /// XORed into the key when it is black's turn to move.
    /// </summary>
    public static ulong SideToMove { get; }

    public static ulong Piece(Color color, Piece type, Square square)
    {
        return s_pieces[((((int)color * PieceTypes) + (int)type) * Square.Count) + square];
    }

    public static ulong Castling(CastlingRights rights) => s_castlingKeys[(int)rights];

    /// <summary>
    /// The key for an en passant square; zero for <see cref="Square.None"/>.
    /// </summary>
    public static ulong EnPassant(Square square) => square.IsValid ? s_enPassantKeys[square] : 0;
}
