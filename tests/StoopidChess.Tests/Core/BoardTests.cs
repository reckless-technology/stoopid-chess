using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class BoardTests
{
    [Fact]
    public void KeysAreMaintainedIncrementallyThroughRandomPlay()
    {
        var random = new Random(2026);
        for (int game = 0; game < 50; game++)
        {
            Board board = Board.StartPosition();
            for (int ply = 0; ply < 120; ply++)
            {
                List<Move> moves = MoveGenerator.GenerateLegalMoves(board);
                if (moves.Count == 0)
                {
                    break;
                }
                board.MakeMove(moves[random.Next(moves.Count)]);
                (ulong key, ulong secondary, ulong pawn) = board.ComputeKeys();
                Assert.Equal(key, board.Key);
                Assert.Equal(secondary, board.SecondaryKey);
                Assert.Equal(pawn, board.PawnKey);
                Assert.Equal(board.ToFen(), Board.FromFen(board.ToFen()).ToFen());
                Assert.Equal(key, Board.FromFen(board.ToFen()).Key);
            }
            // Unwinding the whole game must restore the start position exactly
            string finalFen = board.ToFen();
            int plies = board.Ply;
            for (int i = 0; i < plies; i++)
            {
                board.UnmakeMove();
            }
            Assert.Equal(Board.StartFen, board.ToFen());
            Assert.Equal(Board.StartPosition().Key, board.Key);
            Assert.NotEqual(Board.StartFen, finalFen);
        }
    }

    [Fact]
    public void CapturingARookOnItsHomeSquareRemovesTheCastlingRight()
    {
        // White bishop takes the rook on h8; black must not be able to castle king side later
        Board board = Board.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Board bishop = Board.FromFen("r3k2r/8/8/8/8/8/1B6/R3K2R w KQkq - 0 1");
        Move? capture = FindMove(bishop, "b2h8");
        Assert.NotNull(capture);
        bishop.MakeMove(capture.Value);
        Assert.Equal(CastlingRights.White | CastlingRights.BlackQueenSide, bishop.CastlingRights);
        Assert.Equal(CastlingRights.All, board.CastlingRights);
    }

    [Fact]
    public void MovingKingOrRookRemovesCastlingRights()
    {
        Board board = Board.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        board.MakeMove(FindMove(board, "a1b1")!.Value);
        Assert.Equal(CastlingRights.WhiteKingSide | CastlingRights.Black, board.CastlingRights);
        board.MakeMove(FindMove(board, "e8d8")!.Value);
        Assert.Equal(CastlingRights.WhiteKingSide, board.CastlingRights);
        board.UnmakeMove();
        board.UnmakeMove();
        Assert.Equal(CastlingRights.All, board.CastlingRights);
    }

    [Fact]
    public void CastlingMovesTheRookToo()
    {
        Board board = Board.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        board.MakeMove(FindMove(board, "e1g1")!.Value);
        Assert.Equal(Piece.WhiteKing, board[Squares.G1]);
        Assert.Equal(Piece.WhiteRook, board[Squares.F1]);
        Assert.True(board[Squares.E1].IsNone);
        Assert.True(board[Squares.H1].IsNone);
        board.MakeMove(FindMove(board, "e8c8")!.Value);
        Assert.Equal(Piece.BlackKing, board[Squares.C8]);
        Assert.Equal(Piece.BlackRook, board[Squares.D8]);
        Assert.Equal("2kr3r/8/8/8/8/8/8/R4RK1 w - - 2 2", board.ToFen());
        board.UnmakeMove();
        board.UnmakeMove();
        Assert.Equal("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", board.ToFen());
    }

    [Fact]
    public void EnPassantCaptureRemovesThePassedPawn()
    {
        Board board = Board.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2");
        Move? capture = FindMove(board, "e5d6");
        Assert.NotNull(capture);
        Assert.Equal(MoveKind.EnPassant, capture.Value.Kind);
        board.MakeMove(capture.Value);
        Assert.True(board[Squares.Parse("d5")].IsNone);
        Assert.Equal(Piece.WhitePawn, board[Squares.Parse("d6")]);
        Assert.Equal(1, board.Count(Colour.White, PieceType.Pawn) + board.Count(Colour.Black, PieceType.Pawn));
        board.UnmakeMove();
        Assert.Equal("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2", board.ToFen());
    }

    [Fact]
    public void PromotionReplacesThePawn()
    {
        Board board = Board.FromFen("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1");
        List<Move> promotions = MoveGenerator.GenerateLegalMoves(board).Where(m => m.From == Squares.Parse("b7")).ToList();
        Assert.Equal(4, promotions.Count);
        Move knight = promotions.Single(m => m.Promotion == PieceType.Knight);
        board.MakeMove(knight);
        Assert.Equal(Piece.WhiteKnight, board[Squares.Parse("b8")]);
        Assert.Equal(0, board.Count(Colour.White, PieceType.Pawn));
        board.UnmakeMove();
        Assert.Equal(Piece.WhitePawn, board[Squares.Parse("b7")]);
        Assert.Equal(1, board.Count(Colour.White, PieceType.Pawn));
    }

    [Fact]
    public void HalfMoveClockResetsOnPawnMovesAndCaptures()
    {
        Board board = Board.FromFen("r3k2r/8/8/8/8/8/P7/R3K2R w KQkq - 5 1");
        board.MakeMove(FindMove(board, "a1b1")!.Value);
        Assert.Equal(6, board.HalfMoveClock);
        board.MakeMove(FindMove(board, "a8a2")!.Value);
        Assert.Equal(0, board.HalfMoveClock);
        Assert.Equal(2, board.FullMoveNumber);
        board.MakeMove(FindMove(board, "b1a1")!.Value);
        Assert.Equal(1, board.HalfMoveClock);
    }

    [Fact]
    public void RepetitionCountSeesEarlierIdenticalPositions()
    {
        Board board = Board.FromFen("4k3/8/8/8/8/8/8/R3K3 w - - 0 1");
        Assert.Equal(0, board.RepetitionCount());
        string[] shuffle = ["a1b1", "e8d8", "b1a1", "d8e8"];
        foreach (string uci in shuffle)
        {
            board.MakeMove(FindMove(board, uci)!.Value);
        }
        Assert.Equal(1, board.RepetitionCount());
        foreach (string uci in shuffle)
        {
            board.MakeMove(FindMove(board, uci)!.Value);
        }
        Assert.Equal(2, board.RepetitionCount());
        // A pawn move breaks the chain
        Board pawns = Board.FromFen("4k3/8/8/8/8/8/P7/R3K3 w - - 0 1");
        pawns.MakeMove(FindMove(pawns, "a2a3")!.Value);
        foreach (string uci in new[] { "e8d8", "a1b1", "d8e8", "b1a1" })
        {
            pawns.MakeMove(FindMove(pawns, uci)!.Value);
        }
        Assert.Equal(1, pawns.RepetitionCount());
        pawns.MakeMove(FindMove(pawns, "e8d8")!.Value);
        pawns.MakeMove(FindMove(pawns, "a3a4")!.Value);
        Assert.Equal(0, pawns.RepetitionCount());
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 1", true)]
    [InlineData("4k3/8/8/8/8/8/8/4KB2 w - - 0 1", true)]
    [InlineData("4k3/8/8/8/8/8/8/4KN2 w - - 0 1", true)]
    [InlineData("4k3/8/8/8/8/8/8/4KNN1 w - - 0 1", false)]
    [InlineData("4k3/8/8/8/8/8/5P2/4K3 w - - 0 1", false)]
    [InlineData("4kb2/8/8/8/8/8/8/4KB2 w - - 0 1", false)]   // f8 is dark, f1 is light
    [InlineData("4k1b1/8/8/8/8/8/8/4KB2 w - - 0 1", true)]   // g8 and f1 are both light
    [InlineData("4k3/8/8/8/8/8/8/4KR2 w - - 0 1", false)]
    public void DetectsInsufficientMaterial(string fen, bool expected)
    {
        Assert.Equal(expected, Board.FromFen(fen).IsInsufficientMaterial());
    }

    [Fact]
    public void NullMoveSwitchesSidesAndUnmakesExactly()
    {
        Board board = Board.FromFen("rnbqkbnr/ppp1pppp/8/8/3p4/8/PPP1PPPP/RNBQKBNR w KQkq - 0 3");
        board.MakeMove(FindMove(board, "c2c4")!.Value);     // The black pawn on d4 could capture en passant on c3
        Assert.Equal(Squares.Parse("c3"), board.EnPassantSquare);
        string fen = board.ToFen();
        ulong key = board.Key;
        board.MakeNullMove();
        Assert.True(board.LastMoveWasNull);
        Assert.Equal(Colour.White, board.SideToMove);
        Assert.Equal(Squares.None, board.EnPassantSquare);
        Assert.NotEqual(key, board.Key);
        (ulong computed, _, _) = board.ComputeKeys();
        Assert.Equal(computed, board.Key);
        Assert.Equal(board.FullMoveNumber, Board.FromFen(fen).FullMoveNumber);
        board.UnmakeMove();
        Assert.False(board.LastMoveWasNull);
        Assert.Equal(fen, board.ToFen());
        Assert.Equal(key, board.Key);
        Assert.True(board.HasNonPawnMaterial(Colour.White));
        Assert.False(Board.FromFen("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1").HasNonPawnMaterial(Colour.White));
    }

    [Fact]
    public void IndexersRangeCheck()
    {
        Board board = Board.StartPosition();
        Assert.Throws<ArgumentOutOfRangeException>(() => board[Squares.None]);
        Assert.Throws<ArgumentOutOfRangeException>(() => board[0x78]);
        Assert.Throws<ArgumentOutOfRangeException>(() => board[8, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => board[0, -1]);
        Assert.Equal(Piece.WhiteKing, board[4, 0]);
    }

    [Fact]
    public void NormalisePieceOrderMakesGenerationIndependentOfHistory()
    {
        Board viaOneOrder = Board.StartPosition();
        foreach (string uci in new[] { "e2e4", "e7e6", "d2d4", "d7d5", "b1c3", "f8b4", "e4e5", "c7c5" })
        {
            viaOneOrder.MakeMove(FindMove(viaOneOrder, uci)!.Value);
        }
        Board viaOtherOrder = Board.StartPosition();
        foreach (string uci in new[] { "d2d4", "e7e6", "b1c3", "d7d5", "e2e4", "f8b4", "e4e5", "c7c5" })
        {
            viaOtherOrder.MakeMove(FindMove(viaOtherOrder, uci)!.Value);
        }
        Assert.Equal(viaOneOrder.ToFen(), viaOtherOrder.ToFen());
        viaOneOrder.NormalisePieceOrder();
        viaOtherOrder.NormalisePieceOrder();
        Assert.Equal(MoveGenerator.GenerateLegalMoves(viaOneOrder), MoveGenerator.GenerateLegalMoves(viaOtherOrder));
    }

    [Fact]
    public void CloneIsIndependent()
    {
        Board board = Board.StartPosition();
        Board copy = board.Clone();
        board.MakeMove(FindMove(board, "e2e4")!.Value);
        Assert.Equal(Board.StartFen, copy.ToFen());
        Assert.NotEqual(board.Key, copy.Key);
    }

    internal static Move? FindMove(Board board, string uci)
    {
        foreach (Move move in MoveGenerator.GenerateLegalMoves(board))
        {
            if (move.ToUci() == uci)
            {
                return move;
            }
        }
        return null;
    }
}
