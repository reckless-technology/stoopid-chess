using System.Globalization;
using System.Text;
using StoopidChess.Core;
using StoopidChess.Play;
using Color = StoopidChess.Core.Color;   // The chess color, not Avalonia.Media.Color

namespace StoopidChess.Desktop;

/// <summary>
/// The text the main window shows for the state of a <see cref="GameController"/>: its title, the move
/// list, the status bar's labels, the analysis window's lines and the names and result an exported PGN
/// carries. Plain functions of the controller, so they are tested without a window.
/// </summary>
internal static class GameText
{
    /// <summary>
    /// The window title: the program, and the engine playing or the two engines of a match.
    /// </summary>
    public static string Title(GameController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.IsMatch
            ? $"Stoopid Chess - {controller.EngineName} vs {controller.OpponentName}"
            : controller.EngineName is null ? "Stoopid Chess" : $"Stoopid Chess - {controller.EngineName}";
    }

    /// <summary>
    /// The side the human plays, for the status bar.
    /// </summary>
    public static string HumanColor(GameController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.HumanColor == Color.None ? "Nobody (engine vs engine)" : controller.HumanColor.ToString();
    }

    /// <summary>
    /// The status bar's caption for <see cref="Mode"/>: the clock when the running game has one, otherwise
    /// the difficulty.
    /// </summary>
    public static string ModeAnnotation(GameController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.ActiveClock is null ? "   Difficulty:" : "   Clock:";
    }

    /// <summary>
    /// The clock the running game is actually under, or its level when it is untimed; a differing selection
    /// for the next game is marked as pending.
    /// </summary>
    public static string Mode(GameController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        string mode = controller.ActiveClock?.ToString() ?? controller.Level.ToString();
        if (!Equals(controller.TimeControl, controller.ActiveClock))
        {
            mode += $" (next game: {controller.TimeControl?.ToString() ?? "untimed"})";
        }
        return mode;
    }

    /// <summary>
    /// A clock reading, hours, minutes and seconds.
    /// </summary>
    public static string Clock(TimeSpan time) => time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// The move list: the game's moves in SAN, numbered two to a line, then how the game stands (check, or
    /// how it ended), and the engine's last report when <paramref name="showEngineOutput"/> is set.
    /// </summary>
    public static string MoveList(GameController controller, bool showEngineOutput)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (controller.IsEditing)
        {
            return "Position edited by user: place both kings to continue";
        }
        Game game = controller.Game;
        var text = new StringBuilder();
        Color mover = game.StartSideToMove;
        int moveNumber = game.StartMoveNumber;
        bool lineOpen = false;
        if (mover == Color.Black && game.Moves.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"{moveNumber,2}. {"...",-9}");
            lineOpen = true;
        }
        var replay = Position.FromFen(game.StartFen);
        foreach (Move move in game.Moves)
        {
            if (mover == Color.White)
            {
                text.Append(CultureInfo.InvariantCulture, $"{moveNumber,2}. ");
                lineOpen = true;
            }
            text.Append(CultureInfo.InvariantCulture, $"{San.Format(replay, move),-9}");
            replay.MakeMove(move);
            if (mover == Color.Black)
            {
                text.AppendLine();
                moveNumber++;
                lineOpen = false;
            }
            mover = mover.Opponent();
        }
        if (lineOpen)
        {
            text.AppendLine();
        }
        if (game.Moves.Count == 0 && game.StartFen != Position.StartFen)
        {
            text.AppendLine("Position edited by user");
        }
        switch (game.Status)
        {
            case GameStatus.Checkmate:
                text.Append(CultureInfo.InvariantCulture, $"CHECKMATE: {game.SideToMove.Opponent()} wins");
                break;
            case GameStatus.Stalemate:
                text.Append("GAME OVER: DRAW\nStalemate");
                break;
            case GameStatus.DrawByRepetition:
                text.Append("GAME OVER: DRAW\nDraw by threefold repetition");
                break;
            case GameStatus.DrawByFiftyMoveRule:
                text.Append("GAME OVER: DRAW\nDraw by the fifty-move rule");
                break;
            case GameStatus.DrawByInsufficientMaterial:
                text.Append("GAME OVER: DRAW\nDraw by insufficient material");
                break;
            default:
                if (game.IsInCheck)
                {
                    text.Append("CHECK");
                }
                break;
        }
        if (controller.TimedOut != Color.None)
        {
            text.Append(CultureInfo.InvariantCulture, $"GAME OVER: {controller.TimedOut} lost on time");
        }
        if (showEngineOutput && controller.LastEngineInfo.Length != 0)
        {
            text.AppendLine().Append(controller.LastEngineInfo);
        }
        return text.ToString();
    }

    /// <summary>
    /// One display line per engine "info" report that carries a full report: depth, score, node
    /// count and the principal variation. Null for any other report.
    /// </summary>
    public static string? InfoLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? depth = null;
        string? score = null;
        string? nodes = null;
        int pvStart = -1;
        for (int i = 1; i < tokens.Length; i++)
        {
            switch (tokens[i])
            {
                case "depth" when i + 1 < tokens.Length:
                    depth = tokens[i + 1];
                    break;
                case "score" when i + 2 < tokens.Length && tokens[i + 1] == "cp" && int.TryParse(tokens[i + 2], out int cp):
                    score = (cp / 100.0).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
                    break;
                case "score" when i + 2 < tokens.Length && tokens[i + 1] == "mate":
                    score = "#" + tokens[i + 2];
                    break;
                case "nodes" when i + 1 < tokens.Length && long.TryParse(tokens[i + 1], out long count):
                    nodes = count.ToString("N0", CultureInfo.InvariantCulture);
                    break;
                case "pv":
                    pvStart = i + 1;
                    break;
            }
            if (pvStart >= 0)
            {
                break;
            }
        }
        if (depth is null || score is null || pvStart < 0)
        {
            return null;
        }
        string pv = string.Join(' ', tokens.Skip(pvStart).Take(12));
        return $"d{depth,3}  {score,8}  {nodes,14} n   {pv}";
    }

    /// <summary>
    /// The White and Black names of an exported PGN: the engines of a match, or the human and the engine.
    /// </summary>
    public static (string White, string Black) PlayerNames(GameController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        string engine = controller.EngineName ?? "Engine";
        if (controller.IsMatch)
        {
            return (engine, controller.OpponentName ?? "Engine");
        }
        return controller.HumanColor switch
        {
            Color.White => ("Human", engine),
            Color.Black => (engine, "Human"),
            _ => (engine, engine),
        };
    }

    /// <summary>
    /// The PGN result: who won on time or over the board, a draw, or "*" for a game still going.
    /// </summary>
    public static string Result(GameController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (controller.TimedOut != Color.None)
        {
            return controller.TimedOut == Color.White ? "0-1" : "1-0";
        }
        return controller.Game.Status switch
        {
            GameStatus.Checkmate => controller.Game.SideToMove == Color.White ? "0-1" : "1-0",
            GameStatus.Stalemate or GameStatus.DrawByRepetition or GameStatus.DrawByFiftyMoveRule
                or GameStatus.DrawByInsufficientMaterial => "1/2-1/2",
            _ => "*",
        };
    }
}
