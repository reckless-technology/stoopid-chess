using StoopidChess.Core;
using StoopidChess.Desktop;
using StoopidChess.Desktop.Dialogs;
using StoopidChess.Desktop.Views;
using StoopidChess.Play;

namespace StoopidChess.Tests.Desktop;

/// <summary>
/// The GUI's dialogs, each open over a window: what it starts with, and what it hands back when its buttons are
/// pressed - the choices the main view acts on - and the Analysis window's lines.
/// </summary>
public sealed class DialogTests : IDisposable
{
    private readonly ViewHarness window = new(new TestPanel());

    public void Dispose() => window.Dispose();

    private T Open<T>(T dialog) where T : DialogView
    {
        _ = dialog.ShowAsync(window.Root);
        window.Render();
        return dialog;
    }

    private void Press(DialogView dialog, string text) => window.Click(dialog.Buttons.Single(button => button.Text == text));

    [Fact]
    public void ThePromotionDialogReturnsThePiecePressedOrAQueen()
    {
        PromotionDialog dialog = Open(new PromotionDialog(Color.Black));
        Assert.Equal(Piece.Queen, dialog.Choice);
        window.Click(dialog.Pieces[3]);
        Assert.Equal(Piece.Knight, dialog.Choice);

        PromotionDialog closed = Open(new PromotionDialog(Color.White));
        window.Key(Key.Escape);
        Assert.True(closed.IsClosed);
        Assert.Equal(Piece.Queen, closed.Choice);
    }

    [Fact]
    public async Task AMessageDialogShowsItsMessageAndClosesOnOk()
    {
        Task shown = MessageDialog.ShowAsync(window.Root, "Engine", "The engine could not be started.");
        MessageDialog dialog = Assert.IsType<MessageDialog>(window.Root.Overlays.Single());
        window.Render();
        Assert.Equal("Engine", dialog.Title);
        Assert.Equal("The engine could not be started.", dialog.Message.Text);
        Press(dialog, "OK");
        await shown;
        Assert.Empty(window.Root.Overlays);
    }

    [Fact]
    public void TheAnalysisViewKeepsItsLatestLinesAndClears()
    {
        var analysis = new AnalysisView();
        using var analysisWindow = new ViewHarness(analysis, 720, 400);
        for (int i = 0; i < 450; i++)
        {
            analysis.Append($"line {i}");
        }
        IReadOnlyList<string> lines = analysis.Output.Lines;
        Assert.Equal(AnalysisView.MaximumLines, lines.Count);
        Assert.Equal("line 50", lines[0]);
        Assert.Equal("line 449", lines[^1]);
        Assert.Equal(analysis.Output.MaximumScrollY, analysis.Output.ScrollY);
        analysisWindow.Render();
        analysisWindow.Click(analysis.ClearButton);
        Assert.Empty(analysis.Output.Lines);
    }

    [Fact]
    public void TheVersionIsTheBuildsThreeNumbers()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", Program.Version);
    }
}
