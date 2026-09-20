using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

/// <summary>
/// The legal move generator against a deliberately naive reference: ray walking on the square
/// array, every pseudo-legal move made and tested for leaving the king attacked, then unmade. The
/// reference shares nothing with the bitboard generator except <see cref="Position"/>'s make and
/// unmake, which the key oracle in <see cref="BoardTests"/> covers.
/// </summary>
public class MoveGenerationTests
{
    [Fact]
    public void MatchesTheReferenceGeneratorOnTheStandardPositionsAndTheirChildren()
    {
        foreach (PerftPosition entry in Perft.StandardPositions)
        {
            var position = Position.FromFen(entry.Fen);
            MoveList moves = AssertMatchesReference(position);
            foreach (Move move in moves)
            {
                position.MakeMove(move);
                AssertMatchesReference(position);
                position.UnmakeMove();
            }
        }
    }

    [Fact]
    public void MatchesTheReferenceGeneratorThroughRandomPlay()
    {
        var random = new Random(1912);
        for (int game = 0; game < 60; game++)
        {
            var position = Position.Start();
            for (int ply = 0; ply < 150; ply++)
            {
                MoveList moves = AssertMatchesReference(position);
                if (moves.Count == 0)
                {
                    break;
                }
                position.MakeMove(moves[random.Next(moves.Count)]);
            }
        }
    }

    [Fact]
    public void CapturesAreTheCapturesAndPromotionsAmongTheLegalMoves()
    {
        foreach (PerftPosition entry in Perft.StandardPositions)
        {
            var position = Position.FromFen(entry.Fen);
            List<Move> expected = [.. position.GenerateLegalMoves().Where(m => m.IsCapture || m.IsPromotion).OrderBy(m => m.ToBits())];
            MoveList captures = default;
            position.GenerateLegalCaptures(ref captures);
            Assert.Equal(expected, [.. captures.OrderBy(m => m.ToBits())]);
        }
    }

    [Fact]
    public void EnPassantThatExposesTheKingAlongTheRankIsIllegal()
    {
        // Lifting both pawns off the fifth rank would let the rook on h5 see the king on a5
        var position = Position.FromFen("4k3/8/8/KPp4r/8/8/8/8 w - c6 0 2");
        Assert.DoesNotContain("b5c6", Uci(position));
        Assert.Contains("b5b6", Uci(position));
    }

    [Fact]
    public void EnPassantMayCaptureACheckingPawn()
    {
        var position = Position.FromFen("8/8/8/2k5/3Pp3/8/8/4K3 b - d3 0 1");
        Assert.True(position.IsInCheck);
        Assert.Contains("e4d3", Uci(position));
        Assert.Contains("c5d4", Uci(position));
        Assert.DoesNotContain("e4e3", Uci(position));     // Does not resolve the check
    }

    [Fact]
    public void APinnedPawnCannotCaptureEnPassantOffThePinLine()
    {
        // The bishop on a2 pins the pawn on c4 to the king on e6
        var position = Position.FromFen("8/8/4k3/8/2pP4/8/B7/7K b - d3 0 1");
        Assert.DoesNotContain("c4d3", Uci(position));
        Assert.DoesNotContain("c4c3", Uci(position));
    }

    [Fact]
    public void OnlyTheKingMovesInDoubleCheck()
    {
        var position = Position.FromFen("4k3/8/3N4/8/8/8/8/4RK2 b - - 0 1");
        Assert.Equal(2, position.Checkers.Count);
        Assert.Equal(["e8d7", "e8d8", "e8f8"], Uci(position).Order().ToList());
    }

    [Fact]
    public void CastlingNeedsTheCrossedSquaresUnattacked()
    {
        var position = Position.FromFen("4k3/8/8/8/8/8/5r2/R3K2R w KQ - 0 1");
        Assert.Contains("e1c1", Uci(position));
        Assert.DoesNotContain("e1g1", Uci(position));
        var inCheck = Position.FromFen("4k3/8/8/8/8/8/4r3/R3K2R w KQ - 0 1");
        Assert.DoesNotContain("e1c1", Uci(inCheck));
        Assert.DoesNotContain("e1g1", Uci(inCheck));
        Assert.Contains("e1e2", Uci(inCheck));
        // The b-file square only needs to be empty, not safe
        var bAttacked = Position.FromFen("4k3/8/8/8/8/8/1r6/R3K2R w KQ - 0 1");
        Assert.Contains("e1c1", Uci(bAttacked));
    }

    [Fact]
    public void PinnedPiecesMoveOnlyAlongThePin()
    {
        var knight = Position.FromFen("k3r3/8/8/8/8/8/4N3/4K3 w - - 0 1");
        Assert.DoesNotContain(knight.GenerateLegalMoves(), m => m.From == Square.Parse("e2"));
        var bishop = Position.FromFen("7q/8/8/8/3B4/8/8/K6k w - - 0 1");
        List<string> bishopMoves = [.. Uci(bishop).Where(m => m.StartsWith("d4", StringComparison.Ordinal)).Order()];
        Assert.Equal(["d4b2", "d4c3", "d4e5", "d4f6", "d4g7", "d4h8"], bishopMoves);
    }

    [Fact]
    public void KingMayNotStepAlongAnAttackersLineThroughItsOwnSquare()
    {
        // The rook on a1 attacks the whole first rank; e1 is in check and f1 is behind the king on the same line
        var position = Position.FromFen("4k3/8/8/8/8/8/8/r3K3 w - - 0 1");
        Assert.DoesNotContain("e1f1", Uci(position));
        Assert.DoesNotContain("e1d1", Uci(position));
        Assert.Contains("e1e2", Uci(position));
    }

    private static List<string> Uci(Position position) => [.. position.GenerateLegalMoves().Select(m => m.ToUci())];

    private static MoveList AssertMatchesReference(Position position)
    {
        MoveList moves = position.GenerateLegalMoves();
        List<Move> expected = ReferenceGenerator.LegalMoves(position);
        Assert.Equal(expected.OrderBy(m => m.ToBits()), moves.OrderBy(m => m.ToBits()));
        Assert.Equal(ReferenceGenerator.IsAttacked(position, position.KingSquare(position.SideToMove), position.SideToMove.Opponent()), position.IsInCheck);
        return moves;
    }

    /// <summary>
    /// Plain square-by-square move generation with a make/test/unmake legality filter.
    /// </summary>
    private static class ReferenceGenerator
    {
        private static readonly (int File, int Rank)[] KnightSteps = [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
        private static readonly (int File, int Rank)[] KingSteps = [(0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1)];
        private static readonly (int File, int Rank)[] Diagonals = [(1, 1), (1, -1), (-1, -1), (-1, 1)];
        private static readonly (int File, int Rank)[] Orthogonals = [(0, 1), (1, 0), (0, -1), (-1, 0)];
        private static readonly Piece[] Promotions = [Piece.Queen, Piece.Rook, Piece.Bishop, Piece.Knight];

        public static List<Move> LegalMoves(Position position)
        {
            Color us = position.SideToMove;
            var legal = new List<Move>();
            foreach (Move move in PseudoLegalMoves(position))
            {
                position.MakeMove(move);
                if (!IsAttacked(position, position.KingSquare(us), us.Opponent()))
                {
                    legal.Add(move);
                }
                position.UnmakeMove();
            }
            return legal;
        }

        public static bool IsAttacked(Position position, Square square, Color by)
        {
            int file = square.File;
            int rank = square.Rank;
            int pawnRank = rank - (by == Color.White ? 1 : -1);     // A pawn attacks from the rank behind (from its point of view)
            foreach (int pawnFile in new[] { file - 1, file + 1 })
            {
                if (At(position, pawnFile, pawnRank) == (by, Piece.Pawn))
                {
                    return true;
                }
            }
            foreach ((int df, int dr) in KnightSteps)
            {
                if (At(position, file + df, rank + dr) == (by, Piece.Knight))
                {
                    return true;
                }
            }
            foreach ((int df, int dr) in KingSteps)
            {
                if (At(position, file + df, rank + dr) == (by, Piece.King))
                {
                    return true;
                }
            }
            return SliderAttacks(position, file, rank, by, Diagonals, Piece.Bishop) || SliderAttacks(position, file, rank, by, Orthogonals, Piece.Rook);
        }

        private static bool SliderAttacks(Position position, int file, int rank, Color by, (int File, int Rank)[] directions, Piece slider)
        {
            foreach ((int df, int dr) in directions)
            {
                int f = file + df, r = rank + dr;
                while (IsOnBoard(f, r))
                {
                    (Color Color, Piece Type) piece = position[f, r];
                    if (piece.Type != Piece.None)
                    {
                        if (piece.Color == by && (piece.Type == slider || piece.Type == Piece.Queen))
                        {
                            return true;
                        }
                        break;
                    }
                    f += df;
                    r += dr;
                }
            }
            return false;
        }

        private static IEnumerable<Move> PseudoLegalMoves(Position position)
        {
            Color us = position.SideToMove;
            Color them = us.Opponent();
            foreach (Square from in Square.All)
            {
                (Color Color, Piece Type) piece = position[from];
                if (piece.Color != us)
                {
                    continue;
                }
                int file = from.File;
                int rank = from.Rank;
                switch (piece.Type)
                {
                    case Piece.Pawn:
                        foreach (Move move in PawnMoves(position, us, them, from, file, rank))
                        {
                            yield return move;
                        }
                        break;
                    case Piece.Knight:
                    case Piece.King:
                        foreach ((int df, int dr) in piece.Type == Piece.Knight ? KnightSteps : KingSteps)
                        {
                            if (!IsOnBoard(file + df, rank + dr))
                            {
                                continue;
                            }
                            var to = Square.At(file + df, rank + dr);
                            (Color Color, Piece Type) target = position[to];
                            if (target.Type == Piece.None)
                            {
                                yield return new Move(from, to, piece.Type);
                            }
                            else if (target.Color == them)
                            {
                                yield return new Move(from, to, piece.Type, target.Type);
                            }
                        }
                        if (piece.Type == Piece.King)
                        {
                            foreach (Move move in CastlingMoves(position, us, them, from))
                            {
                                yield return move;
                            }
                        }
                        break;
                    default:
                        (int File, int Rank)[] directions = piece.Type switch
                        {
                            Piece.Bishop => Diagonals,
                            Piece.Rook => Orthogonals,
                            _ => [.. Diagonals, .. Orthogonals],
                        };
                        foreach ((int df, int dr) in directions)
                        {
                            int f = file + df, r = rank + dr;
                            while (IsOnBoard(f, r))
                            {
                                var to = Square.At(f, r);
                                (Color Color, Piece Type) target = position[to];
                                if (target.Type == Piece.None)
                                {
                                    yield return new Move(from, to, piece.Type);
                                }
                                else
                                {
                                    if (target.Color == them)
                                    {
                                        yield return new Move(from, to, piece.Type, target.Type);
                                    }
                                    break;
                                }
                                f += df;
                                r += dr;
                            }
                        }
                        break;
                }
            }
        }

        private static IEnumerable<Move> PawnMoves(Position position, Color us, Color them, Square from, int file, int rank)
        {
            int direction = us == Color.White ? 1 : -1;
            int startRank = us == Color.White ? 1 : 6;
            int promotionRank = us == Color.White ? 7 : 0;
            int forwardRank = rank + direction;
            bool promotes = forwardRank == promotionRank;
            if (position[file, forwardRank].Type == Piece.None)
            {
                var to = Square.At(file, forwardRank);
                if (promotes)
                {
                    foreach (Piece promotion in Promotions)
                    {
                        yield return new Move(from, to, Piece.Pawn, Piece.None, MoveKind.Promotion, promotion);
                    }
                }
                else
                {
                    yield return new Move(from, to, Piece.Pawn);
                    if (rank == startRank && position[file, forwardRank + direction].Type == Piece.None)
                    {
                        yield return new Move(from, Square.At(file, forwardRank + direction), Piece.Pawn, Piece.None, MoveKind.DoublePawnPush);
                    }
                }
            }
            foreach (int captureFile in new[] { file - 1, file + 1 })
            {
                if (!IsOnBoard(captureFile, forwardRank))
                {
                    continue;
                }
                var to = Square.At(captureFile, forwardRank);
                (Color Color, Piece Type) target = position[to];
                if (target.Color == them)
                {
                    if (promotes)
                    {
                        foreach (Piece promotion in Promotions)
                        {
                            yield return new Move(from, to, Piece.Pawn, target.Type, MoveKind.Promotion, promotion);
                        }
                    }
                    else
                    {
                        yield return new Move(from, to, Piece.Pawn, target.Type);
                    }
                }
                else if (to == position.EnPassantSquare && position[captureFile, rank] == (them, Piece.Pawn))
                {
                    yield return new Move(from, to, Piece.Pawn, Piece.Pawn, MoveKind.EnPassant);
                }
            }
        }

        private static IEnumerable<Move> CastlingMoves(Position position, Color us, Color them, Square king)
        {
            Square home = us == Color.White ? Square.E1 : Square.E8;
            if (king != home || IsAttacked(position, home, them))
            {
                yield break;
            }
            (Color, Piece) rook = (us, Piece.Rook);
            CastlingRights kingSide = us == Color.White ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
            CastlingRights queenSide = us == Color.White ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
            if (position.CastlingRights.HasFlag(kingSide) && position[home + 3] == rook && position[home + 1].Type == Piece.None && position[home + 2].Type == Piece.None
                && !IsAttacked(position, home + 1, them) && !IsAttacked(position, home + 2, them))
            {
                yield return new Move(home, home + 2, Piece.King, Piece.None, MoveKind.CastleKingSide);
            }
            if (position.CastlingRights.HasFlag(queenSide) && position[home - 4] == rook && position[home - 1].Type == Piece.None && position[home - 2].Type == Piece.None && position[home - 3].Type == Piece.None
                && !IsAttacked(position, home - 1, them) && !IsAttacked(position, home - 2, them))
            {
                yield return new Move(home, home - 2, Piece.King, Piece.None, MoveKind.CastleQueenSide);
            }
        }

        private static bool IsOnBoard(int file, int rank) => file is >= 0 and <= 7 && rank is >= 0 and <= 7;

        /// <summary>
        /// The piece at file and rank, or the empty pair off the board.
        /// </summary>
        private static (Color Color, Piece Type) At(Position position, int file, int rank) => IsOnBoard(file, rank) ? position[file, rank] : default;
    }
}
