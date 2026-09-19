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

    /// <summary>
    /// The score of the position from the side to move's point of view, in centipawns.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "The evaluator is an instance so that terms may keep per-instance caches (a pawn hash, for one) without changing callers")]
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
                foreach (Square square in board.Pieces(colour, type))
                {
                    score += table[square];
                }
            }
            score += tables[endgame ? KingEndgameIndex : (int)PieceType.King][board.KingSquare(colour)];
        }
        return board.SideToMove == Colour.White ? score : -score;
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
