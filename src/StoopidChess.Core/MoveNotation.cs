using System.Text;

namespace StoopidChess.Core;

/// <summary>
/// Conversions between moves and text.
/// </summary>
public static class MoveNotation
{
    /// <summary>
    /// Parse UCI move text ("e2e4", "e7e8q") into its components. Legality is not checked.
    /// </summary>
    public static bool TryParseUci(string text, out int from, out int to, out PieceType promotion)
    {
        from = to = Squares.None;
        promotion = PieceType.None;
        if (text is null || text.Length is not (4 or 5))
        {
            return false;
        }
        if (!Squares.TryParse(text.AsSpan(0, 2), out from) || !Squares.TryParse(text.AsSpan(2, 2), out to))
        {
            return false;
        }
        if (text.Length == 5)
        {
            promotion = PieceTypeExtensions.FromChar(text[4]);
            if (!PieceTypeExtensions.PromotionTypes.Contains(promotion))
            {
                return false;
            }
        }
        return true;
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
                text.Append(Squares.Name(move.From));
                text.Append(move.IsCapture ? 'x' : '-');
                text.Append(Squares.Name(move.To));
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
