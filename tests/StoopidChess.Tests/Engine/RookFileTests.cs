using StoopidChess.Core;
using StoopidChess.Engine;

namespace StoopidChess.Tests.Engine;

/// <summary>
/// The rook file terms pinned the same way as the pawn structure: turn one parameter up and the
/// evaluation must move by the weight change times the white-minus-black count of the pattern.
/// </summary>
public class RookFileTests
{
    private static int Delta(string fen, string parameter, int increase)
    {
        var position = Position.FromFen(fen);
        var parameters = new EvaluationParameters();
        var evaluator = new Evaluator(parameters);
        int before = evaluator.Evaluate(position);
        Assert.True(parameters.TrySet(parameter, parameters.Get(parameter)!.Value + increase));
        return evaluator.Evaluate(position) - before;
    }

    [Fact]
    public void ARookOnAnOpenFileIsCounted()
    {
        // The e-file holds no pawn of either color, so white's rook there is on an open file
        Assert.Equal(30, Delta("4k3/pppp1ppp/8/8/8/8/PPPP1PPP/4RK2 w - - 0 1", "RookOpenFileBonus", 30));
        Assert.Equal(0, Delta("4k3/pppp1ppp/8/8/8/8/PPPP1PPP/4RK2 w - - 0 1", "RookSemiOpenFileBonus", 30));
    }

    [Fact]
    public void ARookOnASemiOpenFileIsCounted()
    {
        // White has no e-pawn but black keeps one, so the file is semi-open for the rook, not open
        Assert.Equal(30, Delta("4k3/pppppppp/8/8/8/8/PPPP1PPP/4RK2 w - - 0 1", "RookSemiOpenFileBonus", 30));
        Assert.Equal(0, Delta("4k3/pppppppp/8/8/8/8/PPPP1PPP/4RK2 w - - 0 1", "RookOpenFileBonus", 30));
    }

    [Fact]
    public void ARookBehindItsOwnPawnGetsNothing()
    {
        Assert.Equal(0, Delta("4k3/pppppppp/8/8/8/8/PPPPPPPP/4RK2 w - - 0 1", "RookOpenFileBonus", 30));
        Assert.Equal(0, Delta("4k3/pppppppp/8/8/8/8/PPPPPPPP/4RK2 w - - 0 1", "RookSemiOpenFileBonus", 30));
    }

    [Fact]
    public void MatchingRooksCancel()
    {
        // Both sides have a rook on the same open file, so the bonuses cancel exactly
        Assert.Equal(0, Delta("4rk2/pppp1ppp/8/8/8/8/PPPP1PPP/4RK2 w - - 0 1", "RookOpenFileBonus", 30));
    }
}
