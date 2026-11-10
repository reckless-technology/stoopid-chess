using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

/// <summary>
/// The rook file terms pinned the same way as the pawn structure: the white-minus-black rook file
/// bonus of each position must be exactly the bonus of the pattern it shows.
/// </summary>
public class RookFileTests
{
    private static int WhiteMinusBlack(string fen)
    {
        var position = Position.FromFen(fen);
        return Evaluator.RookFiles(position, Color.White) - Evaluator.RookFiles(position, Color.Black);
    }

    [Fact]
    public void ARookOnAnOpenFileIsCounted()
    {
        // The e-file holds no pawn of either color, so white's rook there is on an open file
        Assert.Equal(Evaluator.RookOpenFileBonus, WhiteMinusBlack("4k3/pppp1ppp/8/8/8/8/PPPP1PPP/4RK2 w - - 0 1"));
    }

    [Fact]
    public void ARookOnASemiOpenFileIsCounted()
    {
        // White has no e-pawn but black keeps one, so the file is semi-open for the rook, not open
        Assert.Equal(Evaluator.RookSemiOpenFileBonus, WhiteMinusBlack("4k3/pppppppp/8/8/8/8/PPPP1PPP/4RK2 w - - 0 1"));
    }

    [Fact]
    public void ARookBehindItsOwnPawnGetsNothing()
    {
        Assert.Equal(0, WhiteMinusBlack("4k3/pppppppp/8/8/8/8/PPPPPPPP/4RK2 w - - 0 1"));
    }

    [Fact]
    public void MatchingRooksCancel()
    {
        // Both sides have a rook on the same open file, so the bonuses cancel exactly
        Assert.Equal(0, WhiteMinusBlack("4rk2/pppp1ppp/8/8/8/8/PPPP1PPP/4RK2 w - - 0 1"));
    }
}
