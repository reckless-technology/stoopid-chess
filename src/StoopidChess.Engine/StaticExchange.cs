using StoopidChess.Core;

namespace StoopidChess.Engine;

/// <summary>
/// Static exchange evaluation: the material outcome of a capture if both sides keep recapturing on
/// the square with their least valuable attacker until one side stops, without searching. Used to
/// leave out captures that lose material.
/// See https://www.chessprogramming.org/Static_Exchange_Evaluation
/// </summary>
public static class StaticExchange
{
    private const int MaxExchanges = 32;

    /// <summary>
    /// The material the side to move wins by making the capture, in centipawns, assuming both sides
    /// recapture only while it pays; negative for a losing capture.
    /// </summary>
    public static int Evaluate(Board board, Move move)
    {
        ArgumentNullException.ThrowIfNull(board);
        Square to = move.To;
        Colour side = board.SideToMove.Opponent();          // The side to reply after the capture
        Bitboard occupied = board.Occupied ^ Bitboard.Of(move.From);
        int gain0 = Value(move.Captured);
        int attackerValue = Value(move.Piece);
        if (move.IsPromotion)
        {
            gain0 += Value(move.Promotion) - Value(PieceType.Pawn);
            attackerValue = Value(move.Promotion);
        }
        if (move.Kind == MoveKind.EnPassant)
        {
            occupied ^= Bitboard.Of(Board.EnPassantCaptureSquare(move));
        }

        Span<int> gain = stackalloc int[MaxExchanges];
        gain[0] = gain0;
        int depth = 0;
        Bitboard attackers = board.AttackersTo(to, occupied);
        Bitboard diagonalSliders = board.Pieces(PieceType.Bishop) | board.Pieces(PieceType.Queen);
        Bitboard orthogonalSliders = board.Pieces(PieceType.Rook) | board.Pieces(PieceType.Queen);
        while (true)
        {
            Bitboard own = attackers & board.Pieces(side);
            if (own.IsEmpty || depth + 1 >= MaxExchanges)
            {
                break;
            }
            depth++;
            gain[depth] = attackerValue - gain[depth - 1];    // Speculative: what this side gains if it captures
            if (Math.Max(-gain[depth - 1], gain[depth]) < 0)
            {
                break;                                         // Neither continuing nor stopping helps the side to move
            }
            // Recapture with the least valuable attacker
            PieceType type = LeastValuable(board, side, own, out Square from);
            occupied ^= Bitboard.Of(from);
            attackerValue = Value(type);
            // Lifting a piece may uncover a slider behind it
            if (type is PieceType.Pawn or PieceType.Bishop or PieceType.Rook or PieceType.Queen)
            {
                attackers |= (Attacks.Bishop(occupied, to) & diagonalSliders) | (Attacks.Rook(occupied, to) & orthogonalSliders);
            }
            attackers &= occupied;
            side = side.Opponent();
        }
        for (; depth > 0; depth--)
        {
            gain[depth - 1] = -Math.Max(-gain[depth - 1], gain[depth]);    // Each side may decline to continue the exchange
        }
        return gain[0];
    }

    private static PieceType LeastValuable(Board board, Colour side, Bitboard candidates, out Square from)
    {
        for (PieceType type = PieceType.Pawn; type <= PieceType.King; type++)
        {
            Bitboard pieces = candidates & board.Pieces(side, type);
            if (pieces.IsNotEmpty)
            {
                from = pieces.First;
                return type;
            }
        }
        throw new InvalidOperationException("no attacker");
    }

    /// <summary>
    /// The evaluator's material values; the king is worth more than any exchange so that it is only
    /// ever the last piece to capture.
    /// </summary>
    public static int Value(PieceType type) => type switch
    {
        PieceType.Pawn => Evaluator.PawnValue,
        PieceType.Knight => Evaluator.KnightValue,
        PieceType.Bishop => Evaluator.BishopValue,
        PieceType.Rook => Evaluator.RookValue,
        PieceType.Queen => Evaluator.QueenValue,
        PieceType.King => 10 * Evaluator.QueenValue,
        _ => 0,
    };
}
