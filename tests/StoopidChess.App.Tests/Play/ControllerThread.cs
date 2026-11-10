using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace StoopidChess.Tests.Play;

/// <summary>
/// A thread with a single-threaded <see cref="SynchronizationContext"/>, to drive a GameController the way
/// the GUI drives it on its UI thread. GameController is single-threaded: the engine replies it awaits
/// continue on the context of the thread that called it. The xunit runner's own context spreads them over
/// its worker threads, where they run beside the test's next step - a test that read the controller just
/// after starting an engine match could find the match already over. Here they queue behind the test's
/// steps and run only when the test awaits, exactly as on the UI thread.
/// </summary>
internal sealed class ControllerThread : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = [];

    private ControllerThread()
    {
    }

    /// <summary>
    /// Run <paramref name="test"/> on a new thread of its own context; complete when it has, with its outcome.
    /// </summary>
    public static Task Run(Func<Task> test)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var context = new ControllerThread();
            SetSynchronizationContext(context);
            ExceptionDispatchInfo? failure = null;
            Task task;
            try
            {
                task = test();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                task = Task.FromException(ex);
            }
            task.ContinueWith(_ => context.queue.CompleteAdding(), TaskScheduler.Default);
            foreach ((SendOrPostCallback callback, object? state) in context.queue.GetConsumingEnumerable())
            {
                try
                {
                    callback(state);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    failure ??= ExceptionDispatchInfo.Capture(ex);
                }
            }
            if (task.IsFaulted)
            {
                done.SetException(task.Exception.InnerExceptions);
            }
            else if (task.IsCanceled)
            {
                done.SetCanceled();
            }
            else if (failure is not null)
            {
                done.SetException(failure.SourceException);
            }
            else
            {
                done.SetResult();
            }
        })
        {
            IsBackground = true,
            Name = "Controller thread",
        };
        thread.Start();
        return done.Task;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        try
        {
            queue.Add((d, state));
        }
        catch (InvalidOperationException)
        {
            // The test has finished: something it left behind, such as a disposed engine's last reply,
            // runs on the thread pool instead
            ThreadPool.QueueUserWorkItem(_ => d(state));
        }
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("the controller thread only takes posted work");

    public override SynchronizationContext CreateCopy() => this;
}
