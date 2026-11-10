using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;
using StoopidChess.Desktop.Views;
using StoopidChess.Play;

namespace StoopidChess.Desktop.Sdl;

/// <summary>
/// The application on SDL3: the main window and any other, the event loop that hands their events to the views and
/// redraws what changed, the UI thread's synchronization context it pumps (woken by an SDL user event), the clock's
/// tick, and "--smoke". Everything it does is a call to SDL or to the views; the logic is the views' and MainView's.
/// Excluded from the coverage gate.
/// </summary>
internal sealed unsafe class SdlHost : IDisposable
{
    /// <summary>How often the clocks tick.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How long "--smoke" keeps the GUI running once its main window has opened.</summary>
    private static readonly TimeSpan SmokeRunTime = TimeSpan.FromSeconds(10);

    private static SdlHost? current;       // The one host, for SDL's event watch, which takes no managed state

    private readonly List<SdlWindow> windows = [];
    private readonly Dictionary<SdlWindow, Action> closedHandlers = [];
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly int mainThread = Environment.CurrentManagedThreadId;
    private readonly uint wakeEvent;
    private readonly UiSynchronizationContext context;
    private bool isQuitting;

    private SdlHost()
    {
        wakeEvent = SDL3.SDL_RegisterEvents(1);
        context = new UiSynchronizationContext(Wake);
        SynchronizationContext.SetSynchronizationContext(context);
        current = this;
    }

    /// <summary>The main window.</summary>
    public SdlWindow? MainWindow { get; private set; }

    /// <summary>The UI thread's context.</summary>
    public UiSynchronizationContext Context => context;

    /// <summary>
    /// Run the GUI until it is closed; with <paramref name="isSmokeTest"/>, say when the main window has opened and
    /// close it ten seconds later. Returns the process's exit code.
    /// </summary>
    public static int Run(bool isSmokeTest)
    {
        SDL3.SDL_SetHint("SDL_APP_NAME", "Stoopid Chess");
        SDL3.SDL_SetHint("SDL_MOUSE_FOCUS_CLICKTHROUGH", "1");
        if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
        {
            Console.Error.WriteLine($"Stoopid Chess: SDL could not start its video: {SDL3.SDL_GetError()}");
            return 1;
        }
        try
        {
            using var host = new SdlHost();
            return host.RunMainWindow(isSmokeTest);
        }
        finally
        {
            SDL3.SDL_Quit();
        }
    }

    private int RunMainWindow(bool isSmokeTest)
    {
        var platform = new SdlPlatform(this);
        var root = new ViewRoot();
        using var mainView = new MainView(platform, new GameController());
        root.Content = mainView;
        MainWindow = new SdlWindow("Stoopid Chess", MainView.DefaultSize, MainView.MinimumSize, root);
        windows.Add(MainWindow);
        platform.FlushTitle();
        SDL3.SDL_AddEventWatch(&WatchEvent, IntPtr.Zero);
        MainWindow.Show();
        MainWindow.Render(isForced: true);
        _ = mainView.StartAsync();
        TimeSpan smokeEnd = TimeSpan.MaxValue;
        if (isSmokeTest)
        {
            Console.WriteLine($"Stoopid Chess {Program.Version}: the main window is open");
            smokeEnd = clock.Elapsed + SmokeRunTime;
        }
        TimeSpan lastTick = clock.Elapsed;
        SDL_Event e;
        while (!isQuitting)
        {
            TimeSpan untilTick = lastTick + TickInterval - clock.Elapsed;
            int timeout = context.HasPending ? 0 : Math.Clamp((int)untilTick.TotalMilliseconds, 0, (int)TickInterval.TotalMilliseconds);
            if (SDL3.SDL_WaitEventTimeout(&e, timeout))
            {
                Dispatch(e);
                while (!isQuitting && SDL3.SDL_PollEvent(&e))
                {
                    Dispatch(e);
                }
            }
            context.RunPending();
            TimeSpan now = clock.Elapsed;
            if (now - lastTick >= TickInterval)
            {
                mainView.Tick(now - lastTick);
                lastTick = now;
            }
            if (now >= smokeEnd)
            {
                Console.WriteLine($"Stoopid Chess {Program.Version}: ran for {SmokeRunTime.TotalSeconds:0} seconds; quitting");
                isQuitting = true;
            }
            foreach (SdlWindow window in windows.ToArray())
            {
                window.Render();
                window.UpdateTextInput();
            }
        }
        SDL3.SDL_RemoveEventWatch(&WatchEvent, IntPtr.Zero);
        return 0;
    }

    /// <summary>Close the application at the loop's next turn.</summary>
    public void Quit() => isQuitting = true;

    /// <summary>
    /// Open another window showing <paramref name="root"/>; <paramref name="closed"/> runs once it is closed.
    /// </summary>
    public SdlWindow OpenWindow(string title, int width, int height, ViewRoot root, Action closed)
    {
        var window = new SdlWindow(title, new SkiaSharp.SKSizeI(width, height), new SkiaSharp.SKSizeI(320, 200), root);
        windows.Add(window);
        closedHandlers[window] = closed;
        window.Show();
        window.Render(isForced: true);
        return window;
    }

    /// <summary>
    /// Close a window opened by <see cref="OpenWindow"/>, and tell its owner.
    /// </summary>
    public void CloseWindow(SdlWindow window)
    {
        if (!windows.Remove(window))
        {
            return;
        }
        window.Dispose();
        if (closedHandlers.Remove(window, out Action? closed))
        {
            closed();
        }
    }

    private void Wake()
    {
        SDL_Event wake = default;
        wake.type = wakeEvent;
        SDL3.SDL_PushEvent(&wake);
    }

    private SdlWindow? WindowOf(SDL_WindowID id) => windows.Find(window => window.Id == id);

    private void Dispatch(SDL_Event e)
    {
        switch ((SDL_EventType)e.type)
        {
            case SDL_EventType.SDL_EVENT_QUIT:
                isQuitting = true;
                break;
            case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                if (WindowOf(e.window.windowID) is { } closing)
                {
                    if (closing == MainWindow)
                    {
                        isQuitting = true;
                    }
                    else
                    {
                        CloseWindow(closing);
                    }
                }
                break;
            case SDL_EventType.SDL_EVENT_WINDOW_EXPOSED:
                WindowOf(e.window.windowID)?.Root.Invalidate();
                break;
            case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE:
                WindowOf(e.window.windowID)?.Root.PointerLeave();
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                if (WindowOf(e.motion.windowID) is { } moved)
                {
                    moved.Root.PointerMove(new PointerEvent(moved.ToLogical(e.motion.x, e.motion.y), PointerButton.Left, Modifiers(SDL3.SDL_GetModState())));
                }
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                if (WindowOf(e.button.windowID) is { } pressed && Button(e.button.button) is { } button)
                {
                    var pointer = new PointerEvent(pressed.ToLogical(e.button.x, e.button.y), button, Modifiers(SDL3.SDL_GetModState()));
                    if (e.button.down)
                    {
                        pressed.Root.PointerDown(pointer);
                    }
                    else
                    {
                        pressed.Root.PointerUp(pointer);
                    }
                }
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                if (WindowOf(e.wheel.windowID) is { } wheeled)
                {
                    float flip = e.wheel.direction == SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED ? -1 : 1;
                    wheeled.Root.Wheel(new WheelEvent(wheeled.ToLogical(e.wheel.mouse_x, e.wheel.mouse_y),
                        flip * e.wheel.x, -flip * e.wheel.y, Modifiers(SDL3.SDL_GetModState())));
                }
                break;
            case SDL_EventType.SDL_EVENT_KEY_DOWN:
                if (WindowOf(e.key.windowID) is { } keyed)
                {
                    keyed.Root.KeyDown(new KeyEvent(SdlKeys.KeyOf(e.key.key), Modifiers(e.key.mod)));
                }
                break;
            case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                if (WindowOf(e.text.windowID) is { } typed && Marshal.PtrToStringUTF8((IntPtr)e.text.text) is { } text)
                {
                    typed.Root.TextInput(text);
                }
                break;
            default:
                break;
        }
    }

    private static PointerButton? Button(byte button) => (SDLButton)button switch
    {
        SDLButton.SDL_BUTTON_LEFT => PointerButton.Left,
        SDLButton.SDL_BUTTON_RIGHT => PointerButton.Right,
        SDLButton.SDL_BUTTON_MIDDLE => PointerButton.Middle,
        _ => null,
    };

    /// <summary>
    /// The modifiers held, Primary being Cmd on macOS and Ctrl elsewhere.
    /// </summary>
    private static KeyModifiers Modifiers(SDL_Keymod held)
    {
        KeyModifiers modifiers = KeyModifiers.None;
        SDL_Keymod primary = OperatingSystem.IsMacOS() ? SDL_Keymod.SDL_KMOD_GUI : SDL_Keymod.SDL_KMOD_CTRL;
        if ((held & primary) != 0)
        {
            modifiers |= KeyModifiers.Primary;
        }
        if ((held & SDL_Keymod.SDL_KMOD_SHIFT) != 0)
        {
            modifiers |= KeyModifiers.Shift;
        }
        if ((held & SDL_Keymod.SDL_KMOD_ALT) != 0)
        {
            modifiers |= KeyModifiers.Alt;
        }
        return modifiers;
    }

    /// <summary>
    /// SDL's event watch: a window exposed while its edge is dragged on Windows, where the event loop waits for the
    /// drag to end, is drawn at once, from here, so it follows the drag. Only on the UI thread.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static SDLBool WatchEvent(IntPtr userdata, SDL_Event* e)
    {
        if (current is { } host && Environment.CurrentManagedThreadId == host.mainThread
            && (SDL_EventType)e->type is SDL_EventType.SDL_EVENT_WINDOW_EXPOSED
            && host.WindowOf(e->window.windowID) is { } window)
        {
            window.Render(isForced: true);
        }
        return true;
    }

    public void Dispose()
    {
        foreach (SdlWindow window in windows.ToArray())
        {
            window.Dispose();
        }
        windows.Clear();
        SynchronizationContext.SetSynchronizationContext(null);
        current = null;
    }
}
