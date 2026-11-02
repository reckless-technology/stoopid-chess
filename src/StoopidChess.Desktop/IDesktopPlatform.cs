using StoopidChess.Desktop.Views;

namespace StoopidChess.Desktop;

/// <summary>
/// A kind of file a file dialog offers: its name and its extensions, separated by semicolons without dots
/// ("chess", "pgn"), or "*" for every file.
/// </summary>
internal sealed record FileFilter(string Name, string Extensions)
{
    /// <summary>Every file.</summary>
    public static FileFilter All { get; } = new("All files", "*");
}

/// <summary>
/// A window the main view has opened beside its own: the Analysis window.
/// </summary>
internal interface IDesktopWindow
{
    /// <summary>Bring the window to the front.</summary>
    void Activate();

    /// <summary>Close the window.</summary>
    void Close();
}

/// <summary>
/// What the main view asks of the window system, and nothing more: the SDL host does it for real, and the tests fake
/// it. File dialogs answer with a path, or null when cancelled; the main view reads and writes the files itself.
/// </summary>
internal interface IDesktopPlatform
{
    /// <summary>Ask for a file to open.</summary>
    Task<string?> ChooseFileToOpenAsync(string title, IReadOnlyList<FileFilter> filters);

    /// <summary>Ask for a file to save to, offering <paramref name="suggestedName"/>.</summary>
    Task<string?> ChooseFileToSaveAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters);

    /// <summary>The clipboard's text; null when it holds none.</summary>
    string? GetClipboardText();

    /// <summary>Put <paramref name="text"/> on the clipboard.</summary>
    void SetClipboardText(string text);

    /// <summary>Set the main window's title.</summary>
    void SetTitle(string title);

    /// <summary>
    /// Open another window, <paramref name="width"/> by <paramref name="height"/> logical pixels, showing
    /// <paramref name="content"/>; <paramref name="closed"/> runs once it has been closed, by the person or by
    /// <see cref="IDesktopWindow.Close"/>.
    /// </summary>
    IDesktopWindow OpenWindow(string title, int width, int height, ViewRoot content, Action closed);

    /// <summary>Close the application.</summary>
    void Quit();
}
