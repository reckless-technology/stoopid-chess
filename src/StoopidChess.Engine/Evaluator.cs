using System.Numerics;
using StoopidChess.Core;

namespace StoopidChess.Engine;

/// <summary>
/// Static evaluation: material and piece-square tables, pawn structure (cached by pawn hash) and
/// bishop mobility. Scores are in centipawns from the side to move's point of view.
/// </summary>
public sealed class Evaluator
{
    // Material values chosen so that B > N > 3P, B + N = R + 2P and Q + P = 2R, with a pawn as 100.
    public const int PawnValue = 100;
    public const int KnightValue = 330;
    public const int BishopValue = 370;
    public const int RookValue = 500;
    public const int QueenValue = 900;

    public const int PassedPawnBonus = 50;
    public const int DefendedPawnBonus = 10;
    public const int DoubledPawnPenalty = -12;
    public const int BlockedPawnPenalty = -15;
    public const int BishopMobilityBonus = 2;    // Per reachable square; discourages trapped bishops

    private const int PawnTableSize = 1 << 16;

    // Piece-square tables from white's point of view with rank 8 as the first row, so that they read
    // like a board diagram. Values from https://www.chessprogramming.org/Simplified_Evaluation_Function
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

    // The squares ahead of a pawn on its own file, and on the two adjacent files, indexed
    // [colour][square]. What lies there decides whether the pawn is passed, doubled or blocked.
    private static readonly ulong[][] ForwardFile = BuildForwardMasks(adjacentFiles: false);
    private static readonly ulong[][] ForwardAdjacentFiles = BuildForwardMasks(adjacentFiles: true);

    private readonly PawnHashEntry[] pawnHashTable = new PawnHashEntry[PawnTableSize];

    /// <summary>
    /// Number of times the pawn structure score was found in the pawn hash table.
    /// </summary>
    internal long PawnHashHits { get; private set; }

    /// <summary>
    /// The score of the position from the side to move's point of view, in centipawns.
    /// </summary>
    public int Evaluate(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        int score = 0;
        bool endgame = IsEndgame(board);
        foreach (Colour colour in ColourExtensions.Sides)
        {
            int[][] tables = PieceSquareValues[(int)colour];
            for (PieceType type = PieceType.Pawn; type <= PieceType.Queen; type++)
            {
                int[] table = tables[(int)type];
                for (ulong bits = board.Pieces(colour, type); bits != 0;)
                {
                    score += table[Bitboards.PopFirst(ref bits)];
                }
            }
            score += tables[endgame ? KingEndgameIndex : (int)PieceType.King][board.KingSquare(colour)];
            score += BishopMobility(board, colour) * BishopMobilityBonus * (colour == Colour.White ? 1 : -1);
        }
        score += PawnStructure(board);
        return board.SideToMove == Colour.White ? score : -score;
    }

    /// <summary>
    /// The endgame begins when the queens are gone, or when each queen has at most one other piece.
    /// </summary>
    public static bool IsEndgame(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        foreach (Colour colour in ColourExtensions.Sides)
        {
            if (board.Count(colour, PieceType.Queen) != 0 && MinorAndRookCount(board, colour) > 1)
            {
                return false;
            }
        }
        return true;
    }

    private static int MinorAndRookCount(Board board, Colour colour)
    {
        return BitOperations.PopCount(board.Pieces(colour, PieceType.Knight) | board.Pieces(colour, PieceType.Bishop) | board.Pieces(colour, PieceType.Rook));
    }

    /// <summary>
    /// The squares the colour's bishops can reach: empty squares along their diagonals plus the
    /// first enemy piece on each.
    /// </summary>
    private static int BishopMobility(Board board, Colour colour)
    {
        int squares = 0;
        ulong occupied = board.Occupied;
        ulong own = board.Pieces(colour);
        for (ulong bishops = board.Pieces(colour, PieceType.Bishop); bishops != 0;)
        {
            squares += BitOperations.PopCount(Attacks.Bishop(occupied, Bitboards.PopFirst(ref bishops)) & ~own);
        }
        return squares;
    }

    private int PawnStructure(Board board)
    {
        ulong key = board.PawnKey;
        ref PawnHashEntry entry = ref pawnHashTable[(int)(key >> (64 - 16))];
        if (entry.Key == key)
        {
            PawnHashHits++;
            return entry.Score;
        }
        int score = 0;
        ulong whitePawns = board.Pieces(Colour.White, PieceType.Pawn);
        ulong blackPawns = board.Pieces(Colour.Black, PieceType.Pawn);
        for (ulong bits = whitePawns; bits != 0;)
        {
            score += EvaluatePawn(Bitboards.PopFirst(ref bits), Colour.White, whitePawns, blackPawns);
        }
        for (ulong bits = blackPawns; bits != 0;)
        {
            score -= EvaluatePawn(Bitboards.PopFirst(ref bits), Colour.Black, blackPawns, whitePawns);
        }
        entry = new PawnHashEntry(key, score);
        return score;
    }

    /// <summary>
    /// Bonuses for defended and passed pawns; penalties for doubled and blocked pawns.
    /// </summary>
    private static int EvaluatePawn(int square, Colour colour, ulong ownPawns, ulong enemyPawns)
    {
        int score = 0;

        // Defended by another pawn of the same colour, i.e. by a pawn on one of the squares from
        // which a pawn attacks this one: the squares diagonally behind it.
        score += DefendedPawnBonus * BitOperations.PopCount(Attacks.Pawn(colour.Opponent(), square) & ownPawns);

        // What lies ahead on this file and on the two adjacent files
        ulong ahead = ForwardFile[(int)colour][square];
        bool pawnAheadOnFile = (ahead & (ownPawns | enemyPawns)) != 0;
        bool doubled = (ahead & ownPawns) != 0;
        bool enemyPawnAheadOnAdjacentFile = (ForwardAdjacentFiles[(int)colour][square] & enemyPawns) != 0;

        // Passed: nothing ahead on the file and no enemy pawn that could capture it on the way
        if (!pawnAheadOnFile && !enemyPawnAheadOnAdjacentFile)
        {
            score += PassedPawnBonus;
        }
        else if (doubled)
        {
            score += DoubledPawnPenalty;
        }
        // Blocked: an enemy pawn directly ahead with none on an adjacent file to exchange it away
        ulong inFront = Bitboards.Forward(colour, Bitboards.Of(square));
        if ((inFront & enemyPawns) != 0 && !enemyPawnAheadOnAdjacentFile)
        {
            score += BlockedPawnPenalty;
        }
        return score;
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
                values[colour][type] = new int[Squares.Count];
                (int[] table, int material) = source[type];
                if (table.Length == 0 || colour == (int)Colour.None)
                {
                    continue;
                }
                foreach (int square in Squares.All)
                {
                    int file = Squares.FileOf(square);
                    int rank = Squares.RankOf(square);
                    // White reads the table with rank 8 at the top; black sees the board flipped.
                    int row = colour == (int)Colour.White ? 7 - rank : rank;
                    int value = table[(row * 8) + file] + material;
                    values[colour][type][square] = colour == (int)Colour.White ? value : -value;
                }
            }
        }
        return values;
    }

    private static ulong[][] BuildForwardMasks(bool adjacentFiles)
    {
        ulong[][] masks = new ulong[3][];
        for (int colour = 0; colour < 3; colour++)
        {
            masks[colour] = new ulong[Squares.Count];
            if (colour == (int)Colour.None)
            {
                continue;
            }
            foreach (int square in Squares.All)
            {
                int file = Squares.FileOf(square);
                int rank = Squares.RankOf(square);
                ulong ranksAhead = 0;
                for (int r = 0; r < 8; r++)
                {
                    if (colour == (int)Colour.White ? r > rank : r < rank)
                    {
                        ranksAhead |= Bitboards.Rank(r);
                    }
                }
                ulong files = adjacentFiles
                    ? (file > 0 ? Bitboards.File(file - 1) : 0) | (file < 7 ? Bitboards.File(file + 1) : 0)
                    : Bitboards.File(file);
                masks[colour][square] = files & ranksAhead;
            }
        }
        return masks;
    }

    // An empty slot has key 0 and score 0, which is also the right answer for the one position with
    // pawn key 0: no pawns at all.
    private readonly record struct PawnHashEntry(ulong Key, int Score);
}
