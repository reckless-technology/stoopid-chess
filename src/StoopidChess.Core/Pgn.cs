using System.Globalization;
using System.Text;

namespace StoopidChess.Core;

/// <summary>
/// Portable Game Notation: one game as tag pairs and SAN move text. Writing produces the seven tag
/// roster (with FEN and SetUp tags when the game did not start from the standard position);
/// parsing reads the first game of a file, honours a FEN tag, and ignores comments, variations and
/// numeric annotation glyphs.
/// </summary>
public static class Pgn
{
    /// <summary>
    /// The game as PGN text. The result should be "1-0", "0-1", "1/2-1/2" or "*".
    /// </summary>
    public static string Write(Game game, string white, string black, string result, DateOnly? date = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        var text = new StringBuilder();
        text.AppendLine("[Event \"Stoopid Chess game\"]");
        text.AppendLine("[Site \"?\"]");
        text.AppendLine(CultureInfo.InvariantCulture, $"[Date \"{(date is { } d ? d.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) : "????.??.??")}\"]");
        text.AppendLine("[Round \"?\"]");
        text.AppendLine(CultureInfo.InvariantCulture, $"[White \"{Escape(white)}\"]");
        text.AppendLine(CultureInfo.InvariantCulture, $"[Black \"{Escape(black)}\"]");
        text.AppendLine(CultureInfo.InvariantCulture, $"[Result \"{result}\"]");
        if (game.StartFen != Position.StartFen)
        {
            text.AppendLine("[SetUp \"1\"]");
            text.AppendLine(CultureInfo.InvariantCulture, $"[FEN \"{game.StartFen}\"]");
        }
        text.AppendLine();

        var replay = Position.FromFen(game.StartFen);
        var line = new StringBuilder();
        foreach (Move move in game.Moves)
        {
            string san = San.Format(replay, move);
            string token = replay.SideToMove == Color.White
                ? $"{replay.FullMoveNumber}. {san}"
                : line.Length == 0 && replay.Ply == 0
                    ? $"{replay.FullMoveNumber}... {san}"
                    : san;
            if (line.Length + token.Length + 1 > 80)
            {
                text.AppendLine(line.ToString());
                line.Clear();
            }
            if (line.Length > 0)
            {
                line.Append(' ');
            }
            line.Append(token);
            replay.MakeMove(move);
        }
        if (line.Length + result.Length + 1 > 80)
        {
            text.AppendLine(line.ToString());
            line.Clear();
        }
        if (line.Length > 0)
        {
            line.Append(' ');
        }
        line.Append(result);
        text.AppendLine(line.ToString());
        return text.ToString();
    }

    /// <summary>
    /// The first game in the text, or null when there is none or a move cannot be read.
    /// </summary>
    public static Game? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string startFen = Position.StartFen;
        int position = 0;

        // Tag pairs: [Name "value"], up to the move text
        while (true)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            if (position >= text.Length || text[position] != '[')
            {
                break;
            }
            int close = text.IndexOf(']', position);
            if (close < 0)
            {
                return null;
            }
            string tag = text[(position + 1)..close];
            position = close + 1;
            int firstQuote = tag.IndexOf('"');
            int lastQuote = tag.LastIndexOf('"');
            if (firstQuote >= 0 && lastQuote > firstQuote
                && tag[..firstQuote].Trim().Equals("FEN", StringComparison.OrdinalIgnoreCase))
            {
                startFen = tag[(firstQuote + 1)..lastQuote].Replace("\\\"", "\"", StringComparison.Ordinal);
            }
        }

        if (Fen.TryParse(startFen) is not Position start || !start.IsLegal)
        {
            return null;
        }
        var game = new Game(start);

        foreach (string token in MoveTokens(text, position))
        {
            if (token is "1-0" or "0-1" or "1/2-1/2" or "*")
            {
                break;
            }
            if (San.Parse(game.Position, token) is not Move move)
            {
                return null;
            }
            game.MakeMove(move);
        }
        return game;
    }

    /// <summary>
    /// The move text tokens: comments, variations, annotation glyphs and move numbers removed.
    /// </summary>
    private static IEnumerable<string> MoveTokens(string text, int start)
    {
        var token = new StringBuilder();
        int variationDepth = 0;
        for (int i = start; i <= text.Length; i++)
        {
            char c = i < text.Length ? text[i] : ' ';
            if (c == '{')
            {
                int close = text.IndexOf('}', i);
                i = close < 0 ? text.Length : close;
                continue;
            }
            if (c == ';')
            {
                int close = text.IndexOf('\n', i);
                i = close < 0 ? text.Length : close;
                continue;
            }
            if (c == '(')
            {
                variationDepth++;
                continue;
            }
            if (c == ')')
            {
                variationDepth = Math.Max(0, variationDepth - 1);
                continue;
            }
            if (variationDepth > 0)
            {
                continue;
            }
            if (c == '[')
            {
                yield break;    // The next game's tags begin
            }
            if (!char.IsWhiteSpace(c))
            {
                token.Append(c);
                continue;
            }
            if (token.Length == 0)
            {
                continue;
            }
            string word = token.ToString();
            token.Clear();
            if (word.StartsWith('$'))
            {
                continue;   // A numeric annotation glyph
            }
            if (word is "1-0" or "0-1" or "1/2-1/2" or "*")
            {
                yield return word;
                continue;
            }
            int digits = 0;
            while (digits < word.Length && char.IsAsciiDigit(word[digits]))
            {
                digits++;
            }
            if (digits > 0)
            {
                int dots = digits;
                while (dots < word.Length && word[dots] == '.')
                {
                    dots++;
                }
                if (dots > digits)
                {
                    word = word[dots..];    // "12." alone or "12.e4" run together
                }
                else if (digits == word.Length)
                {
                    continue;               // A bare move number ("12 . e4" split oddly)
                }
                // Otherwise the word merely begins with a digit, like castling written as 0-0
            }
            if (word.Length > 0)
            {
                yield return word;
            }
        }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
