using SkiaSharp;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// A pointer's button.
/// </summary>
internal enum PointerButton
{
    Left,
    Right,
    Middle,
}

/// <summary>
/// The modifier keys held. <see cref="Primary"/> is the platform's command key - Cmd on macOS, Ctrl elsewhere - which
/// the host reports in its place, so the views' shortcuts are written once.
/// </summary>
[Flags]
internal enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Primary = 2,
    Alt = 4,
}

/// <summary>
/// The keys the views act on; every other key arrives as <see cref="Other"/>, and typed characters arrive as text.
/// </summary>
internal enum Key
{
    Other,
    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    Return,
    Escape,
    Tab,
    Backspace,
    Delete,
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    PageUp,
    PageDown,
}

/// <summary>
/// A pointer pressed, moved or released, at <paramref name="Position"/> in the coordinates of the view it reaches.
/// </summary>
internal readonly record struct PointerEvent(
    SKPoint Position,
    PointerButton Button = PointerButton.Left,
    KeyModifiers Modifiers = KeyModifiers.None
)
{
    /// <summary>The same event at another position: the one a parent hands its child, in the child's coordinates.</summary>
    public PointerEvent At(SKPoint position) => this with { Position = position };
}

/// <summary>
/// A wheel turned at <paramref name="Position"/>: <paramref name="DeltaY"/> notches down (negative up),
/// <paramref name="DeltaX"/> to the right.
/// </summary>
internal readonly record struct WheelEvent(
    SKPoint Position,
    float DeltaX,
    float DeltaY,
    KeyModifiers Modifiers = KeyModifiers.None
);

/// <summary>
/// A key pressed, with the modifiers held.
/// </summary>
internal readonly record struct KeyEvent(Key Key, KeyModifiers Modifiers = KeyModifiers.None)
{
    /// <summary>Whether this is <paramref name="key"/> with exactly <paramref name="modifiers"/> held.</summary>
    public bool Is(Key key, KeyModifiers modifiers = KeyModifiers.None) => Key == key && Modifiers == modifiers;
}

/// <summary>
/// The text a shortcut is shown as in a menu: "Ctrl+Shift+N" where Primary is Ctrl, "Cmd+Shift+N" on macOS.
/// </summary>
internal static class Shortcut
{
    /// <summary>The Primary key's name on this platform.</summary>
    public static string PrimaryName { get; } = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";

    /// <summary>The text of <paramref name="key"/> with <paramref name="modifiers"/>.</summary>
    public static string Text(Key key, KeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Primary))
        {
            parts.Add(PrimaryName);
        }
        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }
        parts.Add(key.ToString());
        return string.Join('+', parts);
    }
}
