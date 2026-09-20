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

    private Square dragFrom = Square.None;        // Square of the piece being dragged, or None
    private Point dragPosition;

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
        foreach (Square square in Square.All)
        {
            (Color color, Piece type) = controller.PieceAt(square);
            if (type != Piece.None && square != dragFrom)
            {
                Point origin = SquareOrigin(square);
                context.DrawImage(PieceImages.Get(color, type), new Rect(origin.X, origin.Y, size, size));
            }
        }
        if (!controller.IsEditing && controller.Game.Position.LastMove is Move last && !last.IsNone)
        {
            Point fromOrigin = SquareOrigin(last.From);
            Point toOrigin = SquareOrigin(last.To);
            context.DrawRectangle(null, LastMovePen, new Rect(fromOrigin.X, fromOrigin.Y, size, size));
            context.DrawRectangle(null, LastMovePen, new Rect(toOrigin.X, toOrigin.Y, size, size));
        }
        if (dragFrom != Square.None && controller.PieceAt(dragFrom) is (Color dragColor, Piece dragType) && dragType != Piece.None)
        {
            var rect = new Rect(dragPosition.X - (size / 2.0), dragPosition.Y - (size / 2.0), size, size);
            context.DrawImage(PieceImages.Get(dragColor, dragType), rect);
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
