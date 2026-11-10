// Play a fastchess match between the Stoopid Chess engine and another UCI engine, or against a saved baseline build of
// itself. One file for Windows, Linux and macOS (a file-based C# app, run with the SDK this repository needs anyway):
//
//   dotnet run tools/match.cs -- --opponent ~/tools/pawnstar/pawnstar-0.13.593-linux-x86_64 \
//       --opponent-option option.Threads=1 --opponent-option option.OwnBook=false --rounds 20 --tc 12+0.12
//   dotnet run tools/match.cs -- --baseline          # after a search or evaluation change: 100 games against the baseline
//
// It builds the engine in Release, then runs paired games (each opening played with both colours) from an openings
// file; both engines are deterministic, so an openings file is essential. --baseline plays against the snapshot made with
//   dotnet publish src/StoopidChess.Uci -c Release -o ~/tools/stoopid-baseline
// (tools/setup-dev with --tuning/-Tuning publishes a first one when there is none). fastchess defaults to the build
// the same script installs under ~/tools (tools/tuning-tools.txt), else the one on the PATH.
using System.Diagnostics;
using System.Runtime.InteropServices;

// The repository: the folder above this script's own (a file-based app is compiled elsewhere, so not from the base directory).
string repo = Path.GetFullPath(Path.Combine((string)AppContext.GetData("EntryPointFileDirectoryPath")!, ".."));
string tools = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "tools");
string exe = OperatingSystem.IsWindows() ? ".exe" : "";
string? opponent = null;
var opponentOptions = new List<string>();
string? fastchess = null;
string openings = Path.Combine(repo, "tools", "openings-6ply.epd");
string timeControl = "12+0.12";
string? rounds = null;
string concurrency = "8";
string? pgn = null;
bool isBaseline = false;

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
    switch (args[i])
    {
        case "--opponent":
            opponent = Next();
            break;
        case "--baseline":
            isBaseline = true;
            break;
        case "--opponent-option":
            opponentOptions.Add(Next());
            break;
        case "--fastchess":
            fastchess = Next();
            break;
        case "--openings":
            openings = Next();
            break;
        case "--tc":
            timeControl = Next();
            break;
        case "--rounds":
            rounds = Next();
            break;
        case "--concurrency":
            concurrency = Next();
            break;
        case "--pgn":
            pgn = Next();
            break;
        default:
            return Usage($"unknown argument: {args[i]}");
    }
}
if (isBaseline)
{
    opponent ??= Path.Combine(tools, "stoopid-baseline", "stoopid-chess" + exe);
}
if (opponent is null)
{
    return Usage("give --opponent <uci engine> or --baseline");
}
rounds ??= isBaseline ? "50" : "20";
pgn ??= isBaseline ? "selfplay.pgn" : "match.pgn";
fastchess ??= FindFastchess();

if (
    Run(
        "dotnet",
        [
            "build",
            Path.Combine(repo, "src", "StoopidChess.Uci", "StoopidChess.Uci.csproj"),
            "-c",
            "Release",
            "--nologo",
            "-v",
            "q",
        ]
    ) != 0
)
{
    Console.Error.WriteLine("the engine did not build");
    return 1;
}
string engine = Path.Combine(repo, "src", "StoopidChess.Uci", "bin", "Release", "net10.0", "stoopid-chess" + exe);
List<string> fastchessArguments =
[
    "-engine",
    $"cmd={engine}",
    "name=StoopidChess",
    "-engine",
    $"cmd={opponent}",
    $"name={Path.GetFileNameWithoutExtension(opponent)}",
    .. opponentOptions,
    "-each",
    $"tc={timeControl}",
    "-rounds",
    rounds,
    "-games",
    "2",
    "-repeat",
    "-concurrency",
    concurrency,
    "-openings",
    $"file={openings}",
    "format=epd",
    "order=random",
    "-pgnout",
    $"file={pgn}",
    "-ratinginterval",
    "10",
];
return Run(fastchess, fastchessArguments);

static int Usage(string problem)
{
    Console.Error.WriteLine(problem);
    Console.Error.WriteLine(
        "usage: dotnet run tools/match.cs -- (--opponent <uci engine> | --baseline) [--opponent-option option.X=Y]..."
    );
    Console.Error.WriteLine(
        "       [--fastchess <path>] [--openings <epd>] [--tc 12+0.12] [--rounds 20|50] [--concurrency 8] [--pgn match.pgn]"
    );
    return 2;
}

static int Run(string command, IEnumerable<string> arguments)
{
    var start = new ProcessStartInfo(command) { UseShellExecute = false };
    foreach (string argument in arguments)
    {
        start.ArgumentList.Add(argument);
    }
    using Process process = Process.Start(start)!;
    process.WaitForExit();
    return process.ExitCode;
}

// The fastchess the setup script installed: the pinned line for this platform in tools/tuning-tools.txt, else the PATH's.
string FindFastchess()
{
    string os =
        OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsMacOS() ? "macos"
        : "linux";
    string architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    string platform = $"{os}-{architecture}";
    foreach (string line in File.ReadLines(Path.Combine(repo, "tools", "tuning-tools.txt")))
    {
        string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (
            fields.Length > 3
            && fields[0] == "fastchess"
            && fields[2] == platform
            && File.Exists(Path.Combine(tools, fields[3]))
        )
        {
            return Path.Combine(tools, fields[3]);
        }
    }
    return "fastchess";
}
