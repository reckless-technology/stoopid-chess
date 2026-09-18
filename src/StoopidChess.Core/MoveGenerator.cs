namespace StoopidChess.Core;

/// <summary>
/// Move generation and attack detection for 0x88 boards. Generation order is deterministic, which
/// keeps searches reproducible.
/// </summary>
public static class MoveGenerator
{
    private static readonly int[] KnightDeltas = [0x21, 0x1F, 0x12, 0x0E, -0x0E, -0x12, -0x1F, -0x21];
    private static readonly int[] BishopDeltaTable = [0x11, 0x0F, -0x0F, -0x11];
    private static readonly int[] RookDeltas = [0x10, 0x01, -0x01, -0x10];
    private static readonly int[] KingDeltas = [0x10, 0x01, -0x01, -0x10, 0x11, 0x0F, -0x0F, -0x11];

    // In 0x88 the difference between two squares identifies the direction between them, so a single
    // table lookup on (delta + offset) answers "can a knight/king on square A reach square B".
    private const int DeltaOffset = 0x77;
    private static readonly bool[] KnightReach = BuildReachTable(KnightDeltas);
    private static readonly bool[] KingReach = BuildReachTable(KingDeltas);

    /// <summary>
    /// The four diagonal directions, shared with the evaluator's bishop mobility.
    /// </summary>
    internal static ReadOnlySpan<int> BishopDeltas => BishopDeltaTable;

    /// <summary>
    /// All legal moves for the side to move.
    /// </summary>
    public static List<Move> GenerateLegalMoves(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var moves = new List<Move>(64);
        Generate(board, moves, capturesOnly: false);
        Colour us = board.SideToMove;
        int kept = 0;
        for (int i = 0; i < moves.Count; i++)     // Compacted in place, so no foreach
        {
            board.MakeMove(moves[i]);
            bool legal = !IsKingAttacked(board, us);
            board.UnmakeMove();
            if (legal)
            {
                moves[kept++] = moves[i];
            }
        }
        moves.RemoveRange(kept, moves.Count - kept);
        return moves;
    }

    /// <summary>
    /// All moves for the side to move that follow the movement rules of the pieces. Some may leave
    /// the mover's own king in check. Castling moves are only generated when actually legal.
    /// </summary>
    public static void GeneratePseudoLegalMoves(Board board, List<Move> moves) => Generate(board, moves, capturesOnly: false);

    /// <summary>
    /// Captures and promotions only, for quiescence search. Some may leave the mover's king in check.
    /// </summary>
    public static void GenerateCaptures(Board board, List<Move> moves) => Generate(board, moves, capturesOnly: true);

    private static void Generate(Board board, List<Move> moves, bool capturesOnly)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(moves);
        Colour us = board.SideToMove;
        foreach (int from in board.SquaresOf(us, PieceType.Pawn))
        {
            GeneratePawnMoves(board, us, from, moves, capturesOnly);
        }
        foreach (int from in board.SquaresOf(us, PieceType.Knight))
        {
            GenerateLeaperMoves(board, us, from, PieceType.Knight, KnightDeltas, moves, capturesOnly);
        }
        foreach (int from in board.SquaresOf(us, PieceType.Bishop))
        {
            GenerateSliderMoves(board, us, from, PieceType.Bishop, BishopDeltaTable, moves, capturesOnly);
        }
        foreach (int from in board.SquaresOf(us, PieceType.Rook))
        {
            GenerateSliderMoves(board, us, from, PieceType.Rook, RookDeltas, moves, capturesOnly);
        }
        foreach (int from in board.SquaresOf(us, PieceType.Queen))
        {
            GenerateSliderMoves(board, us, from, PieceType.Queen, KingDeltas, moves, capturesOnly);
        }
        GenerateLeaperMoves(board, us, board.KingSquare(us), PieceType.King, KingDeltas, moves, capturesOnly);
        if (!capturesOnly)
        {
            GenerateCastlingMoves(board, us, moves);
        }
    }

    /// <summary>
    /// Is the side to move in check?
    /// </summary>
    public static bool IsInCheck(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return IsKingAttacked(board, board.SideToMove);
    }

    /// <summary>
    /// Is the given side's king attacked by the other side?
    /// </summary>
    public static bool IsKingAttacked(Board board, Colour side)
    {
        ArgumentNullException.ThrowIfNull(board);
        return IsAttacked(board, board.KingSquare(side), side.Opponent());
    }

    /// <summary>
    /// Can play proceed from this position? Each side must have exactly one king, no pawn may stand
    /// on the first or last rank, and the side that has just moved must not have left its king in
    /// check (which also rules out adjacent kings).
    /// </summary>
    public static bool IsPositionLegal(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return board.HasValidKings && !HasPawnsOnBackRanks(board) && !IsKingAttacked(board, board.SideToMove.Opponent());
    }

    /// <summary>
    /// A pawn on its own first rank or on the promotion rank cannot exist in a real game, and pawn
    /// move generation relies on that.
    /// </summary>
    public static bool HasPawnsOnBackRanks(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        foreach (Colour colour in ColourExtensions.Sides)
        {
            foreach (int square in board.SquaresOf(colour, PieceType.Pawn))
            {
                if (Squares.RankOf(square) is 0 or 7)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Is the square attacked by any piece of the given colour?
    /// </summary>
    public static bool IsAttacked(Board board, int square, Colour by)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (board.IsAttackedByPawn(square, by))
        {
            return true;
        }
        foreach (int knight in board.SquaresOf(by, PieceType.Knight))
        {
            if (KnightReach[knight - square + DeltaOffset])
            {
                return true;
            }
        }
        bool hasQueen = board.Count(by, PieceType.Queen) != 0;
        if ((hasQueen || board.Count(by, PieceType.Rook) != 0) && IsAttackedAlong(board, square, by, RookDeltas, PieceType.Rook))
        {
            return true;
        }
        if ((hasQueen || board.Count(by, PieceType.Bishop) != 0) && IsAttackedAlong(board, square, by, BishopDeltaTable, PieceType.Bishop))
        {
            return true;
        }
        foreach (int king in board.SquaresOf(by, PieceType.King))
        {
            if (KingReach[king - square + DeltaOffset])
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsAttackedAlong(Board board, int square, Colour by, int[] deltas, PieceType slider)
    {
        foreach (int delta in deltas)
        {
            for (int to = square + delta; Squares.IsOnBoard(to); to += delta)
            {
                Piece piece = board.At(to);
                if (piece.IsNone)
                {
                    continue;
                }
                if (piece.Colour == by && (piece.Type == slider || piece.Type == PieceType.Queen))
                {
                    return true;
                }
                break;
            }
        }
        return false;
    }

    private static void GeneratePawnMoves(Board board, Colour us, int from, List<Move> moves, bool capturesOnly)
    {
        int direction = Board.PawnDirection(us);
        int startRank = us == Colour.White ? 1 : 6;
        int promotionRank = us == Colour.White ? 7 : 0;
        Colour them = us.Opponent();

        int forward = from + direction;
        bool promotes = Squares.RankOf(forward) == promotionRank;

        // A pawn is promoted before it can walk off the board, so 'forward' is always on the board.
        if (board.At(forward).IsNone)
        {
            if (promotes)
            {
                AddPromotions(from, forward, PieceType.None, moves);
            }
            else if (!capturesOnly)
            {
                moves.Add(new Move(from, forward, PieceType.Pawn));
                int doublePush = forward + direction;
                if (Squares.RankOf(from) == startRank && board.At(doublePush).IsNone)
                {
                    moves.Add(new Move(from, doublePush, PieceType.Pawn, PieceType.None, MoveKind.DoublePawnPush));
                }
            }
        }

        foreach (int to in stackalloc int[] { forward - 1, forward + 1 })
        {
            if (!Squares.IsOnBoard(to))
            {
                continue;
            }
            Piece target = board.At(to);
            if (target.Colour == them)
            {
                if (promotes)
                {
                    AddPromotions(from, to, target.Type, moves);
                }
                else
                {
                    moves.Add(new Move(from, to, PieceType.Pawn, target.Type));
                }
            }
            else if (to == board.EnPassantSquare && board.At(Squares.Index(Squares.FileOf(to), Squares.RankOf(from))) == new Piece(them, PieceType.Pawn))
            {
                moves.Add(new Move(from, to, PieceType.Pawn, PieceType.Pawn, MoveKind.EnPassant));
            }
        }
    }

    private static void AddPromotions(int from, int to, PieceType captured, List<Move> moves)
    {
        foreach (PieceType promotion in PieceTypeExtensions.PromotionTypes)
        {
            moves.Add(new Move(from, to, PieceType.Pawn, captured, MoveKind.Promotion, promotion));
        }
    }

    private static void GenerateLeaperMoves(Board board, Colour us, int from, PieceType piece, int[] deltas, List<Move> moves, bool capturesOnly)
    {
        foreach (int delta in deltas)
        {
            int to = from + delta;
            if (!Squares.IsOnBoard(to))
            {
                continue;
            }
            Piece target = board.At(to);
            if (target.IsNone)
            {
                if (!capturesOnly)
                {
                    moves.Add(new Move(from, to, piece));
                }
            }
            else if (target.Colour != us)
            {
                moves.Add(new Move(from, to, piece, target.Type));
            }
        }
    }

    private static void GenerateSliderMoves(Board board, Colour us, int from, PieceType piece, int[] deltas, List<Move> moves, bool capturesOnly)
    {
        foreach (int delta in deltas)
        {
            for (int to = from + delta; Squares.IsOnBoard(to); to += delta)
            {
                Piece target = board.At(to);
                if (target.IsNone)
                {
                    if (!capturesOnly)
                    {
                        moves.Add(new Move(from, to, piece));
                    }
                    continue;
                }
                if (target.Colour != us)
                {
                    moves.Add(new Move(from, to, piece, target.Type));
                }
                break;
            }
        }
    }

    /// <summary>
    /// Castling is generated only when fully legal: the right is intact, the squares between king and
    /// rook are empty, and neither the king's square nor the squares it passes over are attacked.
    /// </summary>
    private static void GenerateCastlingMoves(Board board, Colour us, List<Move> moves)
    {
        CastlingRights kingSide = us == Colour.White ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
        CastlingRights queenSide = us == Colour.White ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
        if ((board.CastlingRights & (kingSide | queenSide)) == CastlingRights.None)
        {
            return;
        }
        int king = us == Colour.White ? Squares.E1 : Squares.E8;
        Colour them = us.Opponent();
        Piece rook = new(us, PieceType.Rook);
        if (board.At(king) != new Piece(us, PieceType.King) || IsAttacked(board, king, them))
        {
            return;
        }
        if ((board.CastlingRights & kingSide) != CastlingRights.None
            && board.At(king + 3) == rook && board.At(king + 1).IsNone && board.At(king + 2).IsNone
            && !IsAttacked(board, king + 1, them) && !IsAttacked(board, king + 2, them))
        {
            moves.Add(new Move(king, king + 2, PieceType.King, PieceType.None, MoveKind.CastleKingSide));
        }
        if ((board.CastlingRights & queenSide) != CastlingRights.None
            && board.At(king - 4) == rook && board.At(king - 1).IsNone && board.At(king - 2).IsNone && board.At(king - 3).IsNone
            && !IsAttacked(board, king - 1, them) && !IsAttacked(board, king - 2, them))
        {
            moves.Add(new Move(king, king - 2, PieceType.King, PieceType.None, MoveKind.CastleQueenSide));
        }
    }

    private static bool[] BuildReachTable(int[] deltas)
    {
        bool[] table = new bool[(2 * DeltaOffset) + 1];
        foreach (int delta in deltas)
        {
            table[delta + DeltaOffset] = true;
        }
        return table;
    }
}
