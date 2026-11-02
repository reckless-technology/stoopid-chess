using System.Globalization;
using SkiaSharp;

namespace StoopidChess.Desktop.Views;

/// <summary>
/// A whole number to type in, held between a minimum and a maximum: a text field of digits with two small plates
/// beside it that step the number down and up by <see cref="Increment"/>, as Up and Down and the wheel do. A number
/// typed outside the range is held to it when the field gives up the keyboard, and whenever it is read.
/// </summary>
internal sealed class NumberField : View
{
    private const float StepWidth = 26.0f;

    private readonly PlateButton down = new("−");
    private readonly PlateButton up = new("+");
    private int value;

    public NumberField(int minimum, int maximum, int increment)
    {
        Minimum = minimum;
        Maximum = maximum;
        Increment = increment;
        Field = new TextField { IsAccepted = char.IsAsciiDigit, MaximumLength = 9 };
        Field.FocusLost += ShowHeldValue;
        down.Clicked += () => Value -= Increment;
        up.Clicked += () => Value += Increment;
        AddChild(Field);
        AddChild(down);
        AddChild(up);
        Value = minimum;
    }

    /// <summary>The least number.</summary>
    public int Minimum { get; }

    /// <summary>The greatest number.</summary>
    public int Maximum { get; }

    /// <summary>A step's size.</summary>
    public int Increment { get; }

    /// <summary>The field the number is typed in.</summary>
    public TextField Field { get; }

    /// <summary>The step down's plate.</summary>
    public PlateButton StepDown => down;

    /// <summary>The step up's plate.</summary>
    public PlateButton StepUp => up;

    /// <summary>
    /// The number, held to the range: the one typed, or the last one there was when the text is not a number.
    /// </summary>
    public int Value
    {
        get => int.TryParse(Field.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int typed)
            ? Math.Clamp(typed, Minimum, Maximum)
            : value;
        set
        {
            this.value = Math.Clamp(value, Minimum, Maximum);
            Field.Text = this.value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The text rewritten as the number held to the range, as the field gives up the keyboard.
    /// </summary>
    private void ShowHeldValue()
    {
        int held = Value;
        Value = held;
    }

    protected override void Layout()
    {
        Field.SetBounds(SKRect.Create(0, 0, Width - (2 * StepWidth) - 4, Height));
        down.SetBounds(SKRect.Create(Width - (2 * StepWidth) - 2, 0, StepWidth, Height));
        up.SetBounds(SKRect.Create(Width - StepWidth, 0, StepWidth, Height));
    }

    public override bool OnKeyDown(KeyEvent e)
    {
        if (e.Is(Key.Up))
        {
            Value += Increment;
            Field.SelectAll();
            return true;
        }
        if (e.Is(Key.Down))
        {
            Value -= Increment;
            Field.SelectAll();
            return true;
        }
        return false;
    }

    public override bool OnWheel(WheelEvent e)
    {
        if (e.DeltaY != 0)
        {
            Value -= Math.Sign(e.DeltaY) * Increment;
        }
        return true;
    }
}
