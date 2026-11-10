namespace StoopidChess.Core;

// Position, part 2 of 2: legal move generation. Part 1 (Position.cs) holds the board and makes
// and unmakes moves.
public sealed partial class Position
{
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
    /// The legal move generator.
    ///
    /// The rule it implements: a move is legal if it does not leave the mover's own king attacked.
    /// The obvious way to apply it is to generate every move the pieces could make, play each one,
    /// look whether the king is attacked, and take it back. This generator instead works out once,
    /// before generating anything, every way a move could break the rule, and only ever generates
    /// moves that avoid them all:
    /// <list type="number">
    /// <item>The king must not step onto a square the enemy attacks.</item>
    /// <item>In check, every other move must capture the checking piece or block its line.</item>
    /// <item>A piece pinned to the king by an enemy slider must stay on the line between them.</item>
    /// </list>
    /// Castling has its own conditions, and en passant one special case that none of the three
    /// covers. See https://www.chessprogramming.org/Move_Generation#Legal and docs/BITBOARDS.md.
    /// </summary>
    private void Generate(ref MoveList moves, bool quietMoves)
    {
        Color us = SideToMove;
        Color them = us.Opponent();
        Bitboard occupied = Occupied;
        Square king = KingSquare(us);
        Bitboard checkers = Checkers;

        // Rule 1: the squares the enemy attacks, worked out with our king lifted off the board. A
        // slider checking the king along a line also attacks the squares behind the king on that
        // line, so the king cannot escape by stepping back along it.
        Bitboard attackedByThem = AttackedBy(them, occupied ^ Bitboard.Of(king));

        // Rule 2: the check mask, the squares every move but the king's must land on. Not in check,
        // that is anywhere; in check by one piece, the checker itself or a square between it and the
        // king. No single move can deal with two checkers at once, so in double check only the king
        // may move.
        Bitboard checkMask;
        if (checkers.IsEmpty)
        {
            checkMask = Bitboard.Full;
        }
        else if (checkers.IsSingle)
        {
            checkMask = checkers | Attacks.Between(king, checkers.First);
        }
        else
        {
            GenerateKingMoves(ref moves, king, attackedByThem, occupied, Pieces(them), quietMoves);
            return;
        }

        // Rule 3: the pinned pieces. An enemy bishop, rook or queen on a line through our king with
        // exactly one piece between them, and that piece ours, pins it: it may move only along the
        // line, which for a pinned piece is Attacks.Line(king, piece).
        Bitboard pinned = Bitboard.Empty;
        Bitboard theirQueens = Pieces(them, Piece.Queen);
        Bitboard pinners = (Attacks.BishopRays(king) & (Pieces(them, Piece.Bishop) | theirQueens))
                         | (Attacks.RookRays(king) & (Pieces(them, Piece.Rook) | theirQueens));
        foreach (Square pinner in pinners)
        {
            Bitboard between = Attacks.Between(king, pinner) & occupied;
            if (between.IsSingle && (between & Pieces(us)).IsNotEmpty)
            {
                pinned |= between;
            }
        }

        // Every piece but the king: its targets are cut to the check mask (rule 2) and, if it is
        // pinned, to its pin line (rule 3)
        Bitboard captureTargets = Pieces(them) & checkMask;
        Bitboard emptyTargets = ~occupied & checkMask;
        Bitboard quietTargets = quietMoves ? emptyTargets : Bitboard.Empty;
        GeneratePawnMoves(ref moves, us, king, pinned, checkMask, captureTargets, emptyTargets, quietMoves);
        foreach (Square from in Pieces(us, Piece.Knight))
        {
            AddPieceMoves(ref moves, from, Piece.Knight, Attacks.Knight(from), king, pinned, captureTargets, quietTargets);
        }
        foreach (Square from in Pieces(us, Piece.Bishop))
        {
            AddPieceMoves(ref moves, from, Piece.Bishop, Attacks.Bishop(occupied, from), king, pinned, captureTargets, quietTargets);
        }
        foreach (Square from in Pieces(us, Piece.Rook))
        {
            AddPieceMoves(ref moves, from, Piece.Rook, Attacks.Rook(occupied, from), king, pinned, captureTargets, quietTargets);
        }
        foreach (Square from in Pieces(us, Piece.Queen))
        {
            AddPieceMoves(ref moves, from, Piece.Queen, Attacks.Queen(occupied, from), king, pinned, captureTargets, quietTargets);
        }

        // The king: any square the enemy does not attack (rule 1), and castling when not in check
        GenerateKingMoves(ref moves, king, attackedByThem, occupied, Pieces(them), quietMoves);
        if (quietMoves && checkers.IsEmpty)
        {
            GenerateCastlingMoves(ref moves, us, king, occupied, attackedByThem);
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

    /// <summary>
    /// The moves of a knight, bishop, rook or queen to the squares it attacks: a capture on each
    /// capture target, a quiet move to each quiet target.
    /// </summary>
    private void AddPieceMoves(ref MoveList moves, Square from, Piece piece, Bitboard attacks, Square king, Bitboard pinned, Bitboard captureTargets, Bitboard quietTargets)
    {
        if (pinned.Contains(from))
        {
            attacks &= Attacks.Line(king, from);   // Rule 3: stay on the pin line (a pinned knight never can)
        }
        foreach (Square to in attacks & captureTargets)
        {
            moves.Add(new Move(from, to, piece, At(to)));
        }
        foreach (Square to in attacks & quietTargets)
        {
            moves.Add(new Move(from, to, piece));
        }
    }

    private void GenerateKingMoves(ref MoveList moves, Square king, Bitboard attackedByThem, Bitboard occupied, Bitboard theirs, bool quietMoves)
    {
        Bitboard targets = Attacks.King(king) & ~attackedByThem;
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
    private void GenerateCastlingMoves(ref MoveList moves, Color us, Square king, Bitboard occupied, Bitboard attackedByThem)
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
            && rooks.Contains(home + 3) && (occupied & kingSidePath).IsEmpty && (attackedByThem & kingSidePath).IsEmpty)
        {
            moves.Add(new Move(home, home + 2, Piece.King, Piece.None, MoveKind.CastleKingSide));
        }
        if ((CastlingRights & queenSide) != CastlingRights.None
            && rooks.Contains(home - 4) && (occupied & queenSideEmpty).IsEmpty && (attackedByThem & queenSidePath).IsEmpty)
        {
            moves.Add(new Move(home, home - 2, Piece.King, Piece.None, MoveKind.CastleQueenSide));
        }
    }

    /// <summary>
    /// Pawn moves for the whole pawn set at once: the set is shifted forward for pushes and
    /// diagonally for captures, the shifted set is cut to the legal targets, and each target left is
    /// mapped back to the pawn that moves there. "West" is towards the a-file, for either color.
    /// </summary>
    private void GeneratePawnMoves(ref MoveList moves, Color us, Square king, Bitboard pinned, Bitboard checkMask, Bitboard captureTargets, Bitboard emptyTargets, bool quietMoves)
    {
        Bitboard pawns = Pieces(us, Piece.Pawn);
        if (pawns.IsEmpty)
        {
            return;
        }

        // Rule 3 for pawns, applied before the shifts: a pinned pawn keeps only the directions that
        // leave it on the line through it and its king. Pinned along the king's file it may still
        // push; pinned along a diagonal it may still capture along that diagonal (which can only
        // be the pinner); pinned along the king's rank it cannot move at all.
        Bitboard pinnedPawns = pawns & pinned;
        Bitboard pinnedOffFile = pinnedPawns & ~Bitboard.OfFile(king.File);              // May not push
        Bitboard pinnedOffDiagonal = pinnedPawns & ~Attacks.Diagonal(king);              // May not capture along an a1-h8 diagonal
        Bitboard pinnedOffAntiDiagonal = pinnedPawns & ~Attacks.AntiDiagonal(king);      // May not capture along an a8-h1 diagonal

        Bitboard empty = ~Occupied;
        Bitboard promotionRank, pushes, doublePushes, westAttacks, eastAttacks;
        int pushStep, westStep, eastStep;
        if (us == Color.White)
        {
            // White's captures to the west run along a8-h1 diagonals, to the east along a1-h8 ones
            promotionRank = Bitboard.Rank8;
            pushes = (pawns ^ pinnedOffFile).North() & empty;
            doublePushes = (pushes & Bitboard.Rank3).North() & empty;
            westAttacks = (pawns ^ pinnedOffAntiDiagonal).NorthWest();
            eastAttacks = (pawns ^ pinnedOffDiagonal).NorthEast();
            pushStep = 8;
            westStep = 7;
            eastStep = 9;
        }
        else
        {
            // Black's the other way round
            promotionRank = Bitboard.Rank1;
            pushes = (pawns ^ pinnedOffFile).South() & empty;
            doublePushes = (pushes & Bitboard.Rank6).South() & empty;
            westAttacks = (pawns ^ pinnedOffDiagonal).SouthWest();
            eastAttacks = (pawns ^ pinnedOffAntiDiagonal).SouthEast();
            pushStep = -8;
            westStep = -9;
            eastStep = -7;
        }
        Bitboard capturesWest = westAttacks & captureTargets;
        Bitboard capturesEast = eastAttacks & captureTargets;

        // Promotions are generated even when only captures are wanted
        AddPromotions(ref moves, pushes & promotionRank & emptyTargets, pushStep);
        AddPromotions(ref moves, capturesWest & promotionRank, westStep);
        AddPromotions(ref moves, capturesEast & promotionRank, eastStep);
        AddPawnMoves(ref moves, capturesWest & ~promotionRank, westStep, MoveKind.Normal);
        AddPawnMoves(ref moves, capturesEast & ~promotionRank, eastStep, MoveKind.Normal);
        if (quietMoves)
        {
            AddPawnMoves(ref moves, pushes & ~promotionRank & emptyTargets, pushStep, MoveKind.Normal);
            AddPawnMoves(ref moves, doublePushes & emptyTargets, 2 * pushStep, MoveKind.DoublePawnPush);
        }

        // En passant: the capture lands on an empty square and takes the pawn beside the capturer.
        // Rule 2 applies to the captured pawn (in check, that pawn must be the checker: an en
        // passant capture can never block a check) and rule 3 through the attack sets above. The
        // special case: the capture takes two pawns off the capturer's rank at once, and if our king
        // stands on that rank with an enemy rook or queen beyond the two pawns, that empties the line
        // between them. Neither pawn on its own was pinned, so rule 3 does not see it.
        Square enPassant = EnPassantSquare;
        if (enPassant.IsValid && checkMask.Contains(enPassant - pushStep))
        {
            Square captured = enPassant - pushStep;
            if (eastAttacks.Contains(enPassant))
            {
                AddEnPassant(ref moves, enPassant - eastStep, enPassant, captured, king);
            }
            if (westAttacks.Contains(enPassant))
            {
                AddEnPassant(ref moves, enPassant - westStep, enPassant, captured, king);
            }
        }
    }

    private void AddEnPassant(ref MoveList moves, Square from, Square to, Square captured, Square king)
    {
        if (king.Rank == from.Rank)
        {
            Bitboard withoutPawns = Occupied ^ Bitboard.Of(from) ^ Bitboard.Of(captured);
            Bitboard theirRooksAndQueens = Pieces(SideToMove.Opponent(), Piece.Rook) | Pieces(SideToMove.Opponent(), Piece.Queen);
            if ((Attacks.Rook(withoutPawns, king) & Bitboard.OfRank(king.Rank) & theirRooksAndQueens).IsNotEmpty)
            {
                return;
            }
        }
        moves.Add(new Move(from, to, Piece.Pawn, Piece.Pawn, MoveKind.EnPassant));
    }

    /// <summary>
    /// One move per target, from the pawn a step behind it (a capture when the target is occupied).
    /// </summary>
    private void AddPawnMoves(ref MoveList moves, Bitboard targets, int step, MoveKind kind)
    {
        foreach (Square to in targets)
        {
            moves.Add(new Move(to - step, to, Piece.Pawn, At(to), kind));
        }
    }

    /// <summary>
    /// Four moves per target, one for each piece the pawn can promote to.
    /// </summary>
    private void AddPromotions(ref MoveList moves, Bitboard targets, int step)
    {
        foreach (Square to in targets)
        {
            Piece captured = At(to);
            foreach (Piece promotion in PieceExtensions.PromotionTypes)
            {
                moves.Add(new Move(to - step, to, Piece.Pawn, captured, MoveKind.Promotion, promotion));
            }
        }
    }
}
