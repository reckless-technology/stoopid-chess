using Avalonia;

namespace StoopidChess.Desktop;

internal static class Program
{
    // Avalonia configuration happens in BuildAvaloniaApp; don't touch Avalonia types in Main
    // before it runs, or the previewer and platform detection break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
