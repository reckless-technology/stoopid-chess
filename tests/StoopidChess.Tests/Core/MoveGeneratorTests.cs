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
            Board board = Board.FromFen(position.Fen);
            AssertMatchesReference(board);
            foreach (Move move in MoveGenerator.GenerateLegalMoves(board))
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
            Board board = Board.StartPosition();
            for (int ply = 0; ply < 150; ply++)
            {
                List<Move> moves = AssertMatchesReference(board);
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
            Board board = Board.FromFen(position.Fen);
            List<Move> expected = MoveGenerator.GenerateLegalMoves(board).Where(m => m.IsCapture || m.IsPromotion).OrderBy(m => m.ToBits()).ToList();
            var captures = new List<Move>();
            MoveGenerator.GenerateLegalCaptures(board, captures);
            Assert.Equal(expected, captures.OrderBy(m => m.ToBits()).ToList());
        }
    }

    [Fact]
    public void EnPassantThatExposesTheKingAlongTheRankIsIllegal()
    {
        // Lifting both pawns off the fifth rank would let the rook on h5 see the king on a5
        Board board = Board.FromFen("4k3/8/8/KPp4r/8/8/8/8 w - c6 0 2");
        Assert.DoesNotContain("b5c6", Uci(board));
        Assert.Contains("b5b6", Uci(board));
    }

    [Fact]
    public void EnPassantMayCaptureACheckingPawn()
    {
        Board board = Board.FromFen("8/8/8/2k5/3Pp3/8/8/4K3 b - d3 0 1");
        Assert.True(board.IsInCheck);
        Assert.Contains("e4d3", Uci(board));
        Assert.Contains("c5d4", Uci(board));
        Assert.DoesNotContain("e4e3", Uci(board));     // Does not resolve the check
    }

    [Fact]
    public void APinnedPawnCannotCaptureEnPassantOffThePinLine()
    {
        // The bishop on a2 pins the pawn on c4 to the king on e6
        Board board = Board.FromFen("8/8/4k3/8/2pP4/8/B7/7K b - d3 0 1");
        Assert.DoesNotContain("c4d3", Uci(board));
        Assert.DoesNotContain("c4c3", Uci(board));
    }

    [Fact]
    public void OnlyTheKingMovesInDoubleCheck()
    {
        Board board = Board.FromFen("4k3/8/3N4/8/8/8/8/4RK2 b - - 0 1");
        Assert.Equal(2, Bitboards.Count(board.Checkers));
        Assert.Equal(["e8d7", "e8d8", "e8f8"], Uci(board).Order().ToList());
    }

    [Fact]
    public void CastlingNeedsTheCrossedSquaresUnattacked()
    {
        Board board = Board.FromFen("4k3/8/8/8/8/8/5r2/R3K2R w KQ - 0 1");
        Assert.Contains("e1c1", Uci(board));
        Assert.DoesNotContain("e1g1", Uci(board));
        Board inCheck = Board.FromFen("4k3/8/8/8/8/8/4r3/R3K2R w KQ - 0 1");
        Assert.DoesNotContain("e1c1", Uci(inCheck));
        Assert.DoesNotContain("e1g1", Uci(inCheck));
        Assert.Contains("e1e2", Uci(inCheck));
        // The b-file square only needs to be empty, not safe
        Board bAttacked = Board.FromFen("4k3/8/8/8/8/8/1r6/R3K2R w KQ - 0 1");
        Assert.Contains("e1c1", Uci(bAttacked));
    }

    [Fact]
    public void PinnedPiecesMoveOnlyAlongThePin()
    {
        Board knight = Board.FromFen("k3r3/8/8/8/8/8/4N3/4K3 w - - 0 1");
        Assert.DoesNotContain(MoveGenerator.GenerateLegalMoves(knight), m => m.From == Squares.Parse("e2"));
        Board bishop = Board.FromFen("7q/8/8/8/3B4/8/8/K6k w - - 0 1");
        List<string> bishopMoves = Uci(bishop).Where(m => m.StartsWith("d4", StringComparison.Ordinal)).Order().ToList();
        Assert.Equal(["d4b2", "d4c3", "d4e5", "d4f6", "d4g7", "d4h8"], bishopMoves);
    }

    [Fact]
    public void KingMayNotStepAlongAnAttackersLineThroughItsOwnSquare()
    {
        // The rook on a1 attacks the whole first rank; e1 is in check and f1 is behind the king on the same line
        Board board = Board.FromFen("4k3/8/8/8/8/8/8/r3K3 w - - 0 1");
        Assert.DoesNotContain("e1f1", Uci(board));
        Assert.DoesNotContain("e1d1", Uci(board));
        Assert.Contains("e1e2", Uci(board));
    }

    private static List<string> Uci(Board board) => MoveGenerator.GenerateLegalMoves(board).Select(m => m.ToUci()).ToList();

    private static List<Move> AssertMatchesReference(Board board)
    {
        List<Move> moves = MoveGenerator.GenerateLegalMoves(board);
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

        public static bool IsAttacked(Board board, int square, Colour by)
        {
            int file = Squares.FileOf(square);
            int rank = Squares.RankOf(square);
            int pawnRank = rank - (by == Colour.White ? 1 : -1);     // A pawn attacks from the rank behind (from its point of view)
            foreach (int pawnFile in new[] { file - 1, file + 1 })
            {
                if (At(board, pawnFile, pawnRank) == new Piece(by, PieceType.Pawn))
                {
                    return true;
                }
            }
            foreach ((int df, int dr) in KnightSteps)
            {
                if (At(board, file + df, rank + dr) == new Piece(by, PieceType.Knight))
                {
                    return true;
                }
            }
            foreach ((int df, int dr) in KingSteps)
            {
                if (At(board, file + df, rank + dr) == new Piece(by, PieceType.King))
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
                while (f is >= 0 and <= 7 && r is >= 0 and <= 7)
                {
                    Piece piece = board[f, r];
                    if (!piece.IsNone)
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
            foreach (int from in Squares.All)
            {
                Piece piece = board[from];
                if (piece.Colour != us)
                {
                    continue;
                }
                int file = Squares.FileOf(from);
                int rank = Squares.RankOf(from);
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
                            Piece target = At(board, file + df, rank + dr);
                            if (target.Colour == Colour.None && IsOnBoard(file + df, rank + dr))
                            {
                                yield return new Move(from, Squares.Index(file + df, rank + dr), piece.Type);
                            }
                            else if (target.Colour == them)
                            {
                                yield return new Move(from, Squares.Index(file + df, rank + dr), piece.Type, target.Type);
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
                                Piece target = board[f, r];
                                if (target.IsNone)
                                {
                                    yield return new Move(from, Squares.Index(f, r), piece.Type);
                                }
                                else
                                {
                                    if (target.Colour == them)
                                    {
                                        yield return new Move(from, Squares.Index(f, r), piece.Type, target.Type);
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

        private static IEnumerable<Move> PawnMoves(Board board, Colour us, Colour them, int from, int file, int rank)
        {
            int direction = us == Colour.White ? 1 : -1;
            int startRank = us == Colour.White ? 1 : 6;
            int promotionRank = us == Colour.White ? 7 : 0;
            int forwardRank = rank + direction;
            bool promotes = forwardRank == promotionRank;
            if (board[file, forwardRank].IsNone)
            {
                int to = Squares.Index(file, forwardRank);
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
                    if (rank == startRank && board[file, forwardRank + direction].IsNone)
                    {
                        yield return new Move(from, Squares.Index(file, forwardRank + direction), PieceType.Pawn, PieceType.None, MoveKind.DoublePawnPush);
                    }
                }
            }
            foreach (int captureFile in new[] { file - 1, file + 1 })
            {
                if (!IsOnBoard(captureFile, forwardRank))
                {
                    continue;
                }
                int to = Squares.Index(captureFile, forwardRank);
                Piece target = board[to];
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
                else if (to == board.EnPassantSquare && board[captureFile, rank] == new Piece(them, PieceType.Pawn))
                {
                    yield return new Move(from, to, PieceType.Pawn, PieceType.Pawn, MoveKind.EnPassant);
                }
            }
        }

        private static IEnumerable<Move> CastlingMoves(Board board, Colour us, Colour them, int king)
        {
            int home = us == Colour.White ? Squares.E1 : Squares.E8;
            if (king != home || IsAttacked(board, home, them))
            {
                yield break;
            }
            Piece rook = new(us, PieceType.Rook);
            CastlingRights kingSide = us == Colour.White ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
            CastlingRights queenSide = us == Colour.White ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
            if (board.CastlingRights.HasFlag(kingSide) && board[home + 3] == rook && board[home + 1].IsNone && board[home + 2].IsNone
                && !IsAttacked(board, home + 1, them) && !IsAttacked(board, home + 2, them))
            {
                yield return new Move(home, home + 2, PieceType.King, PieceType.None, MoveKind.CastleKingSide);
            }
            if (board.CastlingRights.HasFlag(queenSide) && board[home - 4] == rook && board[home - 1].IsNone && board[home - 2].IsNone && board[home - 3].IsNone
                && !IsAttacked(board, home - 1, them) && !IsAttacked(board, home - 2, them))
            {
                yield return new Move(home, home - 2, PieceType.King, PieceType.None, MoveKind.CastleQueenSide);
            }
        }

        private static bool IsOnBoard(int file, int rank) => file is >= 0 and <= 7 && rank is >= 0 and <= 7;

        /// <summary>
        /// The piece at file and rank, or <see cref="Piece.None"/> off the board.
        /// </summary>
        private static Piece At(Board board, int file, int rank) => IsOnBoard(file, rank) ? board[file, rank] : Piece.None;
    }
}
