using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class PositionBuilderTests
{
    [Fact]
    public void BuildsTheStartPositionFromScratch()
    {
        var builder = new PositionBuilder { CastlingRights = CastlingRights.All };
        var reference = Board.StartPosition();
        foreach (Square square in Square.All)
        {
            builder[square] = reference[square];
        }
        Board built = builder.Build();
        Assert.Equal(Board.StartFen, built.ToFen());
        Assert.Equal(reference.Key, built.Key);
        Assert.Equal(0, built.Ply);
    }

    [Fact]
    public void FromCopiesAPositionAndEditsDoNotTouchTheOriginal()
    {
        var original = Board.StartPosition();
        var builder = PositionBuilder.From(original);
        builder[Square.E1] = default;
        builder[Square.Parse("d4")] = (Colour.White, PieceType.King);
        Board edited = builder.Build();
        Assert.Equal(Board.StartFen, original.ToFen());
        Assert.Equal((Colour.White, PieceType.King), edited[Square.Parse("d4")]);
        Assert.Equal(PieceType.None, edited[Square.E1].Type);
        (ulong key, _) = edited.ComputeKeys();
        Assert.Equal(key, edited.Key);
    }

    [Fact]
    public void CastlingRightsSurviveOnlyWithKingAndRookAtHome()
    {
        var builder = PositionBuilder.From(Board.StartPosition());
        builder[Square.E1] = default;
        builder[Square.Parse("d4")] = (Colour.White, PieceType.King);
        Assert.Equal(CastlingRights.Black, builder.Build().CastlingRights);
        builder[Square.H8] = default;
        Assert.Equal(CastlingRights.BlackQueenSide, builder.Build().CastlingRights);
        // Rights not requested are not invented
        var plain = new PositionBuilder();
        foreach (Square square in Square.All)
        {
            plain[square] = Board.StartPosition()[square];
        }
        Assert.Equal(CastlingRights.None, plain.Build().CastlingRights);
    }

    [Fact]
    public void SideToMoveIsRespected()
    {
        var builder = PositionBuilder.From(Board.StartPosition());
        builder.SideToMove = Colour.Black;
        Board board = builder.Build();
        Assert.Equal(Colour.Black, board.SideToMove);
        Assert.Equal(Board.FromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1").Key, board.Key);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SideToMove = Colour.None);
    }

    [Fact]
    public void RefusesIllegalPositions()
    {
        var empty = new PositionBuilder();
        Assert.False(empty.IsLegal);
        Assert.Null(empty.TryBuild());
        Assert.Throws<InvalidOperationException>(() => empty.Build());

        var noBlackKing = PositionBuilder.From(Board.StartPosition());
        noBlackKing[Square.E8] = default;
        Assert.False(noBlackKing.IsLegal);

        var backRankPawn = PositionBuilder.From(Board.StartPosition());
        backRankPawn[Square.A8] = (Colour.White, PieceType.Pawn);
        Assert.False(backRankPawn.IsLegal);
        backRankPawn[Square.A8] = (Colour.Black, PieceType.Rook);
        Assert.True(backRankPawn.IsLegal);

        // White to move while the black king is already in check from a white rook
        var checkedKing = new PositionBuilder();
        checkedKing[Square.E8] = (Colour.Black, PieceType.King);
        checkedKing[Square.E1] = (Colour.White, PieceType.King);
        checkedKing[Square.Parse("e7")] = (Colour.White, PieceType.Rook);
        Assert.False(checkedKing.IsLegal);
        checkedKing.SideToMove = Colour.Black;
        Assert.True(checkedKing.IsLegal);

        var adjacentKings = new PositionBuilder();
        adjacentKings[Square.E8] = (Colour.Black, PieceType.King);
        adjacentKings[Square.Parse("e7")] = (Colour.White, PieceType.King);
        Assert.False(adjacentKings.IsLegal);
    }

    [Fact]
    public void RejectsInvalidPiecesAndSquares()
    {
        var builder = new PositionBuilder();
        Assert.Throws<ArgumentException>(() => builder[Square.Parse("e4")] = ((Colour)5, PieceType.Pawn));
        Assert.Throws<ArgumentException>(() => builder[Square.Parse("e4")] = (Colour.White, (PieceType)9));
        Assert.Throws<ArgumentException>(() => builder[Square.Parse("e4")] = (Colour.None, PieceType.Pawn));
        Assert.Throws<ArgumentException>(() => builder[Square.Parse("e4")] = (Colour.White, PieceType.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder[Square.None] = (Colour.White, PieceType.Pawn));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder[Square.None]);
        builder.Clear(Square.Parse("e4"));
        Assert.Equal(default, builder[Square.Parse("e4")]);
        Assert.Equal(1, PositionBuilder.From(Board.StartPosition()).Build().Count(Colour.White, PieceType.Queen));
    }

    [Fact]
    public void EditedPositionsStartAFreshGame()
    {
        var game = new Game();
        game.TryMakeMove("e2e4");
        var builder = PositionBuilder.From(game.Board);
        builder[Square.Parse("d8")] = default;
        var edited = new Game(builder.Build());
        Assert.Empty(edited.Moves);
        Assert.Equal(0, edited.Board.Ply);
        Assert.Equal(Colour.Black, edited.SideToMove);
        Assert.Equal(0, edited.Board.Count(Colour.Black, PieceType.Queen));
        Assert.NotEmpty(edited.LegalMoves);
    }
}
