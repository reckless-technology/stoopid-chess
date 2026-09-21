using System.Text;

namespace StoopidChess.Core;

/// <summary>
/// Standard Algebraic Notation, the move text of PGN: "Nf3", "exd5", "O-O", "e8=Q+", "Nbd2#".
/// Formatting and parsing both need the position the move is played from. Parsing accepts the
/// customary variations (0-0, e8Q, trailing check and annotation marks) and returns null for
/// anything that is not a unique legal move. Both briefly make and unmake moves on the position
/// to decide check and mate suffixes, leaving it as it was.
/// </summary>
public static class San
{
    public static string Format(Position position, Move move)
    {
        ArgumentNullException.ThrowIfNull(position);
        var text = new StringBuilder(8);
        if (move.Kind == MoveKind.CastleKingSide)
        {
            text.Append("O-O");
        }
        else if (move.Kind == MoveKind.CastleQueenSide)
        {
            text.Append("O-O-O");
        }
        else if (move.Piece == Piece.Pawn)
        {
            if (move.IsCapture)
            {
                text.Append((char)('a' + move.From.File)).Append('x');
            }
            text.Append(move.To.Name);
            if (move.Kind == MoveKind.Promotion)
            {
                text.Append('=').Append(move.Promotion.ToChar(Color.White));
            }
        }
        else
        {
            text.Append(move.Piece.ToChar(Color.White));
            text.Append(Disambiguation(position, move));
            if (move.IsCapture)
            {
                text.Append('x');
            }
            text.Append(move.To.Name);
        }
        position.MakeMove(move);
        if (position.IsInCheck)
        {
            MoveList replies = position.GenerateLegalMoves();
            text.Append(replies.Count == 0 ? '#' : '+');
        }
        position.UnmakeMove();
        return text.ToString();
    }

    /// <summary>
    /// The unique legal move the text names, or null. Trailing check, mate and annotation marks are
    /// ignored; castling may be written with the letter O or the digit 0; the promotion "=" is
    /// optional.
    /// </summary>
    public static Move? Parse(Position position, string san)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(san);
        string text = san.Trim().TrimEnd('+', '#', '!', '?');
        if (text.Length == 0)
        {
            return null;
        }
        MoveList moves = position.GenerateLegalMoves();

        string castle = text.Replace('0', 'O').Replace("o", "O", StringComparison.Ordinal);
        if (castle is "O-O" or "O-O-O")
        {
            MoveKind kind = castle == "O-O" ? MoveKind.CastleKingSide : MoveKind.CastleQueenSide;
            foreach (Move move in moves)
            {
                if (move.Kind == kind)
                {
                    return move;
                }
            }
            return null;
        }

        Piece piece = Piece.Pawn;
        int start = 0;
        if (text[0] is 'N' or 'B' or 'R' or 'Q' or 'K')
        {
            piece = FromLetter(text[0]);
            start = 1;
        }

        Piece promotion = Piece.None;
        int end = text.Length;
        if (end > start && text[end - 1] is 'N' or 'B' or 'R' or 'Q' or 'n' or 'r' or 'q' && piece == Piece.Pawn)
        {
            promotion = FromLetter(char.ToUpperInvariant(text[end - 1]));
            end--;
            if (end > start && text[end - 1] == '=')
            {
                end--;
            }
        }

        if (end - start < 2)
        {
            return null;
        }
        Square? to = Square.TryParse(text.AsSpan(end - 2, 2));
        if (to is null)
        {
            return null;
        }
        int fromFile = -1;
        int fromRank = -1;
        for (int i = start; i < end - 2; i++)
        {
            char c = text[i];
            if (c is >= 'a' and <= 'h')
            {
                fromFile = c - 'a';
            }
            else if (c is >= '1' and <= '8')
            {
                fromRank = c - '1';
            }
            else if (c != 'x')
            {
                return null;
            }
        }

        Move? found = null;
        foreach (Move move in moves)
        {
            if (move.Piece != piece || move.To != to.Value
                || move.Kind is MoveKind.CastleKingSide or MoveKind.CastleQueenSide
                || (move.Kind == MoveKind.Promotion) != (promotion != Piece.None)
                || (promotion != Piece.None && move.Promotion != promotion)
                || (fromFile >= 0 && move.From.File != fromFile)
                || (fromRank >= 0 && move.From.Rank != fromRank))
            {
                continue;
            }
            if (found is not null)
            {
                return null;    // Ambiguous
            }
            found = move;
        }
        return found;
    }

    private static Piece FromLetter(char letter) => letter switch
    {
        'N' => Piece.Knight,
        'B' => Piece.Bishop,
        'R' => Piece.Rook,
        'Q' => Piece.Queen,
        'K' => Piece.King,
        _ => Piece.None,
    };

    /// <summary>
    /// What must precede the destination when another piece of the same type could also legally
    /// move there: the file if it settles it, else the rank, else both.
    /// </summary>
    private static string Disambiguation(Position position, Move move)
    {
        bool ambiguous = false;
        bool sameFile = false;
        bool sameRank = false;
        foreach (Move other in position.GenerateLegalMoves())
        {
            if (other.Piece == move.Piece && other.To == move.To && other.From != move.From)
            {
                ambiguous = true;
                sameFile |= other.From.File == move.From.File;
                sameRank |= other.From.Rank == move.From.Rank;
            }
        }
        if (!ambiguous)
        {
            return "";
        }
        if (!sameFile)
        {
            return ((char)('a' + move.From.File)).ToString();
        }
        if (!sameRank)
        {
            return ((char)('1' + move.From.Rank)).ToString();
        }
        return move.From.Name;
    }
}
