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
        Assert.Equal(Colour.White, game.SideToMove);
    }

    [Fact]
    public void StalemateIsDetected()
    {
        Game game = Game.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
        Assert.Equal(GameStatus.Stalemate, game.Status);
        Assert.False(game.IsInCheck);
    }

    [Fact]
    public void ThreefoldRepetitionIsADraw()
    {
        Game game = Game.FromFen("4k3/8/8/8/8/8/8/R3K3 w - - 0 1");
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
        Assert.Throws<ArgumentException>(() => game.MakeMove(new Move(Squares.Parse("e2"), Squares.Parse("e5"), PieceType.Pawn)));
        Assert.Empty(game.Moves);
        Assert.True(game.TryMakeMove("e2e4"));
        Assert.Single(game.Moves);
        Assert.Equal(["e2e4"], game.UciMoves);
    }

    [Fact]
    public void PromotionNeedsAPieceChoice()
    {
        Game game = Game.FromFen("4k3/1P6/8/8/8/8/8/4K3 w - - 0 1");
        int from = Squares.Parse("b7");
        int to = Squares.Parse("b8");
        Assert.Equal(4, game.LegalMovesBetween(from, to).Count());
        Assert.Null(game.FindMove(from, to));
        Assert.Equal(PieceType.Rook, game.FindMove(from, to, PieceType.Rook)!.Value.Promotion);
        Assert.True(game.TryMakeMove("b7b8r"));
        Assert.Equal(Piece.WhiteRook, game.Board[to]);
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
        Assert.Equal("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", game.Board.ToFen());
        Assert.Equal(2, game.Moves.Count);
    }

    [Fact]
    public void CapturedPiecesAreListedByColour()
    {
        var game = new Game();
        foreach (string uci in new[] { "e2e4", "d7d5", "e4d5", "d8d5", "d1g4", "d5g2", "g4g2" })
        {
            Assert.True(game.TryMakeMove(uci), uci);
        }
        Assert.Equal([Piece.BlackPawn, Piece.BlackQueen], game.CapturedPieces(Colour.Black));
        Assert.Equal([Piece.WhitePawn, Piece.WhitePawn], game.CapturedPieces(Colour.White));
    }

    [Fact]
    public void StartFenIsRecordedForBlackToMovePositions()
    {
        Game game = Game.FromFen("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1");
        Assert.True(game.TryMakeMove("a8a1"));
        Assert.Equal([Piece.WhiteRook], game.CapturedPieces(Colour.White));
        Assert.Equal("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1", game.StartFen);
    }

    [Fact]
    public void EditingTheBoardBehindTheGamesBackInvalidatesTheLegalMoves()
    {
        var game = new Game();
        IReadOnlyList<Move> before = game.LegalMoves;
        Move pawnPush = before.Single(m => m.ToUci() == "e2e4");
        game.Board.SetPiece(Squares.Parse("e2"), Piece.None);
        Assert.DoesNotContain(pawnPush, game.LegalMoves);
        Assert.Throws<ArgumentException>(() => game.MakeMove(pawnPush));
        (ulong key, _, _) = game.Board.ComputeKeys();
        Assert.Equal(key, game.Board.Key);
    }

    [Fact]
    public void RejectsBoardsThatAlreadyHaveMoves()
    {
        Board board = Board.StartPosition();
        board.MakeMove(MoveGenerator.GenerateLegalMoves(board)[0]);
        Assert.Throws<ArgumentException>(() => new Game(board));
    }

    [Fact]
    public void RejectsPositionsWithoutKings()
    {
        Board board = Board.StartPosition();
        board.SetPiece(Squares.E8, Piece.None);
        Assert.Throws<ArgumentException>(() => new Game(board));
    }
}
