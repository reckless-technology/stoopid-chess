using System.Diagnostics;

namespace StoopidChess.Core;

/// <summary>
/// A chess position with make/unmake move support.
///
/// The pieces are held as bitboards (see <see cref="Bitboard"/>): one set of squares per piece
/// type and one per colour, plus the set of all occupied squares, so that attack detection and
/// move generation are a few word-sized operations. A 64-square array of piece types beside them
/// answers "what is on this square"; the colour of a piece is read from the colour bitboards.
/// Alongside the pieces the board maintains, incrementally as moves are made and unmade:
/// <list type="bullet">
/// <item>a Zobrist key of the whole position (pieces, side to move, castling rights and en passant
/// square) and a key of the pawn structure alone;</item>
/// <item>the castling rights, en passant square, half-move clock and move number;</item>
/// <item>the set of pieces giving check, computed on demand and cached until the next move;</item>
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

    /// <summary>
    /// The largest half-move clock value kept. Real games end at 100 (or 150 under the seventy-five
    /// move rule); the cap only stops arithmetic overflow on absurd FEN input.
    /// </summary>
    public const int MaxHalfMoveClock = 999;

    private const int Colours = 3;
    private const int PieceTypes = 7;

    private readonly Bitboard[] byType = new Bitboard[PieceTypes];
    private readonly Bitboard[] byColour = new Bitboard[Colours];
    private readonly PieceType[] squares = new PieceType[Square.Count];
    private readonly List<UndoInfo> history = new(256);
    private Bitboard occupied;
    private Bitboard checkers;
    private bool checkersKnown;

    internal Board()
    {
    }

    public static Board StartPosition() => Fen.Parse(StartFen);

    public static Board FromFen(string fen) => Fen.Parse(fen);

    public Colour SideToMove { get; private set; } = Colour.White;

    public CastlingRights CastlingRights { get; private set; }

    /// <summary>
    /// The square a pawn may capture on en passant, or <see cref="Square.None"/>.
    /// </summary>
    public Square EnPassantSquare { get; private set; } = Square.None;

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
    /// Zobrist key of the pawns alone (both colours), for caching pawn structure evaluation.
    /// </summary>
    public ulong PawnKey { get; private set; }

    public int PieceCount => occupied.Count;

    /// <summary>
    /// Number of moves made on this board since it was created (and not unmade).
    /// </summary>
    public int Ply => history.Count;

    public Move? LastMove => history.Count == 0 ? null : history[^1].Move;

    /// <summary>
    /// The piece on a square as its colour and type; (<see cref="Colour.None"/>,
    /// <see cref="PieceType.None"/>) for an empty square.
    /// </summary>
    public (Colour Colour, PieceType Type) this[Square square]
    {
        get
        {
            if (!square.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(square));
            }
            return (ColourAt(square), squares[square]);
        }
    }

    public (Colour Colour, PieceType Type) this[int file, int rank] => this[Square.At(file, rank)];

    /// <summary>
    /// All occupied squares.
    /// </summary>
    public Bitboard Occupied => occupied;

    /// <summary>
    /// The squares occupied by pieces of the colour.
    /// </summary>
    public Bitboard Pieces(Colour colour) => byColour[(int)colour];

    /// <summary>
    /// The squares occupied by pieces of the type, of both colours.
    /// </summary>
    public Bitboard Pieces(PieceType type) => byType[(int)type];

    /// <summary>
    /// The squares occupied by pieces of the given colour and type.
    /// </summary>
    public Bitboard Pieces(Colour colour, PieceType type) => byType[(int)type] & byColour[(int)colour];

    public int Count(Colour colour, PieceType type) => Pieces(colour, type).Count;

    /// <summary>
    /// The type of the piece on a square, without the validity check of the indexer; for the move
    /// generator, which only ever asks about squares it has already checked are on the board.
    /// </summary>
    internal PieceType At(Square square) => squares[square];

    /// <summary>
    /// True when each side has exactly one king, which every rule of chess assumes.
    /// A position that has been edited may violate this.
    /// </summary>
    public bool HasValidKings => Pieces(Colour.White, PieceType.King).IsSingle && Pieces(Colour.Black, PieceType.King).IsSingle;

    public Square KingSquare(Colour colour)
    {
        Bitboard kings = Pieces(colour, PieceType.King);
        return kings.IsSingle ? kings.First : throw new InvalidOperationException($"{colour} does not have exactly one king");
    }

    /// <summary>
    /// The enemy pieces attacking the side to move's king; empty when not in check. Computed on
    /// first use after a move and cached until the next one.
    /// </summary>
    public Bitboard Checkers
    {
        get
        {
            if (!checkersKnown)
            {
                Bitboard king = Pieces(SideToMove, PieceType.King);
                checkers = king.IsEmpty ? Bitboard.Empty : AttackersTo(king.First, SideToMove.Opponent());
                checkersKnown = true;
            }
            return checkers;
        }
    }

    /// <summary>
    /// Is the side to move in check?
    /// </summary>
    public bool IsInCheck => Checkers.IsNotEmpty;

    /// <summary>
    /// The pieces of the given colour that attack the square.
    /// </summary>
    public Bitboard AttackersTo(Square square, Colour by)
    {
        // The squares from which a pawn of the attacking colour reaches the square are the squares
        // a pawn of the other colour standing on it would attack
        Bitboard attackers = (Attacks.Pawn(by.Opponent(), square) & byType[(int)PieceType.Pawn])
            | (Attacks.Knight(square) & byType[(int)PieceType.Knight])
            | (Attacks.King(square) & byType[(int)PieceType.King])
            | (Attacks.Bishop(occupied, square) & (byType[(int)PieceType.Bishop] | byType[(int)PieceType.Queen]))
            | (Attacks.Rook(occupied, square) & (byType[(int)PieceType.Rook] | byType[(int)PieceType.Queen]));
        return attackers & byColour[(int)by];
    }

    /// <summary>
    /// The pieces of both colours that would attack the square if only the given squares were
    /// occupied, for exchange evaluation, which lifts pieces off the board one by one.
    /// </summary>
    public Bitboard AttackersTo(Square square, Bitboard occupied)
    {
        return ((Attacks.Pawn(Colour.Black, square) & byType[(int)PieceType.Pawn] & byColour[(int)Colour.White])
            | (Attacks.Pawn(Colour.White, square) & byType[(int)PieceType.Pawn] & byColour[(int)Colour.Black])
            | (Attacks.Knight(square) & byType[(int)PieceType.Knight])
            | (Attacks.King(square) & byType[(int)PieceType.King])
            | (Attacks.Bishop(occupied, square) & (byType[(int)PieceType.Bishop] | byType[(int)PieceType.Queen]))
            | (Attacks.Rook(occupied, square) & (byType[(int)PieceType.Rook] | byType[(int)PieceType.Queen])))
            & occupied;
    }

    /// <summary>
    /// Is the square attacked by any piece of the given colour?
    /// </summary>
    public bool IsAttacked(Square square, Colour by) => AttackersTo(square, by).IsNotEmpty;

    public string ToFen() => Fen.Write(this);

    /// <summary>
    /// A deep copy including the move history.
    /// </summary>
    public Board Clone()
    {
        Board copy = new();
        Array.Copy(byType, copy.byType, byType.Length);
        Array.Copy(byColour, copy.byColour, byColour.Length);
        Array.Copy(squares, copy.squares, squares.Length);
        copy.history.AddRange(history);
        copy.occupied = occupied;
        copy.checkers = checkers;
        copy.checkersKnown = checkersKnown;
        copy.SideToMove = SideToMove;
        copy.CastlingRights = CastlingRights;
        copy.EnPassantSquare = EnPassantSquare;
        copy.HalfMoveClock = HalfMoveClock;
        copy.FullMoveNumber = FullMoveNumber;
        copy.Key = Key;
        copy.PawnKey = PawnKey;
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
                return (byType[(int)PieceType.Bishop] | byType[(int)PieceType.Knight]).IsNotEmpty;
            case 4:
                Bitboard white = Pieces(Colour.White, PieceType.Bishop);
                Bitboard black = Pieces(Colour.Black, PieceType.Bishop);
                return white.IsNotEmpty && black.IsNotEmpty && (white & Bitboard.LightSquares).IsNotEmpty == (black & Bitboard.LightSquares).IsNotEmpty;
            default:
                return false;
        }
    }

    /// <summary>
    /// Does the side have any piece other than pawns and the king? Null-move pruning is unsafe
    /// without them, because of zugzwang.
    /// </summary>
    public bool HasNonPawnMaterial(Colour colour)
    {
        return (byColour[(int)colour] & ~(byType[(int)PieceType.Pawn] | byType[(int)PieceType.King])).IsNotEmpty;
    }

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
                (Colour colour, PieceType type) = this[file, rank];
                text.Append(type.ToChar(colour));
            }
            text.AppendLine();
        }
        text.Append("  abcdefgh");
        return text.ToString();
    }

    /// <summary>
    /// The colour of the piece on a valid square, or <see cref="Colour.None"/> if it is empty.
    /// </summary>
    private Colour ColourAt(Square square)
    {
        return byColour[(int)Colour.White].Contains(square) ? Colour.White
            : byColour[(int)Colour.Black].Contains(square) ? Colour.Black
            : Colour.None;
    }

    internal void PutPiece(Square square, Colour colour, PieceType type)
    {
        Debug.Assert(squares[square] == PieceType.None, "square must be empty");
        Debug.Assert(type != PieceType.None && colour != Colour.None, "cannot put an empty piece");
        var bit = Bitboard.Of(square);
        byType[(int)type] |= bit;
        byColour[(int)colour] |= bit;
        occupied |= bit;
        squares[square] = type;
        checkersKnown = false;
        ToggleKeys(colour, type, square);
    }

    internal void RemovePiece(Square square, Colour colour, PieceType type)
    {
        Debug.Assert(squares[square] == type && byColour[(int)colour].Contains(square), "the piece is not there");
        var bit = Bitboard.Of(square);
        byType[(int)type] &= ~bit;
        byColour[(int)colour] &= ~bit;
        occupied &= ~bit;
        squares[square] = PieceType.None;
        checkersKnown = false;
        ToggleKeys(colour, type, square);
    }

    private void MovePiece(Square from, Square to, Colour colour, PieceType type)
    {
        Debug.Assert(squares[from] == type && byColour[(int)colour].Contains(from), "the piece is not there");
        Debug.Assert(squares[to] == PieceType.None, "destination square must be empty");
        Bitboard fromTo = Bitboard.Of(from) | Bitboard.Of(to);
        byType[(int)type] ^= fromTo;
        byColour[(int)colour] ^= fromTo;
        occupied ^= fromTo;
        squares[from] = PieceType.None;
        squares[to] = type;
        checkersKnown = false;
        ulong key = Zobrist.Piece(colour, type, from) ^ Zobrist.Piece(colour, type, to);
        Key ^= key;
        if (type == PieceType.Pawn)
        {
            PawnKey ^= key;
        }
    }

    /// <summary>
    /// Adding and removing a piece make the same change to the keys.
    /// </summary>
    private void ToggleKeys(Colour colour, PieceType type, Square square)
    {
        ulong key = Zobrist.Piece(colour, type, square);
        Key ^= key;
        if (type == PieceType.Pawn)
        {
            PawnKey ^= key;
        }
    }

    /// <summary>
    /// Change the side to move, keeping the keys consistent. Used by move making and FEN parsing.
    /// </summary>
    internal void SwitchSideToMove(Colour colour)
    {
        if (colour != SideToMove)
        {
            Key ^= Zobrist.SideToMove;
            SideToMove = colour;
            checkersKnown = false;
        }
    }

    internal void SetCastlingRights(CastlingRights rights)
    {
        Key ^= Zobrist.Castling(CastlingRights) ^ Zobrist.Castling(rights);
        CastlingRights = rights;
    }

    internal void SetEnPassantSquare(Square square)
    {
        Key ^= Zobrist.EnPassant(EnPassantSquare) ^ Zobrist.EnPassant(square);
        EnPassantSquare = square;
    }

    internal void SetClocks(int halfMoveClock, int fullMoveNumber)
    {
        HalfMoveClock = Math.Clamp(halfMoveClock, 0, MaxHalfMoveClock);
        FullMoveNumber = Math.Max(1, fullMoveNumber);
    }

    /// <summary>
    /// The square index step a pawn of the colour moves by: up the board for white, down for black.
    /// </summary>
    public static int PawnDirection(Colour colour) => colour == Colour.White ? 8 : -8;

    /// <summary>
    /// Does a pawn of the given colour attack the square? Also decides whether an en passant square
    /// is worth recording: only if a pawn could actually capture there, so that positions that are
    /// identical under the rules have identical keys.
    /// </summary>
    internal bool IsAttackedByPawn(Square square, Colour by) => (Attacks.Pawn(by.Opponent(), square) & Pieces(by, PieceType.Pawn)).IsNotEmpty;

    /// <summary>
    /// Recompute the keys from scratch. Used by tests to verify the incremental updates.
    /// </summary>
    internal (ulong Key, ulong PawnKey) ComputeKeys()
    {
        ulong key = 0, pawn = 0;
        foreach (Colour colour in ColourExtensions.Sides)
        {
            foreach (Square square in byColour[(int)colour])
            {
                PieceType type = squares[square];
                key ^= Zobrist.Piece(colour, type, square);
                if (type == PieceType.Pawn)
                {
                    pawn ^= Zobrist.Piece(colour, type, square);
                }
            }
        }
        if (SideToMove == Colour.Black)
        {
            key ^= Zobrist.SideToMove;
        }
        key ^= Zobrist.Castling(CastlingRights) ^ Zobrist.EnPassant(EnPassantSquare);
        return (key, pawn);
    }

    /// <summary>
    /// Check that the bitboards, the square array and the occupancy agree. Used by tests.
    /// </summary>
    internal bool IsConsistent()
    {
        Bitboard all = Bitboard.Empty;
        foreach (Square square in Square.All)
        {
            PieceType type = squares[square];
            var bit = Bitboard.Of(square);
            bool white = byColour[(int)Colour.White].Contains(square);
            bool black = byColour[(int)Colour.Black].Contains(square);
            if (type == PieceType.None)
            {
                if ((occupied & bit).IsNotEmpty || white || black)
                {
                    return false;
                }
                continue;
            }
            all |= bit;
            if ((byType[(int)type] & bit).IsEmpty || white == black)
            {
                return false;
            }
        }
        Bitboard typeUnion = Bitboard.Empty;
        for (int type = 1; type < PieceTypes; type++)
        {
            typeUnion |= byType[type];
        }
        return all == occupied && typeUnion == occupied && (byColour[(int)Colour.White] | byColour[(int)Colour.Black]) == occupied
            && byColour[(int)Colour.None].IsEmpty && byType[(int)PieceType.None].IsEmpty;
    }

    /// <summary>
    /// Everything needed to unmake a move and restore the position exactly.
    /// </summary>
    private readonly record struct UndoInfo(
        Move Move,
        CastlingRights CastlingRights,
        Square EnPassantSquare,
        int HalfMoveClock,
        ulong Key,
        ulong PawnKey);
}
