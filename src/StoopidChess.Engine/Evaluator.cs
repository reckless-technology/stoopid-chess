using StoopidChess.Core;

namespace StoopidChess.Engine;

/// <summary>
/// Static evaluation, in centipawns from the side to move's point of view. The baseline is Tomasz
/// Michniewski's Simplified Evaluation Function (https://www.chessprogramming.org/Simplified_Evaluation_Function):
/// material plus piece-square tables, with a separate king table once the endgame is reached.
/// Every further term is added on top of it one at a time and kept only if self-play says so;
/// docs/EVALUATION.md records the experiments.
/// </summary>
public sealed class Evaluator
{
    // Material values from the Simplified Evaluation Function. The king carries no value: both
    // kings are always present.
    public const int PawnValue = 100;
    public const int KnightValue = 320;
    public const int BishopValue = 330;
    public const int RookValue = 500;
    public const int QueenValue = 900;

    // Mobility: centipawns per square a piece can move to or capture on
    public const int KnightMobility = 6;
    public const int BishopMobility = 5;
    public const int RookMobility = 4;
    public const int QueenMobility = 2;

    // King safety (middlegame only): centipawns per square of the king zone (the king's square and
    // its neighbours) attacked by an enemy piece
    public const int KingZoneAttackPenalty = 4;
    public const int KingZoneQueenAttackPenalty = 8;
    public const int WeakKingSquarePenalty = 15;

    // Two bishops cover both square colours
    public const int BishopPairBonus = 45;

    // Having the move is worth something in itself
    public const int TempoBonus = 10;

    // A knight on an outpost: in the opponent's half (ranks 4 to 6 from its own side), defended by
    // an own pawn, on a square no enemy pawn can ever attack
    public const int KnightOutpostBonus = 20;

    // King shelter and pawn storm (middlegame only), on the king's file and the two beside it. The
    // shelter value is by the rank of the own pawn nearest the king in front of it (an unmoved pawn
    // is the best shelter, a file with no pawn is an open line onto the king); the storm penalty is
    // by the enemy pawn nearest the king in front of it, halved when an own pawn blocks it.
    private static readonly int[] ShelterByRank = [0, 17, 10, 3, 0, 0, 0, 0];      // Pawn rank from its own side
    private const int ShelterMissing = -20;
    private static readonly int[] StormByDistance = [0, 0, 20, 13, 7, 0, 0, 0];   // Ranks between the king and the pawn

    // Passed pawns: bonus by the pawn's rank from its own side (index 1 is the second rank)
    private static readonly int[] PassedPawnBonus = [0, 5, 10, 20, 35, 60, 100, 0];

    // The squares ahead of a pawn on its own file and the two beside it, indexed [colour][square]:
    // an enemy pawn there stops it being passed
    private static readonly Bitboard[][] PassedPawnSpan = BuildPassedPawnSpans();

    // Piece-square tables from white's point of view with rank 8 as the first row, so that they read
    // like a board diagram.
    private static readonly int[] PawnTable =
    [
         0,  0,  0,  0,  0,  0,  0,  0,
        50, 50, 50, 50, 50, 50, 50, 50,
        10, 10, 20, 30, 30, 20, 10, 10,
         5,  5, 10, 25, 25, 10,  5,  5,
         0,  0,  0, 20, 20,  0,  0,  0,
         5, -5,-10,  0,  0,-10, -5,  5,
         5, 10, 10,-20,-20, 10, 10,  5,
         0,  0,  0,  0,  0,  0,  0,  0,
    ];

    private static readonly int[] KnightTable =
    [
        -50,-40,-30,-30,-30,-30,-40,-50,
        -40,-20,  0,  0,  0,  0,-20,-40,
        -30,  0, 10, 15, 15, 10,  0,-30,
        -30,  5, 15, 20, 20, 15,  5,-30,
        -30,  0, 15, 20, 20, 15,  0,-30,
        -30,  5, 10, 15, 15, 10,  5,-30,
        -40,-20,  0,  5,  5,  0,-20,-40,
        -50,-40,-30,-30,-30,-30,-40,-50,
    ];

    private static readonly int[] BishopTable =
    [
        -20,-10,-10,-10,-10,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5, 10, 10,  5,  0,-10,
        -10,  5,  5, 10, 10,  5,  5,-10,
        -10,  0, 10, 10, 10, 10,  0,-10,
        -10, 10, 10, 10, 10, 10, 10,-10,
        -10,  5,  0,  0,  0,  0,  5,-10,
        -20,-10,-10,-10,-10,-10,-10,-20,
    ];

    private static readonly int[] RookTable =
    [
          0,  0,  0,  0,  0,  0,  0,  0,
          5, 10, 10, 10, 10, 10, 10,  5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
         -5,  0,  0,  0,  0,  0,  0, -5,
          0,  0,  0,  5,  5,  0,  0,  0,
    ];

    private static readonly int[] QueenTable =
    [
        -20,-10,-10, -5, -5,-10,-10,-20,
        -10,  0,  0,  0,  0,  0,  0,-10,
        -10,  0,  5,  5,  5,  5,  0,-10,
         -5,  0,  5,  5,  5,  5,  0, -5,
          0,  0,  5,  5,  5,  5,  0, -5,
        -10,  5,  5,  5,  5,  5,  0,-10,
        -10,  0,  5,  0,  0,  0,  0,-10,
        -20,-10,-10, -5, -5,-10,-10,-20,
    ];

    // The king stays safely on the back rank in the opening and middle game...
    private static readonly int[] KingTable =
    [
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -30,-40,-40,-50,-50,-40,-40,-30,
        -20,-30,-30,-40,-40,-30,-30,-20,
        -10,-20,-20,-20,-20,-20,-20,-10,
         20, 20,  0,  0,  0,  0, 20, 20,
         20, 30, 10,  0,  0, 10, 30, 20,
    ];

    // ...and heads for the centre in the endgame.
    private static readonly int[] KingEndgameTable =
    [
        -50,-40,-30,-20,-20,-30,-40,-50,
        -30,-20,-10,  0,  0,-10,-20,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 30, 40, 40, 30,-10,-30,
        -30,-10, 20, 30, 30, 20,-10,-30,
        -30,-30,  0,  0,  0,  0,-30,-30,
        -50,-30,-30,-30,-30,-30,-30,-50,
    ];

    // Material plus piece-square value of each piece type on each square, from white's point of
    // view: positive for white pieces, negative (and vertically mirrored) for black pieces.
    // Indexed [colour][type][square]; the king is stored twice, for the middle game and the endgame.
    private static readonly int[][][] PieceSquareValues = BuildPieceSquareValues();
    private const int KingEndgameIndex = 7;

    // The evaluation cache: one 64-bit word per entry holding the top 48 bits of the Zobrist key and
    // the 16-bit score, always replaced. Positions reached again by transposition, by a re-search or
    // by the next iteration are not evaluated twice.
    private const int CacheBits = 16;   // 65,536 entries, 512 KB: small enough to stay in the CPU cache, which measured best
    private readonly ulong[] cache = new ulong[1 << CacheBits];

    /// <summary>
    /// The score of the position from the side to move's point of view, in centipawns.
    /// </summary>
    public int Evaluate(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        ulong key = board.Key;
        ref ulong entry = ref cache[key & ((1UL << CacheBits) - 1)];
        if ((entry >> 16) == (key >> 16))
        {
            return (short)(entry & 0xFFFF);
        }
        int score = Compute(board);
        entry = (key & ~0xFFFFUL) | (ushort)(short)score;
        return score;
    }

    /// <summary>
    /// Forget every cached evaluation, for a new game.
    /// </summary>
    public void Clear()
    {
        Array.Clear(cache);
    }

    private static int Compute(Board board)
    {
        int score = 0;
        bool endgame = IsEndgame(board);
        // One pass over each side's pieces serves mobility, the pressure on the enemy king and the
        // attack maps the weak king squares need
        Bitboard whiteZone = KingZone(board, Colour.White);
        Bitboard blackZone = KingZone(board, Colour.Black);
        SideAttacks white = PieceAttacks(board, Colour.White, blackZone);
        SideAttacks black = PieceAttacks(board, Colour.Black, whiteZone);
        foreach (Colour colour in ColourExtensions.Sides)
        {
            int[][] tables = PieceSquareValues[(int)colour];
            for (PieceType type = PieceType.Pawn; type <= PieceType.Queen; type++)
            {
                int[] table = tables[(int)type];
                foreach (Square square in board.Pieces(colour, type))
                {
                    score += table[square];
                }
            }
            score += tables[endgame ? KingEndgameIndex : (int)PieceType.King][board.KingSquare(colour)];
            (SideAttacks us, SideAttacks them, Bitboard zone) = colour == Colour.White ? (white, black, whiteZone) : (black, white, blackZone);
            int own = us.Mobility + PassedPawns(board, colour, endgame) + KnightOutposts(board, colour);
            if (board.Pieces(colour, PieceType.Bishop).Count >= 2)
            {
                own += BishopPairBonus;
            }
            if (!endgame)
            {
                // The pressure on the king: enemy piece attacks into the zone, and the zone squares the
                // enemy attacks that nothing but the king defends, where an attack breaks through
                own -= them.KingZoneAttack + (WeakKingSquarePenalty * (zone & them.Attacks & ~us.Attacks).Count);
                own += ShelterAndStorm(board, colour);
            }
            score += colour == Colour.White ? own : -own;
        }
        return TempoBonus + (board.SideToMove == Colour.White ? score : -score);
    }

    /// <summary>
    /// The pawn cover in front of the king and the enemy pawns advancing on it, over the king's file
    /// and its neighbours (moved in from the edge so that a king in the corner still counts three).
    /// </summary>
    private static int ShelterAndStorm(Board board, Colour colour)
    {
        Square king = board.KingSquare(colour);
        Bitboard ownPawns = board.Pieces(colour, PieceType.Pawn);
        Bitboard enemyPawns = board.Pieces(colour.Opponent(), PieceType.Pawn);
        // The ranks in front of the king, from its point of view
        Bitboard ahead = colour == Colour.White
            ? new Bitboard(~0UL << (8 * (king.Rank + 1)))
            : new Bitboard(king.Rank == 0 ? 0UL : ~0UL >> (8 * (8 - king.Rank)));
        int score = 0;
        int centreFile = Math.Clamp(king.File, 1, 6);
        for (int file = centreFile - 1; file <= centreFile + 1; file++)
        {
            Bitboard column = Bitboard.OfFile(file) & ahead;
            Bitboard shelter = column & ownPawns;
            if (shelter.IsEmpty)
            {
                score += ShelterMissing;
            }
            else
            {
                Square pawn = colour == Colour.White ? shelter.First : shelter.Last;
                score += ShelterByRank[colour == Colour.White ? pawn.Rank : 7 - pawn.Rank];
            }
            Bitboard storm = column & enemyPawns;
            if (storm.IsNotEmpty)
            {
                Square pawn = colour == Colour.White ? storm.First : storm.Last;
                int penalty = StormByDistance[Math.Abs(pawn.Rank - king.Rank)];
                // An enemy pawn faced by one of ours cannot advance to open lines
                if (ownPawns.Contains(pawn - Board.PawnDirection(colour.Opponent())))
                {
                    penalty /= 2;
                }
                score -= penalty;
            }
        }
        return score;
    }

    /// <summary>
    /// Knights on outposts: squares in the opponent's half that an own pawn defends and that no enemy
    /// pawn can reach (none ahead on the neighbouring files, which the passed-pawn spans encode).
    /// </summary>
    private static int KnightOutposts(Board board, Colour colour)
    {
        Colour them = colour.Opponent();
        Bitboard ownPawns = board.Pieces(colour, PieceType.Pawn);
        Bitboard enemyPawns = board.Pieces(them, PieceType.Pawn);
        Bitboard outpostRanks = colour == Colour.White
            ? Bitboard.Rank4 | Bitboard.Rank5 | Bitboard.Rank6
            : Bitboard.Rank5 | Bitboard.Rank4 | Bitboard.Rank3;
        Bitboard defended = Attacks.Pawns(colour, ownPawns);
        Bitboard[] spans = PassedPawnSpan[(int)colour];
        int bonus = 0;
        foreach (Square square in board.Pieces(colour, PieceType.Knight) & outpostRanks & defended)
        {
            // The span ahead on the neighbouring files, less the knight's own file
            Bitboard adjacentAhead = spans[square] & ~Bitboard.OfFile(square.File);
            if ((adjacentAhead & enemyPawns).IsEmpty)
            {
                bonus += KnightOutpostBonus;
            }
        }
        return bonus;
    }

    /// <summary>
    /// Bonus for each pawn with no enemy pawn ahead of it on its own or the adjacent files, growing
    /// as it advances.
    /// </summary>
    private static int PassedPawns(Board board, Colour colour, bool endgame)
    {
        Colour them = colour.Opponent();
        Bitboard enemyPawns = board.Pieces(them, PieceType.Pawn);
        Bitboard[] spans = PassedPawnSpan[(int)colour];
        Square ownKing = board.KingSquare(colour);
        Square enemyKing = board.KingSquare(them);
        int bonus = 0;
        foreach (Square square in board.Pieces(colour, PieceType.Pawn))
        {
            if ((spans[square] & enemyPawns).IsNotEmpty)
            {
                continue;
            }
            int rankBonus = PassedPawnBonus[colour == Colour.White ? square.Rank : 7 - square.Rank];
            bonus += rankBonus;
            if (endgame)
            {
                // Kings decide passed pawns in the endgame (after Crafty): the further the enemy king is
                // from the square in front of the pawn, and the nearer our own, the better
                var next = new Square(square + (colour == Colour.White ? 8 : -8));
                bonus += rankBonus * ((2 * Distance(enemyKing, next)) - Distance(ownKing, next)) / 8;
            }
        }
        return bonus;
    }

    private static int Distance(Square a, Square b)
    {
        return Math.Max(Math.Abs(a.File - b.File), Math.Abs(a.Rank - b.Rank));
    }

    private static Bitboard[][] BuildPassedPawnSpans()
    {
        Bitboard[][] spans = new Bitboard[3][];
        for (int colour = 0; colour < 3; colour++)
        {
            spans[colour] = new Bitboard[Square.Count];
            if (colour == (int)Colour.None)
            {
                continue;
            }
            foreach (Square square in Square.All)
            {
                Bitboard files = Bitboard.OfFile(square.File)
                    | (square.File > 0 ? Bitboard.OfFile(square.File - 1) : Bitboard.Empty)
                    | (square.File < 7 ? Bitboard.OfFile(square.File + 1) : Bitboard.Empty);
                Bitboard ranksAhead = Bitboard.Empty;
                for (int rank = 0; rank < 8; rank++)
                {
                    if (colour == (int)Colour.White ? rank > square.Rank : rank < square.Rank)
                    {
                        ranksAhead |= Bitboard.OfRank(rank);
                    }
                }
                spans[colour][square] = files & ranksAhead;
            }
        }
        return spans;
    }

    /// <summary>
    /// What one pass over a side's knights, bishops, rooks and queens yields: their mobility, the
    /// pressure they put on the enemy king zone, and every square the side's pieces and pawns attack.
    /// </summary>
    private readonly record struct SideAttacks(int Mobility, int KingZoneAttack, Bitboard Attacks);

    /// <summary>
    /// The king, its neighbours and the three squares beyond them in front, where an attack builds up.
    /// </summary>
    private static Bitboard KingZone(Board board, Colour colour)
    {
        Square king = board.KingSquare(colour);
        Bitboard ring = Attacks.King(king) | Bitboard.Of(king);
        return ring | ring.Forward(colour);
    }

    /// <summary>
    /// Mobility (squares each piece can move to or capture on, weighted by piece type, squares
    /// guarded by enemy pawns being no use to a piece), the attacks into the enemy king zone (each
    /// attacked square counted once per attacking piece) and the side's attack map, in one pass.
    /// </summary>
    private static SideAttacks PieceAttacks(Board board, Colour colour, Bitboard enemyZone)
    {
        Bitboard occupied = board.Occupied;
        Bitboard pawnGuarded = Attacks.Pawns(colour.Opponent(), board.Pieces(colour.Opponent(), PieceType.Pawn));
        Bitboard targets = ~board.Pieces(colour) & ~pawnGuarded;
        Bitboard attacks = Attacks.Pawns(colour, board.Pieces(colour, PieceType.Pawn));
        int mobility = 0;
        int zoneAttack = 0;
        foreach (Square square in board.Pieces(colour, PieceType.Knight))
        {
            Bitboard reach = Attacks.Knight(square);
            attacks |= reach;
            mobility += KnightMobility * (reach & targets).Count;
            zoneAttack += KingZoneAttackPenalty * (reach & enemyZone).Count;
        }
        foreach (Square square in board.Pieces(colour, PieceType.Bishop))
        {
            Bitboard reach = Attacks.Bishop(occupied, square);
            attacks |= reach;
            mobility += BishopMobility * (reach & targets).Count;
            zoneAttack += KingZoneAttackPenalty * (reach & enemyZone).Count;
        }
        foreach (Square square in board.Pieces(colour, PieceType.Rook))
        {
            Bitboard reach = Attacks.Rook(occupied, square);
            attacks |= reach;
            mobility += RookMobility * (reach & targets).Count;
            zoneAttack += KingZoneAttackPenalty * (reach & enemyZone).Count;
        }
        foreach (Square square in board.Pieces(colour, PieceType.Queen))
        {
            Bitboard reach = Attacks.Queen(occupied, square);
            attacks |= reach;
            mobility += QueenMobility * (reach & targets).Count;
            zoneAttack += KingZoneQueenAttackPenalty * (reach & enemyZone).Count;
        }
        return new SideAttacks(mobility, zoneAttack, attacks);
    }

    /// <summary>
    /// The Simplified Evaluation Function's endgame: both sides have no queens, or every side that
    /// has a queen has no other piece beyond at most one minor piece.
    /// </summary>
    public static bool IsEndgame(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        foreach (Colour colour in ColourExtensions.Sides)
        {
            if (board.Pieces(colour, PieceType.Queen).IsNotEmpty
                && (board.Pieces(colour, PieceType.Rook).IsNotEmpty || (board.Pieces(colour, PieceType.Knight) | board.Pieces(colour, PieceType.Bishop)).Count > 1))
            {
                return false;
            }
        }
        return true;
    }

    private static int[][][] BuildPieceSquareValues()
    {
        (int[] Table, int Material)[] source =
        [
            ([], 0),                                // None
            (PawnTable, PawnValue),
            (KnightTable, KnightValue),
            (BishopTable, BishopValue),
            (RookTable, RookValue),
            (QueenTable, QueenValue),
            (KingTable, 0),                         // Kings are always present, so they carry no material value
            (KingEndgameTable, 0),
        ];
        int[][][] values = new int[3][][];
        for (int colour = 0; colour < 3; colour++)
        {
            values[colour] = new int[source.Length][];
            for (int type = 0; type < source.Length; type++)
            {
                values[colour][type] = new int[Square.Count];
                (int[] table, int material) = source[type];
                if (table.Length == 0 || colour == (int)Colour.None)
                {
                    continue;
                }
                foreach (Square square in Square.All)
                {
                    // White reads the table with rank 8 at the top; black sees the board flipped.
                    int row = colour == (int)Colour.White ? 7 - square.Rank : square.Rank;
                    int value = table[(row * 8) + square.File] + material;
                    values[colour][type][square] = colour == (int)Colour.White ? value : -value;
                }
            }
        }
        return values;
    }
}
