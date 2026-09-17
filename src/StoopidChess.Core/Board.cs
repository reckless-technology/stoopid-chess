using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StoopidChess.Core;

/// <summary>
/// A chess position with make/unmake move support.
///
/// The board is an array of 128 squares in 0x88 layout (see <see cref="Squares"/>). Alongside the
/// squares it maintains, incrementally as moves are made and unmade:
/// <list type="bullet">
/// <item>lists of the squares occupied by each piece type of each colour, for fast iteration;</item>
/// <item>primary and secondary Zobrist keys of the whole position (pieces, side to move, castling
/// rights and en passant square) and a key of the pawn structure alone;</item>
/// <item>the castling rights, en passant square, half-move clock and move number;</item>
/// <item>a history of made moves so that they can be unmade in strict last-in first-out order, and
/// so that repetitions can be detected.</item>
/// </list>
/// A board is created from FEN (<see cref="FromFen"/>, <see cref="StartPosition"/>) or by a
/// <see cref="PositionBuilder"/>, and may then be changed only by <see cref="MakeMove"/> and
/// <see cref="UnmakeMove"/>.
/// </summary>
public sealed partial class Board
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private const int Colours = 3;
    private const int PieceTypes = 7;

    private readonly Piece[] squares = new Piece[128];
    private readonly List<int>[] pieceSquares = new List<int>[Colours * PieceTypes];
    private readonly List<UndoInfo> history = new(256);

    internal Board()
    {
        Array.Fill(squares, Piece.None);
        for (int i = 0; i < pieceSquares.Length; i++)
        {
            pieceSquares[i] = new List<int>(10);
        }
    }

    public static Board StartPosition() => Fen.Parse(StartFen);

    public static Board FromFen(string fen) => Fen.Parse(fen);

    public Colour SideToMove { get; private set; } = Colour.White;

    public CastlingRights CastlingRights { get; private set; }

    /// <summary>
    /// The square a pawn may capture on en passant, or <see cref="Squares.None"/>.
    /// </summary>
    public int EnPassantSquare { get; private set; } = Squares.None;

    /// <summary>
    /// Plies since the last capture or pawn move (for the fifty-move rule).
    /// </summary>
    public int HalfMoveClock { get; private set; }

    public int FullMoveNumber { get; private set; } = 1;

    /// <summary>
    /// Primary Zobrist key of the position, suitable for indexing a hash table.
    /// </summary>
    public ulong Key { get; private set; }

    /// <summary>
    /// Independent secondary Zobrist key, for verifying hash table hits.
    /// </summary>
    public ulong SecondaryKey { get; private set; }

    /// <summary>
    /// Zobrist key of the pawns alone (both colours), for caching pawn structure evaluation.
    /// </summary>
    public ulong PawnKey { get; private set; }

    public int PieceCount { get; private set; }

    /// <summary>
    /// Number of moves made on this board since it was created (and not unmade).
    /// </summary>
    public int Ply => history.Count;

    public Move? LastMove => history.Count == 0 ? null : history[^1].Move;

    public Piece this[int square]
    {
        get
        {
            if (!Squares.IsOnBoard(square))
            {
                throw new ArgumentOutOfRangeException(nameof(square));
            }
            return squares[square];
        }
    }

    public Piece this[int file, int rank]
    {
        get
        {
            if (file is < 0 or > 7 || rank is < 0 or > 7)
            {
                throw new ArgumentOutOfRangeException(file is < 0 or > 7 ? nameof(file) : nameof(rank));
            }
            return squares[Squares.Index(file, rank)];
        }
    }

    /// <summary>
    /// The largest half-move clock value kept. Real games end at 100 (or 150 under the seventy-five
    /// move rule); the cap only stops arithmetic overflow on absurd FEN input.
    /// </summary>
    public const int MaxHalfMoveClock = 999;

    /// <summary>
    /// The squares occupied by pieces of the given colour and type. The span is only valid until the
    /// next move is made or unmade.
    /// </summary>
    public ReadOnlySpan<int> SquaresOf(Colour colour, PieceType type) => CollectionsMarshal.AsSpan(pieceSquares[ListIndex(colour, type)]);

    /// <summary>
    /// The piece on a square, without the range check of the indexer; for the move generator and
    /// evaluator, which only ever ask about squares they have already checked are on the board.
    /// </summary>
    internal Piece At(int square) => squares[square];

    public int Count(Colour colour, PieceType type) => pieceSquares[ListIndex(colour, type)].Count;

    /// <summary>
    /// True when each side has exactly one king, which every rule of chess assumes.
    /// A position that has been edited may violate this.
    /// </summary>
    public bool HasValidKings => Count(Colour.White, PieceType.King) == 1 && Count(Colour.Black, PieceType.King) == 1;

    public int KingSquare(Colour colour)
    {
        List<int> kings = pieceSquares[ListIndex(colour, PieceType.King)];
        return kings.Count == 1 ? kings[0] : throw new InvalidOperationException($"{colour} does not have exactly one king");
    }

    public string ToFen() => Fen.Write(this);

    /// <summary>
    /// A deep copy including the move history.
    /// </summary>
    public Board Clone()
    {
        Board copy = new();
        Array.Copy(squares, copy.squares, squares.Length);
        for (int i = 0; i < pieceSquares.Length; i++)
        {
            copy.pieceSquares[i].AddRange(pieceSquares[i]);
        }
        copy.history.AddRange(history);
        copy.SideToMove = SideToMove;
        copy.CastlingRights = CastlingRights;
        copy.EnPassantSquare = EnPassantSquare;
        copy.HalfMoveClock = HalfMoveClock;
        copy.FullMoveNumber = FullMoveNumber;
        copy.Key = Key;
        copy.SecondaryKey = SecondaryKey;
        copy.PawnKey = PawnKey;
        copy.PieceCount = PieceCount;
        return copy;
    }

    /// <summary>
    /// How many earlier positions in the history are identical to the current one. Two means the
    /// position has now occurred three times.
    /// </summary>
    public int RepetitionCount()
    {
        int count = 0;
        // A pawn move or capture makes every earlier position unrepeatable, so only look back as
        // far as the half-move clock allows. Identical positions have the same side to move, so
        // they can only be an even number of plies apart.
        int oldest = history.Count - Math.Clamp(HalfMoveClock, 0, history.Count);
        for (int i = history.Count - 2; i >= oldest; i -= 2)
        {
            if (history[i].Key == Key)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Neither side can possibly deliver mate: king against king, king and one minor piece against
    /// king, or king and bishop against king and bishop with both bishops on the same square colour.
    /// </summary>
    public bool IsInsufficientMaterial()
    {
        switch (PieceCount)
        {
            case 2:
                return true;
            case 3:
                return Count(Colour.White, PieceType.Bishop) + Count(Colour.Black, PieceType.Bishop)
                     + Count(Colour.White, PieceType.Knight) + Count(Colour.Black, PieceType.Knight) == 1;
            case 4:
                if (Count(Colour.White, PieceType.Bishop) == 1 && Count(Colour.Black, PieceType.Bishop) == 1)
                {
                    int white = SquaresOf(Colour.White, PieceType.Bishop)[0];
                    int black = SquaresOf(Colour.Black, PieceType.Bishop)[0];
                    return IsLightSquare(white) == IsLightSquare(black);
                }
                return false;
            default:
                return false;
        }
    }

    public static bool IsLightSquare(int square) => ((Squares.FileOf(square) + Squares.RankOf(square)) & 1) == 1;

    /// <summary>
    /// Text rendering for debugging: rank 8 at the top, upper case for white.
    /// </summary>
    public override string ToString()
    {
        var text = new System.Text.StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            text.Append(rank + 1).Append(' ');
            for (int file = 0; file < 8; file++)
            {
                text.Append(this[file, rank].ToChar());
            }
            text.AppendLine();
        }
        text.Append("  abcdefgh");
        return text.ToString();
    }

    private static int ListIndex(Colour colour, PieceType type) => ((int)colour * PieceTypes) + (int)type;

    internal void PutPiece(int square, Piece piece)
    {
        Debug.Assert(squares[square].IsNone, "square must be empty");
        Debug.Assert(!piece.IsNone, "cannot put an empty piece");
        squares[square] = piece;
        pieceSquares[ListIndex(piece.Colour, piece.Type)].Add(square);
        PieceCount++;
        Key ^= Zobrist.Primary.Piece(piece, square);
        SecondaryKey ^= Zobrist.Secondary.Piece(piece, square);
        if (piece.Type == PieceType.Pawn)
        {
            PawnKey ^= Zobrist.Primary.Piece(piece, square);
        }
    }

    internal void RemovePiece(int square)
    {
        Piece piece = squares[square];
        Debug.Assert(!piece.IsNone, "square must be occupied");
        squares[square] = Piece.None;
        pieceSquares[ListIndex(piece.Colour, piece.Type)].Remove(square);
        PieceCount--;
        Key ^= Zobrist.Primary.Piece(piece, square);
        SecondaryKey ^= Zobrist.Secondary.Piece(piece, square);
        if (piece.Type == PieceType.Pawn)
        {
            PawnKey ^= Zobrist.Primary.Piece(piece, square);
        }
    }

    private void MovePiece(int from, int to)
    {
        Piece piece = squares[from];
        RemovePiece(from);
        PutPiece(to, piece);
    }

    /// <summary>
    /// Sort the piece square lists. Making and unmaking moves leaves the pieces in the lists in an
    /// order that depends on the moves played, and move generation follows list order, so the search
    /// normalises the order first to be a pure function of the position.
    /// </summary>
    public void NormalisePieceOrder()
    {
        foreach (List<int> list in pieceSquares)
        {
            list.Sort();
        }
    }

    /// <summary>
    /// Change the side to move, keeping the keys consistent. Used by move making and FEN parsing.
    /// </summary>
    internal void SwitchSideToMove(Colour colour)
    {
        if (colour != SideToMove)
        {
            Key ^= Zobrist.Primary.SideToMove;
            SecondaryKey ^= Zobrist.Secondary.SideToMove;
            SideToMove = colour;
        }
    }

    internal void SetCastlingRights(CastlingRights rights)
    {
        Key ^= Zobrist.Primary.Castling(CastlingRights) ^ Zobrist.Primary.Castling(rights);
        SecondaryKey ^= Zobrist.Secondary.Castling(CastlingRights) ^ Zobrist.Secondary.Castling(rights);
        CastlingRights = rights;
    }

    internal void SetEnPassantSquare(int square)
    {
        Key ^= Zobrist.Primary.EnPassant(EnPassantSquare) ^ Zobrist.Primary.EnPassant(square);
        SecondaryKey ^= Zobrist.Secondary.EnPassant(EnPassantSquare) ^ Zobrist.Secondary.EnPassant(square);
        EnPassantSquare = square;
    }

    internal void SetClocks(int halfMoveClock, int fullMoveNumber)
    {
        HalfMoveClock = Math.Clamp(halfMoveClock, 0, MaxHalfMoveClock);
        FullMoveNumber = Math.Max(1, fullMoveNumber);
    }

    /// <summary>
    /// Can a pawn of the side to move capture on the given en passant square? Only then is the
    /// square recorded, so that positions that are identical under the rules have identical keys.
    /// </summary>
    internal bool CanCaptureEnPassant(int square)
    {
        int direction = SideToMove == Colour.White ? 0x10 : -0x10;
        Piece pawn = new(SideToMove, PieceType.Pawn);
        int left = square - direction - 1;
        int right = square - direction + 1;
        return (Squares.IsOnBoard(left) && squares[left] == pawn) || (Squares.IsOnBoard(right) && squares[right] == pawn);
    }

    /// <summary>
    /// Recompute the keys from scratch. Used by tests to verify the incremental updates.
    /// </summary>
    internal (ulong Key, ulong SecondaryKey, ulong PawnKey) ComputeKeys()
    {
        ulong key = 0, secondary = 0, pawn = 0;
        foreach (int square in Squares.All)
        {
            Piece piece = squares[square];
            if (!piece.IsNone)
            {
                key ^= Zobrist.Primary.Piece(piece, square);
                secondary ^= Zobrist.Secondary.Piece(piece, square);
                if (piece.Type == PieceType.Pawn)
                {
                    pawn ^= Zobrist.Primary.Piece(piece, square);
                }
            }
        }
        if (SideToMove == Colour.Black)
        {
            key ^= Zobrist.Primary.SideToMove;
            secondary ^= Zobrist.Secondary.SideToMove;
        }
        key ^= Zobrist.Primary.Castling(CastlingRights) ^ Zobrist.Primary.EnPassant(EnPassantSquare);
        secondary ^= Zobrist.Secondary.Castling(CastlingRights) ^ Zobrist.Secondary.EnPassant(EnPassantSquare);
        return (key, secondary, pawn);
    }

    /// <summary>
    /// Everything needed to unmake a move and restore the position exactly.
    /// </summary>
    private readonly record struct UndoInfo(
        Move Move,
        CastlingRights CastlingRights,
        int EnPassantSquare,
        int HalfMoveClock,
        ulong Key,
        ulong SecondaryKey,
        ulong PawnKey);
}
