namespace StoopidChess.Tests;

/// <summary>
/// The tests that run a program's entry point with the process's console redirected to their own
/// readers and writers. The console is one per process, so they run alone, after the parallel tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCapture
{
    public const string Name = "Console";

    /// <summary>
    /// Run <paramref name="main"/> with standard input reading <paramref name="input"/> and standard output
    /// and error captured, then put the console back. Returns the exit code and what was written.
    /// </summary>
    public static (int ExitCode, string Output, string Error) Run(Func<int> main, string input = "")
    {
        ArgumentNullException.ThrowIfNull(main);
        TextReader stdin = Console.In;
        TextWriter stdout = Console.Out;
        TextWriter stderr = Console.Error;
        using var reader = new StringReader(input);
        using var output = new StringWriter();
        using var error = new StringWriter();
        Console.SetIn(reader);
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            int exitCode = main();
            return (exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetIn(stdin);
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
    }
}
