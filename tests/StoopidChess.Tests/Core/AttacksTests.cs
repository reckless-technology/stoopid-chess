using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class AttacksTests
{
    [Fact]
    public void LeaperTablesHaveTheRightShape()
    {
        Assert.Equal(2, Bitboards.Count(Attacks.Knight(Squares.A1)));
        Assert.Equal(8, Bitboards.Count(Attacks.Knight(Squares.Parse("d4"))));
        Assert.Equal(Bitboards.Of(Squares.Parse("b3")) | Bitboards.Of(Squares.Parse("c2")), Attacks.Knight(Squares.A1));
        Assert.Equal(3, Bitboards.Count(Attacks.King(Squares.H8)));
        Assert.Equal(8, Bitboards.Count(Attacks.King(Squares.Parse("e4"))));
        Assert.Equal(Bitboards.Of(Squares.Parse("d5")) | Bitboards.Of(Squares.Parse("f5")), Attacks.Pawn(Colour.White, Squares.Parse("e4")));
        Assert.Equal(Bitboards.Of(Squares.Parse("d3")) | Bitboards.Of(Squares.Parse("f3")), Attacks.Pawn(Colour.Black, Squares.Parse("e4")));
        Assert.Equal(Bitboards.Of(Squares.Parse("b2")), Attacks.Pawn(Colour.White, Squares.A1));   // No wrap around the edge
        Assert.Equal(Bitboards.Of(Squares.Parse("g7")), Attacks.Pawn(Colour.Black, Squares.H8));
        Assert.Equal(Bitboards.Rank3 & (Bitboards.FileA | Bitboards.FileC | Bitboards.FileG), Attacks.Pawns(Colour.White, Bitboards.Rank2 & (Bitboards.FileB | Bitboards.FileH)));
    }

    [Fact]
    public void BetweenAndLineFollowTheGeometry()
    {
        int a1 = Squares.A1, h8 = Squares.H8, e4 = Squares.Parse("e4");
        Assert.Equal(6, Bitboards.Count(Attacks.Between(a1, h8)));
        Assert.Equal(Attacks.Between(a1, h8), Attacks.Between(h8, a1));
        Assert.True(Bitboards.Contains(Attacks.Between(a1, h8), Squares.Parse("d4")));
        Assert.False(Bitboards.Contains(Attacks.Between(a1, h8), e4));
        Assert.Equal(0UL, Attacks.Between(a1, Squares.Parse("b3")));       // Not aligned
        Assert.Equal(0UL, Attacks.Between(a1, Squares.Parse("b1")));       // Adjacent
        Assert.Equal(0UL, Attacks.Between(e4, e4));
        Assert.Equal(Bitboards.Rank4, Attacks.Line(e4, Squares.Parse("a4")));
        Assert.Equal(Bitboards.FileE, Attacks.Line(e4, Squares.Parse("e8")));
        Assert.Equal(8, Bitboards.Count(Attacks.Line(a1, Squares.Parse("e5"))));
        Assert.Equal(0UL, Attacks.Line(a1, e4));
        Assert.Equal(0UL, Attacks.Line(a1, Squares.Parse("c2")));
    }

    [Fact]
    public void MagicSlidersAgreeWithRayWalkingForRandomOccupancies()
    {
        var random = new Random(2026);
        for (int square = 0; square < Squares.Count; square++)
        {
            Assert.Equal(Attacks.SlowBishop(0, square), Attacks.Bishop(0, square));
            Assert.Equal(Attacks.SlowRook(0, square), Attacks.Rook(0, square));
            Assert.Equal(Attacks.SlowBishop(Bitboards.Full, square), Attacks.Bishop(Bitboards.Full, square));
            Assert.Equal(Attacks.SlowRook(Bitboards.Full, square), Attacks.Rook(Bitboards.Full, square));
            for (int i = 0; i < 200; i++)
            {
                // Random occupancies of varying density, with the slider's own square sometimes occupied too
                ulong occupied = RandomBits(random) & RandomBits(random);
                if (i % 3 == 0)
                {
                    occupied |= RandomBits(random);
                }
                Assert.Equal(Attacks.SlowBishop(occupied, square), Attacks.Bishop(occupied, square));
                Assert.Equal(Attacks.SlowRook(occupied, square), Attacks.Rook(occupied, square));
                Assert.Equal(Attacks.Bishop(occupied, square) | Attacks.Rook(occupied, square), Attacks.Queen(occupied, square));
            }
        }
    }

    [Fact]
    public void RayWalkingReferenceIsRight()
    {
        // Rook on d4 with blockers on d6 and f4: up to and including the blockers, the whole way otherwise
        int d4 = Squares.Parse("d4");
        ulong occupied = Bitboards.Of(Squares.Parse("d6")) | Bitboards.Of(Squares.Parse("f4"));
        ulong expected = Bitboards.Of(Squares.Parse("d5")) | Bitboards.Of(Squares.Parse("d6"))
            | Bitboards.Of(Squares.Parse("e4")) | Bitboards.Of(Squares.Parse("f4"))
            | Bitboards.Of(Squares.Parse("d3")) | Bitboards.Of(Squares.Parse("d2")) | Bitboards.Of(Squares.Parse("d1"))
            | Bitboards.Of(Squares.Parse("c4")) | Bitboards.Of(Squares.Parse("b4")) | Bitboards.Of(Squares.Parse("a4"));
        Assert.Equal(expected, Attacks.SlowRook(occupied, d4));
        Assert.Equal(13, Bitboards.Count(Attacks.SlowBishop(0, d4)));
        Assert.Equal(7, Bitboards.Count(Attacks.SlowBishop(0, Squares.A1)));
    }

    [Fact]
    public void BitboardHelpersWork()
    {
        ulong bits = Bitboards.Of(Squares.A1) | Bitboards.Of(Squares.H8) | Bitboards.Of(Squares.Parse("e4"));
        Assert.Equal(3, Bitboards.Count(bits));
        Assert.Equal(Squares.A1, Bitboards.PopFirst(ref bits));
        Assert.Equal(Squares.Parse("e4"), Bitboards.PopFirst(ref bits));
        Assert.Equal(Squares.H8, Bitboards.PopFirst(ref bits));
        Assert.Equal(0UL, bits);
        Assert.True(Bitboards.IsSingle(Bitboards.Of(Squares.Parse("c3"))));
        Assert.False(Bitboards.IsSingle(0));
        Assert.False(Bitboards.IsSingle(Bitboards.Rank1));
        Assert.Equal(Bitboards.Rank2, Bitboards.North(Bitboards.Rank1));
        Assert.Equal(0UL, Bitboards.North(Bitboards.Rank8));
        Assert.Equal(0UL, Bitboards.East(Bitboards.FileH));
        Assert.Equal(Bitboards.FileA, Bitboards.West(Bitboards.FileB));
        Assert.Equal(Bitboards.Of(Squares.Parse("b2")), Bitboards.NorthEast(Bitboards.Of(Squares.A1)));
        Assert.Equal(0UL, Bitboards.NorthWest(Bitboards.Of(Squares.A1)));
        Assert.Equal(0UL, Bitboards.SouthEast(Bitboards.Of(Squares.H1)));
        Assert.Equal(Bitboards.Of(Squares.Parse("g7")), Bitboards.SouthWest(Bitboards.Of(Squares.H8)));
        Assert.Equal(32, Bitboards.Count(Bitboards.LightSquares));
        Assert.False(Bitboards.Contains(Bitboards.LightSquares, Squares.A1));
        Assert.True(Bitboards.Contains(Bitboards.LightSquares, Squares.H1));
        Assert.Equal(Bitboards.Rank5, Bitboards.Rank(4));
        Assert.Equal(Bitboards.FileD, Bitboards.File(3));
        Assert.StartsWith(".......1\n", Bitboards.ToDiagram(Bitboards.Of(Squares.H8)), StringComparison.Ordinal);
    }

    private static ulong RandomBits(Random random)
    {
        Span<byte> bytes = stackalloc byte[8];
        random.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }
}
