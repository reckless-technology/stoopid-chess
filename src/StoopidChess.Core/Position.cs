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
/// <item>the set of pieces giving check, computed on demand and cached until the next move;</item>
/// <item>a history of made moves so that they can be unmade in strict last-in first-out order, and
/// so that repetitions can be detected.</item>
/// </list>
/// A position is created from FEN (<see cref="FromFen"/>, <see cref="Start"/>) or edited into shape
/// (<see cref="Empty"/>, <see cref="Setup"/>, <see cref="Place"/>) before any move is made, and may then be
/// changed only by <see cref="MakeMove"/> and <see cref="UnmakeMove"/>.
/// </summary>
public sealed class Position
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    /// <summary>
    /// The largest half-move clock value kept. Real games end at 100 (or 150 under the seventy-five
    /// move rule); the cap only stops arithmetic overflow on absurd FEN input.
    /// </summary>
    public const int MaxHalfMoveClock = 999;

    private const int Colors = 3;
    private const int PieceTypes = 7;

    private readonly Bitboard[] byType = new Bitboard[PieceTypes];
    private readonly Bitboard[] byColor = new Bitboard[Colors];
    private readonly Piece[] squares = new Piece[Square.Count];
    private readonly List<UndoInfo> history = new(256);
    private Bitboard occupied;
    private Bitboard checkers;
    private bool checkersKnown;

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
    /// Plies since the last capture or pawn move (for the fifty-move rule).
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
    /// The piece on a square as its color and type; (<see cref="Color.None"/>,
    /// <see cref="Piece.None"/>) for an empty square.
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
    /// True when each side has exactly one king, which every rule of chess assumes.
    /// A position that has been edited may violate this.
    /// </summary>
    public bool HasValidKings => Pieces(Color.White, Piece.King).IsSingle && Pieces(Color.Black, Piece.King).IsSingle;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Square KingSquare(Color color)
    {
        Bitboard kings = Pieces(color, Piece.King);
        return kings.IsSingle ? kings.First : throw new InvalidOperationException($"{color} does not have exactly one king");
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
                Bitboard king = Pieces(SideToMove, Piece.King);
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
    /// The pieces of the given color that attack the square.
    /// </summary>
    public Bitboard AttackersTo(Square square, Color by)
    {
        // The squares from which a pawn of the attacking color reaches the square are the squares
        // a pawn of the other color standing on it would attack
        Bitboard attackers = (Attacks.Pawn(by.Opponent(), square) & byType[(int)Piece.Pawn])
            | (Attacks.Knight(square) & byType[(int)Piece.Knight])
            | (Attacks.King(square) & byType[(int)Piece.King])
            | (Attacks.Bishop(occupied, square) & (byType[(int)Piece.Bishop] | byType[(int)Piece.Queen]))
            | (Attacks.Rook(occupied, square) & (byType[(int)Piece.Rook] | byType[(int)Piece.Queen]));
        return attackers & byColor[(int)by];
    }

    /// <summary>
    /// The pieces of both colors that would attack the square if only the given squares were
    /// occupied, for exchange evaluation, which lifts pieces off the board one by one.
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
    public bool IsAttacked(Square square, Color by) => AttackersTo(square, by).IsNotEmpty;

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
    /// The color of the piece on a valid square, or <see cref="Color.None"/> if it is empty.
    /// </summary>
    private Color ColorAt(Square square)
    {
        return byColor[(int)Color.White].Contains(square) ? Color.White
            : byColor[(int)Color.Black].Contains(square) ? Color.Black
            : Color.None;
    }

    internal void PutPiece(Square square, Color color, Piece type)
    {
        Debug.Assert(squares[square] == Piece.None, "square must be empty");
        Debug.Assert(type != Piece.None && color != Color.None, "cannot put an empty piece");
        var bit = Bitboard.Of(square);
        byType[(int)type] |= bit;
        byColor[(int)color] |= bit;
        occupied |= bit;
        squares[square] = type;
        checkersKnown = false;
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
        checkersKnown = false;
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
        checkersKnown = false;
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
            checkersKnown = false;
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
        HalfMoveClock = Math.Clamp(halfMoveClock, 0, MaxHalfMoveClock);
        FullMoveNumber = Math.Max(1, fullMoveNumber);
    }

    /// <summary>
    /// The square index step a pawn of the color moves by: up the board for white, down for black.
    /// </summary>
    public static int PawnDirection(Color color) => color == Color.White ? 8 : -8;

    /// <summary>
    /// Does a pawn of the given color attack the square? Also decides whether an en passant square
    /// is worth recording: only if a pawn could actually capture there, so that positions that are
    /// identical under the rules have identical keys.
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
            && byColor[(int)Color.None].IsEmpty && byType[(int)Piece.None].IsEmpty;
    }

    // Making and unmaking moves. After a move is made and unmade the position, including every key,
    // is identical to what it was before; the search relies on this.

    /// <summary>
    /// Make a move for the side to move. The move must be at least pseudo-legal: the pieces it names
    /// must be where it says they are. No legality check is made here.
    /// </summary>
    public void MakeMove(Move move)
    {
        Debug.Assert(!move.IsNone, "cannot make the null move");
        Debug.Assert(squares[move.From] == move.Piece && byColor[(int)SideToMove].Contains(move.From), "moving piece mismatch");

        history.Add(new UndoInfo(move, CastlingRights, EnPassantSquare, HalfMoveClock, Key, PawnKey));

        Color us = SideToMove;
        Color them = us.Opponent();

        SetEnPassantSquare(Square.None);
        HalfMoveClock = move.Piece == Piece.Pawn || move.IsCapture ? 0 : Math.Min(HalfMoveClock + 1, MaxHalfMoveClock);

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
                MovePiece(move.From, move.To, us, Piece.King);
                MovePiece(move.To + 1, move.To - 1, us, Piece.Rook);    // Rook: h-file to f-file
                break;

            case MoveKind.CastleQueenSide:
                MovePiece(move.From, move.To, us, Piece.King);
                MovePiece(move.To - 2, move.To + 1, us, Piece.Rook);    // Rook: a-file to d-file
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
    }

    /// <summary>
    /// Pass the turn without moving, for null-move pruning in the search. Unmade like any other move
    /// with <see cref="UnmakeMove"/>. Repetitions are not detected across a null move.
    /// </summary>
    public void MakeNullMove()
    {
        history.Add(new UndoInfo(Move.None, CastlingRights, EnPassantSquare, HalfMoveClock, Key, PawnKey));
        SetEnPassantSquare(Square.None);
        HalfMoveClock = Math.Min(HalfMoveClock + 1, MaxHalfMoveClock);
        SwitchSideToMove(SideToMove.Opponent());
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
        checkersKnown = false;
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
                MovePiece(move.To, move.From, us, Piece.King);
                MovePiece(move.To - 1, move.To + 1, us, Piece.Rook);
                break;

            case MoveKind.CastleQueenSide:
                MovePiece(move.To, move.From, us, Piece.King);
                MovePiece(move.To + 1, move.To - 2, us, Piece.Rook);
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
    }

    /// <summary>
    /// The square of the pawn captured by an en passant move: the destination file on the source rank.
    /// </summary>
    public static Square EnPassantCaptureSquare(Move move) => Square.At(move.To.File, move.From.Rank);
    /// <summary>
    /// All legal moves for the side to move.
    /// </summary>
    public MoveList GenerateLegalMoves()
    {
        MoveList moves = default;
        Generate(ref moves, quietMoves: true);
        return moves;
    }

    /// <summary>
    /// Append all legal moves for the side to move to the list.
    /// </summary>
    public void GenerateLegalMoves(ref MoveList moves)
    {
        Generate(ref moves, quietMoves: true);
    }

    /// <summary>
    /// Append the legal captures and promotions for the side to move to the list, for quiescence
    /// search.
    /// </summary>
    public void GenerateLegalCaptures(ref MoveList moves)
    {
        Generate(ref moves, quietMoves: false);
    }

    /// <summary>
    /// Is the given side's king attacked by the other side?
    /// </summary>
    public bool IsKingAttacked(Color side) => IsAttacked(KingSquare(side), side.Opponent());

    /// <summary>
    /// Can play proceed from this position? Each side must have exactly one king, no pawn may stand
    /// on the first or last rank, and the side that has just moved must not have left its king in
    /// check (which also rules out adjacent kings).
    /// </summary>
    public bool IsLegal => HasValidKings && !HasPawnsOnBackRanks && !IsKingAttacked(SideToMove.Opponent());

    /// <summary>
    /// A pawn on its own first rank or on the promotion rank cannot exist in a real game, and pawn
    /// move generation relies on that.
    /// </summary>
    public bool HasPawnsOnBackRanks => (Pieces(Piece.Pawn) & (Bitboard.Rank1 | Bitboard.Rank8)).IsNotEmpty;

    private void Generate(ref MoveList moves, bool quietMoves)
    {
        Color us = SideToMove;
        Color them = us.Opponent();
        Bitboard occupied = Occupied;
        Bitboard ours = Pieces(us);
        Bitboard theirs = Pieces(them);
        Square king = KingSquare(us);
        Bitboard checkers = Checkers;

        // Squares the king may not step onto: everything the enemy attacks once the king has left
        // its square (a slider that is blocked only by the king still controls the squares behind it)
        Bitboard forbidden = AttackedBy(them, occupied ^ Bitboard.Of(king));

        if (checkers.IsNotEmpty && !checkers.IsSingle)
        {
            // Double check: only the king can move
            GenerateKingMoves(ref moves, king, forbidden, occupied, theirs, quietMoves);
            return;
        }

        // In check, the other pieces may only capture the checker or step between it and the king
        Bitboard captureMask = theirs;
        Bitboard pushMask = ~occupied;
        if (checkers.IsNotEmpty)
        {
            captureMask = checkers;
            pushMask = Attacks.Between(king, checkers.First);
        }
        Bitboard quietMask = quietMoves ? pushMask : Bitboard.Empty;

        // Pieces pinned to the king by an enemy slider: exactly one piece of ours between the two
        Bitboard pinned = Bitboard.Empty;
        Bitboard theirQueens = Pieces(them, Piece.Queen);
        Bitboard snipers = (Attacks.BishopRays(king) & (Pieces(them, Piece.Bishop) | theirQueens))
                         | (Attacks.RookRays(king) & (Pieces(them, Piece.Rook) | theirQueens));
        foreach (Square sniper in snipers)
        {
            Bitboard between = Attacks.Between(king, sniper) & occupied;
            if (between.IsSingle && (between & ours).IsNotEmpty)
            {
                pinned |= between;
            }
        }

        GeneratePawnMoves(ref moves, us, them, occupied, theirs, king, pinned, captureMask, pushMask, quietMoves);

        foreach (Square from in Pieces(us, Piece.Knight))
        {
            AddPieceMoves(ref moves, from, Piece.Knight, Attacks.Knight(from), king, pinned, captureMask, quietMask);
        }
        foreach (Square from in Pieces(us, Piece.Bishop))
        {
            AddPieceMoves(ref moves, from, Piece.Bishop, Attacks.Bishop(occupied, from), king, pinned, captureMask, quietMask);
        }
        foreach (Square from in Pieces(us, Piece.Rook))
        {
            AddPieceMoves(ref moves, from, Piece.Rook, Attacks.Rook(occupied, from), king, pinned, captureMask, quietMask);
        }
        foreach (Square from in Pieces(us, Piece.Queen))
        {
            AddPieceMoves(ref moves, from, Piece.Queen, Attacks.Queen(occupied, from), king, pinned, captureMask, quietMask);
        }

        GenerateKingMoves(ref moves, king, forbidden, occupied, theirs, quietMoves);
        if (quietMoves && checkers.IsEmpty)
        {
            GenerateCastlingMoves(ref moves, us, king, occupied, forbidden);
        }
    }

    /// <summary>
    /// Every square attacked by the color, for the given occupancy.
    /// </summary>
    private Bitboard AttackedBy(Color by, Bitboard occupied)
    {
        Bitboard attacks = Attacks.Pawns(by, Pieces(by, Piece.Pawn)) | Attacks.King(KingSquare(by));
        foreach (Square knight in Pieces(by, Piece.Knight))
        {
            attacks |= Attacks.Knight(knight);
        }
        Bitboard queens = Pieces(by, Piece.Queen);
        foreach (Square bishop in Pieces(by, Piece.Bishop) | queens)
        {
            attacks |= Attacks.Bishop(occupied, bishop);
        }
        foreach (Square rook in Pieces(by, Piece.Rook) | queens)
        {
            attacks |= Attacks.Rook(occupied, rook);
        }
        return attacks;
    }

    private void AddPieceMoves(ref MoveList moves, Square from, Piece piece, Bitboard attacks, Square king, Bitboard pinned, Bitboard captureMask, Bitboard quietMask)
    {
        if (pinned.Contains(from))
        {
            attacks &= Attacks.Line(king, from);
        }
        foreach (Square to in attacks & captureMask)
        {
            moves.Add(new Move(from, to, piece, At(to)));
        }
        foreach (Square to in attacks & quietMask)
        {
            moves.Add(new Move(from, to, piece));
        }
    }

    private void GenerateKingMoves(ref MoveList moves, Square king, Bitboard forbidden, Bitboard occupied, Bitboard theirs, bool quietMoves)
    {
        Bitboard targets = Attacks.King(king) & ~forbidden;
        foreach (Square to in targets & theirs)
        {
            moves.Add(new Move(king, to, Piece.King, At(to)));
        }
        if (quietMoves)
        {
            foreach (Square to in targets & ~occupied)
            {
                moves.Add(new Move(king, to, Piece.King));
            }
        }
    }

    /// <summary>
    /// Castling is generated only when fully legal: the right is intact, the king and rook are on
    /// their home squares, the squares between them are empty, and neither the king's square nor the
    /// squares it passes over are attacked. The caller has already established the king is not in
    /// check, so the attack set computed without the king is exact for the squares it crosses.
    /// </summary>
    private void GenerateCastlingMoves(ref MoveList moves, Color us, Square king, Bitboard occupied, Bitboard forbidden)
    {
        CastlingRights kingSide = us == Color.White ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
        CastlingRights queenSide = us == Color.White ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
        Square home = us == Color.White ? Square.E1 : Square.E8;
        if ((CastlingRights & (kingSide | queenSide)) == CastlingRights.None || king != home)
        {
            return;
        }
        Bitboard rooks = Pieces(us, Piece.Rook);
        Bitboard kingSidePath = Bitboard.Of(home + 1) | Bitboard.Of(home + 2);              // f and g
        Bitboard queenSideEmpty = Bitboard.Of(home - 1) | Bitboard.Of(home - 2) | Bitboard.Of(home - 3);   // d, c and b
        Bitboard queenSidePath = Bitboard.Of(home - 1) | Bitboard.Of(home - 2);             // d and c
        if ((CastlingRights & kingSide) != CastlingRights.None
            && rooks.Contains(home + 3) && (occupied & kingSidePath).IsEmpty && (forbidden & kingSidePath).IsEmpty)
        {
            moves.Add(new Move(home, home + 2, Piece.King, Piece.None, MoveKind.CastleKingSide));
        }
        if ((CastlingRights & queenSide) != CastlingRights.None
            && rooks.Contains(home - 4) && (occupied & queenSideEmpty).IsEmpty && (forbidden & queenSidePath).IsEmpty)
        {
            moves.Add(new Move(home, home - 2, Piece.King, Piece.None, MoveKind.CastleQueenSide));
        }
    }

    /// <summary>
    /// Pawn moves for the whole pawn set at once: the set is shifted forward for pushes and
    /// diagonally for captures, masked by what is legal, and each target is mapped back to its source.
    /// </summary>
    private void GeneratePawnMoves(ref MoveList moves, Color us, Color them, Bitboard occupied, Bitboard theirs, Square king, Bitboard pinned, Bitboard captureMask, Bitboard pushMask, bool quietMoves)
    {
        Bitboard pawns = Pieces(us, Piece.Pawn);
        if (pawns.IsEmpty)
        {
            return;
        }
        Bitboard empty = ~occupied;
        Bitboard promotionRank;
        Bitboard pushes, doublePushes, capturesWest, capturesEast;
        int pushStep, westStep, eastStep;
        if (us == Color.White)
        {
            promotionRank = Bitboard.Rank8;
            pushes = pawns.North() & empty;
            doublePushes = (pushes & Bitboard.Rank3).North() & empty;
            capturesWest = pawns.NorthWest() & theirs;
            capturesEast = pawns.NorthEast() & theirs;
            pushStep = 8;
            westStep = 7;
            eastStep = 9;
        }
        else
        {
            promotionRank = Bitboard.Rank1;
            pushes = pawns.South() & empty;
            doublePushes = (pushes & Bitboard.Rank6).South() & empty;
            capturesWest = pawns.SouthWest() & theirs;
            capturesEast = pawns.SouthEast() & theirs;
            pushStep = -8;
            westStep = -9;
            eastStep = -7;
        }

        // Promotions are generated even when only captures are wanted
        AddPromotions(ref moves, pushes & promotionRank & pushMask, pushStep, king, pinned);
        AddPromotions(ref moves, capturesWest & promotionRank & captureMask, westStep, king, pinned);
        AddPromotions(ref moves, capturesEast & promotionRank & captureMask, eastStep, king, pinned);
        AddPawnCaptures(ref moves, capturesWest & ~promotionRank & captureMask, westStep, king, pinned);
        AddPawnCaptures(ref moves, capturesEast & ~promotionRank & captureMask, eastStep, king, pinned);
        if (quietMoves)
        {
            foreach (Square to in pushes & ~promotionRank & pushMask)
            {
                Square from = to - pushStep;
                if (IsAllowedByPins(king, pinned, from, to))
                {
                    moves.Add(new Move(from, to, Piece.Pawn));
                }
            }
            foreach (Square to in doublePushes & pushMask)
            {
                Square from = to - (2 * pushStep);
                if (IsAllowedByPins(king, pinned, from, to))
                {
                    moves.Add(new Move(from, to, Piece.Pawn, Piece.None, MoveKind.DoublePawnPush));
                }
            }
        }

        // En passant. The captured pawn must be in the capture mask (when in check that means it
        // is the checker; an en passant capture can never block a check), the capturing pawn must
        // not be pinned off the line, and lifting both pawns off the king's rank must not expose
        // the king to a rook or queen along it, the one discovered attack the pin test cannot see.
        Square enPassant = EnPassantSquare;
        if (enPassant.IsValid)
        {
            Square captured = enPassant - pushStep;
            if (captureMask.Contains(captured))
            {
                Bitboard theirRooksAndQueens = Pieces(them, Piece.Rook) | Pieces(them, Piece.Queen);
                foreach (Square from in Attacks.Pawn(them, enPassant) & pawns)
                {
                    if (!IsAllowedByPins(king, pinned, from, enPassant))
                    {
                        continue;
                    }
                    if (king.Rank == from.Rank)
                    {
                        Bitboard withoutPawns = occupied ^ Bitboard.Of(from) ^ Bitboard.Of(captured);
                        if ((Attacks.Rook(withoutPawns, king) & Bitboard.OfRank(king.Rank) & theirRooksAndQueens).IsNotEmpty)
                        {
                            continue;
                        }
                    }
                    moves.Add(new Move(from, enPassant, Piece.Pawn, Piece.Pawn, MoveKind.EnPassant));
                }
            }
        }
    }

    private void AddPawnCaptures(ref MoveList moves, Bitboard targets, int step, Square king, Bitboard pinned)
    {
        foreach (Square to in targets)
        {
            Square from = to - step;
            if (IsAllowedByPins(king, pinned, from, to))
            {
                moves.Add(new Move(from, to, Piece.Pawn, At(to)));
            }
        }
    }

    private void AddPromotions(ref MoveList moves, Bitboard targets, int step, Square king, Bitboard pinned)
    {
        foreach (Square to in targets)
        {
            Square from = to - step;
            if (!IsAllowedByPins(king, pinned, from, to))
            {
                continue;
            }
            Piece captured = At(to);
            foreach (Piece promotion in PieceExtensions.PromotionTypes)
            {
                moves.Add(new Move(from, to, Piece.Pawn, captured, MoveKind.Promotion, promotion));
            }
        }
    }

    /// <summary>
    /// A pinned piece may only move along the line through it and the king.
    /// </summary>
    private static bool IsAllowedByPins(Square king, Bitboard pinned, Square from, Square to)
    {
        return !pinned.Contains(from) || Attacks.Line(king, from).Contains(to);
    }

    // Position editing, for position editors and tests. A position is edited before any move has
    // been made on it (an edited position starts a fresh game); the bitboards and keys stay
    // consistent throughout, and castling rights survive only where the king and rook still stand
    // on their home squares.

    /// <summary>
    /// An empty board, white to move, no castling rights.
    /// </summary>
    public static Position Empty() => new();

    /// <summary>
    /// A fresh copy of a position's pieces, side to move and castling rights, without its move
    /// history, ready for editing.
    /// </summary>
    public static Position Setup(Position source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var position = new Position();
        foreach (Square square in Square.All)
        {
            (Color color, Piece type) = source[square];
            if (type != Piece.None)
            {
                position.PutPiece(square, color, type);
            }
        }
        position.SwitchSideToMove(source.SideToMove);
        position.ApplyCastlingRights(source.CastlingRights & position.HomeCastlingRights());
        return position;
    }

    /// <summary>
    /// Put a piece on a square, replacing whatever stood there; the default pair empties the square.
    /// </summary>
    public void Place(Square square, (Color Color, Piece Type) piece)
    {
        RequireEditable();
        if (!square.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(square));
        }
        bool valid = piece.Type == Piece.None
            ? piece.Color == Color.None
            : piece.Color is Color.White or Color.Black && piece.Type is >= Piece.Pawn and <= Piece.King;
        if (!valid)
        {
            throw new ArgumentException($"'{piece.Color} {piece.Type}' is not a valid piece", nameof(piece));
        }
        (Color oldColor, Piece oldType) = this[square];
        if (oldType != Piece.None)
        {
            RemovePiece(square, oldColor, oldType);
        }
        if (piece.Type != Piece.None)
        {
            PutPiece(square, piece.Color, piece.Type);
        }
        SetEnPassantSquare(Square.None);
        ApplyCastlingRights(CastlingRights & HomeCastlingRights());
        checkersKnown = false;
    }

    /// <summary>
    /// Empty a square.
    /// </summary>
    public void Clear(Square square) => Place(square, default);

    /// <summary>
    /// Give the move to a side.
    /// </summary>
    public void SetSideToMove(Color color)
    {
        RequireEditable();
        if (color is not (Color.White or Color.Black))
        {
            throw new ArgumentOutOfRangeException(nameof(color));
        }
        SwitchSideToMove(color);
        checkersKnown = false;
    }

    /// <summary>
    /// Grant castling rights; any whose king or rook is not on its home square is dropped.
    /// </summary>
    public void SetCastlingRights(CastlingRights rights)
    {
        RequireEditable();
        ApplyCastlingRights(rights & HomeCastlingRights());
    }

    // Castling rights survive only where the king or rook still stands on its home square
    private static readonly (Square Square, Color Color, Piece Type)[] CastlingHomes =
    [
        (Square.E1, Color.White, Piece.King), (Square.H1, Color.White, Piece.Rook), (Square.A1, Color.White, Piece.Rook),
        (Square.E8, Color.Black, Piece.King), (Square.H8, Color.Black, Piece.Rook), (Square.A8, Color.Black, Piece.Rook),
    ];

    private CastlingRights HomeCastlingRights()
    {
        CastlingRights rights = CastlingRights.All;
        foreach ((Square square, Color color, Piece type) in CastlingHomes)
        {
            if (this[square] != (color, type))
            {
                rights &= ~CastlingRightsExtensions.LostBySquare(square);
            }
        }
        return rights;
    }

    private void RequireEditable()
    {
        if (history.Count != 0)
        {
            throw new InvalidOperationException("a position can only be edited before moves are made on it; start from Position.Setup");
        }
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
