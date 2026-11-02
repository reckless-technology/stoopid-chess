using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Desktop.Sdl;

/// <summary>
/// What the main view asks of the window system, done by SDL3: its native file dialogs (answered on whatever thread
/// SDL calls back on, and handed to the UI thread's context), its clipboard, the main window's title, another window,
/// and quitting. Excluded from the coverage gate with the rest of the SDL host.
/// </summary>
internal sealed unsafe class SdlPlatform(SdlHost host) : IDesktopPlatform
{
    private string? pendingTitle;

    public Task<string?> ChooseFileToOpenAsync(string title, IReadOnlyList<FileFilter> filters)
    {
        var request = new FileRequest(host.Context, filters);
        SDL3.SDL_ShowOpenFileDialog(&FileChosen, request.Handle, MainWindowHandle, request.Filters, request.FilterCount,
            (string?)null, false);
        return request.Task;
    }

    public Task<string?> ChooseFileToSaveAsync(string title, string suggestedName, IReadOnlyList<FileFilter> filters)
    {
        var request = new FileRequest(host.Context, filters);
        string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string location = Path.Combine(folder.Length > 0 ? folder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), suggestedName);
        SDL3.SDL_ShowSaveFileDialog(&FileChosen, request.Handle, MainWindowHandle, request.Filters, request.FilterCount, location);
        return request.Task;
    }

    public string? GetClipboardText() => SDL3.SDL_GetClipboardText();

    public void SetClipboardText(string text) => SDL3.SDL_SetClipboardText(text);

    public void SetTitle(string title)
    {
        pendingTitle = title;
        FlushTitle();
    }

    /// <summary>The title set before the main window opened, given to it now.</summary>
    public void FlushTitle()
    {
        if (host.MainWindow is { } window && pendingTitle is { } title)
        {
            window.SetTitle(title);
        }
    }

    public IDesktopWindow OpenWindow(string title, int width, int height, ViewRoot content, Action closed) =>
        new OpenedWindow(host, host.OpenWindow(title, width, height, content, closed));

    public void Quit() => host.Quit();

    private SDL_Window* MainWindowHandle => host.MainWindow is { } window ? window.Handle : null;

    /// <summary>
    /// SDL's answer to a file dialog, on whatever thread it comes: the first file chosen, or none, handed to the UI
    /// thread, where the request's task completes and its memory is let go.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void FileChosen(IntPtr userdata, byte** files, int filter)
    {
        var request = (FileRequest)GCHandle.FromIntPtr(userdata).Target!;
        string? path = null;
        string? failure = null;
        if (files == null)
        {
            failure = SDL3.SDL_GetError();
        }
        else if (files[0] != null)
        {
            path = Marshal.PtrToStringUTF8((IntPtr)files[0]);
        }
        request.Answer(path, failure);
    }

    /// <summary>
    /// One file dialog's request: its filters in unmanaged memory, alive until SDL answers, and the task the answer
    /// completes.
    /// </summary>
    private sealed class FileRequest
    {
        private readonly TaskCompletionSource<string?> answer = new();
        private readonly UiSynchronizationContext context;
        private readonly List<IntPtr> strings = [];
        private GCHandle self;

        public FileRequest(UiSynchronizationContext context, IReadOnlyList<FileFilter> filters)
        {
            this.context = context;
            FilterCount = filters.Count;
            Filters = (SDL_DialogFileFilter*)NativeMemory.Alloc((nuint)Math.Max(1, filters.Count), (nuint)sizeof(SDL_DialogFileFilter));
            for (int i = 0; i < filters.Count; i++)
            {
                Filters[i].name = Utf8(filters[i].Name);
                Filters[i].pattern = Utf8(filters[i].Extensions);
            }
            self = GCHandle.Alloc(this);
        }

        public SDL_DialogFileFilter* Filters { get; }

        public int FilterCount { get; }

        public IntPtr Handle => GCHandle.ToIntPtr(self);

        public Task<string?> Task => answer.Task;

        public void Answer(string? path, string? failure) => context.Post(_ =>
        {
            foreach (IntPtr text in strings)
            {
                Marshal.FreeCoTaskMem(text);
            }
            NativeMemory.Free(Filters);
            self.Free();
            if (failure is not null)
            {
                answer.SetException(new InvalidOperationException(failure));
            }
            else
            {
                answer.SetResult(path);
            }
        }, null);

        private byte* Utf8(string text)
        {
            IntPtr pointer = Marshal.StringToCoTaskMemUTF8(text);
            strings.Add(pointer);
            return (byte*)pointer;
        }
    }

    /// <summary>
    /// A window the main view opened, as it sees it.
    /// </summary>
    private sealed class OpenedWindow(SdlHost host, SdlWindow window) : IDesktopWindow
    {
        public void Activate() => window.Raise();

        public void Close() => host.CloseWindow(window);
    }
}
