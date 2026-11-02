using SkiaSharp;
using StoopidChess.Desktop.Views;

namespace StoopidChess.Desktop.Dialogs;

/// <summary>
/// A dialog's fields, a row each: the caption at the left and the field beside it, the captions in one column.
/// </summary>
internal static class FormRows
{
    /// <summary>A row's height.</summary>
    public const float RowHeight = 30.0f;

    /// <summary>The gap between rows.</summary>
    public const float RowGap = 10.0f;

    /// <summary>
    /// Lay out <paramref name="rows"/> from the top of <paramref name="area"/>, each caption
    /// <paramref name="captionWidth"/> wide and each field <paramref name="fieldWidth"/>; returns the y under the last.
    /// </summary>
    public static float Lay(SKRect area, IEnumerable<(LabelView Caption, View Field)> rows, float captionWidth, float fieldWidth)
    {
        ArgumentNullException.ThrowIfNull(rows);
        float y = area.Top;
        foreach ((LabelView caption, View field) in rows)
        {
            caption.SetBounds(SKRect.Create(area.Left, y, captionWidth, RowHeight));
            field.SetBounds(SKRect.Create(area.Left + captionWidth, y, fieldWidth, RowHeight));
            y += RowHeight + RowGap;
        }
        return y;
    }

    /// <summary>
    /// The height of <paramref name="count"/> rows.
    /// </summary>
    public static float Height(int count) => (count * RowHeight) + (Math.Max(0, count - 1) * RowGap);
}
