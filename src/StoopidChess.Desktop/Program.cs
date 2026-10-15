using System.Reflection;
using Avalonia;

namespace StoopidChess.Desktop;

internal static class Program
{
    // Avalonia configuration happens in BuildAvaloniaApp; don't touch Avalonia types in Main
    // before it runs, or the previewer and platform detection break.
    [STAThread]
    public static void Main(string[] args)
    {
        // "--version" prints the version and exits before Avalonia starts, so it needs no display: the
        // release workflow asks each shipped GUI for it.
        if (args is ["--version"])
        {
            Console.WriteLine($"Stoopid Chess {Version}");
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    /// <summary>
    /// The version the build stamped (Directory.Build.targets: MAJOR.MINOR.BUILD from the git tags).
    /// </summary>
    private static string Version =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}
