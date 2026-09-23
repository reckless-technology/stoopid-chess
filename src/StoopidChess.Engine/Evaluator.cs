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
    // Material values from the Simplified Evaluation Function, minor and rook values and everything
    // below down to the passed pawn ladder retuned by SPSA (tools/spsa-runs/2026-09-20-complete).
    // The king carries no value: both kings are always present.
    public const int PawnValue = 100;
    public const int KnightValue = 324;
    public const int BishopValue = 328;
    public const int RookValue = 510;
    public const int QueenValue = 1016;

    // Mobility: centipawns per square a piece can move to or capture on
    public const int KnightMobility = 5;
    public const int BishopMobility = 6;
    public const int RookMobility = 7;
    public const int QueenMobility = 3;

    // King safety (middlegame only): centipawns per square of the king zone (the king's square and
    // its neighbours) attacked by an enemy piece
    public const int KingZoneAttackPenalty = 4;
    public const int KingZoneQueenAttackPenalty = 8;
    public const int WeakKingSquarePenalty = 16;

    // Two bishops cover both square colors
    public const int BishopPairBonus = 43;

    // Having the move is worth something in itself
    public const int TempoBonus = 10;

    // A knight on an outpost: in the opponent's half (ranks 4 to 6 from its own side), defended by
    // an own pawn, on a square no enemy pawn can ever attack
    public const int KnightOutpostBonus = 27;

    // Pawn structure, cached by the pawn key. Isolated: no own pawn on an adjacent file. Doubled:
    // an own pawn ahead on the same file (each extra counted once). Backward: the square in front
    // is controlled by an enemy pawn while no own pawn on an adjacent file stands level or behind
    // to ever support the advance (isolated pawns are not counted twice). Connected: defended by an
    // own pawn or standing beside one.
    public const int IsolatedPawnPenalty = 9;
    public const int DoubledPawnPenalty = 11;
    public const int BackwardPawnPenalty = 7;
    public const int ConnectedPawnBonus = 6;

    // King shelter and pawn storm (middlegame only), on the king's file and the two beside it. The
    // shelter value is by the rank of the own pawn nearest the king in front of it (an unmoved pawn
    // is the best shelter, a file with no pawn is an open line onto the king); the storm penalty is
    // by the enemy pawn nearest the king in front of it, halved when an own pawn blocks it.
    internal static readonly int[] DefaultShelterByRank = [0, 15, 11, 1, 0, 0, 0, 0];      // Pawn rank from its own side
    public const int ShelterMissing = -16;
    internal static readonly int[] DefaultStormByDistance = [0, 0, 19, 11, 9, 0, 0, 0];   // Ranks between the king and the pawn

    // Passed pawns: bonus by the pawn's rank from its own side (index 1 is the second rank)
    internal static readonly int[] DefaultPassedPawnBonus = [0, 5, 6, 22, 42, 74, 124, 0];

    // The squares ahead of a pawn on its own file and the two beside it, indexed [color][square]:
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
    // Indexed [color][type][square]; the king is stored twice, for the middle game and the endgame.
    private readonly EvaluationParameters parameters;
    private int[][][] pieceSquareValues = [];
    private int builtVersion = -1;

    public Evaluator() : this(new EvaluationParameters())
    {
    }

    public Evaluator(EvaluationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        this.parameters = parameters;
    }

    /// <summary>
    /// The tunable values this evaluator reads; set them through <see cref="EvaluationParameters.TrySet"/>.
    /// </summary>
    public EvaluationParameters Parameters => parameters;
    private const int KingEndgameIndex = 7;

    // The evaluation cache: one 64-bit word per entry holding the top 48 bits of the Zobrist key and
    // the 16-bit score, always replaced. Positions reached again by transposition, by a re-search or
    // by the next iteration are not evaluated twice.
    private const int CacheBits = 16;   // 65,536 entries, 512 KB: small enough to stay in the CPU cache, which measured best
    private readonly ulong[] cache = new ulong[1 << CacheBits];

    // The pawn structure cache, the same single-word scheme keyed by the pawn key: pawn structures
    // repeat across vast numbers of positions, so nearly every probe hits
    private const int PawnCacheBits = 14;
    private readonly ulong[] pawnCache = new ulong[1 << PawnCacheBits];

    /// <summary>
    /// The score of the position from the side to move's point of view, in centipawns.
    /// </summary>
    public int Evaluate(Position position)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (builtVersion != parameters.Version)
        {
            // The parameters changed: rebuild the material tables and forget every cached score
            pieceSquareValues = BuildPieceSquareValues();
            builtVersion = parameters.Version;
            Array.Clear(cache);
            Array.Clear(pawnCache);
        }
        ulong key = position.Key;
        ref ulong entry = ref cache[key & ((1UL << CacheBits) - 1)];
        if ((entry >> 16) == (key >> 16))
        {
            return (short)(entry & 0xFFFF);
        }
        int score = Compute(position);
        entry = (key & ~0xFFFFUL) | (ushort)(short)score;
        return score;
    }

    /// <summary>
    /// Forget every cached evaluation, for a new game.
    /// </summary>
    public void Clear()
    {
        Array.Clear(cache);
        Array.Clear(pawnCache);
    }

    private int Compute(Position position)
    {
        int score = 0;
        bool endgame = IsEndgame(position);
        // One pass over each side's pieces serves mobility, the pressure on the enemy king and the
        // attack maps the weak king squares need
        Bitboard whiteZone = KingZone(position, Color.White);
        Bitboard blackZone = KingZone(position, Color.Black);
        SideAttacks white = PieceAttacks(position, Color.White, blackZone);
        SideAttacks black = PieceAttacks(position, Color.Black, whiteZone);
        foreach (Color color in ColorExtensions.Sides)
        {
            int[][] tables = pieceSquareValues[(int)color];
            for (Piece type = Piece.Pawn; type <= Piece.Queen; type++)
            {
                int[] table = tables[(int)type];
                foreach (Square square in position.Pieces(color, type))
                {
                    score += table[square];
                }
            }
            score += tables[endgame ? KingEndgameIndex : (int)Piece.King][position.KingSquare(color)];
            (SideAttacks us, SideAttacks them, Bitboard zone) = color == Color.White ? (white, black, whiteZone) : (black, white, blackZone);
            int own = us.Mobility + PassedPawns(position, color, endgame) + KnightOutposts(position, color);
            if (position.Pieces(color, Piece.Bishop).Count >= 2)
            {
                own += parameters.BishopPairBonus;
            }
            if (!endgame)
            {
                // The pressure on the king: enemy piece attacks into the zone, and the zone squares the
                // enemy attacks that nothing but the king defends, where an attack breaks through
                own -= them.KingZoneAttack + (parameters.WeakKingSquarePenalty * (zone & them.Attacks & ~us.Attacks).Count);
                own += ShelterAndStorm(position, color);
            }
            score += color == Color.White ? own : -own;
        }
        score += PawnStructure(position);
        score = ScaleDrawishness(position, score);
        return parameters.TempoBonus + (position.SideToMove == Color.White ? score : -score);
    }

    /// <summary>
    /// The pawn-only structure terms for both sides, white-positive, served from the pawn cache.
    /// </summary>
    private int PawnStructure(Position position)
    {
        ulong key = position.PawnKey;
        ref ulong entry = ref pawnCache[key & ((1UL << PawnCacheBits) - 1)];
        if ((entry >> 16) == (key >> 16))
        {
            return (short)(entry & 0xFFFF);
        }
        int score = PawnStructure(position, Color.White) - PawnStructure(position, Color.Black);
        entry = (key & ~0xFFFFUL) | (ushort)(short)score;
        return score;
    }

    private int PawnStructure(Position position, Color color)
    {
        Bitboard pawns = position.Pieces(color, Piece.Pawn);
        if (pawns.IsEmpty)
        {
            return 0;
        }
        Bitboard enemyPawns = position.Pieces(color.Opponent(), Piece.Pawn);
        Bitboard fileFill = FileFill(pawns);
        Bitboard isolated = pawns & ~(fileFill.East() | fileFill.West());
        Bitboard aheadOfOwn = color == Color.White ? NorthFill(pawns.North()) : SouthFill(pawns.South());
        Bitboard doubled = pawns & aheadOfOwn;
        Bitboard connected = pawns & (Attacks.Pawns(color, pawns) | pawns.East() | pawns.West());
        // A pawn that no neighbouring pawn can ever step up beside: only squares at or above an
        // adjacent own pawn are reachable support
        Bitboard everSupported = color == Color.White
            ? NorthFill(pawns).East() | NorthFill(pawns).West()
            : SouthFill(pawns).East() | SouthFill(pawns).West();
        Bitboard enemyControl = Attacks.Pawns(color.Opponent(), enemyPawns);
        Bitboard stopControlled = color == Color.White ? enemyControl.South() : enemyControl.North();
        Bitboard backward = pawns & ~everSupported & ~isolated & stopControlled;
        return (parameters.ConnectedPawnBonus * connected.Count)
            - (parameters.IsolatedPawnPenalty * isolated.Count)
            - (parameters.DoubledPawnPenalty * doubled.Count)
            - (parameters.BackwardPawnPenalty * backward.Count);
    }

    private static Bitboard NorthFill(Bitboard pawns)
    {
        Bitboard fill = pawns;
        for (int step = 0; step < 7; step++)
        {
            fill |= fill.North();
        }
        return fill;
    }

    private static Bitboard SouthFill(Bitboard pawns)
    {
        Bitboard fill = pawns;
        for (int step = 0; step < 7; step++)
        {
            fill |= fill.South();
        }
        return fill;
    }

    private static Bitboard FileFill(Bitboard pawns) => NorthFill(pawns) | SouthFill(pawns);

    /// <summary>
    /// Material configurations that cannot (or can barely) be won however far ahead the score says
    /// the winner is, from classical Stockfish's material rules: a side with no pawns needs more
    /// than a bishop of extra material to make progress at all, and two knights cannot force mate
    /// against a bare king.
    /// </summary>
    private int ScaleDrawishness(Position position, int score)
    {
        if (score == 0)
        {
            return 0;
        }
        Color winner = score > 0 ? Color.White : Color.Black;
        if (position.Pieces(winner, Piece.Pawn).IsNotEmpty)
        {
            return score;
        }
        Color loser = winner.Opponent();
        int winnerMaterial = NonPawnMaterial(position, winner);
        int loserMaterial = NonPawnMaterial(position, loser);
        if (winnerMaterial - loserMaterial <= parameters.BishopValue)
        {
            // Up at most a minor with no pawns left: a lone minor can never win, and even a rook
            // for a minor down wins only exceptionally
            return winnerMaterial < parameters.RookValue ? score / 16 : score / 4;
        }
        if (loserMaterial == 0 && position.Pieces(loser, Piece.Pawn).IsEmpty
            && winnerMaterial == 2 * parameters.KnightValue && position.Count(winner, Piece.Knight) == 2)
        {
            return score / 16;
        }
        return score;
    }

    private int NonPawnMaterial(Position position, Color color)
    {
        return (position.Count(color, Piece.Knight) * parameters.KnightValue)
            + (position.Count(color, Piece.Bishop) * parameters.BishopValue)
            + (position.Count(color, Piece.Rook) * parameters.RookValue)
            + (position.Count(color, Piece.Queen) * parameters.QueenValue);
    }

    /// <summary>
    /// The pawn cover in front of the king and the enemy pawns advancing on it, over the king's file
    /// and its neighbours (moved in from the edge so that a king in the corner still counts three).
    /// </summary>
    private int ShelterAndStorm(Position position, Color color)
    {
        Square king = position.KingSquare(color);
        Bitboard ownPawns = position.Pieces(color, Piece.Pawn);
        Bitboard enemyPawns = position.Pieces(color.Opponent(), Piece.Pawn);
        // The ranks in front of the king, from its point of view
        Bitboard ahead = color == Color.White
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
                score += parameters.ShelterMissing;
            }
            else
            {
                Square pawn = color == Color.White ? shelter.First : shelter.Last;
                score += parameters.ShelterByRank[color == Color.White ? pawn.Rank : 7 - pawn.Rank];
            }
            Bitboard storm = column & enemyPawns;
            if (storm.IsNotEmpty)
            {
                Square pawn = color == Color.White ? storm.First : storm.Last;
                int penalty = parameters.StormByDistance[Math.Abs(pawn.Rank - king.Rank)];
                // An enemy pawn faced by one of ours cannot advance to open lines
                if (ownPawns.Contains(pawn - Position.PawnDirection(color.Opponent())))
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
    private int KnightOutposts(Position position, Color color)
    {
        Color them = color.Opponent();
        Bitboard ownPawns = position.Pieces(color, Piece.Pawn);
        Bitboard enemyPawns = position.Pieces(them, Piece.Pawn);
        Bitboard outpostRanks = color == Color.White
            ? Bitboard.Rank4 | Bitboard.Rank5 | Bitboard.Rank6
            : Bitboard.Rank5 | Bitboard.Rank4 | Bitboard.Rank3;
        Bitboard defended = Attacks.Pawns(color, ownPawns);
        Bitboard[] spans = PassedPawnSpan[(int)color];
        int bonus = 0;
        foreach (Square square in position.Pieces(color, Piece.Knight) & outpostRanks & defended)
        {
            // The span ahead on the neighbouring files, less the knight's own file
            Bitboard adjacentAhead = spans[square] & ~Bitboard.OfFile(square.File);
            if ((adjacentAhead & enemyPawns).IsEmpty)
            {
                bonus += parameters.KnightOutpostBonus;
            }
        }
        return bonus;
    }

    /// <summary>
    /// Bonus for each pawn with no enemy pawn ahead of it on its own or the adjacent files, growing
    /// as it advances.
    /// </summary>
    private int PassedPawns(Position position, Color color, bool endgame)
    {
        Color them = color.Opponent();
        Bitboard enemyPawns = position.Pieces(them, Piece.Pawn);
        Bitboard[] spans = PassedPawnSpan[(int)color];
        Square ownKing = position.KingSquare(color);
        Square enemyKing = position.KingSquare(them);
        int bonus = 0;
        foreach (Square square in position.Pieces(color, Piece.Pawn))
        {
            if ((spans[square] & enemyPawns).IsNotEmpty)
            {
                continue;
            }
            var next = new Square(square + (color == Color.White ? 8 : -8));
            int rankBonus = parameters.PassedPawnBonus[color == Color.White ? square.Rank : 7 - square.Rank];
            if (position.Occupied.Contains(next))
            {
                rankBonus /= 2;   // A blocked passer is worth half until it can move again
            }
            bonus += rankBonus;
            if (endgame)
            {
                // Kings decide passed pawns in the endgame (after Crafty): the further the enemy king is
                // from the square in front of the pawn, and the nearer our own, the better
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
        for (int color = 0; color < 3; color++)
        {
            spans[color] = new Bitboard[Square.Count];
            if (color == (int)Color.None)
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
                    if (color == (int)Color.White ? rank > square.Rank : rank < square.Rank)
                    {
                        ranksAhead |= Bitboard.OfRank(rank);
                    }
                }
                spans[color][square] = files & ranksAhead;
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
    private static Bitboard KingZone(Position position, Color color)
    {
        Square king = position.KingSquare(color);
        Bitboard ring = Attacks.King(king) | Bitboard.Of(king);
        return ring | ring.Forward(color);
    }

    /// <summary>
    /// Mobility (squares each piece can move to or capture on, weighted by piece type, squares
    /// guarded by enemy pawns being no use to a piece), the attacks into the enemy king zone (each
    /// attacked square counted once per attacking piece) and the side's attack map, in one pass.
    /// </summary>
    private SideAttacks PieceAttacks(Position position, Color color, Bitboard enemyZone)
    {
        Bitboard occupied = position.Occupied;
        Bitboard pawnGuarded = Attacks.Pawns(color.Opponent(), position.Pieces(color.Opponent(), Piece.Pawn));
        Bitboard targets = ~position.Pieces(color) & ~pawnGuarded;
        Bitboard attacks = Attacks.Pawns(color, position.Pieces(color, Piece.Pawn));
        int mobility = 0;
        int zoneAttack = 0;
        foreach (Square square in position.Pieces(color, Piece.Knight))
        {
            Bitboard reach = Attacks.Knight(square);
            attacks |= reach;
            mobility += parameters.KnightMobility * (reach & targets).Count;
            zoneAttack += parameters.KingZoneAttackPenalty * (reach & enemyZone).Count;
        }
        foreach (Square square in position.Pieces(color, Piece.Bishop))
        {
            Bitboard reach = Attacks.Bishop(occupied, square);
            attacks |= reach;
            mobility += parameters.BishopMobility * (reach & targets).Count;
            zoneAttack += parameters.KingZoneAttackPenalty * (reach & enemyZone).Count;
        }
        foreach (Square square in position.Pieces(color, Piece.Rook))
        {
            Bitboard reach = Attacks.Rook(occupied, square);
            attacks |= reach;
            mobility += parameters.RookMobility * (reach & targets).Count;
            zoneAttack += parameters.KingZoneAttackPenalty * (reach & enemyZone).Count;
        }
        foreach (Square square in position.Pieces(color, Piece.Queen))
        {
            Bitboard reach = Attacks.Queen(occupied, square);
            attacks |= reach;
            mobility += parameters.QueenMobility * (reach & targets).Count;
            zoneAttack += parameters.KingZoneQueenAttackPenalty * (reach & enemyZone).Count;
        }
        return new SideAttacks(mobility, zoneAttack, attacks);
    }

    /// <summary>
    /// The Simplified Evaluation Function's endgame: both sides have no queens, or every side that
    /// has a queen has no other piece beyond at most one minor piece.
    /// </summary>
    public static bool IsEndgame(Position position)
    {
        ArgumentNullException.ThrowIfNull(position);
        foreach (Color color in ColorExtensions.Sides)
        {
            if (position.Pieces(color, Piece.Queen).IsNotEmpty
                && (position.Pieces(color, Piece.Rook).IsNotEmpty || (position.Pieces(color, Piece.Knight) | position.Pieces(color, Piece.Bishop)).Count > 1))
            {
                return false;
            }
        }
        return true;
    }

    private int[][][] BuildPieceSquareValues()
    {
        (int[] Table, int Material)[] source =
        [
            ([], 0),                                // None
            (PawnTable, parameters.PawnValue),
            (KnightTable, parameters.KnightValue),
            (BishopTable, parameters.BishopValue),
            (RookTable, parameters.RookValue),
            (QueenTable, parameters.QueenValue),
            (KingTable, 0),                         // Kings are always present, so they carry no material value
            (KingEndgameTable, 0),
        ];
        int[][][] values = new int[3][][];
        for (int color = 0; color < 3; color++)
        {
            values[color] = new int[source.Length][];
            for (int type = 0; type < source.Length; type++)
            {
                values[color][type] = new int[Square.Count];
                (int[] table, int material) = source[type];
                if (table.Length == 0 || color == (int)Color.None)
                {
                    continue;
                }
                foreach (Square square in Square.All)
                {
                    // White reads the table with rank 8 at the top; black sees the board flipped.
                    int row = color == (int)Color.White ? 7 - square.Rank : square.Rank;
                    int value = table[(row * 8) + square.File] + material;
                    values[color][type][square] = color == (int)Color.White ? value : -value;
                }
            }
        }
        return values;
    }
}
