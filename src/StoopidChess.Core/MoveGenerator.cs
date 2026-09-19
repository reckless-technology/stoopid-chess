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
    public static MoveList GenerateLegalMoves(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        MoveList moves = default;
        Generate(board, ref moves, quietMoves: true);
        return moves;
    }

    /// <summary>
    /// Append all legal moves for the side to move to the list.
    /// </summary>
    public static void GenerateLegalMoves(Board board, ref MoveList moves)
    {
        ArgumentNullException.ThrowIfNull(board);
        Generate(board, ref moves, quietMoves: true);
    }

    /// <summary>
    /// Append the legal captures and promotions for the side to move to the list, for quiescence
    /// search.
    /// </summary>
    public static void GenerateLegalCaptures(Board board, ref MoveList moves)
    {
        ArgumentNullException.ThrowIfNull(board);
        Generate(board, ref moves, quietMoves: false);
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
    public static bool IsAttacked(Board board, Square square, Colour by)
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
        return (board.Pieces(PieceType.Pawn) & (Bitboard.Rank1 | Bitboard.Rank8)).IsNotEmpty;
    }

    private static void Generate(Board board, ref MoveList moves, bool quietMoves)
    {
        Colour us = board.SideToMove;
        Colour them = us.Opponent();
        Bitboard occupied = board.Occupied;
        Bitboard ours = board.Pieces(us);
        Bitboard theirs = board.Pieces(them);
        Square king = board.KingSquare(us);
        Bitboard checkers = board.Checkers;

        // Squares the king may not step onto: everything the enemy attacks once the king has left
        // its square (a slider that is blocked only by the king still controls the squares behind it)
        Bitboard forbidden = AttackedBy(board, them, occupied ^ Bitboard.Of(king));

        if (checkers.IsNotEmpty && !checkers.IsSingle)
        {
            // Double check: only the king can move
            GenerateKingMoves(board, ref moves, king, forbidden, occupied, theirs, quietMoves);
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
        Bitboard theirQueens = board.Pieces(them, PieceType.Queen);
        Bitboard snipers = (Attacks.BishopRays(king) & (board.Pieces(them, PieceType.Bishop) | theirQueens))
                         | (Attacks.RookRays(king) & (board.Pieces(them, PieceType.Rook) | theirQueens));
        foreach (Square sniper in snipers)
        {
            Bitboard between = Attacks.Between(king, sniper) & occupied;
            if (between.IsSingle && (between & ours).IsNotEmpty)
            {
                pinned |= between;
            }
        }

        GeneratePawnMoves(board, ref moves, us, them, occupied, theirs, king, pinned, captureMask, pushMask, quietMoves);

        foreach (Square from in board.Pieces(us, PieceType.Knight))
        {
            AddPieceMoves(board, ref moves, from, PieceType.Knight, Attacks.Knight(from), king, pinned, captureMask, quietMask);
        }
        foreach (Square from in board.Pieces(us, PieceType.Bishop))
        {
            AddPieceMoves(board, ref moves, from, PieceType.Bishop, Attacks.Bishop(occupied, from), king, pinned, captureMask, quietMask);
        }
        foreach (Square from in board.Pieces(us, PieceType.Rook))
        {
            AddPieceMoves(board, ref moves, from, PieceType.Rook, Attacks.Rook(occupied, from), king, pinned, captureMask, quietMask);
        }
        foreach (Square from in board.Pieces(us, PieceType.Queen))
        {
            AddPieceMoves(board, ref moves, from, PieceType.Queen, Attacks.Queen(occupied, from), king, pinned, captureMask, quietMask);
        }

        GenerateKingMoves(board, ref moves, king, forbidden, occupied, theirs, quietMoves);
        if (quietMoves && checkers.IsEmpty)
        {
            GenerateCastlingMoves(board, ref moves, us, king, occupied, forbidden);
        }
    }

    /// <summary>
    /// Every square attacked by the colour, for the given occupancy.
    /// </summary>
    private static Bitboard AttackedBy(Board board, Colour by, Bitboard occupied)
    {
        Bitboard attacks = Attacks.Pawns(by, board.Pieces(by, PieceType.Pawn)) | Attacks.King(board.KingSquare(by));
        foreach (Square knight in board.Pieces(by, PieceType.Knight))
        {
            attacks |= Attacks.Knight(knight);
        }
        Bitboard queens = board.Pieces(by, PieceType.Queen);
        foreach (Square bishop in board.Pieces(by, PieceType.Bishop) | queens)
        {
            attacks |= Attacks.Bishop(occupied, bishop);
        }
        foreach (Square rook in board.Pieces(by, PieceType.Rook) | queens)
        {
            attacks |= Attacks.Rook(occupied, rook);
        }
        return attacks;
    }

    private static void AddPieceMoves(Board board, ref MoveList moves, Square from, PieceType piece, Bitboard attacks, Square king, Bitboard pinned, Bitboard captureMask, Bitboard quietMask)
    {
        if (pinned.Contains(from))
        {
            attacks &= Attacks.Line(king, from);
        }
        foreach (Square to in attacks & captureMask)
        {
            moves.Add(new Move(from, to, piece, board.At(to)));
        }
        foreach (Square to in attacks & quietMask)
        {
            moves.Add(new Move(from, to, piece));
        }
    }

    private static void GenerateKingMoves(Board board, ref MoveList moves, Square king, Bitboard forbidden, Bitboard occupied, Bitboard theirs, bool quietMoves)
    {
        Bitboard targets = Attacks.King(king) & ~forbidden;
        foreach (Square to in targets & theirs)
        {
            moves.Add(new Move(king, to, PieceType.King, board.At(to)));
        }
        if (quietMoves)
        {
            foreach (Square to in targets & ~occupied)
            {
                moves.Add(new Move(king, to, PieceType.King));
            }
        }
    }

    /// <summary>
    /// Castling is generated only when fully legal: the right is intact, the king and rook are on
    /// their home squares, the squares between them are empty, and neither the king's square nor the
    /// squares it passes over are attacked. The caller has already established the king is not in
    /// check, so the attack set computed without the king is exact for the squares it crosses.
    /// </summary>
    private static void GenerateCastlingMoves(Board board, ref MoveList moves, Colour us, Square king, Bitboard occupied, Bitboard forbidden)
    {
        CastlingRights kingSide = us == Colour.White ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
        CastlingRights queenSide = us == Colour.White ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
        Square home = us == Colour.White ? Square.E1 : Square.E8;
        if ((board.CastlingRights & (kingSide | queenSide)) == CastlingRights.None || king != home)
        {
            return;
        }
        Bitboard rooks = board.Pieces(us, PieceType.Rook);
        Bitboard kingSidePath = Bitboard.Of(home + 1) | Bitboard.Of(home + 2);              // f and g
        Bitboard queenSideEmpty = Bitboard.Of(home - 1) | Bitboard.Of(home - 2) | Bitboard.Of(home - 3);   // d, c and b
        Bitboard queenSidePath = Bitboard.Of(home - 1) | Bitboard.Of(home - 2);             // d and c
        if ((board.CastlingRights & kingSide) != CastlingRights.None
            && rooks.Contains(home + 3) && (occupied & kingSidePath).IsEmpty && (forbidden & kingSidePath).IsEmpty)
        {
            moves.Add(new Move(home, home + 2, PieceType.King, PieceType.None, MoveKind.CastleKingSide));
        }
        if ((board.CastlingRights & queenSide) != CastlingRights.None
            && rooks.Contains(home - 4) && (occupied & queenSideEmpty).IsEmpty && (forbidden & queenSidePath).IsEmpty)
        {
            moves.Add(new Move(home, home - 2, PieceType.King, PieceType.None, MoveKind.CastleQueenSide));
        }
    }

    /// <summary>
    /// Pawn moves for the whole pawn set at once: the set is shifted forward for pushes and
    /// diagonally for captures, masked by what is legal, and each target is mapped back to its source.
    /// </summary>
    private static void GeneratePawnMoves(Board board, ref MoveList moves, Colour us, Colour them, Bitboard occupied, Bitboard theirs, Square king, Bitboard pinned, Bitboard captureMask, Bitboard pushMask, bool quietMoves)
    {
        Bitboard pawns = board.Pieces(us, PieceType.Pawn);
        if (pawns.IsEmpty)
        {
            return;
        }
        Bitboard empty = ~occupied;
        Bitboard promotionRank;
        Bitboard pushes, doublePushes, capturesWest, capturesEast;
        int pushStep, westStep, eastStep;
        if (us == Colour.White)
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
        AddPromotions(board, ref moves, pushes & promotionRank & pushMask, pushStep, king, pinned);
        AddPromotions(board, ref moves, capturesWest & promotionRank & captureMask, westStep, king, pinned);
        AddPromotions(board, ref moves, capturesEast & promotionRank & captureMask, eastStep, king, pinned);
        AddPawnCaptures(board, ref moves, capturesWest & ~promotionRank & captureMask, westStep, king, pinned);
        AddPawnCaptures(board, ref moves, capturesEast & ~promotionRank & captureMask, eastStep, king, pinned);
        if (quietMoves)
        {
            foreach (Square to in pushes & ~promotionRank & pushMask)
            {
                Square from = to - pushStep;
                if (IsAllowedByPins(king, pinned, from, to))
                {
                    moves.Add(new Move(from, to, PieceType.Pawn));
                }
            }
            foreach (Square to in doublePushes & pushMask)
            {
                Square from = to - (2 * pushStep);
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
        Square enPassant = board.EnPassantSquare;
        if (enPassant.IsValid)
        {
            Square captured = enPassant - pushStep;
            if (captureMask.Contains(captured))
            {
                Bitboard theirRooksAndQueens = board.Pieces(them, PieceType.Rook) | board.Pieces(them, PieceType.Queen);
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
                    moves.Add(new Move(from, enPassant, PieceType.Pawn, PieceType.Pawn, MoveKind.EnPassant));
                }
            }
        }
    }

    private static void AddPawnCaptures(Board board, ref MoveList moves, Bitboard targets, int step, Square king, Bitboard pinned)
    {
        foreach (Square to in targets)
        {
            Square from = to - step;
            if (IsAllowedByPins(king, pinned, from, to))
            {
                moves.Add(new Move(from, to, PieceType.Pawn, board.At(to)));
            }
        }
    }

    private static void AddPromotions(Board board, ref MoveList moves, Bitboard targets, int step, Square king, Bitboard pinned)
    {
        foreach (Square to in targets)
        {
            Square from = to - step;
            if (!IsAllowedByPins(king, pinned, from, to))
            {
                continue;
            }
            PieceType captured = board.At(to);
            foreach (PieceType promotion in PieceTypeExtensions.PromotionTypes)
            {
                moves.Add(new Move(from, to, PieceType.Pawn, captured, MoveKind.Promotion, promotion));
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
}
