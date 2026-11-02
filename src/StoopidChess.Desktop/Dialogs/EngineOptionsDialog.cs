using System.Globalization;
using SkiaSharp;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Desktop.Dialogs;

/// <summary>
/// The common UCI options of a registered engine: Hash (MB) and Threads, zero meaning the engine's own default. The
/// result is in <see cref="Saved"/> once the dialog closes; null means it was cancelled.
/// </summary>
internal sealed class EngineOptionsDialog : DialogView
{
    private const float CaptionWidth = 150.0f;
    private const float FieldWidth = 170.0f;

    private readonly LabelView note = new("Zero leaves the engine's own default. Applied when the engine starts.") { IsWrapped = true };
    private readonly (LabelView Caption, View Field)[] rows;

    public EngineOptionsDialog(string engineName, IReadOnlyDictionary<string, string> current) : base($"{engineName} options")
    {
        ArgumentNullException.ThrowIfNull(current);
        Hash.Value = current.TryGetValue("Hash", out string? hashValue) && int.TryParse(hashValue, CultureInfo.InvariantCulture, out int h) ? h : 0;
        Threads.Value = current.TryGetValue("Threads", out string? threadsValue) && int.TryParse(threadsValue, CultureInfo.InvariantCulture, out int t) ? t : 0;
        rows = [(new LabelView("Hash (MB):"), Hash), (new LabelView("Threads:"), Threads)];
        AddChild(note);
        foreach ((LabelView caption, View field) in rows)
        {
            AddChild(caption);
            AddChild(field);
        }
        AddButton("OK", () =>
        {
            var options = new Dictionary<string, string>();
            foreach ((string key, string value) in current)
            {
                if (key is not ("Hash" or "Threads"))
                {
                    options[key] = value;   // Keep any options this dialog does not edit
                }
            }
            if (Hash.Value > 0)
            {
                options["Hash"] = Hash.Value.ToString(CultureInfo.InvariantCulture);
            }
            if (Threads.Value > 0)
            {
                options["Threads"] = Threads.Value.ToString(CultureInfo.InvariantCulture);
            }
            Saved = options;
            Close();
        }, isDefault: true);
        AddButton("Cancel", Close, isCancel: true);
    }

    /// <summary>The hash table's size in MB; 0 for the engine's default.</summary>
    public NumberField Hash { get; } = new(0, 4096, 16);

    /// <summary>The threads; 0 for the engine's default.</summary>
    public NumberField Threads { get; } = new(0, 64, 1);

    /// <summary>The options to save; null when cancelled.</summary>
    public Dictionary<string, string>? Saved { get; private set; }

    private float NoteHeight => note.HeightFor(CaptionWidth + FieldWidth);

    protected override SKSize ContentSize => new(CaptionWidth + FieldWidth, NoteHeight + FormRows.RowGap + FormRows.Height(rows.Length));

    protected override void LayoutContent(SKRect area)
    {
        note.SetBounds(SKRect.Create(area.Left, area.Top, area.Width, NoteHeight));
        FormRows.Lay(new SKRect(area.Left, area.Top + NoteHeight + FormRows.RowGap, area.Right, area.Bottom), rows, CaptionWidth, FieldWidth);
    }
}
