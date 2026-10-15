using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace StoopidChess.Desktop;

/// <summary>
/// Gives "--version" and "--smoke" somewhere to print on Windows. The app is a GUI-subsystem executable, so Windows
/// starts it with no console: run from a command prompt, whatever it writes to standard output goes nowhere. This
/// attaches it to the console of the process that started it (the command prompt's), never a new one, and points the
/// standard streams that are not redirected at it. A stream already redirected to a pipe or a file (the release
/// workflow captures both) is left exactly as it is.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsConsole
{
    private const uint AttachParentProcess = unchecked((uint)-1);
    private const int StandardOutputHandle = -11;
    private const int StandardErrorHandle = -12;
    private const uint FileTypeUnknown = 0;
    private const uint FileTypeChar = 2;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 3;

    /// <summary>
    /// Attaches to the parent's console, if it has one and a standard stream needs it. Does nothing when both
    /// streams are redirected, or when the parent has no console (started from Explorer, say): no console is ever
    /// allocated.
    /// </summary>
    public static void AttachToParent()
    {
        nint output = GetStdHandle(StandardOutputHandle);
        nint error = GetStdHandle(StandardErrorHandle);
        if ((IsPipeOrFile(output) && IsPipeOrFile(error)) || !AttachConsole(AttachParentProcess))
        {
            return;
        }
        if (IsRedirected(output))
        {
            _ = SetStdHandle(StandardOutputHandle, output); // As it was, whatever the attaching did
        }
        else
        {
            Console.SetOut(OpenConsoleWriter(StandardOutputHandle));
        }
        if (IsRedirected(error))
        {
            _ = SetStdHandle(StandardErrorHandle, error);
        }
        else
        {
            Console.SetError(OpenConsoleWriter(StandardErrorHandle));
        }
    }

    /// <summary>
    /// Whether the handle is a pipe or a file the starting process gave this one.
    /// </summary>
    private static bool IsPipeOrFile(nint handle) =>
        handle != 0 && handle != -1 && GetFileType(handle) is not (FileTypeUnknown or FileTypeChar);

    /// <summary>
    /// Whether the handle, looked at once attached, is anything but a console: a pipe, a file, or a character device
    /// such as NUL ("> nul" from the command prompt), which only a console handle's mode tells apart from a console.
    /// </summary>
    private static bool IsRedirected(nint handle) =>
        IsPipeOrFile(handle)
        || (handle != 0 && handle != -1 && GetFileType(handle) == FileTypeChar && !GetConsoleMode(handle, out _));

    /// <summary>
    /// A writer to the console just attached, through the standard handle where attaching set it, else through a
    /// handle of the console's screen buffer, which then becomes that standard handle.
    /// </summary>
    private static StreamWriter OpenConsoleWriter(int standardHandle)
    {
        nint handle = GetStdHandle(standardHandle);
        if (handle == 0 || handle == -1 || GetFileType(handle) != FileTypeChar)
        {
            handle = CreateFileW("CONOUT$", GenericRead | GenericWrite, FileShareWrite, 0, OpenExisting, 0, 0);
            _ = SetStdHandle(standardHandle, handle);
        }
        Stream stream = standardHandle == StandardOutputHandle ? Console.OpenStandardOutput() : Console.OpenStandardError();
        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int standardHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetStdHandle(int standardHandle, nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetFileType(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFileW(string fileName, uint access, uint share, nint securityAttributes,
        uint creationDisposition, uint flags, nint template);
}
