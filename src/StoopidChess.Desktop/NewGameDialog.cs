using Avalonia.Controls;
using Avalonia.Layout;
using StoopidChess.Play;

namespace StoopidChess.Desktop;

/// <summary>
/// Modal dialog shown before a new game starts, choosing the clock it is played under. When the
/// dialog closes, <see cref="Confirmed"/> says whether to go ahead, and then either
/// <see cref="CustomRequested"/> is set or <see cref="Choice"/> holds the clock.
/// </summary>
internal sealed class NewGameDialog : Window
{
    private static readonly (string Label, TimeControl? Clock)[] Choices =
    [
        ("Standard: 40 moves in 40 minutes", TimeControl.Standard(40, 40)),
        ("Standard: 40 moves in 5 minutes", TimeControl.Standard(40, 5)),
        ("Standard: 40 moves in 1 minute", TimeControl.Standard(40, 1)),
        ("Incremental: 5 minutes + 5 seconds per move", TimeControl.OfMinutes(5, 5)),
        ("Custom...", null),
    ];

    private const int DefaultIndex = 1;   // 40 moves in 5 minutes
    private const int CustomIndex = 4;

    private readonly List<RadioButton> buttons = [];

    public NewGameDialog(TimeControl? current)
    {
        Title = "New game";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        int selected = DefaultIndex;
        for (int i = 0; i < Choices.Length - 1; i++)
        {
            if (Equals(Choices[i].Clock, current))
            {
                selected = i;
            }
        }
        if (current is not null && !Choices.Any(c => Equals(c.Clock, current)))
        {
            selected = CustomIndex;   // The current clock is one only the custom dialog can express
        }

        var options = new StackPanel { Spacing = 6 };
        options.Children.Add(new TextBlock { Text = "Clock for the new game:", FontWeight = Avalonia.Media.FontWeight.Bold });
        for (int i = 0; i < Choices.Length; i++)
        {
            var button = new RadioButton { Content = Choices[i].Label, GroupName = "clock", IsChecked = i == selected };
            buttons.Add(button);
            options.Children.Add(button);
        }

        var okButton = new Button { Content = "Start game", IsDefault = true, MinWidth = 100 };
        okButton.Click += (_, _) =>
        {
            int index = buttons.FindIndex(b => b.IsChecked == true);
            Confirmed = index >= 0;
            if (index == CustomIndex)
            {
                CustomRequested = true;
            }
            else if (index >= 0)
            {
                Choice = Choices[index].Clock;
            }
            Close();
        };
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        cancelButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                options,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { okButton, cancelButton },
                },
            },
        };
    }

    public bool Confirmed { get; private set; }

    public bool CustomRequested { get; private set; }

    public TimeControl? Choice { get; private set; }
}
