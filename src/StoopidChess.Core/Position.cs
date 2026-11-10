using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace StoopidChess.Core;

/// <summary>
/// A chess position with make/unmake move support.
///
/// The pieces are held as bitboards (see <see cref="Bitboard"/>): one set of squares per piece
/// type and one per color, plus the set of all occupied squares, so that attack detection and
/// move generation are a few word-sized operations. A 64-square array of piece types beside them
/// answers "what is on this square"; the color of a piece is read from the color bitboards.
/// Alongside the pieces the board maintains, incrementally as moves are made and unmade:
/// <list type="bullet">
/// <item>a Zobrist key of the whole position (pieces, side to move, castling rights and en passant
/// square) and a key of the pawn structure alone;</item>
/// <item>the castling rights, en passant square, half-move clock and move number;</item>
/// <item>the set of pieces giving check, worked out once per move;</item>
/// <item>a history of made moves so that they can be unmade in strict last-in first-out order, and
/// so that repetitions can be detected.</item>
/// </list>
/// A position is created from FEN (<see cref="FromFen"/>, <see cref="Start"/>) and is then changed
/// only by <see cref="MakeMove"/> and <see cref="UnmakeMove"/>.
///
/// The class is in two files: this one holds the board and makes and unmakes moves, and
/// Position.MoveGeneration.cs generates the legal moves.
/// </summary>
public sealed partial class Position
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private const int Colors = 2;
    private const int PieceTypes = 7;

    private readonly Bitboard[] byType = new Bitboard[PieceTypes];
    private readonly Bitboard[] byColor = new Bitboard[Colors];
    private readonly Piece[] squares = new Piece[Square.Count];
    private readonly List<UndoInfo> history = new(256);
    private Bitboard occupied;
    private Bitboard checkers;

    internal Position()
    {
    }

    public static Position Start() => Fen.Parse(StartFen);

    public static Position FromFen(string fen) => Fen.Parse(fen);

    public Color SideToMove { get; private set; } = Color.White;

    public CastlingRights CastlingRights { get; private set; }

    /// <summary>
    /// The square a pawn may capture on en passant, or <see cref="Square.None"/>.
    /// </summary>
    public Square EnPassantSquare { get; private set; } = Square.None;

    /// <summary>
    /// Plies since the last capture or pawn move (for the fifty-move rule). En passant resets it, being
    /// both; castling does not, being neither.
    /// </summary>
    public int HalfMoveClock { get; private set; }

    public int FullMoveNumber { get; private set; } = 1;

    /// <summary>
    /// Primary Zobrist key of the position, suitable for indexing a hash table.
    /// </summary>
    public ulong Key { get; private set; }

    /// <summary>
    /// Zobrist key of the pawns alone (both colors), for caching pawn structure evaluation.
    /// </summary>
    public ulong PawnKey { get; private set; }

    public int PieceCount => occupied.Count;

    /// <summary>
    /// Number of moves made on this board since it was created (and not unmade).
    /// </summary>
    public int Ply => history.Count;

    public Move? LastMove => history.Count == 0 ? null : history[^1].Move;

    /// <summary>
    /// The piece on a square as its color and type. An empty square's type is
    /// <see cref="Piece.None"/>, and its color means nothing (it is the default, white).
    /// </summary>
    public (Color Color, Piece Type) this[Square square]
    {
        get
        {
            if (!square.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(square));
            }
            return (ColorAt(square), squares[square]);
        }
    }

    public (Color Color, Piece Type) this[int file, int rank] => this[Square.At(file, rank)];

    /// <summary>
    /// All occupied squares.
    /// </summary>
    public Bitboard Occupied => occupied;

    /// <summary>
    /// The squares occupied by pieces of the color.
    /// </summary>
    public Bitboard Pieces(Color color) => byColor[(int)color];

    /// <summary>
    /// The squares occupied by pieces of the type, of both colors.
    /// </summary>
    public Bitboard Pieces(Piece type) => byType[(int)type];

    /// <summary>
    /// The squares occupied by pieces of the given color and type.
    /// </summary>
    public Bitboard Pieces(Color color, Piece type) => byType[(int)type] & byColor[(int)color];

    public int Count(Color color, Piece type) => Pieces(color, type).Count;

    /// <summary>
    /// The type of the piece on a square, without the validity check of the indexer; for the move
    /// generator, which only ever asks about squares it has already checked are on the board.
    /// </summary>
    internal Piece At(Square square) => squares[square];

    /// <summary>
    /// True when each side has exactly one king, which every rule of chess assumes. FEN parsing
    /// refuses a board without, so every position has them.
    /// </summary>
    public bool HasValidKings => Pieces(Color.White, Piece.King).IsSingle && Pieces(Color.Black, Piece.King).IsSingle;

    /// <summary>
    /// Can play proceed from this position? Each side must have exactly one king, no pawn may stand
    /// on the first or last rank, and the side that has just moved must not have left its king in
    /// check (which also rules out adjacent kings).
    /// </summary>
    public bool IsValid => HasValidKings && !HasPawnsOnBackRanks && !IsAttacked(KingSquare(SideToMove.Opponent()), SideToMove);

    /// <summary>
    /// A pawn on its own first rank or on the promotion rank cannot exist in a real game, and pawn
    /// move generation relies on that.
    /// </summary>
    public bool HasPawnsOnBackRanks => (Pieces(Piece.Pawn) & (Bitboard.Rank1 | Bitboard.Rank8)).IsNotEmpty;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Square KingSquare(Color color)
    {
        Bitboard kings = Pieces(color, Piece.King);
        return kings.IsSingle ? kings.First : throw new InvalidOperationException($"{color} does not have exactly one king");
    }

    /// <summary>
    /// The enemy pieces attacking the side to move's king; empty when not in check. Worked out once
    /// whenever the position changes (at the end of every move, and by FEN parsing), and
    /// restored from the undo record when a move is unmade.
    /// </summary>
    public Bitboard Checkers => checkers;

    /// <summary>
    /// Is the side to move in check?
    /// </summary>
    public bool IsInCheck => Checkers.IsNotEmpty;

    /// <summary>
    /// The pieces of both colors that attack the square when only the given squares are occupied.
    /// With <see cref="Occupied"/> that is every attacker; exchange evaluation passes an occupancy
    /// with pieces lifted off, one by one, to find the attackers uncovered behind them. Each piece's
    /// attacks are read backwards from the square: a knight attacks the square from exactly the
    /// squares a knight on it would attack, and a white pawn from the squares a black pawn on it
    /// would attack.
    /// </summary>
    public Bitboard AttackersTo(Square square, Bitboard occupied)
    {
        return ((Attacks.Pawn(Color.Black, square) & byType[(int)Piece.Pawn] & byColor[(int)Color.White])
            | (Attacks.Pawn(Color.White, square) & byType[(int)Piece.Pawn] & byColor[(int)Color.Black])
            | (Attacks.Knight(square) & byType[(int)Piece.Knight])
            | (Attacks.King(square) & byType[(int)Piece.King])
            | (Attacks.Bishop(occupied, square) & (byType[(int)Piece.Bishop] | byType[(int)Piece.Queen]))
            | (Attacks.Rook(occupied, square) & (byType[(int)Piece.Rook] | byType[(int)Piece.Queen])))
            & occupied;
    }

    /// <summary>
    /// Is the square attacked by any piece of the given color?
    /// </summary>
    public bool IsAttacked(Square square, Color by) => (AttackersTo(square, occupied) & byColor[(int)by]).IsNotEmpty;

    /// <summary>
    /// Work out <see cref="Checkers"/> for the side to move.
    /// </summary>
    internal void UpdateCheckers()
    {
        checkers = AttackersTo(KingSquare(SideToMove), occupied) & byColor[(int)SideToMove.Opponent()];
    }

    public string ToFen() => Fen.Write(this);

    /// <summary>
    /// A deep copy including the move history.
    /// </summary>
    public Position Clone()
    {
        Position copy = new();
        Array.Copy(byType, copy.byType, byType.Length);
        Array.Copy(byColor, copy.byColor, byColor.Length);
        Array.Copy(squares, copy.squares, squares.Length);
        copy.history.AddRange(history);
        copy.occupied = occupied;
        copy.checkers = checkers;
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
    /// king, or king and bishop against king and bishop with both bishops on the same square color.
    /// </summary>
    public bool IsInsufficientMaterial()
    {
        switch (PieceCount)
        {
            case 2:
                return true;
            case 3:
                return (byType[(int)Piece.Bishop] | byType[(int)Piece.Knight]).IsNotEmpty;
            case 4:
                Bitboard white = Pieces(Color.White, Piece.Bishop);
                Bitboard black = Pieces(Color.Black, Piece.Bishop);
                return white.IsNotEmpty && black.IsNotEmpty && (white & Bitboard.LightSquares).IsNotEmpty == (black & Bitboard.LightSquares).IsNotEmpty;
            default:
                return false;
        }
    }

    /// <summary>
    /// Does the side have any piece other than pawns and the king? Null-move pruning is unsafe
    /// without them, because of zugzwang.
    /// </summary>
    public bool HasNonPawnMaterial(Color color)
    {
        return (byColor[(int)color] & ~(byType[(int)Piece.Pawn] | byType[(int)Piece.King])).IsNotEmpty;
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
                (Color color, Piece type) = this[file, rank];
                text.Append(type.ToChar(color));
            }
            text.AppendLine();
        }
        text.Append("  abcdefgh");
        return text.ToString();
    }

    /// <summary>
    /// The color of the piece on a valid square; white for an empty one.
    /// </summary>
    private Color ColorAt(Square square) => byColor[(int)Color.Black].Contains(square) ? Color.Black : Color.White;

    internal void PutPiece(Square square, Color color, Piece type)
    {
        Debug.Assert(squares[square] == Piece.None, "square must be empty");
        Debug.Assert(type != Piece.None, "cannot put an empty piece");
        var bit = Bitboard.Of(square);
        byType[(int)type] |= bit;
        byColor[(int)color] |= bit;
        occupied |= bit;
        squares[square] = type;
        ToggleKeys(color, type, square);
    }

    internal void RemovePiece(Square square, Color color, Piece type)
    {
        Debug.Assert(squares[square] == type && byColor[(int)color].Contains(square), "the piece is not there");
        var bit = Bitboard.Of(square);
        byType[(int)type] &= ~bit;
        byColor[(int)color] &= ~bit;
        occupied &= ~bit;
        squares[square] = Piece.None;
        ToggleKeys(color, type, square);
    }

    private void MovePiece(Square from, Square to, Color color, Piece type)
    {
        Debug.Assert(squares[from] == type && byColor[(int)color].Contains(from), "the piece is not there");
        Debug.Assert(squares[to] == Piece.None, "destination square must be empty");
        Bitboard fromTo = Bitboard.Of(from) | Bitboard.Of(to);
        byType[(int)type] ^= fromTo;
        byColor[(int)color] ^= fromTo;
        occupied ^= fromTo;
        squares[from] = Piece.None;
        squares[to] = type;
        ulong key = Zobrist.Piece(color, type, from) ^ Zobrist.Piece(color, type, to);
        Key ^= key;
        if (type == Piece.Pawn)
        {
            PawnKey ^= key;
        }
    }

    /// <summary>
    /// Adding and removing a piece make the same change to the keys.
    /// </summary>
    private void ToggleKeys(Color color, Piece type, Square square)
    {
        ulong key = Zobrist.Piece(color, type, square);
        Key ^= key;
        if (type == Piece.Pawn)
        {
            PawnKey ^= key;
        }
    }

    /// <summary>
    /// Change the side to move, keeping the keys consistent. Used by move making and FEN parsing.
    /// </summary>
    internal void SwitchSideToMove(Color color)
    {
        if (color != SideToMove)
        {
            Key ^= Zobrist.SideToMove;
            SideToMove = color;
        }
    }

    internal void ApplyCastlingRights(CastlingRights rights)
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
        HalfMoveClock = halfMoveClock;
        FullMoveNumber = Math.Max(1, fullMoveNumber);
    }

    /// <summary>
    /// The square index step a pawn of the color moves by: up the board for white, down for black.
    /// </summary>
    public static int PawnDirection(Color color) => color == Color.White ? 8 : -8;

    /// <summary>
    /// Does a pawn of the given color attack the square? Also decides whether an en passant square
    /// is worth recording: only if an enemy pawn stands ready to capture there. A double push no
    /// pawn can answer then leaves the same key as any other move to that position, which is what
    /// repetition detection needs. (A capture that a pin would forbid still records the square, so
    /// keys match in nearly every case the rules call identical, not quite all.)
    /// </summary>
    internal bool IsAttackedByPawn(Square square, Color by) => (Attacks.Pawn(by.Opponent(), square) & Pieces(by, Piece.Pawn)).IsNotEmpty;

    /// <summary>
    /// Recompute the keys from scratch. Used by tests to verify the incremental updates.
    /// </summary>
    internal (ulong Key, ulong PawnKey) ComputeKeys()
    {
        ulong key = 0, pawn = 0;
        foreach (Color color in ColorExtensions.Sides)
        {
            foreach (Square square in byColor[(int)color])
            {
                Piece type = squares[square];
                key ^= Zobrist.Piece(color, type, square);
                if (type == Piece.Pawn)
                {
                    pawn ^= Zobrist.Piece(color, type, square);
                }
            }
        }
        if (SideToMove == Color.Black)
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
            Piece type = squares[square];
            var bit = Bitboard.Of(square);
            bool white = byColor[(int)Color.White].Contains(square);
            bool black = byColor[(int)Color.Black].Contains(square);
            if (type == Piece.None)
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
        return all == occupied && typeUnion == occupied && (byColor[(int)Color.White] | byColor[(int)Color.Black]) == occupied
            && (byColor[(int)Color.White] & byColor[(int)Color.Black]).IsEmpty && byType[(int)Piece.None].IsEmpty;
    }

    // Making and unmaking moves. The search walks the game tree on this one position: it makes a
    // move, searches the position that results, and unmakes the move to try the next, millions of
    // times a second, rather than copying the board for every child (copy-make). Make changes the
    // pieces, and XORs the same Zobrist keys in and out that it changes, so the keys stay right
    // without being recomputed. What cannot be worked back out of the move - the castling rights,
    // en passant square and half-move clock it replaced, the keys and the checkers - goes onto the
    // history stack as an UndoInfo, and unmake puts it back. After a move is made and unmade the
    // position, including every key, is identical to what it was before; the search relies on this.
    // See https://www.chessprogramming.org/Make_Move and https://www.chessprogramming.org/Unmake_Move

    /// <summary>
    /// Make a move for the side to move. The move must be at least pseudo-legal: the pieces it names
    /// must be where it says they are. No legality check is made here.
    /// </summary>
    public void MakeMove(Move move)
    {
        Debug.Assert(!move.IsNone, "cannot make the null move");
        Debug.Assert(squares[move.From] == move.Piece && byColor[(int)SideToMove].Contains(move.From), "moving piece mismatch");

        history.Add(new UndoInfo(move, CastlingRights, EnPassantSquare, HalfMoveClock, Key, PawnKey, checkers));

        Color us = SideToMove;
        Color them = us.Opponent();

        SetEnPassantSquare(Square.None);
        // Castling moves the king and captures nothing, so it counts towards the fifty-move rule
        HalfMoveClock = move.Piece == Piece.Pawn || move.IsCapture ? 0 : HalfMoveClock + 1;

        switch (move.Kind)
        {
            case MoveKind.Normal:
                if (move.IsCapture)
                {
                    RemovePiece(move.To, them, move.Captured);
                }
                MovePiece(move.From, move.To, us, move.Piece);
                break;

            case MoveKind.DoublePawnPush:
                MovePiece(move.From, move.To, us, Piece.Pawn);
                // Record the en passant square only if an enemy pawn could actually capture there
                var passed = Square.Unchecked((move.From.Index + move.To.Index) >> 1);
                if (IsAttackedByPawn(passed, them))
                {
                    SetEnPassantSquare(passed);
                }
                break;

            case MoveKind.EnPassant:
                RemovePiece(EnPassantCaptureSquare(move), them, Piece.Pawn);
                MovePiece(move.From, move.To, us, Piece.Pawn);
                break;

            case MoveKind.Promotion:
                if (move.IsCapture)
                {
                    RemovePiece(move.To, them, move.Captured);
                }
                RemovePiece(move.From, us, Piece.Pawn);
                PutPiece(move.To, us, move.Promotion);
                break;

            case MoveKind.CastleKingSide:
            case MoveKind.CastleQueenSide:
                MovePiece(move.From, move.To, us, Piece.King);
                (Square rookFrom, Square rookTo) = CastlingRookSquares(move);
                MovePiece(rookFrom, rookTo, us, Piece.Rook);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(move), move.Kind, "unknown move kind");
        }

        // Moving the king or a rook off its home square, or capturing a rook on its home square,
        // forfeits the corresponding castling rights.
        CastlingRights lost = CastlingRightsExtensions.LostBySquare(move.From) | CastlingRightsExtensions.LostBySquare(move.To);
        if ((CastlingRights & lost) != CastlingRights.None)
        {
            ApplyCastlingRights(CastlingRights & ~lost);
        }

        if (us == Color.Black)
        {
            FullMoveNumber++;
        }
        SwitchSideToMove(them);
        UpdateCheckers();
    }

    /// <summary>
    /// Pass the turn without moving, for null-move pruning in the search. Unmade like any other move
    /// with <see cref="UnmakeMove"/>. Repetitions are not detected across a null move: the half-move
    /// clock, which bounds how far back <see cref="RepetitionCount"/> looks, starts again from zero,
    /// as after a capture. A position reached by passing is not a real repetition, and treating it as
    /// one would let a null-move line score a draw that no game could reach.
    /// </summary>
    public void MakeNullMove()
    {
        history.Add(new UndoInfo(Move.None, CastlingRights, EnPassantSquare, HalfMoveClock, Key, PawnKey, checkers));
        SetEnPassantSquare(Square.None);
        HalfMoveClock = 0;
        SwitchSideToMove(SideToMove.Opponent());
        UpdateCheckers();
    }

    /// <summary>
    /// True if the last move made was a null move.
    /// </summary>
    public bool LastMoveWasNull => history.Count != 0 && history[^1].Move.IsNone;

    /// <summary>
    /// Unmake the most recently made move (or null move).
    /// </summary>
    public void UnmakeMove()
    {
        if (history.Count == 0)
        {
            throw new InvalidOperationException("no move to unmake");
        }
        UndoInfo undo = history[^1];
        history.RemoveAt(history.Count - 1);
        Move move = undo.Move;

        Color them = SideToMove;
        Color us = them.Opponent();
        SideToMove = us;
        if (move.IsNone)
        {
            RestoreState(undo);
            return;
        }
        if (us == Color.Black)
        {
            FullMoveNumber--;
        }

        switch (move.Kind)
        {
            case MoveKind.Normal:
            case MoveKind.DoublePawnPush:
                MovePiece(move.To, move.From, us, move.Piece);
                if (move.IsCapture)
                {
                    PutPiece(move.To, them, move.Captured);
                }
                break;

            case MoveKind.EnPassant:
                MovePiece(move.To, move.From, us, Piece.Pawn);
                PutPiece(EnPassantCaptureSquare(move), them, Piece.Pawn);
                break;

            case MoveKind.Promotion:
                RemovePiece(move.To, us, move.Promotion);
                PutPiece(move.From, us, Piece.Pawn);
                if (move.IsCapture)
                {
                    PutPiece(move.To, them, move.Captured);
                }
                break;

            case MoveKind.CastleKingSide:
            case MoveKind.CastleQueenSide:
                MovePiece(move.To, move.From, us, Piece.King);
                (Square rookFrom, Square rookTo) = CastlingRookSquares(move);
                MovePiece(rookTo, rookFrom, us, Piece.Rook);
                break;

            default:
                throw new InvalidOperationException("corrupt move history");
        }

        RestoreState(undo);
    }

    /// <summary>
    /// The keys were changed by the piece movements of the unmake; restore the saved values directly.
    /// </summary>
    private void RestoreState(in UndoInfo undo)
    {
        CastlingRights = undo.CastlingRights;
        EnPassantSquare = undo.EnPassantSquare;
        HalfMoveClock = undo.HalfMoveClock;
        Key = undo.Key;
        PawnKey = undo.PawnKey;
        checkers = undo.Checkers;
    }

    /// <summary>
    /// Where the rook of a castling move starts and ends: h1 to f1 and a1 to d1 for white, h8 to f8
    /// and a8 to d8 for black.
    /// </summary>
    private static (Square From, Square To) CastlingRookSquares(Move move) => (move.Kind, move.From == Square.E1) switch
    {
        (MoveKind.CastleKingSide, true) => (Square.H1, Square.F1),
        (MoveKind.CastleQueenSide, true) => (Square.A1, Square.D1),
        (MoveKind.CastleKingSide, false) => (Square.H8, Square.F8),
        _ => (Square.A8, Square.D8),
    };

    /// <summary>
    /// The square of the pawn captured by an en passant move: the destination file on the source rank.
    /// </summary>
    public static Square EnPassantCaptureSquare(Move move) => Square.At(move.To.File, move.From.Rank);

    /// <summary>
    /// Everything needed to unmake a move and restore the position exactly: the move itself (which
    /// names the piece moved and the piece captured, so the pieces can be put back), and the state
    /// the move overwrote, which no move records. The keys are saved too, being cheaper to restore
    /// than to XOR back, and the history of keys is what repetition detection reads.
    /// </summary>
    private readonly record struct UndoInfo(
        Move Move,
        CastlingRights CastlingRights,
        Square EnPassantSquare,
        int HalfMoveClock,
        ulong Key,
        ulong PawnKey,
        Bitboard Checkers);
}
