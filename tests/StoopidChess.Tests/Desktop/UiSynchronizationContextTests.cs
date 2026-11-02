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
        context.Post(_ =>
        {
            ran.Add(2);
            context.Post(_ => ran.Add(3), null);            // Posted while running: runs in the same turn
        }, null);
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

        Task sent = Task.Run(() => context.Send(_ => value = 2, null));
        while (!context.HasPending)
        {
            await Task.Delay(1);
        }
        Assert.False(sent.IsCompleted);
        context.RunPending();
        await sent.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, value);

        Task failing = Task.Run(() => context.Send(_ => throw new InvalidDataException("bad"), null));
        while (!context.HasPending)
        {
            await Task.Delay(1);
        }
        context.RunPending();
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => failing.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsType<InvalidDataException>(failure.InnerException);
    }
}
