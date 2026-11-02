using SkiaSharp;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// A window with no window system: a <see cref="ViewRoot"/> of a given size, drawn to a raster bitmap and driven by
/// the events the SDL host would hand it, synthesized - the way every GUI test reaches the views.
/// </summary>
internal sealed class ViewHarness : IDisposable
{
    public ViewHarness(View content, int width = 860, int height = 660)
    {
        Bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Root.Resize(width, height);
        Root.Content = content;
    }

    public ViewRoot Root { get; } = new();

    public SKBitmap Bitmap { get; }

    /// <summary>
    /// The window drawn afresh; returns the bitmap.
    /// </summary>
    public SKBitmap Render()
    {
        using var canvas = new SKCanvas(Bitmap);
        Root.Render(canvas);
        canvas.Flush();
        return Bitmap;
    }

    /// <summary>The colour at a point, once the window is drawn.</summary>
    public SKColor ColorAt(float x, float y) => Render().GetPixel((int)x, (int)y);

    public void Press(SKPoint at, PointerButton button = PointerButton.Left, KeyModifiers modifiers = KeyModifiers.None) =>
        Root.PointerDown(new PointerEvent(at, button, modifiers));

    public void Move(SKPoint to) => Root.PointerMove(new PointerEvent(to));

    public void Release(SKPoint at, PointerButton button = PointerButton.Left) => Root.PointerUp(new PointerEvent(at, button));

    /// <summary>A press and release at one point, the window drawn between them.</summary>
    public void Click(SKPoint at, PointerButton button = PointerButton.Left, KeyModifiers modifiers = KeyModifiers.None)
    {
        Move(at);
        Press(at, button, modifiers);
        Render();
        Release(at, button);
    }

    /// <summary>A click at the centre of <paramref name="view"/>.</summary>
    public void Click(View view) => Click(Centre(view));

    /// <summary>A press at one point, a move to another and the release there, the window drawn mid-drag.</summary>
    public void Drag(SKPoint from, SKPoint to, PointerButton button = PointerButton.Left)
    {
        Move(from);
        Press(from, button);
        Move(new SKPoint((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        Move(to);
        Render();
        Release(to, button);
    }

    public void Key(Key key, KeyModifiers modifiers = KeyModifiers.None) => Root.KeyDown(new KeyEvent(key, modifiers));

    public void Type(string text) => Root.TextInput(text);

    public void Wheel(SKPoint at, float down, float across = 0, KeyModifiers modifiers = KeyModifiers.None) =>
        Root.Wheel(new WheelEvent(at, across, down, modifiers));

    /// <summary>The centre of <paramref name="view"/>, in the window's coordinates.</summary>
    public static SKPoint Centre(View view) => view.ToRoot(new SKPoint(view.Width / 2, view.Height / 2));

    public void Dispose() => Bitmap.Dispose();
}
