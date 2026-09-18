using System.Diagnostics;
using System.Numerics;

namespace StoopidChess.Core;

/// <summary>
/// A chess position with make/unmake move support.
///
/// The pieces are held as bitboards (see <see cref="Bitboards"/>): one set of squares per piece
/// type and one per colour, plus the set of all occupied squares, so that attack detection and
/// move generation are a few word-sized operations. A 64-square array beside them answers "what is
/// on this square". Alongside the pieces the board maintains, incrementally as moves are made and
/// unmade:
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

    private readonly ulong[] byType = new ulong[PieceTypes];
    private readonly ulong[] byColour = new ulong[Colours];
    private readonly Piece[] squares = new Piece[Squares.Count];
    private readonly List<UndoInfo> history = new(256);
    private ulong occupied;
    private ulong checkers;
    private bool checkersKnown;

    internal Board()
    {
        Array.Fill(squares, Piece.None);
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
    /// Zobrist key of the pawns alone (both colours), for caching pawn structure evaluation.
    /// </summary>
    public ulong PawnKey { get; private set; }

    public int PieceCount => BitOperations.PopCount(occupied);

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
    /// All occupied squares.
    /// </summary>
    public ulong Occupied => occupied;

    /// <summary>
    /// The squares occupied by pieces of the colour.
    /// </summary>
    public ulong Pieces(Colour colour) => byColour[(int)colour];

    /// <summary>
    /// The squares occupied by pieces of the type, of both colours.
    /// </summary>
    public ulong Pieces(PieceType type) => byType[(int)type];

    /// <summary>
    /// The squares occupied by pieces of the given colour and type.
    /// </summary>
    public ulong Pieces(Colour colour, PieceType type) => byType[(int)type] & byColour[(int)colour];

    public int Count(Colour colour, PieceType type) => BitOperations.PopCount(Pieces(colour, type));

    /// <summary>
    /// The piece on a square, without the range check of the indexer; for the move generator and
    /// evaluator, which only ever ask about squares they have already checked are on the board.
    /// </summary>
    internal Piece At(int square) => squares[square];

    /// <summary>
    /// True when each side has exactly one king, which every rule of chess assumes.
    /// A position that has been edited may violate this.
    /// </summary>
    public bool HasValidKings => Count(Colour.White, PieceType.King) == 1 && Count(Colour.Black, PieceType.King) == 1;

    public int KingSquare(Colour colour)
    {
        ulong kings = Pieces(colour, PieceType.King);
        return Bitboards.IsSingle(kings) ? BitOperations.TrailingZeroCount(kings) : throw new InvalidOperationException($"{colour} does not have exactly one king");
    }

    /// <summary>
    /// The enemy pieces attacking the side to move's king; empty when not in check. Computed on
    /// first use after a move and cached until the next one.
    /// </summary>
    public ulong Checkers
    {
        get
        {
            if (!checkersKnown)
            {
                ulong king = Pieces(SideToMove, PieceType.King);
                checkers = king == 0 ? 0 : AttackersTo(BitOperations.TrailingZeroCount(king), SideToMove.Opponent());
                checkersKnown = true;
            }
            return checkers;
        }
    }

    /// <summary>
    /// Is the side to move in check?
    /// </summary>
    public bool IsInCheck => Checkers != 0;

    /// <summary>
    /// The pieces of the given colour that attack the square.
    /// </summary>
    public ulong AttackersTo(int square, Colour by)
    {
        // The squares from which a pawn of the attacking colour reaches the square are the squares
        // a pawn of the other colour standing on it would attack
        ulong attackers = (Attacks.Pawn(by.Opponent(), square) & byType[(int)PieceType.Pawn])
            | (Attacks.Knight(square) & byType[(int)PieceType.Knight])
            | (Attacks.King(square) & byType[(int)PieceType.King])
            | (Attacks.Bishop(occupied, square) & (byType[(int)PieceType.Bishop] | byType[(int)PieceType.Queen]))
            | (Attacks.Rook(occupied, square) & (byType[(int)PieceType.Rook] | byType[(int)PieceType.Queen]));
        return attackers & byColour[(int)by];
    }

    /// <summary>
    /// Is the square attacked by any piece of the given colour?
    /// </summary>
    public bool IsAttacked(int square, Colour by) => AttackersTo(square, by) != 0;

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
                return (byType[(int)PieceType.Bishop] | byType[(int)PieceType.Knight]) != 0;
            case 4:
                ulong white = Pieces(Colour.White, PieceType.Bishop);
                ulong black = Pieces(Colour.Black, PieceType.Bishop);
                return white != 0 && black != 0 && ((white & Bitboards.LightSquares) != 0) == ((black & Bitboards.LightSquares) != 0);
            default:
                return false;
        }
    }

    public static bool IsLightSquare(int square) => Bitboards.Contains(Bitboards.LightSquares, square);

    /// <summary>
    /// Does the side have any piece other than pawns and the king? Null-move pruning is unsafe
    /// without them, because of zugzwang.
    /// </summary>
    public bool HasNonPawnMaterial(Colour colour)
    {
        return (byColour[(int)colour] & ~(byType[(int)PieceType.Pawn] | byType[(int)PieceType.King])) != 0;
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
                text.Append(this[file, rank].ToChar());
            }
            text.AppendLine();
        }
        text.Append("  abcdefgh");
        return text.ToString();
    }

    internal void PutPiece(int square, Piece piece)
    {
        Debug.Assert(squares[square].IsNone, "square must be empty");
        Debug.Assert(!piece.IsNone, "cannot put an empty piece");
        ulong bit = Bitboards.Of(square);
        byType[(int)piece.Type] |= bit;
        byColour[(int)piece.Colour] |= bit;
        occupied |= bit;
        squares[square] = piece;
        checkersKnown = false;
        ToggleKeys(piece, square);
    }

    internal void RemovePiece(int square)
    {
        Piece piece = squares[square];
        Debug.Assert(!piece.IsNone, "square must be occupied");
        ulong bit = Bitboards.Of(square);
        byType[(int)piece.Type] &= ~bit;
        byColour[(int)piece.Colour] &= ~bit;
        occupied &= ~bit;
        squares[square] = Piece.None;
        checkersKnown = false;
        ToggleKeys(piece, square);
    }

    /// <summary>
    /// Adding and removing a piece make the same change to the keys.
    /// </summary>
    private void ToggleKeys(Piece piece, int square)
    {
        ulong key = Zobrist.Piece(piece, square);
        Key ^= key;
        if (piece.Type == PieceType.Pawn)
        {
            PawnKey ^= key;
        }
    }

    private void MovePiece(int from, int to)
    {
        Piece piece = squares[from];
        Debug.Assert(!piece.IsNone, "source square must be occupied");
        Debug.Assert(squares[to].IsNone, "destination square must be empty");
        ulong fromTo = Bitboards.Of(from) | Bitboards.Of(to);
        byType[(int)piece.Type] ^= fromTo;
        byColour[(int)piece.Colour] ^= fromTo;
        occupied ^= fromTo;
        squares[from] = Piece.None;
        squares[to] = piece;
        checkersKnown = false;
        ulong key = Zobrist.Piece(piece, from) ^ Zobrist.Piece(piece, to);
        Key ^= key;
        if (piece.Type == PieceType.Pawn)
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

    internal void SetEnPassantSquare(int square)
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
    internal bool IsAttackedByPawn(int square, Colour by) => (Attacks.Pawn(by.Opponent(), square) & Pieces(by, PieceType.Pawn)) != 0;

    /// <summary>
    /// Recompute the keys from scratch. Used by tests to verify the incremental updates.
    /// </summary>
    internal (ulong Key, ulong PawnKey) ComputeKeys()
    {
        ulong key = 0, pawn = 0;
        for (int square = 0; square < Squares.Count; square++)
        {
            Piece piece = squares[square];
            if (!piece.IsNone)
            {
                key ^= Zobrist.Piece(piece, square);
                if (piece.Type == PieceType.Pawn)
                {
                    pawn ^= Zobrist.Piece(piece, square);
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
        ulong all = 0;
        for (int square = 0; square < Squares.Count; square++)
        {
            Piece piece = squares[square];
            ulong bit = Bitboards.Of(square);
            if (piece.IsNone)
            {
                if ((occupied & bit) != 0)
                {
                    return false;
                }
                continue;
            }
            all |= bit;
            if ((byType[(int)piece.Type] & bit) == 0 || (byColour[(int)piece.Colour] & bit) == 0)
            {
                return false;
            }
        }
        ulong typeUnion = 0;
        for (int type = 1; type < PieceTypes; type++)
        {
            typeUnion |= byType[type];
        }
        return all == occupied && typeUnion == occupied && (byColour[(int)Colour.White] | byColour[(int)Colour.Black]) == occupied
            && (byColour[(int)Colour.White] & byColour[(int)Colour.Black]) == 0 && byColour[(int)Colour.None] == 0 && byType[(int)PieceType.None] == 0;
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
        ulong PawnKey);
}
