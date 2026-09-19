using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

public class AttacksTests
{
    [Fact]
    public void LeaperTablesHaveTheRightShape()
    {
        Assert.Equal(2, Attacks.Knight(Square.A1).Count);
        Assert.Equal(8, Attacks.Knight(Square.Parse("d4")).Count);
        Assert.Equal(Bitboard.Of(Square.Parse("b3")) | Bitboard.Of(Square.Parse("c2")), Attacks.Knight(Square.A1));
        Assert.Equal(3, Attacks.King(Square.H8).Count);
        Assert.Equal(8, Attacks.King(Square.Parse("e4")).Count);
        Assert.Equal(Bitboard.Of(Square.Parse("d5")) | Bitboard.Of(Square.Parse("f5")), Attacks.Pawn(Colour.White, Square.Parse("e4")));
        Assert.Equal(Bitboard.Of(Square.Parse("d3")) | Bitboard.Of(Square.Parse("f3")), Attacks.Pawn(Colour.Black, Square.Parse("e4")));
        Assert.Equal(Bitboard.Of(Square.Parse("b2")), Attacks.Pawn(Colour.White, Square.A1));   // No wrap around the edge
        Assert.Equal(Bitboard.Of(Square.Parse("g7")), Attacks.Pawn(Colour.Black, Square.H8));
        Assert.Equal(Bitboard.Rank3 & (Bitboard.FileA | Bitboard.FileC | Bitboard.FileG), Attacks.Pawns(Colour.White, Bitboard.Rank2 & (Bitboard.FileB | Bitboard.FileH)));
    }

    [Fact]
    public void BetweenAndLineFollowTheGeometry()
    {
        Square a1 = Square.A1, h8 = Square.H8, e4 = Square.Parse("e4");
        Assert.Equal(6, Attacks.Between(a1, h8).Count);
        Assert.Equal(Attacks.Between(a1, h8), Attacks.Between(h8, a1));
        Assert.True(Attacks.Between(a1, h8).Contains(Square.Parse("d4")));
        Assert.False(Attacks.Between(a1, h8).Contains(e4));
        Assert.Equal(Bitboard.Empty, Attacks.Between(a1, Square.Parse("b3")));       // Not aligned
        Assert.Equal(Bitboard.Empty, Attacks.Between(a1, Square.Parse("b1")));       // Adjacent
        Assert.Equal(Bitboard.Empty, Attacks.Between(e4, e4));
        Assert.Equal(Bitboard.Rank4, Attacks.Line(e4, Square.Parse("a4")));
        Assert.Equal(Bitboard.FileE, Attacks.Line(e4, Square.Parse("e8")));
        Assert.Equal(8, Attacks.Line(a1, Square.Parse("e5")).Count);
        Assert.Equal(Bitboard.Empty, Attacks.Line(a1, e4));
        Assert.Equal(Bitboard.Empty, Attacks.Line(a1, Square.Parse("c2")));
    }

    [Fact]
    public void MagicSlidersAgreeWithRayWalkingForRandomOccupancies()
    {
        var random = new Random(2026);
        foreach (Square square in Square.All)
        {
            Assert.Equal(Attacks.SlowBishop(Bitboard.Empty, square), Attacks.Bishop(Bitboard.Empty, square));
            Assert.Equal(Attacks.SlowRook(Bitboard.Empty, square), Attacks.Rook(Bitboard.Empty, square));
            Assert.Equal(Attacks.SlowBishop(Bitboard.Full, square), Attacks.Bishop(Bitboard.Full, square));
            Assert.Equal(Attacks.SlowRook(Bitboard.Full, square), Attacks.Rook(Bitboard.Full, square));
            for (int i = 0; i < 200; i++)
            {
                // Random occupancies of varying density, with the slider's own square sometimes occupied too
                Bitboard occupied = RandomBits(random) & RandomBits(random);
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
        var d4 = Square.Parse("d4");
        Bitboard occupied = Bitboard.Of(Square.Parse("d6")) | Bitboard.Of(Square.Parse("f4"));
        Bitboard expected = Bitboard.Of(Square.Parse("d5")) | Bitboard.Of(Square.Parse("d6"))
            | Bitboard.Of(Square.Parse("e4")) | Bitboard.Of(Square.Parse("f4"))
            | Bitboard.Of(Square.Parse("d3")) | Bitboard.Of(Square.Parse("d2")) | Bitboard.Of(Square.Parse("d1"))
            | Bitboard.Of(Square.Parse("c4")) | Bitboard.Of(Square.Parse("b4")) | Bitboard.Of(Square.Parse("a4"));
        Assert.Equal(expected, Attacks.SlowRook(occupied, d4));
        Assert.Equal(13, Attacks.SlowBishop(Bitboard.Empty, d4).Count);
        Assert.Equal(7, Attacks.SlowBishop(Bitboard.Empty, Square.A1).Count);
    }

    [Fact]
    public void BitboardOperationsWork()
    {
        Bitboard bits = Bitboard.Of(Square.A1) | Bitboard.Of(Square.H8) | Bitboard.Of(Square.Parse("e4"));
        Assert.Equal(3, bits.Count);
        Assert.Equal(Square.A1, bits.First);
        Assert.Equal([Square.A1, Square.Parse("e4"), Square.H8], Enumerate(bits));
        Assert.Equal(Square.A1, Bitboard.PopFirst(ref bits));
        Assert.Equal(Square.Parse("e4"), Bitboard.PopFirst(ref bits));
        Assert.Equal(Square.H8, Bitboard.PopFirst(ref bits));
        Assert.True(bits.IsEmpty);
        Assert.Equal(Square.None, bits.First);
        Assert.Empty(Enumerate(bits));
        Assert.True(Bitboard.Of(Square.Parse("c3")).IsSingle);
        Assert.False(Bitboard.Empty.IsSingle);
        Assert.False(Bitboard.Rank1.IsSingle);
        Assert.Equal(Bitboard.Rank2, Bitboard.Rank1.North());
        Assert.Equal(Bitboard.Empty, Bitboard.Rank8.North());
        Assert.Equal(Bitboard.Empty, Bitboard.FileH.East());
        Assert.Equal(Bitboard.FileA, Bitboard.FileB.West());
        Assert.Equal(Bitboard.Of(Square.Parse("b2")), Bitboard.Of(Square.A1).NorthEast());
        Assert.Equal(Bitboard.Empty, Bitboard.Of(Square.A1).NorthWest());
        Assert.Equal(Bitboard.Empty, Bitboard.Of(Square.H1).SouthEast());
        Assert.Equal(Bitboard.Of(Square.Parse("g7")), Bitboard.Of(Square.H8).SouthWest());
        Assert.Equal(Bitboard.Rank3, Bitboard.Rank2.Forward(Colour.White));
        Assert.Equal(Bitboard.Rank1, Bitboard.Rank2.Forward(Colour.Black));
        Assert.Equal(32, Bitboard.LightSquares.Count);
        Assert.Equal(Bitboard.Full, Bitboard.LightSquares | Bitboard.DarkSquares);
        Assert.Equal(Bitboard.Empty, Bitboard.LightSquares & Bitboard.DarkSquares);
        Assert.Equal(~Bitboard.LightSquares, Bitboard.DarkSquares);
        Assert.False(Bitboard.LightSquares.Contains(Square.A1));
        Assert.True(Bitboard.LightSquares.Contains(Square.H1));
        Assert.False(Square.A1.IsLight);
        Assert.True(Square.H1.IsLight);
        Assert.Equal(Bitboard.Rank5, Bitboard.OfRank(4));
        Assert.Equal(Bitboard.FileD, Bitboard.OfFile(3));
        Assert.Equal(0xFFUL, Bitboard.Rank1.Value);
        Assert.Equal(0xFFUL, (ulong)Bitboard.Rank1);
        Assert.Equal("0x00000000000000FF", Bitboard.Rank1.ToString());
        Assert.StartsWith(".......1\n", Bitboard.Of(Square.H8).ToDiagram(), StringComparison.Ordinal);
    }

    private static List<Square> Enumerate(Bitboard bits)
    {
        var squares = new List<Square>();
        foreach (Square square in bits)
        {
            squares.Add(square);
        }
        return squares;
    }

    private static Bitboard RandomBits(Random random)
    {
        Span<byte> bytes = stackalloc byte[8];
        random.NextBytes(bytes);
        return new Bitboard(BitConverter.ToUInt64(bytes));
    }
}
