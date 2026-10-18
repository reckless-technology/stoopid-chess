using StoopidChess.Desktop;
using StoopidChess.Play;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// The GUI's engine list on disk: written and read back, and a missing or unwritable file handled
/// without an exception.
/// </summary>
public sealed class EngineRosterFileTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"stoopid-chess-tests-{Guid.NewGuid():N}");

    public EngineRosterFileTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void TheRosterLivesInTheUsersApplicationData()
    {
        Assert.EndsWith(Path.Combine("StoopidChess", "engines.json"), EngineRosterFile.DefaultPath, StringComparison.Ordinal);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), EngineRosterFile.DefaultPath, StringComparison.Ordinal);
    }

    [Fact]
    public void SavedRostersLoadBackAndTheFolderIsMade()
    {
        string path = Path.Combine(folder, "nested", "engines.json");
        var roster = new EngineRoster();
        roster.Add("Pawnstar", "/tools/pawnstar");
        Assert.Null(EngineRosterFile.Save(roster, path));
        Assert.Equal(roster.Engines, EngineRosterFile.Load(path).Engines);
    }

    [Fact]
    public void AMissingOrUnreadableFileIsAnEmptyRoster()
    {
        Assert.Empty(EngineRosterFile.Load(Path.Combine(folder, "missing", "engines.json")).Engines);
        Assert.Empty(EngineRosterFile.Load(folder).Engines);      // A folder, not a file
    }

    [Fact]
    public void AFailedSaveSaysWhy()
    {
        string file = Path.Combine(folder, "a file");
        File.WriteAllText(file, "");
        string? failure = EngineRosterFile.Save(new EngineRoster(), Path.Combine(file, "engines.json"));
        Assert.False(string.IsNullOrEmpty(failure));
        Assert.Throws<ArgumentNullException>(() => EngineRosterFile.Save(null!, file));
    }
}
