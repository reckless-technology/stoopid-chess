using StoopidChess.Desktop;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// The window system as the main view's tests play it: file dialogs answered from a queue (null for a cancel, an
/// exception for a dialog that cannot open), a clipboard in a string, the title kept, other windows recorded.
/// </summary>
internal sealed class FakePlatform : IDesktopPlatform
{
    public Queue<object?> FileAnswers { get; } = new();

    public List<(string Title, IReadOnlyList<FileFilter> Filters)> FileDialogs { get; } = [];

    public string? Clipboard { get; set; }

    public string? Title { get; private set; }

    public List<FakeWindow> Windows { get; } = [];

    public bool HasQuit { get; private set; }

    public Task<string?> ChooseFileToOpenAsync(string title, IReadOnlyList<FileFilter> filters) => Answer(title, filters);

    public Task<string?> ChooseFileToSaveAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters) =>
        Answer(title, filters);

    public string? GetClipboardText() => Clipboard;

    public void SetClipboardText(string text) => Clipboard = text;

    public void SetTitle(string title) => Title = title;

    public IDesktopWindow OpenWindow(string title, int width, int height, ViewRoot content, Action closed)
    {
        var window = new FakeWindow(title, content, closed);
        Windows.Add(window);
        return window;
    }

    public void Quit() => HasQuit = true;

    private Task<string?> Answer(string title, IReadOnlyList<FileFilter> filters)
    {
        FileDialogs.Add((title, filters));
        object? answer = FileAnswers.Count > 0 ? FileAnswers.Dequeue() : null;
        return answer is Exception failure ? Task.FromException<string?>(failure) : Task.FromResult((string?)answer);
    }

    internal sealed class FakeWindow(string title, ViewRoot content, Action closed) : IDesktopWindow
    {
        public string Title { get; } = title;

        public ViewRoot Content { get; } = content;

        public int Activations { get; private set; }

        public bool IsClosed { get; private set; }

        public void Activate() => Activations++;

        public void Close()
        {
            if (!IsClosed)
            {
                IsClosed = true;
                closed();
            }
        }
    }
}
