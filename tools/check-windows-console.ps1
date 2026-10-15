# Checks that the Windows GUI's --version and --smoke print into the command prompt they are typed in, as a person sees
# them, on a machine with no interactive desktop (the release workflow's Windows runner, a service).
#
#   pwsh -NoProfile -File tools/check-windows-console.ps1 -Gui <the GUI's .exe> -Version <X.Y.Z>
#
# The GUI is a GUI-subsystem executable, which Windows starts with no console, so it attaches itself to its parent's
# (src/StoopidChess.Desktop/WindowsConsole.cs). To see what a person would, this runs cmd.exe in a pseudoconsole
# (ConPTY, as Windows Terminal and OpenSSH host a shell) and reads the screen it draws, while cmd runs:
#   - "<gui> --version": the version on the screen;
#   - "<gui> --version > file": the version in the file (which cmd then types);
#   - "<gui> --smoke": both of its lines on the screen.
# cmd.exe does not wait for a GUI program, so each command line keeps cmd alive (ping as a sleep) until the GUI has
# finished: AttachConsole(ATTACH_PARENT_PROCESS) needs the parent still running. Exits non-zero on any failure.
param(
    [Parameter(Mandatory)] [string] $Gui,
    [Parameter(Mandatory)] [string] $Version
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

public static class PseudoConsoleRun
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Coord { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb; public IntPtr lpReserved; public IntPtr lpDesktop; public IntPtr lpTitle;
        public int dwX; public int dwY; public int dwXSize; public int dwYSize; public int dwXCountChars;
        public int dwYCountChars; public int dwFillAttribute; public int dwFlags; public short wShowWindow;
        public short cbReserved2; public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr lpAttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }

    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const int UseStandardHandles = 0x00000100;
    private static readonly IntPtr PseudoConsoleAttribute = (IntPtr)0x00020016;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);
    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);
    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfoEx startupInfo, out ProcessInformation information);
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    // Runs the command line in a new pseudoconsole and returns everything drawn on it (with the terminal's escape
    // sequences), once the process has exited or the time is up.
    public static string Run(string commandLine, string directory, int timeoutMilliseconds)
    {
        SafeFileHandle inputRead, inputWrite, outputRead, outputWrite;
        if (!CreatePipe(out inputRead, out inputWrite, IntPtr.Zero, 0) || !CreatePipe(out outputRead, out outputWrite, IntPtr.Zero, 0))
        {
            throw new InvalidOperationException("CreatePipe failed: " + Marshal.GetLastWin32Error());
        }
        IntPtr console;
        int result = CreatePseudoConsole(new Coord { X = 160, Y = 50 }, inputRead, outputWrite, 0, out console);
        if (result != 0)
        {
            throw new InvalidOperationException("CreatePseudoConsole failed: 0x" + result.ToString("x8"));
        }
        inputRead.Dispose();
        outputWrite.Dispose();
        StringBuilder screen = new StringBuilder();
        Thread reader = new Thread(() =>
        {
            byte[] buffer = new byte[4096];
            using (FileStream stream = new FileStream(outputRead, FileAccess.Read))
            {
                try
                {
                    int count;
                    while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        lock (screen) { screen.Append(Encoding.UTF8.GetString(buffer, 0, count)); }
                    }
                }
                catch (IOException) { }
            }
        });
        reader.IsBackground = true;
        reader.Start();

        IntPtr size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        IntPtr list = Marshal.AllocHGlobal(size);
        try
        {
            if (!InitializeProcThreadAttributeList(list, 1, 0, ref size)
                || !UpdateProcThreadAttribute(list, 0, PseudoConsoleAttribute, console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            {
                throw new InvalidOperationException("the attribute list failed: " + Marshal.GetLastWin32Error());
            }
            StartupInfoEx startup = new StartupInfoEx();
            startup.StartupInfo.cb = Marshal.SizeOf(typeof(StartupInfoEx));
            // Null standard handles, given explicitly: otherwise cmd.exe inherits this process's, which the runner
            // redirects to its log, instead of the pseudoconsole's, which Windows then gives it.
            startup.StartupInfo.dwFlags = UseStandardHandles;
            startup.lpAttributeList = list;
            ProcessInformation process;
            if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                ExtendedStartupInfoPresent, IntPtr.Zero, directory, ref startup, out process))
            {
                throw new InvalidOperationException("CreateProcess failed: " + Marshal.GetLastWin32Error());
            }
            bool hasExited = WaitForSingleObject(process.hProcess, (uint)timeoutMilliseconds) == 0;
            if (!hasExited)
            {
                TerminateProcess(process.hProcess, 1);
            }
            CloseHandle(process.hThread);
            CloseHandle(process.hProcess);
            Thread.Sleep(500);
            ClosePseudoConsole(console);
            reader.Join(5000);
            inputWrite.Dispose();
            lock (screen) { return (hasExited ? "" : "[timed out]\n") + screen.ToString(); }
        }
        finally
        {
            DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list);
        }
    }
}
'@

$gui = (Resolve-Path $Gui).Path
$folder = Join-Path ([System.IO.Path]::GetTempPath()) "stoopid-console-check-$PID"
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $folder
New-Item -ItemType Directory $folder | Out-Null
Copy-Item $gui (Join-Path $folder 'gui.exe')
$failures = @()

function Show-Screen([string] $what, [string] $screen) {
    # The terminal's escape sequences out, so the log shows the text as it reads
    $text = $screen -replace "`e\][^`a]*`a", '' -replace "`e\[[0-9;?]*[ -/]*[@-~]", '' -replace "`r", ''
    Write-Host "--- $what, as the command prompt shows it:"
    Write-Host (($text -split "`n" | Where-Object { $_.Trim() }) -join "`n")
    return $text
}

$said = "Stoopid Chess $Version"

# --version: cmd starts it and goes on; the ping keeps cmd (the console's owner) alive while the GUI attaches and prints.
$screen = [PseudoConsoleRun]::Run('cmd.exe /d /c "gui.exe --version & ping -n 4 127.0.0.1 > nul & echo the prompt is still here"', $folder, 60000)
$text = Show-Screen 'gui.exe --version' $screen
if ($text -notmatch [regex]::Escape($said)) { $failures += "--version printed nothing in the command prompt" }
if ($text -notmatch 'the prompt is still here') { $failures += "the pseudoconsole showed nothing of cmd.exe's own: the check itself is broken" }

# --version > file: the version goes to the file, and nothing of it to the screen.
$screen = [PseudoConsoleRun]::Run('cmd.exe /d /c "gui.exe --version > version.txt & ping -n 4 127.0.0.1 > nul & type version.txt"', $folder, 60000)
$text = Show-Screen 'gui.exe --version > version.txt, then type version.txt' $screen
$file = Get-Content -Raw -ErrorAction SilentlyContinue (Join-Path $folder 'version.txt')
Write-Host "--- version.txt: $file"
if ("$file".Trim() -ne $said) { $failures += "--version > file wrote '$file' to the file" }

# --smoke: its two lines, the window opening and the quitting ten seconds later, on the screen.
$screen = [PseudoConsoleRun]::Run('cmd.exe /d /c "gui.exe --smoke & ping -n 21 127.0.0.1 > nul"', $folder, 90000)
$text = Show-Screen 'gui.exe --smoke' $screen
if ($text -notmatch "$([regex]::Escape($said)): the main window is open") { $failures += "--smoke did not say its window opened in the command prompt" }
if ($text -notmatch "$([regex]::Escape($said)): ran for 10 seconds; quitting") { $failures += "--smoke did not say it quit in the command prompt" }

Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $folder 'gui.exe') } | Stop-Process -Force
Start-Sleep -Seconds 1
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $folder
if ($failures) {
    $failures | ForEach-Object { Write-Host "::error::$_" }
    exit 1
}
"the GUI's --version and --smoke print into a command prompt"
