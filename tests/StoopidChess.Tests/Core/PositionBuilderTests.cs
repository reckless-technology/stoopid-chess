using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class PositionBuilderTests
{
    [Fact]
    public void BuildsTheStartPositionFromScratch()
    {
        var builder = new PositionBuilder { CastlingRights = CastlingRights.All };
        Board reference = Board.StartPosition();
        foreach (int square in Squares.All)
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
        Board original = Board.StartPosition();
        PositionBuilder builder = PositionBuilder.From(original);
        builder[Squares.E1] = Piece.None;
        builder[Squares.Parse("d4")] = Piece.WhiteKing;
        Board edited = builder.Build();
        Assert.Equal(Board.StartFen, original.ToFen());
        Assert.Equal(Piece.WhiteKing, edited[Squares.Parse("d4")]);
        Assert.True(edited[Squares.E1].IsNone);
        (ulong key, _) = edited.ComputeKeys();
        Assert.Equal(key, edited.Key);
    }

    [Fact]
    public void CastlingRightsSurviveOnlyWithKingAndRookAtHome()
    {
        PositionBuilder builder = PositionBuilder.From(Board.StartPosition());
        builder[Squares.E1] = Piece.None;
        builder[Squares.Parse("d4")] = Piece.WhiteKing;
        Assert.Equal(CastlingRights.Black, builder.Build().CastlingRights);
        builder[Squares.H8] = Piece.None;
        Assert.Equal(CastlingRights.BlackQueenSide, builder.Build().CastlingRights);
        // Rights not requested are not invented
        var plain = new PositionBuilder();
        foreach (int square in Squares.All)
        {
            plain[square] = Board.StartPosition()[square];
        }
        Assert.Equal(CastlingRights.None, plain.Build().CastlingRights);
    }

    [Fact]
    public void SideToMoveIsRespected()
    {
        PositionBuilder builder = PositionBuilder.From(Board.StartPosition());
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

        PositionBuilder noBlackKing = PositionBuilder.From(Board.StartPosition());
        noBlackKing[Squares.E8] = Piece.None;
        Assert.False(noBlackKing.IsLegal);

        PositionBuilder backRankPawn = PositionBuilder.From(Board.StartPosition());
        backRankPawn[Squares.A8] = Piece.WhitePawn;
        Assert.False(backRankPawn.IsLegal);
        backRankPawn[Squares.A8] = Piece.BlackRook;
        Assert.True(backRankPawn.IsLegal);

        // White to move while the black king is already in check from a white rook
        var checkedKing = new PositionBuilder();
        checkedKing[Squares.E8] = Piece.BlackKing;
        checkedKing[Squares.E1] = Piece.WhiteKing;
        checkedKing[Squares.Parse("e7")] = Piece.WhiteRook;
        Assert.False(checkedKing.IsLegal);
        checkedKing.SideToMove = Colour.Black;
        Assert.True(checkedKing.IsLegal);

        var adjacentKings = new PositionBuilder();
        adjacentKings[Squares.E8] = Piece.BlackKing;
        adjacentKings[Squares.Parse("e7")] = Piece.WhiteKing;
        Assert.False(adjacentKings.IsLegal);
    }

    [Fact]
    public void RejectsInvalidPiecesAndSquares()
    {
        var builder = new PositionBuilder();
        Assert.Throws<ArgumentException>(() => builder[Squares.Parse("e4")] = new Piece((Colour)5, PieceType.Pawn));
        Assert.Throws<ArgumentException>(() => builder[Squares.Parse("e4")] = new Piece(Colour.White, (PieceType)9));
        Assert.Throws<ArgumentException>(() => builder[Squares.Parse("e4")] = new Piece(Colour.None, PieceType.Pawn));
        Assert.Throws<ArgumentException>(() => builder[Squares.Parse("e4")] = new Piece(Colour.White, PieceType.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder[Squares.Count] = Piece.WhitePawn);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder[Squares.None]);
        builder[Squares.Parse("e4")] = Piece.None;
        Assert.Equal(1, PositionBuilder.From(Board.StartPosition()).Build().Count(Colour.White, PieceType.Queen));
    }

    [Fact]
    public void EditedPositionsStartAFreshGame()
    {
        var game = new Game();
        game.TryMakeMove("e2e4");
        PositionBuilder builder = PositionBuilder.From(game.Board);
        builder[Squares.Parse("d8")] = Piece.None;
        var edited = new Game(builder.Build());
        Assert.Empty(edited.Moves);
        Assert.Equal(0, edited.Board.Ply);
        Assert.Equal(Colour.Black, edited.SideToMove);
        Assert.Equal(0, edited.Board.Count(Colour.Black, PieceType.Queen));
        Assert.NotEmpty(edited.LegalMoves);
    }
}
