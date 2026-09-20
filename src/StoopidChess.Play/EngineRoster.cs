using System.Text.Json;

namespace StoopidChess.Play;

/// <summary>
/// An external UCI engine the user has registered: the name it reported and the path it runs from.
/// </summary>
public sealed record EngineEntry(string Name, string Path);

/// <summary>
/// The external UCI engines the user has registered, persisted as JSON so that the GUI can offer
/// them across sessions. The GUI decides where the file lives; this class only reads and writes it.
/// </summary>
public sealed class EngineRoster
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IList<EngineEntry> Engines { get; init; } = [];

    /// <summary>
    /// Register an engine, replacing any entry with the same path.
    /// </summary>
    public void Add(string name, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(path);
        Remove(path);
        Engines.Add(new EngineEntry(name, path));
    }

    public void Remove(string path)
    {
        for (int i = Engines.Count - 1; i >= 0; i--)
        {
            if (Engines[i].Path == path)
            {
                Engines.RemoveAt(i);
            }
        }
    }

    public void Save(Stream stream)
    {
        JsonSerializer.Serialize(stream, this, JsonOptions);
    }

    /// <summary>
    /// Read a roster, returning an empty one if the stream does not hold a valid roster.
    /// </summary>
    public static EngineRoster Load(Stream stream)
    {
        try
        {
            EngineRoster? roster = JsonSerializer.Deserialize<EngineRoster>(stream, JsonOptions);
            if (roster?.Engines is null || roster.Engines.Any(e => string.IsNullOrEmpty(e?.Name) || string.IsNullOrEmpty(e?.Path)))
            {
                return new EngineRoster();
            }
            return roster;
        }
        catch (JsonException)
        {
            return new EngineRoster();
        }
    }
}
