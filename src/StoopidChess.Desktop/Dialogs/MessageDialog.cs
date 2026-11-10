using SkiaSharp;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Desktop.Dialogs;

/// <summary>
/// A message and an OK button.
/// </summary>
internal sealed class MessageDialog : DialogView
{
    private const float MessageWidth = 440.0f;

    public MessageDialog(string title, string message)
        : base(title)
    {
        Message = new LabelView(message) { IsWrapped = true };
        AddChild(Message);
        AddButton("OK", Close, isDefault: true, isCancel: true);
    }

    /// <summary>The message.</summary>
    public LabelView Message { get; }

    /// <summary>
    /// Show a message over <paramref name="root"/>; complete once it is dismissed.
    /// </summary>
    public static Task ShowAsync(ViewRoot root, string title, string message) =>
        new MessageDialog(title, message).ShowAsync(root);

    protected override SKSize ContentSize => new(MessageWidth, Message.HeightFor(MessageWidth));

    protected override void LayoutContent(SKRect area) => Message.SetBounds(area);
}
