using System.Reflection;
using StoopidChess.Desktop.Sdl;

namespace StoopidChess.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // "--version" prints the version and exits before SDL starts, so it needs no display: the release workflow
        // asks each shipped GUI for it. "--smoke" starts the GUI as usual and quits it ten seconds after its main
        // window opens (SdlHost), the exit code saying whether it ran. The Windows build is a GUI-subsystem
        // executable with no console of its own, so both print only where their output is redirected.
        if (args is ["--version"])
        {
            Console.WriteLine($"Stoopid Chess {Version}");
            return 0;
        }
        return SdlHost.Run(isSmokeTest: args is ["--smoke"]);
    }

    /// <summary>
    /// The version the build stamped (Directory.Build.targets: MAJOR.MINOR.BUILD from the git tags).
    /// </summary>
    internal static string Version =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";
}
