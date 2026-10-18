using StoopidChess.Play;

namespace StoopidChess.Desktop;

/// <summary>
/// Where the GUI keeps the registered engines (<see cref="EngineRoster"/>) and how it reads and writes
/// the file: per user, under the application data folder.
/// </summary>
internal static class EngineRosterFile
{
    /// <summary>
    /// The user's engine list, ApplicationData/StoopidChess/engines.json.
    /// </summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StoopidChess", "engines.json");

    /// <summary>
    /// The roster in the file, or an empty one when the file is missing or cannot be read (an unreadable
    /// or invalid file is not an error: the user simply has no engines registered yet).
    /// </summary>
    public static EngineRoster Load(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return EngineRoster.Load(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or FileNotFoundException)
        {
            return new EngineRoster();
        }
    }

    /// <summary>
    /// Write the roster, creating the folder if need be. Returns null on success, or why it failed.
    /// </summary>
    public static string? Save(EngineRoster roster, string path)
    {
        ArgumentNullException.ThrowIfNull(roster);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using FileStream stream = File.Create(path);
            roster.Save(stream);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }
}
