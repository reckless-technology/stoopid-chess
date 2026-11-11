using System.Text;
using StoopidChess.Core;

namespace StoopidChess.Play;

/// <summary>
/// A board being set up by hand in the GUI: what stands on each square, whose move it is, and the
/// castling rights. Unlike a <see cref="Position"/> it may hold anything while it is edited (no
/// kings, a pawn on the last rank, the side not to move in check); <see cref="ToPosition"/> turns
/// it into a position, through FEN, once play can proceed from it. Castling rights survive only
/// while the king and the rook still stand on their home squares.
/// </summary>
public sealed class BoardSetup
{
    // The king and rook squares each castling right needs
    private static readonly (Square Square, Color Color, Piece Type)[] CastlingHomes =
    [
        (Square.E1, Color.White, Piece.King),
        (Square.H1, Color.White, Piece.Rook),
        (Square.A1, Color.White, Piece.Rook),
        (Square.E8, Color.Black, Piece.King),
        (Square.H8, Color.Black, Piece.Rook),
        (Square.A8, Color.Black, Piece.Rook),
    ];

    private readonly (Color Color, Piece Type)[] _squares = new (Color, Piece)[Square.Count];
    private Color _sideToMove;

    private BoardSetup(Color sideToMove)
    {
        SideToMove = sideToMove;
    }

    /// <summary>
    /// An empty board with the given side to move and no castling rights.
    /// </summary>
    public static BoardSetup Empty(Color sideToMove) => new(sideToMove);

    /// <summary>
    /// The pieces, side to move and castling rights of a position, ready for editing. Its move
    /// history, en passant square and clocks are not carried over: an edited board is a new game.
    /// </summary>
    public static BoardSetup From(Position position)
    {
        ArgumentNullException.ThrowIfNull(position);
        var setup = new BoardSetup(position.SideToMove);
        foreach (Square square in Square.All)
        {
            setup._squares[square] = position[square];
        }
        setup.CastlingRights = position.CastlingRights & setup.HomeCastlingRights();
        return setup;
    }

    /// <summary>
    /// Whose move it is.
    /// </summary>
    public Color SideToMove
    {
        get => _sideToMove;
        set =>
            _sideToMove = value is Color.White or Color.Black
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public CastlingRights CastlingRights { get; private set; }

    /// <summary>
    /// The piece on a square; an empty square's type is <see cref="Piece.None"/>.
    /// </summary>
    public (Color Color, Piece Type) this[Square square] =>
        square.IsValid ? _squares[square] : throw new ArgumentOutOfRangeException(nameof(square));

    /// <summary>
    /// Put a piece on a square, replacing whatever stood there; a type of <see cref="Piece.None"/>
    /// (whatever the color, as in the default pair) empties the square.
    /// </summary>
    public void Place(Square square, (Color Color, Piece Type) piece)
    {
        if (!square.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(square));
        }
        bool isValid =
            piece.Type == Piece.None
            || (piece.Color is Color.White or Color.Black && piece.Type is >= Piece.Pawn and <= Piece.King);
        if (!isValid)
        {
            throw new ArgumentException($"'{piece.Color} {piece.Type}' is not a valid piece", nameof(piece));
        }
        _squares[square] = piece.Type == Piece.None ? default : piece;
        CastlingRights &= HomeCastlingRights();
    }

    /// <summary>
    /// The board as FEN, with no en passant square and fresh clocks.
    /// </summary>
    public string ToFen()
    {
        var fen = new StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                (Color color, Piece type) = _squares[Square.At(file, rank)];
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
        fen.Append(SideToMove == Color.White ? " w " : " b ").Append(CastlingRights.ToFenString()).Append(" - 0 1");
        return fen.ToString();
    }

    /// <summary>
    /// The position this board sets up, or null while play cannot proceed from it: each side needs
    /// exactly one king, no pawn may stand on the first or last rank, and the side not to move must
    /// not be in check.
    /// </summary>
    public Position? ToPosition() => Fen.TryParse(ToFen()) is { IsValid: true } position ? position : null;

    private CastlingRights HomeCastlingRights()
    {
        CastlingRights rights = CastlingRights.All;
        foreach ((Square square, Color color, Piece type) in CastlingHomes)
        {
            if (_squares[square] != (color, type))
            {
                rights &= ~CastlingRightsExtensions.LostBySquare(square);
            }
        }
        return rights;
    }
}
