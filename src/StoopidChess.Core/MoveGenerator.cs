namespace StoopidChess.Core;

/// <summary>
/// Legal move generation and attack detection on bitboards. Every move generated is legal: pins,
/// checks, en passant and castling are all resolved during generation, so nothing needs to be made
/// and tested. Generation order depends only on the position, which keeps searches reproducible.
///
/// The method follows pawnstar. The king's moves exclude every square the enemy attacks with the
/// king itself lifted off the board (so that sliders see through its square). In double check
/// nothing else can move; in single check the other pieces may only capture the checker or block
/// its line. Pieces pinned against the king may only move along the pin line. Pawn moves are
/// derived for all pawns at once by shifting the pawn set.
/// </summary>
public static class MoveGenerator
{
    /// <summary>
    /// All legal moves for the side to move.
    /// </summary>
    public static List<Move> GenerateLegalMoves(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var moves = new List<Move>(64);
        Generate(board, moves, quietMoves: true);
        return moves;
    }

    /// <summary>
    /// Append all legal moves for the side to move to the list.
    /// </summary>
    public static void GenerateLegalMoves(Board board, List<Move> moves)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(moves);
        Generate(board, moves, quietMoves: true);
    }

    /// <summary>
    /// Append the legal captures and promotions for the side to move to the list, for quiescence
    /// search.
    /// </summary>
    public static void GenerateLegalCaptures(Board board, List<Move> moves)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(moves);
        Generate(board, moves, quietMoves: false);
    }

    /// <summary>
    /// Is the side to move in check?
    /// </summary>
    public static bool IsInCheck(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        return board.IsInCheck;
    }

    /// <summary>
    /// Is the given side's king attacked by the other side?
    /// </summary>
    public static bool IsKingAttacked(Board board, Colour side)
    {
        ArgumentNullException.ThrowIfNull(board);
        return board.IsAttacked(board.KingSquare(side), side.Opponent());
    }

    /// <summary>
    /// Is the square attacked by any piece of the given colour?
    /// </summary>
    public static bool IsAttacked(Board board, int square, Colour by)
    {
        ArgumentNullException.ThrowIfNull(board);
        return board.IsAttacked(square, by);
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
        return (board.Pieces(PieceType.Pawn) & (Bitboards.Rank1 | Bitboards.Rank8)) != 0;
    }

    private static void Generate(Board board, List<Move> moves, bool quietMoves)
    {
        Colour us = board.SideToMove;
        Colour them = us.Opponent();
        ulong occupied = board.Occupied;
        ulong ours = board.Pieces(us);
        ulong theirs = board.Pieces(them);
        int king = board.KingSquare(us);
        ulong checkers = board.Checkers;

        // Squares the king may not step onto: everything the enemy attacks once the king has left
        // its square (a slider that is blocked only by the king still controls the squares behind it)
        ulong forbidden = AttackedBy(board, them, occupied ^ Bitboards.Of(king));

        if (checkers != 0 && !Bitboards.IsSingle(checkers))
        {
            // Double check: only the king can move
            GenerateKingMoves(board, moves, king, forbidden, occupied, theirs, quietMoves);
            return;
        }

        // In check, the other pieces may only capture the checker or step between it and the king
        ulong captureMask = theirs;
        ulong pushMask = ~occupied;
        if (checkers != 0)
        {
            captureMask = checkers;
            pushMask = Attacks.Between(king, Bitboards.First(checkers));
        }
        ulong quietMask = quietMoves ? pushMask : 0;

        // Pieces pinned to the king by an enemy slider: exactly one piece of ours between the two
        ulong pinned = 0;
        ulong theirQueens = board.Pieces(them, PieceType.Queen);
        ulong snipers = (Attacks.BishopRays(king) & (board.Pieces(them, PieceType.Bishop) | theirQueens))
                      | (Attacks.RookRays(king) & (board.Pieces(them, PieceType.Rook) | theirQueens));
        for (ulong bits = snipers; bits != 0;)
        {
            ulong between = Attacks.Between(king, Bitboards.PopFirst(ref bits)) & occupied;
            if (Bitboards.IsSingle(between) && (between & ours) != 0)
            {
                pinned |= between;
            }
        }

        GeneratePawnMoves(board, moves, us, them, occupied, theirs, king, pinned, captureMask, pushMask, quietMoves);

        for (ulong bits = board.Pieces(us, PieceType.Knight); bits != 0;)
        {
            int from = Bitboards.PopFirst(ref bits);
            AddPieceMoves(board, moves, from, PieceType.Knight, Attacks.Knight(from), king, pinned, captureMask, quietMask);
        }
        for (ulong bits = board.Pieces(us, PieceType.Bishop); bits != 0;)
        {
            int from = Bitboards.PopFirst(ref bits);
            AddPieceMoves(board, moves, from, PieceType.Bishop, Attacks.Bishop(occupied, from), king, pinned, captureMask, quietMask);
        }
        for (ulong bits = board.Pieces(us, PieceType.Rook); bits != 0;)
        {
            int from = Bitboards.PopFirst(ref bits);
            AddPieceMoves(board, moves, from, PieceType.Rook, Attacks.Rook(occupied, from), king, pinned, captureMask, quietMask);
        }
        for (ulong bits = board.Pieces(us, PieceType.Queen); bits != 0;)
        {
            int from = Bitboards.PopFirst(ref bits);
            AddPieceMoves(board, moves, from, PieceType.Queen, Attacks.Queen(occupied, from), king, pinned, captureMask, quietMask);
        }

        GenerateKingMoves(board, moves, king, forbidden, occupied, theirs, quietMoves);
        if (quietMoves && checkers == 0)
        {
            GenerateCastlingMoves(board, moves, us, king, occupied, forbidden);
        }
    }

    /// <summary>
    /// Every square attacked by the colour, for the given occupancy.
    /// </summary>
    private static ulong AttackedBy(Board board, Colour by, ulong occupied)
    {
        ulong attacks = Attacks.Pawns(by, board.Pieces(by, PieceType.Pawn)) | Attacks.King(board.KingSquare(by));
        for (ulong bits = board.Pieces(by, PieceType.Knight); bits != 0;)
        {
            attacks |= Attacks.Knight(Bitboards.PopFirst(ref bits));
        }
        ulong queens = board.Pieces(by, PieceType.Queen);
        for (ulong bits = board.Pieces(by, PieceType.Bishop) | queens; bits != 0;)
        {
            attacks |= Attacks.Bishop(occupied, Bitboards.PopFirst(ref bits));
        }
        for (ulong bits = board.Pieces(by, PieceType.Rook) | queens; bits != 0;)
        {
            attacks |= Attacks.Rook(occupied, Bitboards.PopFirst(ref bits));
        }
        return attacks;
    }

    private static void AddPieceMoves(Board board, List<Move> moves, int from, PieceType piece, ulong attacks, int king, ulong pinned, ulong captureMask, ulong quietMask)
    {
        if (Bitboards.Contains(pinned, from))
        {
            attacks &= Attacks.Line(king, from);
        }
        for (ulong targets = attacks & captureMask; targets != 0;)
        {
            int to = Bitboards.PopFirst(ref targets);
            moves.Add(new Move(from, to, piece, board.At(to).Type));
        }
        for (ulong targets = attacks & quietMask; targets != 0;)
        {
            moves.Add(new Move(from, Bitboards.PopFirst(ref targets), piece));
        }
    }

    private static void GenerateKingMoves(Board board, List<Move> moves, int king, ulong forbidden, ulong occupied, ulong theirs, bool quietMoves)
    {
        ulong targets = Attacks.King(king) & ~forbidden;
        for (ulong captures = targets & theirs; captures != 0;)
        {
            int to = Bitboards.PopFirst(ref captures);
            moves.Add(new Move(king, to, PieceType.King, board.At(to).Type));
        }
        if (quietMoves)
        {
            for (ulong quiets = targets & ~occupied; quiets != 0;)
            {
                moves.Add(new Move(king, Bitboards.PopFirst(ref quiets), PieceType.King));
            }
        }
    }

    /// <summary>
    /// Castling is generated only when fully legal: the right is intact, the king and rook are on
    /// their home squares, the squares between them are empty, and neither the king's square nor the
    /// squares it passes over are attacked. The caller has already established the king is not in
    /// check, so the attack set computed without the king is exact for the squares it crosses.
    /// </summary>
    private static void GenerateCastlingMoves(Board board, List<Move> moves, Colour us, int king, ulong occupied, ulong forbidden)
    {
        CastlingRights kingSide = us == Colour.White ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
        CastlingRights queenSide = us == Colour.White ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
        int home = us == Colour.White ? Squares.E1 : Squares.E8;
        if ((board.CastlingRights & (kingSide | queenSide)) == CastlingRights.None || king != home)
        {
            return;
        }
        Piece rook = new(us, PieceType.Rook);
        ulong kingSidePath = Bitboards.Of(home + 1) | Bitboards.Of(home + 2);              // f and g
        ulong queenSideEmpty = Bitboards.Of(home - 1) | Bitboards.Of(home - 2) | Bitboards.Of(home - 3);   // d, c and b
        ulong queenSidePath = Bitboards.Of(home - 1) | Bitboards.Of(home - 2);             // d and c
        if ((board.CastlingRights & kingSide) != CastlingRights.None
            && board.At(home + 3) == rook && (occupied & kingSidePath) == 0 && (forbidden & kingSidePath) == 0)
        {
            moves.Add(new Move(home, home + 2, PieceType.King, PieceType.None, MoveKind.CastleKingSide));
        }
        if ((board.CastlingRights & queenSide) != CastlingRights.None
            && board.At(home - 4) == rook && (occupied & queenSideEmpty) == 0 && (forbidden & queenSidePath) == 0)
        {
            moves.Add(new Move(home, home - 2, PieceType.King, PieceType.None, MoveKind.CastleQueenSide));
        }
    }

    /// <summary>
    /// Pawn moves for the whole pawn set at once: the set is shifted forward for pushes and
    /// diagonally for captures, masked by what is legal, and each target is mapped back to its source.
    /// </summary>
    private static void GeneratePawnMoves(Board board, List<Move> moves, Colour us, Colour them, ulong occupied, ulong theirs, int king, ulong pinned, ulong captureMask, ulong pushMask, bool quietMoves)
    {
        ulong pawns = board.Pieces(us, PieceType.Pawn);
        if (pawns == 0)
        {
            return;
        }
        ulong empty = ~occupied;
        ulong promotionRank;
        ulong pushes, doublePushes, capturesWest, capturesEast;
        int pushStep, westStep, eastStep;
        if (us == Colour.White)
        {
            promotionRank = Bitboards.Rank8;
            pushes = Bitboards.North(pawns) & empty;
            doublePushes = Bitboards.North(pushes & Bitboards.Rank3) & empty;
            capturesWest = Bitboards.NorthWest(pawns) & theirs;
            capturesEast = Bitboards.NorthEast(pawns) & theirs;
            pushStep = 8;
            westStep = 7;
            eastStep = 9;
        }
        else
        {
            promotionRank = Bitboards.Rank1;
            pushes = Bitboards.South(pawns) & empty;
            doublePushes = Bitboards.South(pushes & Bitboards.Rank6) & empty;
            capturesWest = Bitboards.SouthWest(pawns) & theirs;
            capturesEast = Bitboards.SouthEast(pawns) & theirs;
            pushStep = -8;
            westStep = -9;
            eastStep = -7;
        }

        // Promotions are generated even when only captures are wanted
        AddPromotions(board, moves, pushes & promotionRank & pushMask, pushStep, king, pinned);
        AddPromotions(board, moves, capturesWest & promotionRank & captureMask, westStep, king, pinned);
        AddPromotions(board, moves, capturesEast & promotionRank & captureMask, eastStep, king, pinned);
        AddPawnCaptures(board, moves, capturesWest & ~promotionRank & captureMask, westStep, king, pinned);
        AddPawnCaptures(board, moves, capturesEast & ~promotionRank & captureMask, eastStep, king, pinned);
        if (quietMoves)
        {
            for (ulong targets = pushes & ~promotionRank & pushMask; targets != 0;)
            {
                int to = Bitboards.PopFirst(ref targets);
                int from = to - pushStep;
                if (IsAllowedByPins(king, pinned, from, to))
                {
                    moves.Add(new Move(from, to, PieceType.Pawn));
                }
            }
            for (ulong targets = doublePushes & pushMask; targets != 0;)
            {
                int to = Bitboards.PopFirst(ref targets);
                int from = to - (2 * pushStep);
                if (IsAllowedByPins(king, pinned, from, to))
                {
                    moves.Add(new Move(from, to, PieceType.Pawn, PieceType.None, MoveKind.DoublePawnPush));
                }
            }
        }

        // En passant. The captured pawn must be in the capture mask (when in check that means it
        // is the checker; an en passant capture can never block a check), the capturing pawn must
        // not be pinned off the line, and lifting both pawns off the king's rank must not expose
        // the king to a rook or queen along it, the one discovered attack the pin test cannot see.
        int enPassant = board.EnPassantSquare;
        if (enPassant != Squares.None)
        {
            int captured = enPassant - pushStep;
            if ((captureMask & Bitboards.Of(captured)) != 0)
            {
                ulong theirRooksAndQueens = board.Pieces(them, PieceType.Rook) | board.Pieces(them, PieceType.Queen);
                for (ulong sources = Attacks.Pawn(them, enPassant) & pawns; sources != 0;)
                {
                    int from = Bitboards.PopFirst(ref sources);
                    if (!IsAllowedByPins(king, pinned, from, enPassant))
                    {
                        continue;
                    }
                    if (Squares.RankOf(king) == Squares.RankOf(from))
                    {
                        ulong withoutPawns = occupied ^ Bitboards.Of(from) ^ Bitboards.Of(captured);
                        if ((Attacks.Rook(withoutPawns, king) & Bitboards.Rank(Squares.RankOf(king)) & theirRooksAndQueens) != 0)
                        {
                            continue;
                        }
                    }
                    moves.Add(new Move(from, enPassant, PieceType.Pawn, PieceType.Pawn, MoveKind.EnPassant));
                }
            }
        }
    }

    private static void AddPawnCaptures(Board board, List<Move> moves, ulong targets, int step, int king, ulong pinned)
    {
        while (targets != 0)
        {
            int to = Bitboards.PopFirst(ref targets);
            int from = to - step;
            if (IsAllowedByPins(king, pinned, from, to))
            {
                moves.Add(new Move(from, to, PieceType.Pawn, board.At(to).Type));
            }
        }
    }

    private static void AddPromotions(Board board, List<Move> moves, ulong targets, int step, int king, ulong pinned)
    {
        while (targets != 0)
        {
            int to = Bitboards.PopFirst(ref targets);
            int from = to - step;
            if (!IsAllowedByPins(king, pinned, from, to))
            {
                continue;
            }
            PieceType captured = board.At(to).Type;
            foreach (PieceType promotion in PieceTypeExtensions.PromotionTypes)
            {
                moves.Add(new Move(from, to, PieceType.Pawn, captured, MoveKind.Promotion, promotion));
            }
        }
    }

    /// <summary>
    /// A pinned piece may only move along the line through it and the king.
    /// </summary>
    private static bool IsAllowedByPins(int king, ulong pinned, int from, int to)
    {
        return !Bitboards.Contains(pinned, from) || Bitboards.Contains(Attacks.Line(king, from), to);
    }
}
