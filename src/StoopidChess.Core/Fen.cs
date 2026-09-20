using System.Globalization;
using System.Text;

namespace StoopidChess.Core;

/// <summary>
/// Forsyth-Edwards Notation. See https://www.chessprogramming.org/Forsyth-Edwards_Notation
/// </summary>
public static class Fen
{
    /// <summary>
    /// Parse a FEN string. The half-move clock and move number fields are optional.
    /// </summary>
    /// <exception cref="FormatException">The string is not valid FEN or describes a position without
    /// exactly one king per side.</exception>
    public static Position Parse(string fen)
    {
        ArgumentNullException.ThrowIfNull(fen);
        string[] fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is < 4 or > 6)
        {
            throw new FormatException("FEN must have between 4 and 6 fields");
        }

        Position position = new();
        ParsePlacement(fields[0], position);

        Color sideToMove = fields[1] switch
        {
            "w" => Color.White,
            "b" => Color.Black,
            _ => throw new FormatException($"invalid side to move '{fields[1]}'"),
        };

        CastlingRights rights = ParseCastling(fields[2]);

        Square enPassant = Square.None;
        if (fields[3] != "-")
        {
            enPassant = Square.TryParse(fields[3]) ?? Square.None;
            if (!enPassant.IsValid || !IsConsistentEnPassantSquare(position, sideToMove, enPassant))
            {
                throw new FormatException($"invalid en passant square '{fields[3]}'");
            }
        }

        int halfMoveClock = fields.Length > 4 ? ParseNumber(fields[4], "half-move clock") : 0;
        int fullMoveNumber = fields.Length > 5 ? Math.Max(1, ParseNumber(fields[5], "move number")) : 1;

        if (!position.HasValidKings)
        {
            throw new FormatException("each side must have exactly one king");
        }
        if (position.HasPawnsOnBackRanks)
        {
            throw new FormatException("pawns cannot stand on the first or last rank");
        }

        position.SwitchSideToMove(sideToMove);
        position.ApplyCastlingRights(rights);
        // An en passant square nobody can capture on is dropped, so that FIDE-identical positions
        // have identical keys whichever way they were reached
        position.SetEnPassantSquare(enPassant.IsValid && position.IsAttackedByPawn(enPassant, sideToMove) ? enPassant : Square.None);
        position.SetClocks(halfMoveClock, fullMoveNumber);
        return position;
    }

    /// <summary>
    /// Parse a FEN string, returning null rather than throwing on invalid input.
    /// </summary>
    public static Position? TryParse(string fen)
    {
        try
        {
            return Parse(fen);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string Write(Position position)
    {
        ArgumentNullException.ThrowIfNull(position);
        var fen = new StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                (Color color, Piece type) = position[file, rank];
                if (type == Piece.None)
                {
                    empty++;
                    continue;
                }
                if (empty != 0)
                {
                    fen.Append(empty);
                    empty = 0;
                }
                fen.Append(type.ToChar(color));
            }
            if (empty != 0)
            {
                fen.Append(empty);
            }
            if (rank != 0)
            {
                fen.Append('/');
            }
        }
        fen.Append(' ').Append(position.SideToMove == Color.White ? 'w' : 'b');
        fen.Append(' ').Append(position.CastlingRights.ToFenString());
        fen.Append(' ').Append(position.EnPassantSquare.Name);
        fen.Append(' ').Append(position.HalfMoveClock);
        fen.Append(' ').Append(position.FullMoveNumber);
        return fen.ToString();
    }

    /// <summary>
    /// The en passant square must be empty, on the rank behind the pawn that just made a double push,
    /// and that pawn must be there.
    /// </summary>
    private static bool IsConsistentEnPassantSquare(Position position, Color sideToMove, Square square)
    {
        int expectedRank = sideToMove == Color.White ? 5 : 2;
        if (position[square].Type != Piece.None || square.Rank != expectedRank)
        {
            return false;
        }
        Square pushedPawn = square - Position.PawnDirection(sideToMove);
        return position[pushedPawn] == (sideToMove.Opponent(), Piece.Pawn);
    }

    private static void ParsePlacement(string placement, Position position)
    {
        string[] ranks = placement.Split('/');
        if (ranks.Length != 8)
        {
            throw new FormatException("piece placement must have 8 ranks");
        }
        for (int i = 0; i < 8; i++)
        {
            int rank = 7 - i;
            int file = 0;
            foreach (char c in ranks[i])
            {
                if (c is >= '1' and <= '8')
                {
                    file += c - '0';
                }
                else if (PieceExtensions.FromChar(c) is not Piece.None and Piece type)
                {
                    if (file > 7)
                    {
                        throw new FormatException($"too many squares on rank {rank + 1}");
                    }
                    position.PutPiece(Square.At(file, rank), char.IsUpper(c) ? Color.White : Color.Black, type);
                    file++;
                }
                else
                {
                    throw new FormatException($"invalid character '{c}' in piece placement");
                }
            }
            if (file != 8)
            {
                throw new FormatException($"rank {rank + 1} does not have 8 squares");
            }
        }
    }

    private static CastlingRights ParseCastling(string field)
    {
        if (field == "-")
        {
            return CastlingRights.None;
        }
        CastlingRights rights = CastlingRights.None;
        foreach (char c in field)
        {
            rights |= c switch
            {
                'K' => CastlingRights.WhiteKingSide,
                'Q' => CastlingRights.WhiteQueenSide,
                'k' => CastlingRights.BlackKingSide,
                'q' => CastlingRights.BlackQueenSide,
                _ => throw new FormatException($"invalid castling character '{c}'"),
            };
        }
        return rights;
    }

    private static int ParseNumber(string field, string name)
    {
        // Digits only; anything beyond the board's clock cap is clamped rather than rejected
        if (field.Length > 9 && field.All(char.IsAsciiDigit))
        {
            return int.MaxValue;
        }
        return int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new FormatException($"invalid {name} '{field}'");
    }
}
