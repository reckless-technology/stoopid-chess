using System.Text;
using StoopidChess.Engine;
using StoopidChess.Play;

namespace StoopidChess.Tests.Play;

/// <summary>
/// The value types behind the GUI's settings: the levels' search limits, and what a saved game
/// file must hold to be accepted.
/// </summary>
public class PlaySettingsTests
{
    [Theory]
    [InlineData(Level.Beginner, 1, 1)]
    [InlineData(Level.Easy, 3, 3)]
    [InlineData(Level.Medium, 6, 10)]
    [InlineData(Level.Hard, 8, 20)]
    [InlineData(Level.Best, 13, 40)]
    public void EachLevelLimitsDepthAndTime(Level level, int depth, int seconds)
    {
        SearchLimits limits = level.Limits();
        Assert.Equal(depth, limits.Depth);
        Assert.Equal(TimeSpan.FromSeconds(seconds), limits.MoveTime);
    }

    [Fact]
    public void AnUnknownLevelHasNoLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((Level)99).Limits());
    }

    [Fact]
    public void ATimeControlHasItsCustomaryShortForm()
    {
        Assert.Equal("5+3", TimeControl.OfMinutes(5, 3).ToString());
        Assert.Equal("10+0", TimeControl.Default.ToString());
    }

    private static SavedGame? LoadSaved(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return SavedGame.Load(stream);
    }

    private const string ValidGame =
        """{ "Signature": "Stoopid Chess saved game", "Version": 2, "StartFen": "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "Moves": ["e2e4"], "HumanColor": "White", "IsBoardFlipped": false, "Level": "Easy" }""";

    [Fact]
    public void AValidSavedGameLoads()
    {
        SavedGame? saved = LoadSaved(ValidGame);
        Assert.NotNull(saved);
        Assert.Single(saved.ToGame()!.Moves);
    }

    [Theory]
    [InlineData("\"Signature\": \"Stoopid Chess saved game\"", "\"Signature\": \"Somebody else's game\"")]
    [InlineData("\"Version\": 2", "\"Version\": 1")]
    [InlineData("\"Moves\": [\"e2e4\"]", "\"Moves\": null")]
    [InlineData("\"Moves\": [\"e2e4\"]", "\"Moves\": [null]")]
    [InlineData("\"StartFen\": \"rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1\"", "\"StartFen\": null")]
    [InlineData("\"HumanColor\": \"White\"", "\"HumanColor\": 7")]
    [InlineData("\"Level\": \"Easy\"", "\"Level\": 42")]
    [InlineData("\"Level\": \"Easy\" }", "\"Level\": \"Easy\" ")]
    public void ASavedGameMissingAnythingIsRefused(string field, string replacement)
    {
        Assert.Null(LoadSaved(ValidGame.Replace(field, replacement, StringComparison.Ordinal)));
    }

    [Fact]
    public void ASavedGameWithAnIllegalMoveDoesNotReplay()
    {
        SavedGame? saved = LoadSaved(ValidGame.Replace("e2e4", "e2e5", StringComparison.Ordinal));
        Assert.NotNull(saved);
        Assert.Null(saved.ToGame());
    }
}
