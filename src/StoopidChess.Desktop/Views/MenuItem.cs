namespace StoopidChess.Desktop.Views;

/// <summary>
/// One row of a menu: an action, a submenu (its <see cref="Items"/>), or a separator. A menu bar's titles are menu
/// items too, whose items are the drop-down's rows; a title with none acts when pressed (Help).
/// </summary>
internal sealed class MenuItem(string text, Action? chosen = null)
{
    /// <summary>The row's text.</summary>
    public string Text { get; } = text;

    /// <summary>What choosing the row does; null for a submenu or a separator.</summary>
    public Action? Chosen { get; } = chosen;

    /// <summary>The shortcut shown at the row's right, as <see cref="Views.Shortcut.Text"/> writes it.</summary>
    public string? Shortcut { get; init; }

    /// <summary>The submenu's rows; empty for a row that acts.</summary>
    public List<MenuItem> Items { get; } = [];

    /// <summary>Whether the row carries a tick.</summary>
    public bool IsChecked { get; set; }

    /// <summary>Whether the row can be chosen; a disabled row is drawn faint.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether the row is a separator's rule.</summary>
    public bool IsSeparator { get; private init; }

    /// <summary>Whether the row opens a submenu.</summary>
    public bool HasSubmenu => Items.Count > 0;

    /// <summary>A separator's rule between two groups of rows.</summary>
    public static MenuItem Separator() => new("") { IsSeparator = true };
}
