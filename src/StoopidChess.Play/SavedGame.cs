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

    [JsonConverter(typeof(HumanColorConverter))]
    public Color HumanColor { get; set; } = Color.White;

    // The on-disk name is part of the file format: renaming the property must not rename it
    [JsonPropertyName("IsBoardFlipped")]
    public bool IsBoardFlipped { get; set; }

    [JsonConverter(typeof(LevelConverter))]
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
            if (saved.HumanColor is not (Color.White or Color.Black) || !Enum.IsDefined(saved.Level))
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
        if (Fen.TryParse(StartFen) is not Position position || !position.IsValid)
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

    /// <summary>
    /// Reads the level by name. The "Beginner" level no longer exists: a game saved at it loads as Easy.
    /// </summary>
    private sealed class LevelConverter : JsonConverter<Level>
    {
        public override Level Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? name = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (string.Equals(name, "Beginner", StringComparison.OrdinalIgnoreCase))
            {
                return Level.Easy;
            }
            if (Enum.TryParse(name, ignoreCase: true, out Level level) && Enum.IsDefined(level))
            {
                return level;
            }
            throw new JsonException($"invalid level '{name}'");
        }

        public override void Write(Utf8JsonWriter writer, Level value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value.ToString());
        }
    }

    /// <summary>
    /// Reads the human's color by name. A game saved while engine matches existed may name "None"
    /// (an engine played both sides): it loads with the human as white.
    /// </summary>
    private sealed class HumanColorConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? name = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            return name?.ToUpperInvariant() switch
            {
                "WHITE" or "NONE" => Color.White,
                "BLACK" => Color.Black,
                _ => throw new JsonException($"invalid color '{name}'"),
            };
        }

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value.ToString());
        }
    }
}
