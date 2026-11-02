using System.Collections.Concurrent;

namespace StoopidChess.Desktop;

/// <summary>
/// The UI thread's synchronization context: work posted from any thread (an engine's reply, a file dialog's answer)
/// waits in a queue until the UI thread's loop runs it with <see cref="RunPending"/>, and <paramref name="wake"/> is
/// called at each post so a loop asleep waiting for events wakes up (the SDL host pushes an event). Engine replies
/// therefore continue on the UI thread, which <see cref="Play.GameController"/> requires.
/// </summary>
internal sealed class UiSynchronizationContext(Action wake) : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> queue = new();
    private readonly int ownerThread = Environment.CurrentManagedThreadId;

    /// <summary>Whether work is waiting.</summary>
    public bool HasPending => !queue.IsEmpty;

    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        queue.Enqueue((d, state));
        wake();
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (Environment.CurrentManagedThreadId == ownerThread)
        {
            d(state);
            return;
        }
        var done = new ManualResetEventSlim(); // Not disposed: Set may still be returning as Wait does
        Exception? failure = null;
        Post(_ =>
        {
            try
            {
                d(state);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failure = ex;
            }
            finally
            {
                done.Set();
            }
        }, null);
        done.Wait();
        if (failure is not null)
        {
            throw new InvalidOperationException("Work sent to the UI thread failed.", failure);
        }
    }

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>
    /// Run the work waiting, in the order it was posted, including any posted while it runs; returns how much ran.
    /// </summary>
    public int RunPending()
    {
        int count = 0;
        while (queue.TryDequeue(out (SendOrPostCallback Callback, object? State) work))
        {
            work.Callback(work.State);
            count++;
        }
        return count;
    }
}
