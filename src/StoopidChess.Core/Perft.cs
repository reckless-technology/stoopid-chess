using System.Globalization;
using System.Reflection;

namespace StoopidChess.Core;

/// <summary>
/// A perft reference position: a FEN and the known leaf node counts at successive depths.
/// </summary>
public sealed record PerftPosition(string Fen, IReadOnlyList<(int Depth, long Nodes)> Depths);

/// <summary>
/// Performance test: count the leaf nodes of the legal move tree to a fixed depth. The counts for
/// standard positions are well known, which makes this the definitive test of move generation.
/// See https://www.chessprogramming.org/Perft
/// </summary>
public static class Perft
{
    public static long Count(Board board, int depth)
    {
        ArgumentNullException.ThrowIfNull(board);
        if (depth <= 0)
        {
            return 1;
        }
        // One move list per depth, reused across the whole tree
        return Count(board, depth, new MoveList[depth]);
    }

    private static long Count(Board board, int depth, MoveList[] lists)
    {
        ref MoveList moves = ref lists[depth - 1];
        moves.Clear();
        MoveGenerator.GenerateLegalMoves(board, ref moves);
        if (depth == 1)
        {
            return moves.Count;
        }
        long nodes = 0;
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            nodes += Count(board, depth - 1, lists);
            board.UnmakeMove();
        }
        return nodes;
    }

    /// <summary>
    /// The node count beneath each root move, as printed by "go perft" in most engines.
    /// </summary>
    public static IEnumerable<(Move Move, long Nodes)> Divide(Board board, int depth)
    {
        ArgumentNullException.ThrowIfNull(board);
        foreach (Move move in MoveGenerator.GenerateLegalMoves(board).ToArray())
        {
            board.MakeMove(move);
            long nodes = Count(board, depth - 1);
            board.UnmakeMove();
            yield return (move, nodes);
        }
    }

    /// <summary>
    /// The 128 standard perft positions (Ethereal's standard.epd), embedded in the assembly.
    /// </summary>
    public static IReadOnlyList<PerftPosition> StandardPositions { get; } = LoadStandardPositions();

    /// <summary>
    /// Parse EPD lines of the form "FEN;D1 nodes;D2 nodes;...".
    /// </summary>
    public static IEnumerable<PerftPosition> ParseEpd(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            string[] fields = line.Split(';');
            string fen = fields[0].Trim();
            if (fen.Length == 0 || fields.Length < 2)
            {
                throw new FormatException($"invalid perft line '{line}'");
            }
            var depths = new List<(int, long)>();
            foreach (string field in fields.Skip(1))
            {
                string[] parts = field.Trim().Split(' ');
                if (parts.Length != 2 || parts[0].Length < 2 || parts[0][0] != 'D'
                    || !int.TryParse(parts[0].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int depth)
                    || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long nodes))
                {
                    throw new FormatException($"invalid perft depth field '{field}'");
                }
                depths.Add((depth, nodes));
            }
            yield return new PerftPosition(fen, depths);
        }
    }

    private static List<PerftPosition> LoadStandardPositions()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("perft.epd")
            ?? throw new InvalidOperationException("perft.epd resource is missing");
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is string line)
        {
            lines.Add(line);
        }
        return [.. ParseEpd(lines)];
    }
}
