using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class KpkBitbaseTests
{
    // The bitbase stores the pawn on files a-d only (the caller mirrors e-h), so every case here
    // uses those files.
    [Theory]
    // The king on the sixth rank in front of its pawn wins no matter who moves
    [InlineData("d6", "d5", "d8", true, true)]
    [InlineData("d6", "d5", "d8", false, true)]
    // A rook pawn with the defending king in the corner ahead of it is a dead draw
    [InlineData("a1", "a2", "a8", true, false)]
    [InlineData("a1", "a2", "a8", false, false)]
    // The pawn on the seventh with the king beside it queens at once
    [InlineData("d6", "c6", "a8", true, true)]
    // Stalemate: the boxed-in defender has nowhere to go and is not in check
    [InlineData("d6", "d7", "d8", false, false)]
    // The bare-opposition textbook draw: pawn on its home square, defender takes the opposition
    [InlineData("d3", "d2", "d5", true, false)]
    // The knight-file trap: king beside its pawn on the sixth, but the defender slips to b8 where
    // c7+ is answered by Kc8 and the king cannot approach without stalemate or losing the pawn.
    // A famous draw (verified against pawnstar's search when this bitbase was built), and the same
    // holds for the rook pawn.
    [InlineData("b6", "c6", "c8", false, false)]
    [InlineData("b6", "c6", "b8", true, false)]
    [InlineData("b6", "a6", "a8", false, false)]
    public void MatchesTheTextbook(string strongKing, string pawn, string weakKing, bool strongToMove, bool expectedWin)
    {
        Assert.Equal(expectedWin, KpkBitbase.Probe(Square.Parse(strongKing), Square.Parse(pawn), Square.Parse(weakKing), strongToMove));
    }

    [Fact]
    public void TheWinCountIsFrozen()
    {
        // Like the bench signature: any change to the solver changes this count, which should only
        // ever be deliberate. The textbook cases above anchor it to reality.
        int wins = 0;
        foreach (Square pawn in Square.All)
        {
            if (pawn.File > 3 || pawn.Rank is 0 or 7)
            {
                continue;
            }
            foreach (Square strongKing in Square.All)
            {
                foreach (Square weakKing in Square.All)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        if (KpkBitbase.Probe(strongKing, pawn, weakKing, side == 0))
                        {
                            wins++;
                        }
                    }
                }
            }
        }
        Assert.Equal(FrozenWinCount, wins);
    }

    // 111,279 of the 196,608 arrangements are wins. Anchored by the textbook cases above and, when
    // this bitbase was built, by 39 random positions adjudicated with pawnstar's search: all agreed.
    private const int FrozenWinCount = 111279;
}
