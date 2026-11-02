using SkiaSharp;
using StoopidChess.Desktop.Views;
using StoopidChess.Play;

namespace StoopidChess.Desktop.Dialogs;

/// <summary>
/// Asks for a base time in minutes, a Fischer increment in seconds and the moves of a session (0 for the whole game).
/// The chosen control is in <see cref="Choice"/> once the dialog closes; null means it was cancelled.
/// </summary>
internal sealed class CustomTimeControlDialog : DialogView
{
    private const float CaptionWidth = 270.0f;
    private const float FieldWidth = 150.0f;

    private readonly (LabelView Caption, View Field)[] rows;

    public CustomTimeControlDialog(TimeControl? current) : base("Custom time control")
    {
        Minutes.Value = (int)Math.Round(current?.Base.TotalMinutes ?? 5);
        Increment.Value = (int)Math.Round(current?.Increment.TotalSeconds ?? 3);
        MovesPerSession.Value = current?.MovesPerSession ?? 0;
        rows =
        [
            (new LabelView("Minutes per side:"), Minutes),
            (new LabelView("Increment (seconds):"), Increment),
            (new LabelView("Moves per session (0 = whole game):"), MovesPerSession),
        ];
        foreach ((LabelView caption, View field) in rows)
        {
            AddChild(caption);
            AddChild(field);
        }
        AddButton("OK", () =>
        {
            int session = MovesPerSession.Value;
            Choice = new TimeControl(TimeSpan.FromMinutes(Minutes.Value), TimeSpan.FromSeconds(Increment.Value),
                session > 0 ? session : null);
            Close();
        }, isDefault: true);
        AddButton("Cancel", Close, isCancel: true);
    }

    /// <summary>The minutes each side has.</summary>
    public NumberField Minutes { get; } = new(1, 180, 1);

    /// <summary>The seconds added after each move.</summary>
    public NumberField Increment { get; } = new(0, 120, 1);

    /// <summary>The moves of a session; 0 for the whole game.</summary>
    public NumberField MovesPerSession { get; } = new(0, 100, 5);

    /// <summary>The clock chosen; null when cancelled.</summary>
    public TimeControl? Choice { get; private set; }

    protected override SKSize ContentSize => new(CaptionWidth + FieldWidth, FormRows.Height(rows.Length));

    protected override void LayoutContent(SKRect area) => FormRows.Lay(area, rows, CaptionWidth, FieldWidth);
}
