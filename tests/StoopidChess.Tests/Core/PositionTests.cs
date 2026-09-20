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
            var position = Position.Start();
            for (int ply = 0; ply < 120; ply++)
            {
                MoveList moves = position.GenerateLegalMoves();
                if (moves.Count == 0)
                {
                    break;
                }
                position.MakeMove(moves[random.Next(moves.Count)]);
                (ulong key, ulong pawn) = position.ComputeKeys();
                Assert.Equal(key, position.Key);
                Assert.Equal(pawn, position.PawnKey);
                Assert.True(position.IsConsistent());
                Assert.Equal(position.ToFen(), Position.FromFen(position.ToFen()).ToFen());
                Assert.Equal(key, Position.FromFen(position.ToFen()).Key);
            }
            // Unwinding the whole game must restore the start position exactly
            string finalFen = position.ToFen();
            int plies = position.Ply;
            for (int i = 0; i < plies; i++)
            {
                position.UnmakeMove();
            }
            Assert.Equal(Position.StartFen, position.ToFen());
            Assert.Equal(Position.Start().Key, position.Key);
            Assert.NotEqual(Position.StartFen, finalFen);
        }
    }

    [Fact]
    public void CapturingARookOnItsHomeSquareRemovesTheCastlingRight()
    {
        // White bishop takes the rook on h8; black must not be able to castle king side later
        var position = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        var bishop = Position.FromFen("r3k2r/8/8/8/8/8/1B6/R3K2R w KQkq - 0 1");
        Move? capture = FindMove(bishop, "b2h8");
        Assert.NotNull(capture);
        bishop.MakeMove(capture.Value);
        Assert.Equal(CastlingRights.White | CastlingRights.BlackQueenSide, bishop.CastlingRights);
        Assert.Equal(CastlingRights.All, position.CastlingRights);
    }

    [Fact]
    public void MovingKingOrRookRemovesCastlingRights()
    {
        var position = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        position.MakeMove(FindMove(position, "a1b1")!.Value);
        Assert.Equal(CastlingRights.WhiteKingSide | CastlingRights.Black, position.CastlingRights);
        position.MakeMove(FindMove(position, "e8d8")!.Value);
        Assert.Equal(CastlingRights.WhiteKingSide, position.CastlingRights);
        position.UnmakeMove();
        position.UnmakeMove();
        Assert.Equal(CastlingRights.All, position.CastlingRights);
    }

    [Fact]
    public void CastlingMovesTheRookToo()
    {
        var position = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        position.MakeMove(FindMove(position, "e1g1")!.Value);
        Assert.Equal((Color.White, Piece.King), position[Square.G1]);
        Assert.Equal((Color.White, Piece.Rook), position[Square.F1]);
        Assert.Equal(Piece.None, position[Square.E1].Type);
        Assert.Equal(Piece.None, position[Square.H1].Type);
        position.MakeMove(FindMove(position, "e8c8")!.Value);
        Assert.Equal((Color.Black, Piece.King), position[Square.C8]);
        Assert.Equal((Color.Black, Piece.Rook), position[Square.D8]);
        Assert.Equal("2kr3r/8/8/8/8/8/8/R4RK1 w - - 2 2", position.ToFen());
        position.UnmakeMove();
        position.UnmakeMove();
        Assert.Equal("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", position.ToFen());
    }

    [Fact]
    public void EnPassantCaptureRemovesThePassedPawn()
    {
        var position = Position.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2");
        Move? capture = FindMove(position, "e5d6");
        Assert.NotNull(capture);
        Assert.Equal(MoveKind.EnPassant, capture.Value.Kind);
        position.MakeMove(capture.Value);
        Assert.Equal(Piece.None, position[Square.Parse("d5")].Type);
        Assert.Equal((Color.White, Piece.Pawn), position[Square.Parse("d6")]);
        Assert.Equal(1, position.Count(Color.White, Piece.Pawn) + position.Count(Color.Black, Piece.Pawn));
        position.UnmakeMove();
        Assert.Equal("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2", position.ToFen());
    }

    [Fact]
    public void PromotionReplacesThePawn()
    {
        var position = Position.FromFen("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1");
        List<Move> promotions = [.. position.GenerateLegalMoves().Where(m => m.From == Square.Parse("b7"))];
        Assert.Equal(4, promotions.Count);
        Move knight = promotions.Single(m => m.Promotion == Piece.Knight);
        position.MakeMove(knight);
        Assert.Equal((Color.White, Piece.Knight), position[Square.Parse("b8")]);
        Assert.Equal(0, position.Count(Color.White, Piece.Pawn));
        position.UnmakeMove();
        Assert.Equal((Color.White, Piece.Pawn), position[Square.Parse("b7")]);
        Assert.Equal(1, position.Count(Color.White, Piece.Pawn));
    }

    [Fact]
    public void HalfMoveClockResetsOnPawnMovesAndCaptures()
    {
        var position = Position.FromFen("r3k2r/8/8/8/8/8/P7/R3K2R w KQkq - 5 1");
        position.MakeMove(FindMove(position, "a1b1")!.Value);
        Assert.Equal(6, position.HalfMoveClock);
        position.MakeMove(FindMove(position, "a8a2")!.Value);
        Assert.Equal(0, position.HalfMoveClock);
        Assert.Equal(2, position.FullMoveNumber);
        position.MakeMove(FindMove(position, "b1a1")!.Value);
        Assert.Equal(1, position.HalfMoveClock);
    }

    [Fact]
    public void RepetitionCountSeesEarlierIdenticalPositions()
    {
        var position = Position.FromFen("4k3/8/8/8/8/8/8/R3K3 w - - 0 1");
        Assert.Equal(0, position.RepetitionCount());
        string[] shuffle = ["a1b1", "e8d8", "b1a1", "d8e8"];
        foreach (string uci in shuffle)
        {
            position.MakeMove(FindMove(position, uci)!.Value);
        }
        Assert.Equal(1, position.RepetitionCount());
        foreach (string uci in shuffle)
        {
            position.MakeMove(FindMove(position, uci)!.Value);
        }
        Assert.Equal(2, position.RepetitionCount());
        // A pawn move breaks the chain
        var pawns = Position.FromFen("4k3/8/8/8/8/8/P7/R3K3 w - - 0 1");
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
        Assert.Equal(expected, Position.FromFen(fen).IsInsufficientMaterial());
    }

    [Fact]
    public void NullMoveSwitchesSidesAndUnmakesExactly()
    {
        var position = Position.FromFen("rnbqkbnr/ppp1pppp/8/8/3p4/8/PPP1PPPP/RNBQKBNR w KQkq - 0 3");
        position.MakeMove(FindMove(position, "c2c4")!.Value);     // The black pawn on d4 could capture en passant on c3
        Assert.Equal(Square.Parse("c3"), position.EnPassantSquare);
        string fen = position.ToFen();
        ulong key = position.Key;
        position.MakeNullMove();
        Assert.True(position.LastMoveWasNull);
        Assert.Equal(Color.White, position.SideToMove);
        Assert.Equal(Square.None, position.EnPassantSquare);
        Assert.NotEqual(key, position.Key);
        (ulong computed, _) = position.ComputeKeys();
        Assert.Equal(computed, position.Key);
        Assert.Equal(position.FullMoveNumber, Position.FromFen(fen).FullMoveNumber);
        position.UnmakeMove();
        Assert.False(position.LastMoveWasNull);
        Assert.Equal(fen, position.ToFen());
        Assert.Equal(key, position.Key);
        Assert.True(position.HasNonPawnMaterial(Color.White));
        Assert.False(Position.FromFen("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1").HasNonPawnMaterial(Color.White));
    }

    [Fact]
    public void IndexersRangeCheck()
    {
        var position = Position.Start();
        Assert.Throws<ArgumentOutOfRangeException>(() => position[Square.None]);
        Assert.Throws<ArgumentOutOfRangeException>(() => position[Square.None]);
        Assert.Throws<ArgumentOutOfRangeException>(() => position[8, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => position[0, -1]);
        Assert.Equal((Color.White, Piece.King), position[4, 0]);
    }

    [Fact]
    public void GenerationOrderIsIndependentOfHistory()
    {
        var viaOneOrder = Position.Start();
        foreach (string uci in new[] { "e2e4", "e7e6", "d2d4", "d7d5", "b1c3", "f8b4", "e4e5", "c7c5" })
        {
            viaOneOrder.MakeMove(FindMove(viaOneOrder, uci)!.Value);
        }
        var viaOtherOrder = Position.Start();
        foreach (string uci in new[] { "d2d4", "e7e6", "b1c3", "d7d5", "e2e4", "f8b4", "e4e5", "c7c5" })
        {
            viaOtherOrder.MakeMove(FindMove(viaOtherOrder, uci)!.Value);
        }
        Assert.Equal(viaOneOrder.ToFen(), viaOtherOrder.ToFen());
        Assert.Equal(viaOneOrder.GenerateLegalMoves(), viaOtherOrder.GenerateLegalMoves());
    }

    [Fact]
    public void CloneIsIndependent()
    {
        var position = Position.Start();
        Position copy = position.Clone();
        position.MakeMove(FindMove(position, "e2e4")!.Value);
        Assert.Equal(Position.StartFen, copy.ToFen());
        Assert.NotEqual(position.Key, copy.Key);
    }

    internal static Move? FindMove(Position position, string uci)
    {
        MoveList moves = position.GenerateLegalMoves();
        foreach (Move move in moves)
        {
            if (move.ToUci() == uci)
            {
                return move;
            }
        }
        return null;
    }
}
