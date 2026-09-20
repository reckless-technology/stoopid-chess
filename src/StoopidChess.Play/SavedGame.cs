using System.Text.Json;
using System.Text.Json.Serialization;
using StoopidChess.Core;

namespace StoopidChess.Play;

/// <summary>
/// The contents of a Stoopid Chess saved game file: JSON holding the starting position as FEN, the
/// moves played in UCI notation, and the settings of the game in progress.
/// </summary>
public sealed class SavedGame
{
    public const string FileSignature = "Stoopid Chess saved game";
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Signature { get; set; } = FileSignature;

    public int Version { get; set; } = CurrentVersion;

    public string StartFen { get; set; } = Position.StartFen;

    public IList<string> Moves { get; init; } = [];

    public Color HumanColor { get; set; } = Color.White;

    public bool IsBoardFlipped { get; set; }

    public Level Level { get; set; } = Level.Easy;

    public static SavedGame FromGame(Game game, Color humanColor, bool isBoardFlipped, Level level)
    {
        ArgumentNullException.ThrowIfNull(game);
        return new SavedGame
        {
            StartFen = game.StartFen,
            Moves = [.. game.UciMoves],
            HumanColor = humanColor,
            IsBoardFlipped = isBoardFlipped,
            Level = level,
        };
    }

    public void Save(Stream stream)
    {
        JsonSerializer.Serialize(stream, this, JsonOptions);
    }

    /// <summary>
    /// Read a saved game, returning null if the stream does not hold a valid one.
    /// </summary>
    public static SavedGame? Load(Stream stream)
    {
        try
        {
            SavedGame? saved = JsonSerializer.Deserialize<SavedGame>(stream, JsonOptions);
            if (saved is null || saved.Signature != FileSignature || saved.Version != CurrentVersion)
            {
                return null;
            }
            // The serializer does not enforce the non-nullable annotations
            if (saved.StartFen is null || saved.Moves is null || saved.Moves.Any(move => move is null))
            {
                return null;
            }
            if (saved.HumanColor is not (Color.White or Color.Black or Color.None) || !Enum.IsDefined(saved.Level))
            {
                return null;
            }
            return saved;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rebuild the game by replaying the moves, returning null if the position or any move is invalid.
    /// </summary>
    public Game? ToGame()
    {
        if (Fen.TryParse(StartFen) is not Position position || !position.IsLegal)
        {
            return null;
        }
        var game = new Game(position);
        foreach (string move in Moves)
        {
            if (!game.TryMakeMove(move))
            {
                return null;
            }
        }
        return game;
    }
}
