using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace StoopidChess.Core;

/// <summary>
/// A list of moves with room for every move a position can have, stored inline rather than on the
/// heap: the moves live inside the struct itself (a kilobyte), so a search keeps one per ply and a
/// perft one per depth, and generating into one allocates nothing. Pass it by <c>ref</c>; a copy
/// copies every move. It enumerates as a span for <c>foreach</c>; <c>ToArray()</c> copies the moves out.
/// </summary>
public struct MoveList
{
    /// <summary>
    /// More than the 218 moves of the most mobile known position.
    /// </summary>
    public const int Capacity = 256;

    private Buffer _moves;
    private int _count;

    public readonly int Count => _count;

    public readonly Move this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return _moves[index];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(Move move)
    {
        Debug.Assert(_count < Capacity, "move list is full");
        _moves[_count++] = move;
    }

    public void Clear() => _count = 0;

    /// <summary>
    /// The moves as a span over the list's own storage, for ordering them in place.
    /// </summary>
    [UnscopedRef]
    public Span<Move> AsSpan() => ((Span<Move>)_moves)[.._count];

    [UnscopedRef]
    public Span<Move>.Enumerator GetEnumerator() => AsSpan().GetEnumerator();

    public readonly bool Contains(Move move)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_moves[i] == move)
            {
                return true;
            }
        }
        return false;
    }

    public readonly Move[] ToArray()
    {
        var array = new Move[_count];
        for (int i = 0; i < _count; i++)
        {
            array[i] = _moves[i];
        }
        return array;
    }

    [InlineArray(Capacity)]
    private struct Buffer
    {
        private Move _first;
    }
}
