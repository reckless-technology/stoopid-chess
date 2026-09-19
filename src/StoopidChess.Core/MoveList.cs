using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace StoopidChess.Core;

/// <summary>
/// A list of moves with room for every move a position can have, stored inline rather than on the
/// heap: the moves live inside the struct itself (a kilobyte), so a search keeps one per ply and a
/// perft one per depth, and generating into one allocates nothing. Pass it by <c>ref</c>; a copy
/// copies every move. It enumerates as a span for <c>foreach</c> and as a sequence for LINQ.
/// </summary>
public struct MoveList : IEnumerable<Move>
{
    /// <summary>
    /// More than the 218 moves of the most mobile known position.
    /// </summary>
    public const int Capacity = 256;

    private Buffer moves;
    private int count;

    public readonly int Count => count;

    public readonly Move this[int index]
    {
        get
        {
            if ((uint)index >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return moves[index];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Move move)
    {
        Debug.Assert(count < Capacity, "move list is full");
        moves[count++] = move;
    }

    public void Clear() => count = 0;

    /// <summary>
    /// The moves as a span over the list's own storage, for ordering them in place.
    /// </summary>
    [UnscopedRef]
    public Span<Move> AsSpan() => ((Span<Move>)moves)[..count];

    [UnscopedRef]
    public Span<Move>.Enumerator GetEnumerator() => AsSpan().GetEnumerator();

    public readonly bool Contains(Move move)
    {
        for (int i = 0; i < count; i++)
        {
            if (moves[i] == move)
            {
                return true;
            }
        }
        return false;
    }

    public readonly Move[] ToArray()
    {
        var array = new Move[count];
        for (int i = 0; i < count; i++)
        {
            array[i] = moves[i];
        }
        return array;
    }

    readonly IEnumerator<Move> IEnumerable<Move>.GetEnumerator() => ((IEnumerable<Move>)ToArray()).GetEnumerator();

    readonly IEnumerator IEnumerable.GetEnumerator() => ToArray().GetEnumerator();

    [InlineArray(Capacity)]
    private struct Buffer
    {
        private Move first;
    }
}
