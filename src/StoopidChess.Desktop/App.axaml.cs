using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace StoopidChess.Desktop;

public partial class App : Application
{
    /// <summary>
    /// How long "--smoke" keeps the GUI running once its main window has opened.
    /// </summary>
    private static readonly TimeSpan SmokeRunTime = TimeSpan.FromSeconds(10);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            if (desktop.Args is ["--smoke"])
            {
                RunSmokeCheck(desktop, window);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// "--smoke", the release workflow's check that a shipped GUI starts where no one can look at it (the
    /// Windows runner has no interactive desktop): the GUI starts as usual, says on standard output when its
    /// main window has opened, and quits by itself ten seconds later with exit code 0. A GUI that fails to
    /// start, or dies while it runs, exits with anything but 0 or never says its window opened.
    /// </summary>
    private static void RunSmokeCheck(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window) =>
        window.Opened += (_, _) =>
        {
            Console.WriteLine($"Stoopid Chess {Program.Version}: the main window is open");
            DispatcherTimer.RunOnce(() =>
            {
                Console.WriteLine($"Stoopid Chess {Program.Version}: ran for {SmokeRunTime.TotalSeconds:0} seconds; quitting");
                desktop.Shutdown(0);
            }, SmokeRunTime);
        };
}
