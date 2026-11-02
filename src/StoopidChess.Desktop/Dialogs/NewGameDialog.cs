using SkiaSharp;
using StoopidChess.Desktop.Look;
using StoopidChess.Desktop.Views;
using StoopidChess.Play;

namespace StoopidChess.Desktop.Dialogs;

/// <summary>
/// Shown before a new game starts, choosing the clock it is played under. When the dialog closes,
/// <see cref="Confirmed"/> says whether to go ahead, and then either <see cref="CustomRequested"/> is set or
/// <see cref="Choice"/> holds the clock.
/// </summary>
internal sealed class NewGameDialog : DialogView
{
    private static readonly (string Label, TimeControl? Clock)[] Clocks =
    [
        ("Standard: 40 moves in 40 minutes", TimeControl.Standard(40, 40)),
        ("Standard: 40 moves in 5 minutes", TimeControl.Standard(40, 5)),
        ("Standard: 40 moves in 1 minute", TimeControl.Standard(40, 1)),
        ("Incremental: 5 minutes + 5 seconds per move", TimeControl.OfMinutes(5, 5)),
        ("Custom...", null),
    ];

    private const int DefaultIndex = 1;   // 40 moves in 5 minutes
    private const int CustomIndex = 4;
    private const float CaptionHeight = 26.0f;

    private readonly LabelView caption = new("Clock for the new game:") { Font = Fonts.BodyBold };

    public NewGameDialog(TimeControl? current) : base("New game")
    {
        int selected = DefaultIndex;
        for (int i = 0; i < Clocks.Length - 1; i++)
        {
            if (Equals(Clocks[i].Clock, current))
            {
                selected = i;
            }
        }
        if (current is not null && !Clocks.Any(c => Equals(c.Clock, current)))
        {
            selected = CustomIndex;   // The current clock is one only the custom dialog can express
        }
        Choices = new ChoiceList([.. Clocks.Select(c => c.Label)]) { SelectedIndex = selected };
        AddChild(caption);
        AddChild(Choices);
        AddButton("Start game", () =>
        {
            int index = Choices.SelectedIndex;
            Confirmed = index >= 0;
            if (index == CustomIndex)
            {
                CustomRequested = true;
            }
            else if (index >= 0)
            {
                Choice = Clocks[index].Clock;
            }
            Close();
        }, isDefault: true);
        AddButton("Cancel", Close, isCancel: true);
    }

    /// <summary>The clocks to choose from.</summary>
    public ChoiceList Choices { get; }

    /// <summary>Whether to start the game.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>Whether the clock is to be set in the custom time control dialog.</summary>
    public bool CustomRequested { get; private set; }

    /// <summary>The clock chosen.</summary>
    public TimeControl? Choice { get; private set; }

    protected override SKSize ContentSize => new(380, CaptionHeight + 6 + Choices.PreferredHeight);

    protected override void LayoutContent(SKRect area)
    {
        caption.SetBounds(SKRect.Create(area.Left, area.Top, area.Width, CaptionHeight));
        Choices.SetBounds(SKRect.Create(area.Left, area.Top + CaptionHeight + 6, area.Width, Choices.PreferredHeight));
    }
}
