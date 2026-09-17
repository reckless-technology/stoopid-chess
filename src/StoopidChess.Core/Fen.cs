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
    public static Board Parse(string fen)
    {
        ArgumentNullException.ThrowIfNull(fen);
        string[] fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is < 4 or > 6)
        {
            throw new FormatException("FEN must have between 4 and 6 fields");
        }

        Board board = new();
        ParsePlacement(fields[0], board);

        Colour sideToMove = fields[1] switch
        {
            "w" => Colour.White,
            "b" => Colour.Black,
            _ => throw new FormatException($"invalid side to move '{fields[1]}'"),
        };

        CastlingRights rights = ParseCastling(fields[2]);

        int enPassant = Squares.None;
        if (fields[3] != "-")
        {
            if (!Squares.TryParse(fields[3], out enPassant) || !IsConsistentEnPassantSquare(board, sideToMove, enPassant))
            {
                throw new FormatException($"invalid en passant square '{fields[3]}'");
            }
        }

        int halfMoveClock = fields.Length > 4 ? ParseNumber(fields[4], "half-move clock") : 0;
        int fullMoveNumber = fields.Length > 5 ? Math.Max(1, ParseNumber(fields[5], "move number")) : 1;

        if (!board.HasValidKings)
        {
            throw new FormatException("each side must have exactly one king");
        }
        if (MoveGenerator.HasPawnsOnBackRanks(board))
        {
            throw new FormatException("pawns cannot stand on the first or last rank");
        }

        board.SwitchSideToMove(sideToMove);
        board.SetCastlingRights(rights);
        board.SetEnPassantSquare(enPassant);
        board.SetClocks(halfMoveClock, fullMoveNumber);
        return board;
    }

    /// <summary>
    /// Try to parse a FEN string, returning false rather than throwing on invalid input.
    /// </summary>
    public static bool TryParse(string fen, out Board board)
    {
        try
        {
            board = Parse(fen);
            return true;
        }
        catch (FormatException)
        {
            board = null!;
            return false;
        }
    }

    public static string Write(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var fen = new StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                Piece piece = board[file, rank];
                if (piece.IsNone)
                {
                    empty++;
                    continue;
                }
                if (empty != 0)
                {
                    fen.Append(empty);
                    empty = 0;
                }
                fen.Append(piece.ToChar());
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
        fen.Append(' ').Append(board.SideToMove == Colour.White ? 'w' : 'b');
        fen.Append(' ').Append(board.CastlingRights.ToFenString());
        fen.Append(' ').Append(Squares.Name(board.EnPassantSquare));
        fen.Append(' ').Append(board.HalfMoveClock);
        fen.Append(' ').Append(board.FullMoveNumber);
        return fen.ToString();
    }

    /// <summary>
    /// The en passant square must be empty, on the rank behind the pawn that just made a double push,
    /// and that pawn must be there.
    /// </summary>
    private static bool IsConsistentEnPassantSquare(Board board, Colour sideToMove, int square)
    {
        const int up = 0x10;
        if (!board[square].IsNone)
        {
            return false;
        }
        return sideToMove == Colour.White
            ? Squares.RankOf(square) == 5 && board[square - up] == Piece.BlackPawn
            : Squares.RankOf(square) == 2 && board[square + up] == Piece.WhitePawn;
    }

    private static void ParsePlacement(string placement, Board board)
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
                else if (Piece.TryFromChar(c, out Piece piece))
                {
                    if (file > 7)
                    {
                        throw new FormatException($"too many squares on rank {rank + 1}");
                    }
                    board.PutPiece(Squares.Index(file, rank), piece);
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
        if (!int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            throw new FormatException($"invalid {name} '{field}'");
        }
        return value;
    }
}
