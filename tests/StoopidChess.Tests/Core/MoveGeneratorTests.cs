using StoopidChess.Core;

namespace StoopidChess.Tests.Core;

/// <summary>
/// The legal move generator against a deliberately naive reference: ray walking on the square
/// array, every pseudo-legal move made and tested for leaving the king attacked, then unmade. The
/// reference shares nothing with the bitboard generator except <see cref="Board"/>'s make and
/// unmake, which the key oracle in <see cref="BoardTests"/> covers.
/// </summary>
public class MoveGeneratorTests
{
    [Fact]
    public void MatchesTheReferenceGeneratorOnTheStandardPositionsAndTheirChildren()
    {
        foreach (PerftPosition position in Perft.StandardPositions)
        {
            var board = Board.FromFen(position.Fen);
            MoveList moves = AssertMatchesReference(board);
            foreach (Move move in moves)
            {
                board.MakeMove(move);
                AssertMatchesReference(board);
                board.UnmakeMove();
            }
        }
    }

    [Fact]
    public void MatchesTheReferenceGeneratorThroughRandomPlay()
    {
        var random = new Random(1912);
        for (int game = 0; game < 60; game++)
        {
            var board = Board.StartPosition();
            for (int ply = 0; ply < 150; ply++)
            {
                MoveList moves = AssertMatchesReference(board);
                if (moves.Count == 0)
                {
                    break;
                }
                board.MakeMove(moves[random.Next(moves.Count)]);
            }
        }
    }

    [Fact]
    public void CapturesAreTheCapturesAndPromotionsAmongTheLegalMoves()
    {
        foreach (PerftPosition position in Perft.StandardPositions)
        {
            var board = Board.FromFen(position.Fen);
            List<Move> expected = [.. MoveGenerator.GenerateLegalMoves(board).Where(m => m.IsCapture || m.IsPromotion).OrderBy(m => m.ToBits())];
            MoveList captures = default;
            MoveGenerator.GenerateLegalCaptures(board, ref captures);
            Assert.Equal(expected, [.. captures.OrderBy(m => m.ToBits())]);
        }
    }

    [Fact]
    public void EnPassantThatExposesTheKingAlongTheRankIsIllegal()
    {
        // Lifting both pawns off the fifth rank would let the rook on h5 see the king on a5
        var board = Board.FromFen("4k3/8/8/KPp4r/8/8/8/8 w - c6 0 2");
        Assert.DoesNotContain("b5c6", Uci(board));
        Assert.Contains("b5b6", Uci(board));
    }

    [Fact]
    public void EnPassantMayCaptureACheckingPawn()
    {
        var board = Board.FromFen("8/8/8/2k5/3Pp3/8/8/4K3 b - d3 0 1");
        Assert.True(board.IsInCheck);
        Assert.Contains("e4d3", Uci(board));
        Assert.Contains("c5d4", Uci(board));
        Assert.DoesNotContain("e4e3", Uci(board));     // Does not resolve the check
    }

    [Fact]
    public void APinnedPawnCannotCaptureEnPassantOffThePinLine()
    {
        // The bishop on a2 pins the pawn on c4 to the king on e6
        var board = Board.FromFen("8/8/4k3/8/2pP4/8/B7/7K b - d3 0 1");
        Assert.DoesNotContain("c4d3", Uci(board));
        Assert.DoesNotContain("c4c3", Uci(board));
    }

    [Fact]
    public void OnlyTheKingMovesInDoubleCheck()
    {
        var board = Board.FromFen("4k3/8/3N4/8/8/8/8/4RK2 b - - 0 1");
        Assert.Equal(2, board.Checkers.Count);
        Assert.Equal(["e8d7", "e8d8", "e8f8"], Uci(board).Order().ToList());
    }

    [Fact]
    public void CastlingNeedsTheCrossedSquaresUnattacked()
    {
        var board = Board.FromFen("4k3/8/8/8/8/8/5r2/R3K2R w KQ - 0 1");
        Assert.Contains("e1c1", Uci(board));
        Assert.DoesNotContain("e1g1", Uci(board));
        var inCheck = Board.FromFen("4k3/8/8/8/8/8/4r3/R3K2R w KQ - 0 1");
        Assert.DoesNotContain("e1c1", Uci(inCheck));
        Assert.DoesNotContain("e1g1", Uci(inCheck));
        Assert.Contains("e1e2", Uci(inCheck));
        // The b-file square only needs to be empty, not safe
        var bAttacked = Board.FromFen("4k3/8/8/8/8/8/1r6/R3K2R w KQ - 0 1");
        Assert.Contains("e1c1", Uci(bAttacked));
    }

    [Fact]
    public void PinnedPiecesMoveOnlyAlongThePin()
    {
        var knight = Board.FromFen("k3r3/8/8/8/8/8/4N3/4K3 w - - 0 1");
        Assert.DoesNotContain(MoveGenerator.GenerateLegalMoves(knight), m => m.From == Square.Parse("e2"));
        var bishop = Board.FromFen("7q/8/8/8/3B4/8/8/K6k w - - 0 1");
        List<string> bishopMoves = [.. Uci(bishop).Where(m => m.StartsWith("d4", StringComparison.Ordinal)).Order()];
        Assert.Equal(["d4b2", "d4c3", "d4e5", "d4f6", "d4g7", "d4h8"], bishopMoves);
    }

    [Fact]
    public void KingMayNotStepAlongAnAttackersLineThroughItsOwnSquare()
    {
        // The rook on a1 attacks the whole first rank; e1 is in check and f1 is behind the king on the same line
        var board = Board.FromFen("4k3/8/8/8/8/8/8/r3K3 w - - 0 1");
        Assert.DoesNotContain("e1f1", Uci(board));
        Assert.DoesNotContain("e1d1", Uci(board));
        Assert.Contains("e1e2", Uci(board));
    }

    private static List<string> Uci(Board board) => [.. MoveGenerator.GenerateLegalMoves(board).Select(m => m.ToUci())];

    private static MoveList AssertMatchesReference(Board board)
    {
        MoveList moves = MoveGenerator.GenerateLegalMoves(board);
        List<Move> expected = ReferenceGenerator.LegalMoves(board);
        Assert.Equal(expected.OrderBy(m => m.ToBits()), moves.OrderBy(m => m.ToBits()));
        Assert.Equal(ReferenceGenerator.IsAttacked(board, board.KingSquare(board.SideToMove), board.SideToMove.Opponent()), board.IsInCheck);
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
        private static readonly PieceType[] Promotions = [PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight];

        public static List<Move> LegalMoves(Board board)
        {
            Colour us = board.SideToMove;
            var legal = new List<Move>();
            foreach (Move move in PseudoLegalMoves(board))
            {
                board.MakeMove(move);
                if (!IsAttacked(board, board.KingSquare(us), us.Opponent()))
                {
                    legal.Add(move);
                }
                board.UnmakeMove();
            }
            return legal;
        }

        public static bool IsAttacked(Board board, Square square, Colour by)
        {
            int file = square.File;
            int rank = square.Rank;
            int pawnRank = rank - (by == Colour.White ? 1 : -1);     // A pawn attacks from the rank behind (from its point of view)
            foreach (int pawnFile in new[] { file - 1, file + 1 })
            {
                if (At(board, pawnFile, pawnRank) == (by, PieceType.Pawn))
                {
                    return true;
                }
            }
            foreach ((int df, int dr) in KnightSteps)
            {
                if (At(board, file + df, rank + dr) == (by, PieceType.Knight))
                {
                    return true;
                }
            }
            foreach ((int df, int dr) in KingSteps)
            {
                if (At(board, file + df, rank + dr) == (by, PieceType.King))
                {
                    return true;
                }
            }
            return SliderAttacks(board, file, rank, by, Diagonals, PieceType.Bishop) || SliderAttacks(board, file, rank, by, Orthogonals, PieceType.Rook);
        }

        private static bool SliderAttacks(Board board, int file, int rank, Colour by, (int File, int Rank)[] directions, PieceType slider)
        {
            foreach ((int df, int dr) in directions)
            {
                int f = file + df, r = rank + dr;
                while (IsOnBoard(f, r))
                {
                    (Colour Colour, PieceType Type) piece = board[f, r];
                    if (piece.Type != PieceType.None)
                    {
                        if (piece.Colour == by && (piece.Type == slider || piece.Type == PieceType.Queen))
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

        private static IEnumerable<Move> PseudoLegalMoves(Board board)
        {
            Colour us = board.SideToMove;
            Colour them = us.Opponent();
            foreach (Square from in Square.All)
            {
                (Colour Colour, PieceType Type) piece = board[from];
                if (piece.Colour != us)
                {
                    continue;
                }
                int file = from.File;
                int rank = from.Rank;
                switch (piece.Type)
                {
                    case PieceType.Pawn:
                        foreach (Move move in PawnMoves(board, us, them, from, file, rank))
                        {
                            yield return move;
                        }
                        break;
                    case PieceType.Knight:
                    case PieceType.King:
                        foreach ((int df, int dr) in piece.Type == PieceType.Knight ? KnightSteps : KingSteps)
                        {
                            if (!IsOnBoard(file + df, rank + dr))
                            {
                                continue;
                            }
                            var to = Square.At(file + df, rank + dr);
                            (Colour Colour, PieceType Type) target = board[to];
                            if (target.Type == PieceType.None)
                            {
                                yield return new Move(from, to, piece.Type);
                            }
                            else if (target.Colour == them)
                            {
                                yield return new Move(from, to, piece.Type, target.Type);
                            }
                        }
                        if (piece.Type == PieceType.King)
                        {
                            foreach (Move move in CastlingMoves(board, us, them, from))
                            {
                                yield return move;
                            }
                        }
                        break;
                    default:
                        (int File, int Rank)[] directions = piece.Type switch
                        {
                            PieceType.Bishop => Diagonals,
                            PieceType.Rook => Orthogonals,
                            _ => [.. Diagonals, .. Orthogonals],
                        };
                        foreach ((int df, int dr) in directions)
                        {
                            int f = file + df, r = rank + dr;
                            while (IsOnBoard(f, r))
                            {
                                var to = Square.At(f, r);
                                (Colour Colour, PieceType Type) target = board[to];
                                if (target.Type == PieceType.None)
                                {
                                    yield return new Move(from, to, piece.Type);
                                }
                                else
                                {
                                    if (target.Colour == them)
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

        private static IEnumerable<Move> PawnMoves(Board board, Colour us, Colour them, Square from, int file, int rank)
        {
            int direction = us == Colour.White ? 1 : -1;
            int startRank = us == Colour.White ? 1 : 6;
            int promotionRank = us == Colour.White ? 7 : 0;
            int forwardRank = rank + direction;
            bool promotes = forwardRank == promotionRank;
            if (board[file, forwardRank].Type == PieceType.None)
            {
                var to = Square.At(file, forwardRank);
                if (promotes)
                {
                    foreach (PieceType promotion in Promotions)
                    {
                        yield return new Move(from, to, PieceType.Pawn, PieceType.None, MoveKind.Promotion, promotion);
                    }
                }
                else
                {
                    yield return new Move(from, to, PieceType.Pawn);
                    if (rank == startRank && board[file, forwardRank + direction].Type == PieceType.None)
                    {
                        yield return new Move(from, Square.At(file, forwardRank + direction), PieceType.Pawn, PieceType.None, MoveKind.DoublePawnPush);
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
                (Colour Colour, PieceType Type) target = board[to];
                if (target.Colour == them)
                {
                    if (promotes)
                    {
                        foreach (PieceType promotion in Promotions)
                        {
                            yield return new Move(from, to, PieceType.Pawn, target.Type, MoveKind.Promotion, promotion);
                        }
                    }
                    else
                    {
                        yield return new Move(from, to, PieceType.Pawn, target.Type);
                    }
                }
                else if (to == board.EnPassantSquare && board[captureFile, rank] == (them, PieceType.Pawn))
                {
                    yield return new Move(from, to, PieceType.Pawn, PieceType.Pawn, MoveKind.EnPassant);
                }
            }
        }

        private static IEnumerable<Move> CastlingMoves(Board board, Colour us, Colour them, Square king)
        {
            Square home = us == Colour.White ? Square.E1 : Square.E8;
            if (king != home || IsAttacked(board, home, them))
            {
                yield break;
            }
            (Colour, PieceType) rook = (us, PieceType.Rook);
            CastlingRights kingSide = us == Colour.White ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
            CastlingRights queenSide = us == Colour.White ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
            if (board.CastlingRights.HasFlag(kingSide) && board[home + 3] == rook && board[home + 1].Type == PieceType.None && board[home + 2].Type == PieceType.None
                && !IsAttacked(board, home + 1, them) && !IsAttacked(board, home + 2, them))
            {
                yield return new Move(home, home + 2, PieceType.King, PieceType.None, MoveKind.CastleKingSide);
            }
            if (board.CastlingRights.HasFlag(queenSide) && board[home - 4] == rook && board[home - 1].Type == PieceType.None && board[home - 2].Type == PieceType.None && board[home - 3].Type == PieceType.None
                && !IsAttacked(board, home - 1, them) && !IsAttacked(board, home - 2, them))
            {
                yield return new Move(home, home - 2, PieceType.King, PieceType.None, MoveKind.CastleQueenSide);
            }
        }

        private static bool IsOnBoard(int file, int rank) => file is >= 0 and <= 7 && rank is >= 0 and <= 7;

        /// <summary>
        /// The piece at file and rank, or the empty pair off the board.
        /// </summary>
        private static (Colour Colour, PieceType Type) At(Board board, int file, int rank) => IsOnBoard(file, rank) ? board[file, rank] : default;
    }
}
