using StoopidChess.Desktop;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// The UI thread's context: work posted from anywhere waits, wakes the loop, and runs in order on the loop's turn;
/// work sent from another thread runs there and is waited for.
/// </summary>
public class UiSynchronizationContextTests
{
    [Fact]
    public void PostedWorkWakesTheLoopAndRunsInOrder()
    {
        int wakes = 0;
        var context = new UiSynchronizationContext(() => wakes++);
        var ran = new List<int>();
        context.Post(_ => ran.Add(1), null);
        context.Post(
            _ =>
            {
                ran.Add(2);
                context.Post(_ => ran.Add(3), null); // Posted while running: runs in the same turn
            },
            null
        );
        Assert.Equal(2, wakes);
        Assert.True(context.HasPending);
        Assert.Empty(ran);
        Assert.Equal(3, context.RunPending());
        Assert.Equal([1, 2, 3], ran);
        Assert.False(context.HasPending);
        Assert.Same(context, context.CreateCopy());
        Assert.Throws<ArgumentNullException>(() => context.Post(null!, null));
        Assert.Throws<ArgumentNullException>(() => context.Send(null!, null));
    }

    [Fact]
    public async Task SentWorkRunsAtOnceOnTheUiThreadAndIsWaitedForFromAnother()
    {
        var context = new UiSynchronizationContext(() => { });
        int value = 0;
        context.Send(_ => value = 1, null);
        Assert.Equal(1, value);

        Task sent = OnAnotherThread(() => context.Send(_ => value = 2, null));
        await UntilPendingAsync(context);
        Assert.False(sent.IsCompleted);
        context.RunPending();
        await sent.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, value);

        Task failing = OnAnotherThread(() => context.Send(_ => throw new InvalidDataException("bad"), null));
        await UntilPendingAsync(context);
        context.RunPending();
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failing.WaitAsync(TimeSpan.FromSeconds(10))
        );
        Assert.IsType<InvalidDataException>(failure.InnerException);
    }

    /// <summary>
    /// Run <paramref name="action"/> on a thread of its own: never the thread the context was made on, as a pool
    /// thread might be.
    /// </summary>
    private static Task OnAnotherThread(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                done.SetResult();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                done.SetException(ex);
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();
        return done.Task;
    }

    private static async Task UntilPendingAsync(UiSynchronizationContext context)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!context.HasPending)
        {
            Assert.True(DateTime.UtcNow < deadline, "nothing was posted");
            await Task.Delay(1);
        }
    }
}
