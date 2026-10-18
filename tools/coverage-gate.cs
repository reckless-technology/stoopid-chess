// The line-coverage gate: reads the Cobertura reports coverlet's collector writes (the test run's
// --collect "XPlat Code Coverage" with tests/coverage.runsettings, which leaves out the test assembly and
// the layout layer), prints each assembly's line coverage and the total, and exits 1 when the
// total is under the threshold below - the one place the threshold lives - or when an assembly it gates is
// missing from the report.
//
//   dotnet run tools/coverage-gate.cs -- <report or directory>... [--uncovered]
//
// A directory is searched for coverage.cobertura.xml files. Lines are counted once per source line: the
// reports' classes overlap (a lambda's or an async method's compiler-generated class repeats its
// method's lines), and several reports, from several test projects, are merged by taking a line as
// covered when any report ran it. --uncovered lists every line no test ran, file by file.

using System.Globalization;
using System.Xml.Linq;

const double Threshold = 0.90;

// Every assembly the gate covers must be in the report: one that vanished (a filter that stopped matching
// it, a reference dropped from the test project) would otherwise raise the total by leaving its lines out.
string[] gated = ["StoopidChess.Core", "StoopidChess.Engine", "StoopidChess.Play", "StoopidChess.Desktop", "stoopid-chess"];

bool listUncovered = args.Contains("--uncovered");
List<string> reports = [];
foreach (string arg in args.Where(a => a != "--uncovered"))
{
    if (Directory.Exists(arg))
    {
        reports.AddRange(Directory.EnumerateFiles(arg, "coverage.cobertura.xml", SearchOption.AllDirectories));
    }
    else if (File.Exists(arg))
    {
        reports.Add(arg);
    }
    else
    {
        Console.Error.WriteLine($"coverage-gate: no such report or directory: {arg}");
        return 2;
    }
}
if (reports.Count == 0)
{
    Console.Error.WriteLine("coverage-gate: no coverage.cobertura.xml found; usage: coverage-gate.cs <report or directory>... [--uncovered]");
    return 2;
}

// (assembly, file) -> line number -> covered
var lines = new SortedDictionary<(string Assembly, string File), SortedDictionary<int, bool>>();
foreach (string report in reports)
{
    XDocument document = XDocument.Load(report);
    foreach (XElement package in document.Descendants("package"))
    {
        string assembly = (string?)package.Attribute("name") ?? "?";
        foreach (XElement cls in package.Descendants("class"))
        {
            string file = ((string?)cls.Attribute("filename") ?? "?").Replace('\\', '/');
            if (!lines.TryGetValue((assembly, file), out SortedDictionary<int, bool>? fileLines))
            {
                fileLines = [];
                lines[(assembly, file)] = fileLines;
            }
            foreach (XElement line in cls.Elements("lines").Elements("line"))
            {
                int number = int.Parse((string)line.Attribute("number")!, CultureInfo.InvariantCulture);
                bool hit = long.Parse((string)line.Attribute("hits")!, CultureInfo.InvariantCulture) > 0;
                fileLines[number] = (fileLines.TryGetValue(number, out bool seen) && seen) || hit;
            }
        }
    }
}

int totalValid = 0;
int totalCovered = 0;
Console.WriteLine($"{"assembly",-24} {"lines",7} {"covered",8} {"rate",8}");
foreach (IGrouping<string, KeyValuePair<(string Assembly, string File), SortedDictionary<int, bool>>> assembly in lines.GroupBy(e => e.Key.Assembly))
{
    int valid = assembly.Sum(e => e.Value.Count);
    int covered = assembly.Sum(e => e.Value.Count(l => l.Value));
    totalValid += valid;
    totalCovered += covered;
    Console.WriteLine($"{assembly.Key,-24} {valid,7} {covered,8} {Percent(covered, valid),8}");
}
Console.WriteLine($"{"total",-24} {totalValid,7} {totalCovered,8} {Percent(totalCovered, totalValid),8}");

if (listUncovered)
{
    Console.WriteLine();
    foreach (((string assemblyName, string file), SortedDictionary<int, bool> fileLines) in lines)
    {
        List<int> missed = [.. fileLines.Where(l => !l.Value).Select(l => l.Key)];
        if (missed.Count > 0)
        {
            Console.WriteLine($"{file} ({missed.Count} of {fileLines.Count} uncovered): {Ranges(missed)}");
        }
    }
}

string[] missing = [.. gated.Where(name => !lines.Keys.Any(key => key.Assembly == name))];
if (missing.Length > 0)
{
    Console.Error.WriteLine($"coverage-gate: the report has no coverage of {string.Join(", ", missing)}.");
    return 1;
}
double rate = totalValid == 0 ? 0 : (double)totalCovered / totalValid;
if (rate < Threshold)
{
    Console.Error.WriteLine($"coverage-gate: line coverage {Percent(totalCovered, totalValid)} is under the {Threshold:P0} threshold.");
    return 1;
}
Console.WriteLine($"coverage-gate: line coverage {Percent(totalCovered, totalValid)} meets the {Threshold:P0} threshold.");
return 0;

static string Percent(int covered, int valid) =>
    valid == 0 ? "-" : ((double)covered / valid).ToString("P2", CultureInfo.InvariantCulture);

// 3,4,5,9 -> "3-5, 9"
static string Ranges(List<int> numbers)
{
    var parts = new List<string>();
    int start = numbers[0];
    int previous = start;
    foreach (int n in numbers.Skip(1).Append(int.MaxValue))
    {
        if (n != previous + 1)
        {
            parts.Add(start == previous ? $"{start}" : $"{start}-{previous}");
            start = n;
        }
        previous = n;
    }
    return string.Join(", ", parts);
}
