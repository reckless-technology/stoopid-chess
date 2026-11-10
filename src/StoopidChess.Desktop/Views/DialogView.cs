using SkiaSharp;
using StoopidChess.Desktop.Look;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// A modal dialog drawn inside the window, as the toolkit's <c>view/Dialog.h</c> draws one and Amphitheater's dialogs
/// look: a veil over the whole window that takes every press, and a card in its middle with the title, a rule under
/// it, the dialog's own content, and its buttons along the bottom at the right. Return presses the default button and
/// Escape the cancel button (or closes the dialog); Tab moves the keyboard between the fields. A subclass sizes and
/// lays out its content; <see cref="ShowAsync"/> opens it and completes once it has closed.
/// </summary>
internal abstract class DialogView : View
{
    /// <summary>A button's height.</summary>
    public const float ButtonHeight = 30.0f;

    private const float Padding = 18.0f;
    private const float TitleHeight = 36.0f;
    private const float ButtonGap = 8.0f;
    private const float MinimumButtonWidth = 92.0f;

    private readonly List<PlateButton> _buttons = [];
    private readonly TaskCompletionSource _closed = new();

    protected DialogView(string title) => Title = title;

    /// <summary>The dialog's title.</summary>
    public string Title { get; }

    /// <summary>The card, in the dialog's coordinates (the window's).</summary>
    public SKRect Card { get; private set; }

    /// <summary>The button Return presses; null for none.</summary>
    public PlateButton? DefaultButton { get; private set; }

    /// <summary>The button Escape presses; null to close the dialog.</summary>
    public PlateButton? CancelButton { get; private set; }

    /// <summary>The buttons, left to right.</summary>
    public IReadOnlyList<PlateButton> Buttons => _buttons;

    /// <summary>Whether the dialog has closed.</summary>
    public bool IsClosed => _closed.Task.IsCompleted;

    /// <summary>
    /// Open the dialog over <paramref name="root"/>, the keyboard on its first field; complete once it has closed.
    /// </summary>
    public Task ShowAsync(ViewRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.ShowOverlay(this);
        root.Focus(FirstFocusable(this));
        return _closed.Task;
    }

    /// <summary>
    /// Close the dialog.
    /// </summary>
    public void Close()
    {
        Root?.CloseOverlay(this);
        _closed.TrySetResult();
    }

    /// <summary>The content's size: the card is this and its margins, the title and the buttons.</summary>
    protected abstract SKSize ContentSize { get; }

    /// <summary>Lay out the content in <paramref name="area"/>, in the dialog's coordinates.</summary>
    protected abstract void LayoutContent(SKRect area);

    /// <summary>
    /// A button along the bottom, right of those added before it.
    /// </summary>
    protected PlateButton AddButton(string text, Action clicked, bool isDefault = false, bool isCancel = false)
    {
        var button = new PlateButton(text);
        button.Clicked += clicked;
        _buttons.Add(button);
        AddChild(button);
        if (isDefault)
        {
            DefaultButton = button;
        }
        if (isCancel)
        {
            CancelButton = button;
        }
        return button;
    }

    protected override void Layout()
    {
        SKSize content = ContentSize;
        float buttonsWidth = _buttons.Sum(ButtonWidth) + (ButtonGap * Math.Max(0, _buttons.Count - 1));
        float width =
            Math.Max(Math.Max(content.Width, buttonsWidth), Drawing.Measure(Fonts.Title, Title)) + (2 * Padding);
        float height =
            TitleHeight + Padding + content.Height + (_buttons.Count > 0 ? Padding + ButtonHeight : 0) + Padding;
        Card = SKRect.Create(MathF.Round((Width - width) / 2), MathF.Round((Height - height) / 2), width, height);
        LayoutContent(
            SKRect.Create(Card.Left + Padding, Card.Top + TitleHeight + Padding, width - (2 * Padding), content.Height)
        );
        float x = Card.Right - Padding - buttonsWidth;
        foreach (PlateButton button in _buttons)
        {
            float buttonWidth = ButtonWidth(button);
            button.SetBounds(SKRect.Create(x, Card.Bottom - Padding - ButtonHeight, buttonWidth, ButtonHeight));
            x += buttonWidth + ButtonGap;
        }
    }

    protected override void Draw(SKCanvas canvas)
    {
        using var paint = new SKPaint { Color = Theme.Scrim, IsAntialias = true };
        canvas.DrawRect(LocalBounds, paint);
        paint.Color = Theme.Window;
        canvas.DrawRoundRect(Card, 6, 6, paint);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        paint.Color = Theme.Rule.Brighter(0.1f);
        canvas.DrawRoundRect(Plates.Reduced(Card, 0.5f), 6, 6, paint);
        Drawing.Text(
            canvas,
            Title,
            SKRect.Create(Card.Left + Padding, Card.Top, Card.Width - (2 * Padding), TitleHeight + 6),
            Fonts.Title,
            Theme.Ink
        );
        paint.Style = SKPaintStyle.Fill;
        paint.Color = Theme.Rule;
        canvas.DrawRect(
            SKRect.Create(Card.Left + Padding, Card.Top + TitleHeight + 6, Card.Width - (2 * Padding), 1),
            paint
        );
    }

    public override bool OnPointerDown(PointerEvent e) => true; // Modal: the veil takes every press

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.Is(Key.Return))
        {
            DefaultButton?.Click();
            return true;
        }
        if (e.Is(Key.Escape))
        {
            if (CancelButton is { } cancel)
            {
                cancel.Click();
            }
            else
            {
                Close();
            }
            return true;
        }
        return e.Key != Key.Tab; // Modal: no key but Tab goes past it
    }

    private static float ButtonWidth(PlateButton button) =>
        Math.Max(MinimumButtonWidth, MathF.Ceiling(Drawing.Measure(Fonts.Plate, button.Text)) + 28);

    private static View? FirstFocusable(View view)
    {
        foreach (View child in view.Children)
        {
            if (child.IsVisible && child.IsEnabled && (child.CanFocus ? child : FirstFocusable(child)) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
