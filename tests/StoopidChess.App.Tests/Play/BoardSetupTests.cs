using StoopidChess.Core;
using StoopidChess.Play;

namespace StoopidChess.Tests.Play;

public class BoardSetupTests
{
    [Fact]
    public void BuildsTheStartPositionFromScratch()
    {
        BoardSetup edited = BoardSetup.Empty(Color.White);
        var reference = Position.Start();
        foreach (Square square in Square.All)
        {
            edited.Place(square, reference[square]);
        }
        // Castling rights are carried over from a position, never invented
        Assert.Equal("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w - - 0 1", edited.ToFen());
        Position? position = edited.ToPosition();
        Assert.NotNull(position);
        Assert.Equal(Position.FromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w - - 0 1").Key, position.Key);
        Assert.Equal(0, position.Ply);
        Assert.Equal(Position.StartFen, BoardSetup.From(reference).ToFen());
    }

    [Fact]
    public void FromCopiesAPositionAndEditsDoNotTouchTheOriginal()
    {
        var original = Position.Start();
        BoardSetup edited = BoardSetup.From(original);
        edited.Place(Square.E1, default);
        edited.Place(Square.Parse("d4"), (Color.White, Piece.King));
        Assert.Equal(Position.StartFen, original.ToFen());
        Assert.Equal((Color.White, Piece.King), edited[Square.Parse("d4")]);
        Assert.Equal(Piece.None, edited[Square.E1].Type);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/3K4/8/PPPPPPPP/RNBQ1BNR w kq - 0 1", edited.ToFen());
    }

    [Fact]
    public void CastlingRightsSurviveOnlyWithKingAndRookAtHome()
    {
        BoardSetup edited = BoardSetup.From(Position.Start());
        edited.Place(Square.E1, default);
        edited.Place(Square.Parse("d4"), (Color.White, Piece.King));
        Assert.Equal(CastlingRights.Black, edited.CastlingRights);
        edited.Place(Square.H8, default);
        Assert.Equal(CastlingRights.BlackQueenSide, edited.CastlingRights);
        // A right lost is not regained by putting the piece back
        edited.Place(Square.H8, (Color.Black, Piece.Rook));
        Assert.Equal(CastlingRights.BlackQueenSide, edited.CastlingRights);
    }

    [Fact]
    public void SideToMoveIsRespected()
    {
        BoardSetup edited = BoardSetup.From(Position.Start());
        edited.SideToMove = Color.Black;
        Assert.Equal(Color.Black, edited.SideToMove);
        Assert.Equal(
            Position.FromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1").Key,
            edited.ToPosition()!.Key
        );
        Assert.Throws<ArgumentOutOfRangeException>(() => edited.SideToMove = (Color)2);
    }

    [Fact]
    public void BecomesAPositionOnlyWhenPlayCanProceed()
    {
        Assert.Null(BoardSetup.Empty(Color.White).ToPosition());

        BoardSetup noBlackKing = BoardSetup.From(Position.Start());
        noBlackKing.Place(Square.E8, default);
        Assert.Null(noBlackKing.ToPosition());

        BoardSetup backRankPawn = BoardSetup.From(Position.Start());
        backRankPawn.Place(Square.A8, (Color.White, Piece.Pawn));
        Assert.Null(backRankPawn.ToPosition());
        backRankPawn.Place(Square.A8, (Color.Black, Piece.Rook));
        Assert.NotNull(backRankPawn.ToPosition());

        // White to move while the black king is already in check from a white rook
        BoardSetup checkedKing = BoardSetup.Empty(Color.White);
        checkedKing.Place(Square.E8, (Color.Black, Piece.King));
        checkedKing.Place(Square.E1, (Color.White, Piece.King));
        checkedKing.Place(Square.Parse("e7"), (Color.White, Piece.Rook));
        Assert.Null(checkedKing.ToPosition());
        checkedKing.SideToMove = Color.Black;
        Assert.NotNull(checkedKing.ToPosition());

        BoardSetup adjacentKings = BoardSetup.Empty(Color.White);
        adjacentKings.Place(Square.E8, (Color.Black, Piece.King));
        adjacentKings.Place(Square.Parse("e7"), (Color.White, Piece.King));
        Assert.Null(adjacentKings.ToPosition());
    }

    [Fact]
    public void RejectsInvalidPiecesAndSquares()
    {
        BoardSetup edited = BoardSetup.Empty(Color.White);
        Assert.Throws<ArgumentException>(() => edited.Place(Square.Parse("e4"), ((Color)5, Piece.Pawn)));
        Assert.Throws<ArgumentException>(() => edited.Place(Square.Parse("e4"), (Color.White, (Piece)9)));
        Assert.Throws<ArgumentException>(() => edited.Place(Square.Parse("e4"), ((Color)2, Piece.Pawn)));
        Assert.Throws<ArgumentOutOfRangeException>(() => edited.Place(Square.None, (Color.White, Piece.Pawn)));
        Assert.Throws<ArgumentOutOfRangeException>(() => edited[Square.None]);
        edited.Place(Square.Parse("e4"), (Color.White, Piece.Queen));
        edited.Place(Square.Parse("e4"), (Color.Black, Piece.None));
        Assert.Equal(default, edited[Square.Parse("e4")]);
    }

    [Fact]
    public void AnEditedBoardStartsAFreshGame()
    {
        var game = new Game();
        game.TryMakeMove("e2e4");
        BoardSetup edited = BoardSetup.From(game.Position);
        edited.Place(Square.Parse("d8"), default);
        var fresh = new Game(edited.ToPosition()!);
        Assert.Empty(fresh.Moves);
        Assert.Equal(0, fresh.Position.Ply);
        Assert.Equal(Color.Black, fresh.SideToMove);
        Assert.Equal(0, fresh.Position.Count(Color.Black, Piece.Queen));
        Assert.NotEmpty(fresh.LegalMoves);
        Assert.Equal(Square.None, fresh.Position.EnPassantSquare);
    }
}
