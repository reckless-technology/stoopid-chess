using StoopidChess.Play;

namespace StoopidChess.Tests.Play;

public class EngineRosterTests
{
    [Fact]
    public void RoundTripsThroughJson()
    {
        var roster = new EngineRoster();
        roster.Add("Pawnstar 0.13.593", "/home/user/tools/pawnstar");
        roster.Add("Zenith 1.2", "/home/user/tools/zenith");
        using var stream = new MemoryStream();
        roster.Save(stream);
        stream.Position = 0;
        EngineRoster loaded = EngineRoster.Load(stream);
        Assert.Equal(roster.Engines, loaded.Engines);
    }

    [Fact]
    public void AddingTheSamePathReplacesTheEntry()
    {
        var roster = new EngineRoster();
        roster.Add("Old name", "/tools/engine");
        roster.Add("New name", "/tools/engine");
        Assert.Single(roster.Engines);
        Assert.Equal("New name", roster.Engines[0].Name);
    }

    [Fact]
    public void RemoveDropsTheEntry()
    {
        var roster = new EngineRoster();
        roster.Add("Engine", "/tools/engine");
        roster.Remove("/tools/engine");
        Assert.Empty(roster.Engines);
    }

    [Fact]
    public void AnInvalidStreamLoadsAsAnEmptyRoster()
    {
        using var garbage = new MemoryStream("not json"u8.ToArray());
        Assert.Empty(EngineRoster.Load(garbage).Engines);
        using var empty = new MemoryStream();
        Assert.Empty(EngineRoster.Load(empty).Engines);
    }
}
