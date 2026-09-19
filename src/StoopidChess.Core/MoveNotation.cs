using System.Text;

namespace StoopidChess.Core;

/// <summary>
/// Conversions between moves and text.
/// </summary>
public static class MoveNotation
{
    /// <summary>
    /// Parse UCI move text ("e2e4", "e7e8q") into its components, or null if malformed. Legality is
    /// not checked.
    /// </summary>
    public static (Square From, Square To, PieceType Promotion)? ParseUci(string? text)
    {
        if (text is null || text.Length is not (4 or 5)
            || Square.TryParse(text.AsSpan(0, 2)) is not Square from || Square.TryParse(text.AsSpan(2, 2)) is not Square to)
        {
            return null;
        }
        PieceType promotion = PieceType.None;
        if (text.Length == 5)
        {
            promotion = PieceTypeExtensions.FromChar(text[4]);
            if (!PieceTypeExtensions.PromotionTypes.Contains(promotion))
            {
                return null;
            }
        }
        return (from, to, promotion);
    }

    /// <summary>
    /// Long algebraic notation for display: "e2-e4", "Ng1-f3", "e4xd5", "e5xd6 e.p.", "e7-e8=Q", "O-O".
    /// </summary>
    public static string ToLongAlgebraic(Move move)
    {
        switch (move.Kind)
        {
            case MoveKind.CastleKingSide:
                return "O-O";
            case MoveKind.CastleQueenSide:
                return "O-O-O";
            default:
                var text = new StringBuilder(12);
                if (move.Piece != PieceType.Pawn)
                {
                    text.Append(move.Piece.ToChar());
                }
                text.Append(move.From.Name);
                text.Append(move.IsCapture ? 'x' : '-');
                text.Append(move.To.Name);
                if (move.IsPromotion)
                {
                    text.Append('=').Append(move.Promotion.ToChar());
                }
                if (move.Kind == MoveKind.EnPassant)
                {
                    text.Append(" e.p.");
                }
                return text.ToString();
        }
    }
}
