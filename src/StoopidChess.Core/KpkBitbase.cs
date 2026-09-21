using System.Collections;
using System.Diagnostics;

namespace StoopidChess.Core;

/// <summary>
/// The king-and-pawn-versus-king bitbase: whether the pawn's side forces a win, for every legal
/// arrangement, solved once by retrograde iteration (the classical Stockfish construction). The
/// caller normalizes the strong side to white and the pawn to files a-d by flipping and mirroring
/// (the evaluator does); 196,608 positions, 24 KB, built on first use in a few milliseconds.
/// </summary>
public static class KpkBitbase
{
    private const int PawnSlots = 24;                       // Files a-d, ranks 2-7
    private const int Size = 2 * 64 * 64 * PawnSlots;

    private const byte Unknown = 0;
    private const byte Draw = 1;
    private const byte Win = 2;
    private const byte Invalid = 3;

    private static readonly Lazy<BitArray> Wins = new(Solve);

    /// <summary>
    /// Does white (the side with the pawn) force a win? The pawn must be on files a-d, ranks 2-7.
    /// </summary>
    public static bool Probe(Square strongKing, Square pawn, Square weakKing, bool strongToMove)
    {
        Debug.Assert(pawn.File < 4 && pawn.Rank is >= 1 and <= 6, "the caller normalizes the pawn to files a-d, ranks 2-7");
        return Wins.Value[Index(strongToMove, strongKing, weakKing, pawn)];
    }

    private static int Index(bool whiteToMove, Square whiteKing, Square blackKing, Square pawn)
    {
        int pawnSlot = pawn.File + (4 * (pawn.Rank - 1));
        return (whiteToMove ? 0 : 1) + (2 * ((int)whiteKing + (64 * ((int)blackKing + (64 * pawnSlot)))));
    }

    private static BitArray Solve()
    {
        byte[] results = new byte[Size];

        // First pass: illegal arrangements and terminal positions
        for (int index = 0; index < Size; index++)
        {
            results[index] = ClassifyTerminal(index);
        }

        // Iterate to the fixpoint: white to move wins if any reply wins, draws once every reply
        // draws; black to move draws if any reply draws, loses once every reply loses
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int index = 0; index < Size; index++)
            {
                if (results[index] == Unknown)
                {
                    byte result = ClassifySuccessors(results, index);
                    if (result != Unknown)
                    {
                        results[index] = result;
                        changed = true;
                    }
                }
            }
        }

        // Positions never resolved are repetition holds: nobody forces anything, so they are draws
        var wins = new BitArray(Size);
        for (int index = 0; index < Size; index++)
        {
            wins[index] = results[index] == Win;
        }
        return wins;
    }

    private static void Decode(int index, out bool whiteToMove, out int whiteKing, out int blackKing, out int pawn)
    {
        whiteToMove = (index & 1) == 0;
        whiteKing = (index >> 1) & 63;
        blackKing = (index >> 7) & 63;
        int slot = index >> 13;
        pawn = (slot & 3) + (8 * ((slot >> 2) + 1));
    }

    private static byte ClassifyTerminal(int index)
    {
        Decode(index, out bool whiteToMove, out int whiteKing, out int blackKing, out int pawn);
        bool blackInCheck = PawnAttacks(pawn, blackKing);
        if (whiteKing == blackKing || Adjacent(whiteKing, blackKing) || whiteKing == pawn || blackKing == pawn
            || (whiteToMove && blackInCheck))
        {
            return Invalid;
        }
        if (!whiteToMove)
        {
            // Black draws at once by winning the pawn, is mated or stalemated with no move, and
            // otherwise waits for the iteration
            bool anyMove = false;
            foreach (int to in KingMoves(blackKing))
            {
                if (Adjacent(to, whiteKing) || to == whiteKing)
                {
                    continue;
                }
                if (to == pawn)
                {
                    if (!Adjacent(whiteKing, pawn))
                    {
                        return Draw;    // The pawn falls: king versus king
                    }
                    continue;           // Defended; the square itself stays unavailable
                }
                if (PawnAttacks(pawn, to))
                {
                    continue;
                }
                anyMove = true;
            }
            if (!anyMove)
            {
                return blackInCheck ? Win : Draw;   // Mate or stalemate
            }
            return Unknown;
        }
        // White to move: a winning promotion decides the position at once (queening is enough in
        // this ending). A promotion that only draws is merely one of white's options - declining
        // it and escorting with the king first is the classic win - so it is left to the
        // successor scan.
        if (Rank(pawn) == 6)
        {
            int promotion = pawn + 8;
            if (promotion != whiteKing && promotion != blackKing
                && PromotionOutcome(whiteKing, blackKing, promotion) == Win)
            {
                return Win;
            }
        }
        return Unknown;
    }

    /// <summary>
    /// The position after the pawn queens, black to move: a win unless black captures the new queen
    /// at once or is stalemated (king and queen against a bare king is otherwise always won).
    /// </summary>
    private static byte PromotionOutcome(int whiteKing, int blackKing, int queen)
    {
        bool inCheck = QueenAttacks(queen, whiteKing, blackKing);
        bool anyMove = false;
        foreach (int to in KingMoves(blackKing))
        {
            if (to == whiteKing || Adjacent(to, whiteKing))
            {
                continue;
            }
            if (to == queen)
            {
                if (!Adjacent(whiteKing, queen))
                {
                    return Draw;        // The queen falls the move after it appears
                }
                continue;
            }
            // The king must step off every attacked square; its own origin does not block the ray
            if (QueenAttacks(queen, whiteKing, to))
            {
                continue;
            }
            anyMove = true;
        }
        if (!anyMove)
        {
            return inCheck ? Win : Draw;    // Mate or stalemate by the fresh queen
        }
        return Win;
    }

    private static byte ClassifySuccessors(byte[] results, int index)
    {
        Decode(index, out bool whiteToMove, out int whiteKing, out int blackKing, out int pawn);
        bool anyLegal = false;
        bool anyUnknown = false;
        if (whiteToMove)
        {
            foreach (int to in KingMoves(whiteKing))
            {
                if (to == pawn)
                {
                    continue;
                }
                byte result = results[Index(false, new Square(to), new Square(blackKing), new Square(pawn))];
                if (result == Invalid)
                {
                    continue;
                }
                anyLegal = true;
                if (result == Win)
                {
                    return Win;
                }
                anyUnknown |= result == Unknown;
            }
            // A promotion that draws still counts as a legal option (winning ones were terminal)
            if (Rank(pawn) == 6)
            {
                int promotion = pawn + 8;
                if (promotion != whiteKing && promotion != blackKing)
                {
                    anyLegal = true;
                }
            }
            if (Rank(pawn) < 6)
            {
                int single = pawn + 8;
                if (single != whiteKing && single != blackKing)
                {
                    byte result = results[Index(false, new Square(whiteKing), new Square(blackKing), new Square(single))];
                    if (result != Invalid)
                    {
                        anyLegal = true;
                        if (result == Win)
                        {
                            return Win;
                        }
                        anyUnknown |= result == Unknown;
                    }
                    int twoSquares = pawn + 16;
                    if (Rank(pawn) == 1 && twoSquares != whiteKing && twoSquares != blackKing)
                    {
                        result = results[Index(false, new Square(whiteKing), new Square(blackKing), new Square(twoSquares))];
                        if (result != Invalid)
                        {
                            anyLegal = true;
                            if (result == Win)
                            {
                                return Win;
                            }
                            anyUnknown |= result == Unknown;
                        }
                    }
                }
            }
            if (!anyLegal)
            {
                return Draw;    // White is stalemated: king boxed in, pawn blocked
            }
            return anyUnknown ? Unknown : Draw;
        }
        foreach (int to in KingMoves(blackKing))
        {
            if (to == pawn)
            {
                continue;       // Capture was handled terminally; a defended pawn is unreachable
            }
            byte result = results[Index(true, new Square(whiteKing), new Square(to), new Square(pawn))];
            if (result == Invalid)
            {
                continue;
            }
            anyLegal = true;
            if (result == Draw)
            {
                return Draw;
            }
            anyUnknown |= result == Unknown;
        }
        if (!anyLegal)
        {
            // No-move positions were classified terminally; reaching here means every step is off
            // the board, next to the white king or into the pawn's teeth: already handled there too
            return Unknown;
        }
        return anyUnknown ? Unknown : Win;
    }

    private static int Rank(int square) => square >> 3;

    private static bool Adjacent(int a, int b)
    {
        int fileDistance = Math.Abs((a & 7) - (b & 7));
        int rankDistance = Math.Abs((a >> 3) - (b >> 3));
        return Math.Max(fileDistance, rankDistance) == 1;
    }

    private static bool PawnAttacks(int pawn, int square)
    {
        int file = pawn & 7;
        return ((file > 0 && square == pawn + 7) || (file < 7 && square == pawn + 9)) && square < 64;
    }

    private static IEnumerable<int> KingMoves(int from)
    {
        int file = from & 7;
        int rank = from >> 3;
        for (int df = -1; df <= 1; df++)
        {
            for (int dr = -1; dr <= 1; dr++)
            {
                if ((df | dr) == 0)
                {
                    continue;
                }
                int f = file + df;
                int r = rank + dr;
                if (f is >= 0 and <= 7 && r is >= 0 and <= 7)
                {
                    yield return f + (8 * r);
                }
            }
        }
    }

    /// <summary>
    /// Does a queen on <paramref name="queen"/> attack <paramref name="target"/>? Only the white
    /// king blocks its rays: the black king is considered lifted off its origin, so a king
    /// stepping backwards along the ray it stood on is still attacked.
    /// </summary>
    private static bool QueenAttacks(int queen, int whiteKing, int target)
    {
        int fileStep = Math.Sign((target & 7) - (queen & 7));
        int rankStep = Math.Sign((target >> 3) - (queen >> 3));
        int fileDistance = Math.Abs((target & 7) - (queen & 7));
        int rankDistance = Math.Abs((target >> 3) - (queen >> 3));
        if (fileDistance != 0 && rankDistance != 0 && fileDistance != rankDistance)
        {
            return false;
        }
        int step = fileStep + (8 * rankStep);
        for (int square = queen + step; square != target; square += step)
        {
            if (square == whiteKing)
            {
                return false;   // The own king blocks; the black king's origin does not
            }
        }
        return true;
    }
}
