using Avalonia.Controls;
using Avalonia.Layout;
using StoopidChess.Core;
using Color = StoopidChess.Core.Color;   // The chess color, not Avalonia.Media.Color

namespace StoopidChess.Desktop;

/// <summary>
/// Modal dialog asking which piece a pawn should promote to. The choice is in <see cref="Choice"/>
/// once the dialog closes; closing it any other way promotes to a queen.
/// </summary>
internal sealed class PromotionDialog : Window
{
    private const int ImageSize = 64;

    public PromotionDialog(Color color)
    {
        Title = "Promote pawn to";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Avalonia.Thickness(12) };
        foreach (Piece type in PieceExtensions.PromotionTypes)
        {
            var button = new Button
            {
                Content = new Image { Source = PieceImages.Get(color, type), Width = ImageSize, Height = ImageSize },
            };
            button.Click += (_, _) =>
            {
                Choice = type;
                Close();
            };
            panel.Children.Add(button);
        }
        Content = panel;
    }

    public Piece Choice { get; private set; } = Piece.Queen;
}
