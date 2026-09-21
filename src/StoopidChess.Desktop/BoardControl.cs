using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using StoopidChess.Core;
using StoopidChess.Play;
using Color = StoopidChess.Core.Color;   // The chess color, not Avalonia.Media.Color

namespace StoopidChess.Desktop;

/// <summary>
/// The chess board: drawing and pointer input. All game and engine logic lives in
/// <see cref="GameController"/>; the window owns the controller and wires the events.
/// </summary>
internal sealed class BoardControl : Control
{
    private static readonly Pen LastMovePen = new(Brushes.DarkGoldenrod, 2.5);
    private static readonly IBrush CheckBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(110, 220, 50, 50));
    private static readonly IBrush HintBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(90, 30, 30, 30));
    private static readonly Pen CaptureHintPen = new(new SolidColorBrush(Avalonia.Media.Color.FromArgb(110, 30, 30, 30)), 3);
    private static readonly IBrush LightLabelBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(235, 80, 80, 80));
    private static readonly IBrush DarkLabelBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(235, 255, 255, 240));
    private static readonly Typeface LabelTypeface = new(FontFamily.Default, weight: FontWeight.SemiBold);

    private Square dragFrom = Square.None;        // Square of the piece being dragged, or None
    private Point dragPosition;
    private Bitboard dragTargets;                 // Legal destinations of the dragged piece

    public GameController? Controller { get; set; }

    /// <summary>A piece was dropped from one square onto another; legality is the window's business.</summary>
    public event Action<Square, Square>? MoveDropped;

    /// <summary>A right click asked to edit a square (the controller allows it right now).</summary>
    public event Action<Square>? EditRequested;

    /// <summary>A left click while the edited position is not legal yet.</summary>
    public event Action? EditingIncomplete;

    private int SquareSize => (int)(Math.Min(Bounds.Width, Bounds.Height) / 8);

    private Point SquareOrigin(Square square)
    {
        bool flipped = Controller?.IsBoardFlipped ?? false;
        int x = flipped ? 7 - square.File : square.File;
        int y = flipped ? square.Rank : 7 - square.Rank;
        return new Point(x * SquareSize, y * SquareSize);
    }

    private Square SquareAt(Point location)
    {
        int size = SquareSize;
        bool flipped = Controller?.IsBoardFlipped ?? false;
        int x = Math.Clamp((int)(location.X / size), 0, 7);
        int y = Math.Clamp((int)(location.Y / size), 0, 7);
        int file = flipped ? 7 - x : x;
        int rank = flipped ? y : 7 - y;
        return Square.At(file, rank);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        int size = SquareSize;
        if (Controller is not { } controller || size <= 0)
        {
            return;
        }
        foreach (Square square in Square.All)
        {
            Point origin = SquareOrigin(square);
            context.FillRectangle(square.IsLight ? Brushes.Ivory : Brushes.Gray, new Rect(origin.X, origin.Y, size, size));
        }
        if (!controller.IsEditing && controller.Game.Position.IsInCheck)
        {
            Point origin = SquareOrigin(controller.Game.Position.KingSquare(controller.Game.SideToMove));
            context.FillRectangle(CheckBrush, new Rect(origin.X, origin.Y, size, size));
        }
        foreach (Square square in Square.All)
        {
            (Color color, Piece type) = controller.PieceAt(square);
            if (type != Piece.None && square != dragFrom)
            {
                Point origin = SquareOrigin(square);
                context.DrawImage(PieceImages.Get(color, type), new Rect(origin.X, origin.Y, size, size));
            }
        }
        DrawCoordinates(context, size);
        if (!controller.IsEditing && controller.Game.Position.LastMove is Move last && !last.IsNone)
        {
            Point fromOrigin = SquareOrigin(last.From);
            Point toOrigin = SquareOrigin(last.To);
            context.DrawRectangle(null, LastMovePen, new Rect(fromOrigin.X, fromOrigin.Y, size, size));
            context.DrawRectangle(null, LastMovePen, new Rect(toOrigin.X, toOrigin.Y, size, size));
        }
        if (dragFrom != Square.None)
        {
            // Where the dragged piece may go: a dot on an empty square, a ring around a capture
            foreach (Square target in dragTargets)
            {
                Point origin = SquareOrigin(target);
                var centre = new Point(origin.X + (size / 2.0), origin.Y + (size / 2.0));
                if (controller.PieceAt(target).Type == Piece.None)
                {
                    context.DrawEllipse(HintBrush, null, centre, size / 7.0, size / 7.0);
                }
                else
                {
                    context.DrawEllipse(null, CaptureHintPen, centre, (size / 2.0) - 3, (size / 2.0) - 3);
                }
            }
        }
        if (dragFrom != Square.None && controller.PieceAt(dragFrom) is (Color dragColor, Piece dragType) && dragType != Piece.None)
        {
            var rect = new Rect(dragPosition.X - (size / 2.0), dragPosition.Y - (size / 2.0), size, size);
            context.DrawImage(PieceImages.Get(dragColor, dragType), rect);
        }
    }

    /// <summary>
    /// File letters along the bottom edge and rank digits along the left, in the corner of each
    /// border square, following the board's orientation.
    /// </summary>
    private void DrawCoordinates(DrawingContext context, int size)
    {
        bool flipped = Controller?.IsBoardFlipped ?? false;
        double fontSize = Math.Max(10, size / 4.5);
        for (int x = 0; x < 8; x++)
        {
            int file = flipped ? 7 - x : x;
            Square square = Square.At(file, flipped ? 7 : 0);
            var label = new FormattedText(((char)('a' + file)).ToString(), System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, LabelTypeface, fontSize, square.IsLight ? LightLabelBrush : DarkLabelBrush);
            context.DrawText(label, new Point((x * size) + size - label.Width - 2, (8 * size) - label.Height - 1));
        }
        for (int y = 0; y < 8; y++)
        {
            int rank = flipped ? y : 7 - y;
            Square square = Square.At(flipped ? 7 : 0, rank);
            var label = new FormattedText(((char)('1' + rank)).ToString(), System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, LabelTypeface, fontSize, square.IsLight ? LightLabelBrush : DarkLabelBrush);
            context.DrawText(label, new Point(2, y * size));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Controller is not { } controller || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        if (controller.IsEditing)
        {
            EditingIncomplete?.Invoke();
            return;
        }
        if (!controller.IsHumanTurn)
        {
            return;
        }
        Square square = SquareAt(e.GetPosition(this));
        if (controller.PieceAt(square).Color == controller.HumanColor)
        {
            dragFrom = square;
            dragPosition = e.GetPosition(this);
            dragTargets = Bitboard.Empty;
            foreach (Move move in controller.Game.Position.GenerateLegalMoves())
            {
                if (move.From == square)
                {
                    dragTargets |= Bitboard.Of(move.To);
                }
            }
            e.Pointer.Capture(this);
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragFrom != Square.None)
        {
            dragPosition = e.GetPosition(this);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left && dragFrom != Square.None)
        {
            Square from = dragFrom;
            Square to = SquareAt(e.GetPosition(this));
            dragFrom = Square.None;
            dragTargets = Bitboard.Empty;
            InvalidateVisual();
            if (to != from)
            {
                MoveDropped?.Invoke(from, to);
            }
        }
        else if (e.InitialPressMouseButton == MouseButton.Right && Controller is { } controller
                 && !controller.IsThinking && !(controller.IsEngineTurn && controller.HumanColor != Color.None))
        {
            EditRequested?.Invoke(SquareAt(e.GetPosition(this)));
        }
    }
}
