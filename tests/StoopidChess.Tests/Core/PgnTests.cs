using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class PgnTests
{
    [Fact]
    public void AGameRoundTripsThroughPgn()
    {
        var game = new Game();
        foreach (string uci in new[] { "e2e4", "e7e5", "g1f3", "b8c6", "f1b5", "a7a6", "b5c6", "d7c6", "e1g1" })
        {
            Assert.True(game.TryMakeMove(uci));
        }
        string pgn = Pgn.Write(game, "Alice", "Bob", "*", new DateOnly(2026, 9, 20));
        Assert.Contains("[White \"Alice\"]", pgn, StringComparison.Ordinal);
        Assert.Contains("1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Bxc6 dxc6 5. O-O *", pgn, StringComparison.Ordinal);
        Game? parsed = Pgn.Parse(pgn);
        Assert.NotNull(parsed);
        Assert.Equal(game.Position.ToFen(), parsed.Position.ToFen());
    }

    [Fact]
    public void AGameFromAnEditedPositionCarriesItsFen()
    {
        var game = new Game(Position.FromFen("4k3/8/8/8/8/8/4P3/4K3 b - - 0 30"));
        Assert.True(game.TryMakeMove("e8d7"));
        string pgn = Pgn.Write(game, "White", "Black", "*");
        Assert.Contains("[SetUp \"1\"]", pgn, StringComparison.Ordinal);
        Assert.Contains("[FEN \"4k3/8/8/8/8/8/4P3/4K3 b - - 0 30\"]", pgn, StringComparison.Ordinal);
        Assert.Contains("30... Kd7", pgn, StringComparison.Ordinal);
        Game? parsed = Pgn.Parse(pgn);
        Assert.NotNull(parsed);
        Assert.Equal(game.Position.ToFen(), parsed.Position.ToFen());
    }

    [Fact]
    public void CommentsVariationsAndGlyphsAreIgnored()
    {
        const string pgn = """
            [Event "Test"]
            [Result "1-0"]

            1. e4 {a fine move} e5 ; to the end of this line
            2. Nf3 $1 (2. f4 {the king's gambit} exf4) Nc6 3.Bb5 1-0
            """;
        Game? parsed = Pgn.Parse(pgn);
        Assert.NotNull(parsed);
        Assert.Equal(["e2e4", "e7e5", "g1f3", "b8c6", "f1b5"], parsed.UciMoves);
    }

    [Fact]
    public void OnlyTheFirstGameOfAFileIsRead()
    {
        const string pgn = """
            [Event "First"]

            1. d4 d5 1/2-1/2

            [Event "Second"]

            1. e4 e5 *
            """;
        Game? parsed = Pgn.Parse(pgn);
        Assert.NotNull(parsed);
        Assert.Equal(["d2d4", "d7d5"], parsed.UciMoves);
    }

    [Fact]
    public void BrokenInputIsRefused()
    {
        Assert.Null(Pgn.Parse("1. e4 e9"));
        Assert.Null(Pgn.Parse("[FEN \"not a fen\"]\n\n1. e4"));
    }
}
