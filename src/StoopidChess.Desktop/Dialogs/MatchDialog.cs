using SkiaSharp;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Desktop.Dialogs;

/// <summary>
/// Chooses the two engines of an engine match. The selection is in <see cref="Choice"/> once the dialog closes
/// (indices into the list of names the dialog was given); null means it was cancelled.
/// </summary>
internal sealed class MatchDialog : DialogView
{
    private const float CaptionWidth = 70.0f;
    private const float FieldWidth = 300.0f;

    private readonly LabelView note = new("The match plays under the chosen time control (or by level when untimed).") { IsWrapped = true };
    private readonly (LabelView Caption, View Field)[] rows;

    public MatchDialog(IReadOnlyList<string> engineNames) : base("Engine match")
    {
        ArgumentNullException.ThrowIfNull(engineNames);
        White = new ChoiceButton(engineNames) { SelectedIndex = 0 };
        Black = new ChoiceButton(engineNames) { SelectedIndex = engineNames.Count > 1 ? 1 : 0 };
        rows = [(new LabelView("White:"), White), (new LabelView("Black:"), Black)];
        foreach ((LabelView caption, View field) in rows)
        {
            AddChild(caption);
            AddChild(field);
        }
        AddChild(note);
        AddButton("Start match", () =>
        {
            if (White.SelectedIndex >= 0 && Black.SelectedIndex >= 0)
            {
                Choice = (White.SelectedIndex, Black.SelectedIndex);
            }
            Close();
        }, isDefault: true);
        AddButton("Cancel", Close, isCancel: true);
    }

    /// <summary>White's engine.</summary>
    public ChoiceButton White { get; }

    /// <summary>Black's engine.</summary>
    public ChoiceButton Black { get; }

    /// <summary>The engines chosen, as indices into the names; null when cancelled.</summary>
    public (int White, int Black)? Choice { get; private set; }

    private float NoteHeight => note.HeightFor(CaptionWidth + FieldWidth);

    protected override SKSize ContentSize => new(CaptionWidth + FieldWidth, FormRows.Height(rows.Length) + FormRows.RowGap + NoteHeight);

    protected override void LayoutContent(SKRect area)
    {
        float y = FormRows.Lay(area, rows, CaptionWidth, FieldWidth);
        note.SetBounds(SKRect.Create(area.Left, y, area.Width, NoteHeight));
    }
}
