using SkiaSharp;
using StoopidChess.Core;
using StoopidChess.Desktop.Look;
using StoopidChess.Desktop.Views;
using StoopidChess.Play;

namespace StoopidChess.Desktop;

/// <summary>
/// The chess board: drawing and pointer input, in the series' colours (Amphitheater's board): the light and dark
/// squares in a near-black frame, the last move's squares lit in the accent, the checked king's square glowing red, the
/// coordinates in the corners of the edge squares in the other square colour, and, while a piece is dragged, its legal
/// destinations - a dot on an empty square, a ring round a capture. All game and engine logic lives in
/// <see cref="GameController"/>; the main view owns the controller and acts on the board's events.
/// </summary>
internal sealed class BoardView : View
{
    /// <summary>The frame's width round the squares.</summary>
    public const float Edge = 2.0f;

    private static readonly SKColor HintColor = new(0x14, 0x12, 0x10, 0x60);
    private static readonly SKColor CaptureHintColor = new(0x14, 0x12, 0x10, 0x78);

    private Square dragFrom = Square.None;        // Square of the piece being dragged, or None
    private SKPoint dragPosition;
    private Bitboard dragTargets;                 // Legal destinations of the dragged piece

    /// <summary>The game shown.</summary>
    public GameController? Controller { get; set; }

    /// <summary>A piece was dropped from one square onto another; legality is the main view's business.</summary>
    public event Action<Square, Square>? MoveDropped;

    /// <summary>A right click asked to edit a square (the controller allows it right now), at a point in the window.</summary>
    public event Action<Square, SKPoint>? EditRequested;

    /// <summary>A left click while the edited position is not legal yet.</summary>
    public event Action? EditingIncomplete;

    /// <summary>The square being dragged from; None when no piece is.</summary>
    public Square DragFrom => dragFrom;

    /// <summary>The legal destinations of the piece being dragged.</summary>
    public Bitboard DragTargets => dragTargets;

    /// <summary>A square's side, in pixels.</summary>
    public float SquareSize => MathF.Floor((Math.Min(Width, Height) - (2 * Edge)) / 8);

    private bool IsFlipped => Controller?.IsBoardFlipped ?? false;

    /// <summary>
    /// The top left of <paramref name="square"/>, in the board's coordinates, as the board is oriented.
    /// </summary>
    public SKPoint SquareOrigin(Square square)
    {
        int x = IsFlipped ? 7 - square.File : square.File;
        int y = IsFlipped ? square.Rank : 7 - square.Rank;
        return new SKPoint(Edge + (x * SquareSize), Edge + (y * SquareSize));
    }

    /// <summary>
    /// The centre of <paramref name="square"/>, in the board's coordinates.
    /// </summary>
    public SKPoint SquareCentre(Square square)
    {
        SKPoint origin = SquareOrigin(square);
        return new SKPoint(origin.X + (SquareSize / 2), origin.Y + (SquareSize / 2));
    }

    /// <summary>
    /// The square under a point in the board's coordinates, or <see cref="Square.None"/> for a point beyond
    /// the squares (the frame, or off the board: a piece dragged off the board is not dropped anywhere).
    /// </summary>
    public Square SquareAt(SKPoint location)
    {
        float size = SquareSize;
        int x = (int)MathF.Floor((location.X - Edge) / size);
        int y = (int)MathF.Floor((location.Y - Edge) / size);
        if (x is < 0 or > 7 || y is < 0 or > 7)
        {
            return Square.None;
        }
        return Square.At(IsFlipped ? 7 - x : x, IsFlipped ? y : 7 - y);
    }

    protected override void Draw(SKCanvas canvas)
    {
        float size = SquareSize;
        if (Controller is not { } controller || size <= 0)
        {
            return;
        }
        using var paint = new SKPaint { IsAntialias = true };
        paint.Color = Theme.BoardEdge;
        canvas.DrawRect(SKRect.Create((8 * size) + (2 * Edge), (8 * size) + (2 * Edge)), paint);
        Move? last = !controller.IsEditing && controller.Game.Position.LastMove is { IsNone: false } move ? move : null;
        foreach (Square square in Square.All)
        {
            bool isLastMove = last is { } lastMove && (lastMove.From == square || lastMove.To == square);
            paint.Color = isLastMove
                ? (square.IsLight ? Theme.LastMoveLight : Theme.LastMoveDark)
                : (square.IsLight ? Theme.LightSquare : Theme.DarkSquare);
            canvas.DrawRect(SquareRect(square), paint);
        }
        if (!controller.IsEditing && controller.Game.Position.IsInCheck)
        {
            DrawCheck(canvas, SquareRect(controller.Game.Position.KingSquare(controller.Game.SideToMove)));
        }
        foreach (Square square in Square.All)
        {
            (Color color, Piece type) = controller.PieceAt(square);
            if (type != Piece.None && square != dragFrom)
            {
                Drawing.Image(canvas, PieceImages.Get(color, type), SquareRect(square));
            }
        }
        DrawCoordinates(canvas, size);
        if (dragFrom == Square.None)
        {
            return;
        }
        // Where the dragged piece may go: a dot on an empty square, a ring round a capture
        foreach (Square target in dragTargets)
        {
            SKPoint centre = SquareCentre(target);
            if (controller.PieceAt(target).Type == Piece.None)
            {
                paint.Style = SKPaintStyle.Fill;
                paint.Color = HintColor;
                canvas.DrawCircle(centre, size / 7, paint);
            }
            else
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = Math.Max(3, size / 16);
                paint.Color = CaptureHintColor;
                canvas.DrawCircle(centre, (size / 2) - (paint.StrokeWidth / 2) - 1, paint);
            }
        }
        if (controller.PieceAt(dragFrom) is (Color dragColor, Piece dragType) && dragType != Piece.None)
        {
            SKRect rect = SKRect.Create(dragPosition.X - (size / 2), dragPosition.Y - (size / 2), size, size);
            Drawing.Image(canvas, PieceImages.Get(dragColor, dragType), rect);
        }
    }

    private SKRect SquareRect(Square square)
    {
        SKPoint origin = SquareOrigin(square);
        return SKRect.Create(origin.X, origin.Y, SquareSize, SquareSize);
    }

    /// <summary>
    /// The checked king's square: a red glow, strongest at the centre, fading to the square's edge.
    /// </summary>
    private static void DrawCheck(SKCanvas canvas, SKRect square)
    {
        using var paint = new SKPaint { IsAntialias = true };
        paint.Shader = SKShader.CreateRadialGradient(new SKPoint(square.MidX, square.MidY), square.Width * 0.62f,
            [Theme.Minus.WithOpacity(0.95f), Theme.Minus.WithOpacity(0.55f), Theme.Minus.WithOpacity(0)],
            [0, 0.55f, 1], SKShaderTileMode.Clamp);
        canvas.DrawRect(square, paint);
    }

    /// <summary>
    /// File letters along the bottom edge and rank digits along the left, in the corner of each edge square, each in
    /// the other square colour, following the board's orientation.
    /// </summary>
    private void DrawCoordinates(SKCanvas canvas, float size)
    {
        using SKFont font = Fonts.SerifBoldAt(Math.Max(10, size / 5));
        float lineHeight = Drawing.LineHeight(font);
        for (int x = 0; x < 8; x++)
        {
            int file = IsFlipped ? 7 - x : x;
            Square square = Square.At(file, IsFlipped ? 7 : 0);
            SKRect rect = SquareRect(square);
            Drawing.Text(canvas, ((char)('a' + file)).ToString(), new SKRect(rect.Left, rect.Bottom - lineHeight, rect.Right - 3, rect.Bottom),
                font, square.IsLight ? Theme.DarkSquare : Theme.LightSquare, TextAlignment.Right);
        }
        for (int y = 0; y < 8; y++)
        {
            int rank = IsFlipped ? y : 7 - y;
            Square square = Square.At(IsFlipped ? 7 : 0, rank);
            SKRect rect = SquareRect(square);
            Drawing.Text(canvas, ((char)('1' + rank)).ToString(), new SKRect(rect.Left + 3, rect.Top, rect.Right, rect.Top + lineHeight),
                font, square.IsLight ? Theme.DarkSquare : Theme.LightSquare);
        }
    }

    /// <summary>
    /// A press: with the left button on one of the human's own pieces, on the human's turn, it starts a drag; the piece's
    /// legal destinations are worked out now so they can be drawn as hints while it is dragged. (The right button asks
    /// to edit the square instead, at release.)
    /// </summary>
    public override bool OnPointerDown(PointerEvent e)
    {
        if (Controller is not { } controller)
        {
            return false;
        }
        if (e.Button == PointerButton.Right)
        {
            return true; // The edit menu opens on the release
        }
        if (e.Button != PointerButton.Left)
        {
            return false;
        }
        if (controller.IsEditing)
        {
            EditingIncomplete?.Invoke();
            return false;
        }
        Square square = SquareAt(e.Position);
        if (square == Square.None || !controller.IsHumanTurn || !controller.Game.Position.Pieces(controller.HumanColor).Contains(square))
        {
            return false;
        }
        dragFrom = square;
        dragPosition = e.Position;
        dragTargets = Bitboard.Empty;
        foreach (Move move in controller.Game.Position.GenerateLegalMoves())
        {
            if (move.From == square)
            {
                dragTargets |= Bitboard.Of(move.To);
            }
        }
        Invalidate();
        return true;
    }

    public override void OnPointerMove(PointerEvent e)
    {
        if (dragFrom != Square.None)
        {
            dragPosition = e.Position;
            Invalidate();
        }
    }

    /// <summary>
    /// A release. Ending a drag raises <see cref="MoveDropped"/> with the two squares, unless it was let go on the square
    /// it came from or off the board, which cancels it. This view never judges legality or plays the move: the main
    /// view hears the event and asks the controller. A right-button release asks to edit the square.
    /// </summary>
    public override void OnPointerUp(PointerEvent e)
    {
        if (e.Button == PointerButton.Left && dragFrom != Square.None)
        {
            Square from = dragFrom;
            Square to = SquareAt(e.Position);
            dragFrom = Square.None;
            dragTargets = Bitboard.Empty;
            Invalidate();
            if (to != Square.None && to != from)     // Dropped off the board: the move is cancelled
            {
                MoveDropped?.Invoke(from, to);
            }
        }
        else if (e.Button == PointerButton.Right && Controller is { } controller
                 && !controller.IsThinking && !controller.IsEngineTurn)
        {
            Square square = SquareAt(e.Position);
            if (square != Square.None)
            {
                EditRequested?.Invoke(square, ToRoot(e.Position));
            }
        }
    }
}
