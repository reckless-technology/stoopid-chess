using System.Text;
using StoopidChess.Core;
using StoopidChess.Engine;
using StoopidChess.Play;

namespace StoopidChess.Tests.Play;

/// <summary>
/// Saved game files written before the naming clean-up (2026-11-10) must keep loading, and new ones must keep their names.
/// </summary>
public class SavedGameFileTests
{
    // Written by SavedGame.Save at a662cee, before any property or field was renamed
    private const string FileWrittenBeforeRenaming = """
        {
          "Signature": "Stoopid Chess saved game",
          "Version": 2,
          "StartFen": "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
          "Moves": [
            "e2e4",
            "e7e5",
            "g1f3"
          ],
          "HumanColor": "Black",
          "IsBoardFlipped": true,
          "Level": "Hard"
        }
        """;

    [Fact]
    public void LoadsAFileWrittenBeforeTheRenaming()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(FileWrittenBeforeRenaming));
        SavedGame? saved = SavedGame.Load(stream);
        Assert.NotNull(saved);
        Assert.Equal(Color.Black, saved.HumanColor);
        Assert.True(saved.IsBoardFlipped);
        Assert.Equal(Level.Hard, saved.Level);
        Assert.Equal(["e2e4", "e7e5", "g1f3"], saved.Moves);
        Assert.NotNull(saved.ToGame());
    }

    [Fact]
    public void WritesTheSameNamesAsBefore()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(FileWrittenBeforeRenaming));
        SavedGame saved = SavedGame.Load(stream)!;
        using var written = new MemoryStream();
        saved.Save(written);
        Assert.Equal(
            FileWrittenBeforeRenaming.ReplaceLineEndings("\n"),
            Encoding.UTF8.GetString(written.ToArray()).ReplaceLineEndings("\n")
        );
    }
}
