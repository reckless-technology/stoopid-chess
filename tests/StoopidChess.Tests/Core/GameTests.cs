using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class GameTests
{
    [Fact]
    public void FoolsMateIsCheckmate()
    {
        var game = new Game();
        foreach (string uci in new[] { "f2f3", "e7e5", "g2g4", "d8h4" })
        {
            Assert.True(game.TryMakeMove(uci), uci);
        }
        Assert.Equal(GameStatus.Checkmate, game.Status);
        Assert.True(game.IsOver);
        Assert.True(game.IsInCheck);
        Assert.Empty(game.LegalMoves);
        Assert.Equal(Color.White, game.SideToMove);
    }

    [Fact]
    public void StalemateIsDetected()
    {
        var game = Game.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
        Assert.Equal(GameStatus.Stalemate, game.Status);
        Assert.False(game.IsInCheck);
    }

    [Fact]
    public void ThreefoldRepetitionIsADraw()
    {
        var game = Game.FromFen("4k3/8/8/8/8/8/8/R3K3 w - - 0 1");
        for (int i = 0; i < 2; i++)
        {
            foreach (string uci in new[] { "a1b1", "e8d8", "b1a1", "d8e8" })
            {
                Assert.True(game.TryMakeMove(uci));
            }
        }
        Assert.Equal(GameStatus.DrawByRepetition, game.Status);
        game.UndoMove();
        Assert.Equal(GameStatus.InProgress, game.Status);
    }

    [Fact]
    public void FiftyMoveRuleNeedsOneHundredPlies()
    {
        Assert.Equal(GameStatus.InProgress, Game.FromFen("4k3/8/8/8/8/8/8/R3K3 w - - 99 60").Status);
        Assert.Equal(GameStatus.DrawByFiftyMoveRule, Game.FromFen("4k3/8/8/8/8/8/8/R3K3 w - - 100 60").Status);
    }

    [Fact]
    public void FiftyMoveRuleCountsAcrossCastling()
    {
        // Castling is neither a pawn move nor a capture, so two castles take the 98th ply to the 100th
        var game = Game.FromFen("r3k3/8/8/8/8/8/8/4K2R w Kq - 98 60");
        Assert.True(game.TryMakeMove("e1g1"));
        Assert.Equal(99, game.Position.HalfMoveClock);
        Assert.Equal(GameStatus.InProgress, game.Status);
        Assert.True(game.TryMakeMove("e8c8"));
        Assert.Equal(100, game.Position.HalfMoveClock);
        Assert.Equal(GameStatus.DrawByFiftyMoveRule, game.Status);
        Assert.Equal("2kr4/8/8/8/8/8/8/5RK1 w - - 100 61", game.Position.ToFen());
        game.UndoMove();
        Assert.Equal(GameStatus.InProgress, game.Status);
    }

    [Fact]
    public void InsufficientMaterialIsADraw()
    {
        Assert.Equal(GameStatus.DrawByInsufficientMaterial, Game.FromFen("4k3/8/8/8/8/8/8/4KN2 w - - 0 1").Status);
    }

    [Fact]
    public void CheckmateTakesPrecedenceOverDrawRules()
    {
        // Mate delivered on the hundredth ply is still mate
        Assert.Equal(GameStatus.Checkmate, Game.FromFen("7k/6Q1/6K1/8/8/8/8/8 b - - 100 60").Status);
    }

    [Fact]
    public void OnlyLegalMovesAreAccepted()
    {
        var game = new Game();
        Assert.False(game.TryMakeMove("e2e5"));
        Assert.False(game.TryMakeMove("e7e5"));
        Assert.False(game.TryMakeMove("nonsense"));
        Assert.Throws<ArgumentException>(() => game.MakeMove(new Move(Square.Parse("e2"), Square.Parse("e5"), Piece.Pawn)));
        Assert.Empty(game.Moves);
        Assert.True(game.TryMakeMove("e2e4"));
        Assert.Single(game.Moves);
        Assert.Equal(["e2e4"], game.UciMoves);
    }

    [Fact]
    public void PromotionNeedsAPieceChoice()
    {
        var game = Game.FromFen("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1");
        var from = Square.Parse("b7");
        var to = Square.Parse("b8");
        Assert.Equal(4, game.LegalMovesBetween(from, to).Count());
        Assert.Null(game.FindMove(from, to));
        Assert.Equal(Piece.Rook, game.FindMove(from, to, Piece.Rook)!.Value.Promotion);
        Assert.True(game.TryMakeMove("b7b8r"));
        Assert.Equal((Color.White, Piece.Rook), game.Position[to]);
    }

    [Fact]
    public void UndoRestoresPreviousPosition()
    {
        var game = new Game();
        Assert.False(game.UndoMove());
        game.TryMakeMove("e2e4");
        game.TryMakeMove("d7d5");
        game.TryMakeMove("e4d5");
        Assert.True(game.UndoMove());
        // No white pawn can capture on d6, so no en passant square is recorded
        Assert.Equal("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", game.Position.ToFen());
        Assert.Equal(2, game.Moves.Count);
    }

    [Fact]
    public void CapturedPiecesAreListedByColor()
    {
        var game = new Game();
        foreach (string uci in new[] { "e2e4", "d7d5", "e4d5", "d8d5", "d1g4", "d5g2", "g4g2" })
        {
            Assert.True(game.TryMakeMove(uci), uci);
        }
        Assert.Equal([Piece.Pawn, Piece.Queen], game.CapturedPieces(Color.Black));
        Assert.Equal([Piece.Pawn, Piece.Pawn], game.CapturedPieces(Color.White));
    }

    [Fact]
    public void StartFenIsRecordedForBlackToMovePositions()
    {
        var game = Game.FromFen("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1");
        Assert.True(game.TryMakeMove("a8a1"));
        Assert.Equal([Piece.Rook], game.CapturedPieces(Color.White));
        Assert.Equal("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1", game.StartFen);
    }

    [Fact]
    public void RejectsBoardsThatAlreadyHaveMoves()
    {
        var position = Position.Start();
        position.MakeMove(position.GenerateLegalMoves()[0]);
        Assert.Throws<ArgumentException>(() => new Game(position));
    }

    [Fact]
    public void RejectsPositionsWithoutKings()
    {
        Position edited = Position.Setup(Position.Start());
        edited.Clear(Square.E8);
        Assert.Throws<ArgumentException>(() => new Game(edited));
    }
}
