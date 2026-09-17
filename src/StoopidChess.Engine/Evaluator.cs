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
    private const int Up = 0x10;

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

    // Material plus piece-square value of each piece type on each 0x88 square, from white's point
    // of view: positive for white pieces, negative (and vertically mirrored) for black pieces.
    // Indexed [colour][type][square]; the king is stored twice, for the middle game and the endgame.
    private static readonly int[][][] PieceSquareValues = BuildPieceSquareValues();
    private const int KingEndgameIndex = 7;

    private readonly PawnHashEntry[] pawnHashTable = new PawnHashEntry[PawnTableSize];

    /// <summary>
    /// Number of times the pawn structure score was found in the pawn hash table.
    /// </summary>
    public long PawnHashHits { get; private set; }

    /// <summary>
    /// The score of the position from the side to move's point of view, in centipawns.
    /// </summary>
    public int Evaluate(Board board)
    {
        ArgumentNullException.ThrowIfNull(board);
        int score = 0;
        bool endgame = IsEndgame(board);
        foreach (Colour colour in stackalloc[] { Colour.White, Colour.Black })
        {
            int[][] tables = PieceSquareValues[(int)colour];
            for (PieceType type = PieceType.Pawn; type <= PieceType.Queen; type++)
            {
                int[] table = tables[(int)type];
                foreach (int square in board.SquaresOf(colour, type))
                {
                    score += table[square];
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
        foreach (Colour colour in stackalloc[] { Colour.White, Colour.Black })
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
        return board.Count(colour, PieceType.Knight) + board.Count(colour, PieceType.Bishop) + board.Count(colour, PieceType.Rook);
    }

    private static int BishopMobility(Board board, Colour colour)
    {
        int squares = 0;
        foreach (int from in board.SquaresOf(colour, PieceType.Bishop))
        {
            foreach (int delta in stackalloc[] { 0x11, 0x0F, -0x0F, -0x11 })
            {
                for (int to = from + delta; Squares.IsOnBoard(to); to += delta)
                {
                    Piece target = board[to];
                    if (target.IsNone)
                    {
                        squares++;
                        continue;
                    }
                    if (target.Colour != colour)
                    {
                        squares++;
                    }
                    break;
                }
            }
        }
        return squares;
    }

    private int PawnStructure(Board board)
    {
        ulong key = board.PawnKey;
        ref PawnHashEntry entry = ref pawnHashTable[(int)(key >> (64 - 16))];
        if (entry.Key == key && entry.IsValid)
        {
            PawnHashHits++;
            return entry.Score;
        }
        int score = 0;
        foreach (int square in board.SquaresOf(Colour.White, PieceType.Pawn))
        {
            score += EvaluatePawn(board, square, Colour.White);
        }
        foreach (int square in board.SquaresOf(Colour.Black, PieceType.Pawn))
        {
            score -= EvaluatePawn(board, square, Colour.Black);
        }
        entry = new PawnHashEntry(key, score);
        return score;
    }

    /// <summary>
    /// Bonuses for defended and passed pawns; penalties for doubled and blocked pawns.
    /// </summary>
    private static int EvaluatePawn(Board board, int square, Colour colour)
    {
        int forward = colour == Colour.White ? Up : -Up;
        Piece ownPawn = new(colour, PieceType.Pawn);
        Piece enemyPawn = new(colour.Opponent(), PieceType.Pawn);
        int score = 0;

        // Defended by another pawn of the same colour (i.e. it is on one of the squares this pawn attacks)?
        // Note: a pawn defends the squares diagonally in front of it, so look diagonally behind this one.
        foreach (int side in stackalloc[] { -1, 1 })
        {
            int defender = square - forward + side;
            if (Squares.IsOnBoard(defender) && board[defender] == ownPawn)
            {
                score += DefendedPawnBonus;
            }
        }

        // Passed: no pawn of either colour ahead on this file and no enemy pawn ahead on the adjacent files.
        bool passed = true;
        bool doubled = false;
        for (int ahead = square + forward; Squares.IsOnBoard(ahead); ahead += forward)
        {
            Piece piece = board[ahead];
            if (piece.Type == PieceType.Pawn)
            {
                passed = false;
                if (piece == ownPawn)
                {
                    doubled = true;
                }
            }
        }
        if (passed)
        {
            foreach (int side in stackalloc[] { -1, 1 })
            {
                for (int ahead = square + forward + side; Squares.IsOnBoard(ahead); ahead += forward)
                {
                    if (board[ahead] == enemyPawn)
                    {
                        passed = false;
                        break;
                    }
                }
            }
        }
        if (passed)
        {
            score += PassedPawnBonus;
        }
        else if (doubled)
        {
            score += DoubledPawnPenalty;
        }

        // Blocked: an enemy pawn directly ahead with no enemy pawn on an adjacent file that could be
        // exchanged to unblock it.
        int inFront = square + forward;
        if (Squares.IsOnBoard(inFront) && board[inFront] == enemyPawn)
        {
            bool blocked = true;
            foreach (int side in stackalloc[] { -1, 1 })
            {
                for (int ahead = square + forward + side; Squares.IsOnBoard(ahead) && blocked; ahead += forward)
                {
                    if (board[ahead] == enemyPawn)
                    {
                        blocked = false;
                    }
                }
            }
            if (blocked)
            {
                score += BlockedPawnPenalty;
            }
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
                values[colour][type] = new int[128];
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

    private readonly record struct PawnHashEntry(ulong Key, int Score)
    {
        // A struct array starts zeroed; a zero pawn key (no pawns) scores zero anyway, but mark
        // real entries so that an empty slot is never mistaken for one.
        public bool IsValid => Key != 0 || Score != 0;
    }
}
