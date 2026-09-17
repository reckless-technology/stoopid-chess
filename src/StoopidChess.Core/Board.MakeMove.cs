using System.Diagnostics;

namespace StoopidChess.Core;

/// <summary>
/// Making and unmaking moves. After a move is made and unmade the board state, including every key,
/// is identical to what it was before; the search relies on this.
/// </summary>
public sealed partial class Board
{
    /// <summary>
    /// Make a move for the side to move. The move must be at least pseudo-legal: the pieces it names
    /// must be where it says they are. No legality check is made here.
    /// </summary>
    public void MakeMove(Move move)
    {
        Debug.Assert(!move.IsNone, "cannot make the null move");
        Debug.Assert(squares[move.From] == new Piece(SideToMove, move.Piece), "moving piece mismatch");

        history.Add(new UndoInfo(move, CastlingRights, EnPassantSquare, HalfMoveClock, Key, SecondaryKey, PawnKey));

        Colour us = SideToMove;
        Colour them = us.Opponent();

        SetEnPassantSquare(Squares.None);
        HalfMoveClock = move.Piece == PieceType.Pawn || move.IsCapture ? 0 : Math.Min(HalfMoveClock + 1, MaxHalfMoveClock);

        switch (move.Kind)
        {
            case MoveKind.Normal:
                if (move.IsCapture)
                {
                    RemovePiece(move.To);
                }
                MovePiece(move.From, move.To);
                break;

            case MoveKind.DoublePawnPush:
                MovePiece(move.From, move.To);
                // Record the en passant square only if an enemy pawn stands beside the pushed pawn
                int passed = (move.From + move.To) >> 1;
                Piece enemyPawn = new(them, PieceType.Pawn);
                if ((Squares.IsOnBoard(move.To - 1) && squares[move.To - 1] == enemyPawn)
                    || (Squares.IsOnBoard(move.To + 1) && squares[move.To + 1] == enemyPawn))
                {
                    SetEnPassantSquare(passed);
                }
                break;

            case MoveKind.EnPassant:
                RemovePiece(EnPassantCaptureSquare(move));
                MovePiece(move.From, move.To);
                break;

            case MoveKind.Promotion:
                if (move.IsCapture)
                {
                    RemovePiece(move.To);
                }
                RemovePiece(move.From);
                PutPiece(move.To, new Piece(us, move.Promotion));
                break;

            case MoveKind.CastleKingSide:
                MovePiece(move.From, move.To);
                MovePiece(move.To + 1, move.To - 1);    // Rook: h-file to f-file
                break;

            case MoveKind.CastleQueenSide:
                MovePiece(move.From, move.To);
                MovePiece(move.To - 2, move.To + 1);    // Rook: a-file to d-file
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(move), move.Kind, "unknown move kind");
        }

        // Moving the king or a rook off its home square, or capturing a rook on its home square,
        // forfeits the corresponding castling rights.
        CastlingRights lost = CastlingRightsExtensions.LostBySquare(move.From) | CastlingRightsExtensions.LostBySquare(move.To);
        if ((CastlingRights & lost) != CastlingRights.None)
        {
            SetCastlingRights(CastlingRights & ~lost);
        }

        if (us == Colour.Black)
        {
            FullMoveNumber++;
        }
        SwitchSideToMove(them);
    }

    /// <summary>
    /// Unmake the most recently made move.
    /// </summary>
    public void UnmakeMove()
    {
        if (history.Count == 0)
        {
            throw new InvalidOperationException("no move to unmake");
        }
        UndoInfo undo = history[^1];
        history.RemoveAt(history.Count - 1);
        Move move = undo.Move;

        Colour them = SideToMove;
        Colour us = them.Opponent();
        SideToMove = us;
        if (us == Colour.Black)
        {
            FullMoveNumber--;
        }

        switch (move.Kind)
        {
            case MoveKind.Normal:
            case MoveKind.DoublePawnPush:
                MovePiece(move.To, move.From);
                if (move.IsCapture)
                {
                    PutPiece(move.To, new Piece(them, move.Captured));
                }
                break;

            case MoveKind.EnPassant:
                MovePiece(move.To, move.From);
                PutPiece(EnPassantCaptureSquare(move), new Piece(them, PieceType.Pawn));
                break;

            case MoveKind.Promotion:
                RemovePiece(move.To);
                PutPiece(move.From, new Piece(us, PieceType.Pawn));
                if (move.IsCapture)
                {
                    PutPiece(move.To, new Piece(them, move.Captured));
                }
                break;

            case MoveKind.CastleKingSide:
                MovePiece(move.To, move.From);
                MovePiece(move.To - 1, move.To + 1);
                break;

            case MoveKind.CastleQueenSide:
                MovePiece(move.To, move.From);
                MovePiece(move.To + 1, move.To - 2);
                break;

            default:
                throw new InvalidOperationException("corrupt move history");
        }

        // The keys were changed by the piece movements above; restore the saved values directly.
        CastlingRights = undo.CastlingRights;
        EnPassantSquare = undo.EnPassantSquare;
        HalfMoveClock = undo.HalfMoveClock;
        Key = undo.Key;
        SecondaryKey = undo.SecondaryKey;
        PawnKey = undo.PawnKey;
    }

    /// <summary>
    /// The square of the pawn captured by an en passant move: the destination file on the source rank.
    /// </summary>
    public static int EnPassantCaptureSquare(Move move)
    {
        return Squares.Index(Squares.FileOf(move.To), Squares.RankOf(move.From));
    }
}
