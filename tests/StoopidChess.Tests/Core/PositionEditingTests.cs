using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class PositionEditingTests
{
    [Fact]
    public void BuildsTheStartPositionFromScratch()
    {
        Position edited = Position.Empty();
        var reference = Position.Start();
        foreach (Square square in Square.All)
        {
            edited.Place(square, reference[square]);
        }
        edited.SetCastlingRights(CastlingRights.All);
        Assert.True(edited.IsLegal);
        Assert.Equal(Position.StartFen, edited.ToFen());
        Assert.Equal(reference.Key, edited.Key);
        Assert.Equal(0, edited.Ply);
    }

    [Fact]
    public void SetupCopiesAPositionAndEditsDoNotTouchTheOriginal()
    {
        var original = Position.Start();
        Position edited = Position.Setup(original);
        edited.Clear(Square.E1);
        edited.Place(Square.Parse("d4"), (Color.White, Piece.King));
        Assert.Equal(Position.StartFen, original.ToFen());
        Assert.Equal((Color.White, Piece.King), edited[Square.Parse("d4")]);
        Assert.Equal(Piece.None, edited[Square.E1].Type);
        (ulong key, ulong pawnKey) = edited.ComputeKeys();
        Assert.Equal(key, edited.Key);
        Assert.Equal(pawnKey, edited.PawnKey);
        Assert.True(edited.IsConsistent());
    }

    [Fact]
    public void CastlingRightsSurviveOnlyWithKingAndRookAtHome()
    {
        Position edited = Position.Setup(Position.Start());
        edited.Clear(Square.E1);
        edited.Place(Square.Parse("d4"), (Color.White, Piece.King));
        Assert.Equal(CastlingRights.Black, edited.CastlingRights);
        edited.Clear(Square.H8);
        Assert.Equal(CastlingRights.BlackQueenSide, edited.CastlingRights);
        // Rights not granted are not invented, and rights granted for absent pieces are dropped
        Position plain = Position.Empty();
        foreach (Square square in Square.All)
        {
            plain.Place(square, Position.Start()[square]);
        }
        Assert.Equal(CastlingRights.None, plain.CastlingRights);
        plain.Clear(Square.A1);
        plain.SetCastlingRights(CastlingRights.All);
        Assert.Equal(CastlingRights.All & ~CastlingRights.WhiteQueenSide, plain.CastlingRights);
    }

    [Fact]
    public void SideToMoveIsRespected()
    {
        Position edited = Position.Setup(Position.Start());
        edited.SetSideToMove(Color.Black);
        Assert.Equal(Color.Black, edited.SideToMove);
        Assert.Equal(Position.FromFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 1").Key, edited.Key);
        Assert.Throws<ArgumentOutOfRangeException>(() => edited.SetSideToMove(Color.None));
    }

    [Fact]
    public void RecognisesIllegalPositions()
    {
        Position empty = Position.Empty();
        Assert.False(empty.IsLegal);
        Assert.Throws<ArgumentException>(() => new Game(empty));

        Position noBlackKing = Position.Setup(Position.Start());
        noBlackKing.Clear(Square.E8);
        Assert.False(noBlackKing.IsLegal);

        Position backRankPawn = Position.Setup(Position.Start());
        backRankPawn.Place(Square.A8, (Color.White, Piece.Pawn));
        Assert.False(backRankPawn.IsLegal);
        backRankPawn.Place(Square.A8, (Color.Black, Piece.Rook));
        Assert.True(backRankPawn.IsLegal);

        // White to move while the black king is already in check from a white rook
        Position checkedKing = Position.Empty();
        checkedKing.Place(Square.E8, (Color.Black, Piece.King));
        checkedKing.Place(Square.E1, (Color.White, Piece.King));
        checkedKing.Place(Square.Parse("e7"), (Color.White, Piece.Rook));
        Assert.False(checkedKing.IsLegal);
        checkedKing.SetSideToMove(Color.Black);
        Assert.True(checkedKing.IsLegal);

        Position adjacentKings = Position.Empty();
        adjacentKings.Place(Square.E8, (Color.Black, Piece.King));
        adjacentKings.Place(Square.Parse("e7"), (Color.White, Piece.King));
        Assert.False(adjacentKings.IsLegal);
    }

    [Fact]
    public void RejectsInvalidPiecesAndSquares()
    {
        Position edited = Position.Empty();
        Assert.Throws<ArgumentException>(() => edited.Place(Square.Parse("e4"), ((Color)5, Piece.Pawn)));
        Assert.Throws<ArgumentException>(() => edited.Place(Square.Parse("e4"), (Color.White, (Piece)9)));
        Assert.Throws<ArgumentException>(() => edited.Place(Square.Parse("e4"), (Color.None, Piece.Pawn)));
        Assert.Throws<ArgumentException>(() => edited.Place(Square.Parse("e4"), (Color.White, Piece.None)));
        Assert.Throws<ArgumentOutOfRangeException>(() => edited.Place(Square.None, (Color.White, Piece.Pawn)));
        edited.Place(Square.Parse("e4"), (Color.White, Piece.Queen));
        edited.Clear(Square.Parse("e4"));
        Assert.Equal(default, edited[Square.Parse("e4")]);
        Assert.Equal(1, Position.Setup(Position.Start()).Count(Color.White, Piece.Queen));
    }

    [Fact]
    public void EditingIsOnlyAllowedBeforeMovesAreMade()
    {
        var played = Position.Start();
        played.MakeMove(played.GenerateLegalMoves()[0]);
        Assert.Throws<InvalidOperationException>(() => played.Clear(Square.Parse("d8")));
        Assert.Throws<InvalidOperationException>(() => played.SetSideToMove(Color.White));
        // Setup gives a fresh copy that can be edited
        Position edited = Position.Setup(played);
        edited.Clear(Square.Parse("d8"));
        Assert.Equal(0, edited.Ply);
        Assert.True(edited.IsLegal);
    }

    [Fact]
    public void EditedPositionsStartAFreshGame()
    {
        var game = new Game();
        game.TryMakeMove("e2e4");
        Position edited = Position.Setup(game.Position);
        edited.Clear(Square.Parse("d8"));
        var fresh = new Game(edited);
        Assert.Empty(fresh.Moves);
        Assert.Equal(0, fresh.Position.Ply);
        Assert.Equal(Color.Black, fresh.SideToMove);
        Assert.Equal(0, fresh.Position.Count(Color.Black, Piece.Queen));
        Assert.NotEmpty(fresh.LegalMoves);
    }
}
