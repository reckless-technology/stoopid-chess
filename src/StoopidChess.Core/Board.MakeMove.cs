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
        Debug.Assert(squares[move.From] == move.Piece && byColour[(int)SideToMove].Contains(move.From), "moving piece mismatch");

        history.Add(new UndoInfo(move, CastlingRights, EnPassantSquare, HalfMoveClock, Key, PawnKey));

        Colour us = SideToMove;
        Colour them = us.Opponent();

        SetEnPassantSquare(Square.None);
        HalfMoveClock = move.Piece == PieceType.Pawn || move.IsCapture ? 0 : Math.Min(HalfMoveClock + 1, MaxHalfMoveClock);

        switch (move.Kind)
        {
            case MoveKind.Normal:
                if (move.IsCapture)
                {
                    RemovePiece(move.To, them, move.Captured);
                }
                MovePiece(move.From, move.To, us, move.Piece);
                break;

            case MoveKind.DoublePawnPush:
                MovePiece(move.From, move.To, us, PieceType.Pawn);
                // Record the en passant square only if an enemy pawn could actually capture there
                var passed = Square.Unchecked((move.From.Index + move.To.Index) >> 1);
                if (IsAttackedByPawn(passed, them))
                {
                    SetEnPassantSquare(passed);
                }
                break;

            case MoveKind.EnPassant:
                RemovePiece(EnPassantCaptureSquare(move), them, PieceType.Pawn);
                MovePiece(move.From, move.To, us, PieceType.Pawn);
                break;

            case MoveKind.Promotion:
                if (move.IsCapture)
                {
                    RemovePiece(move.To, them, move.Captured);
                }
                RemovePiece(move.From, us, PieceType.Pawn);
                PutPiece(move.To, us, move.Promotion);
                break;

            case MoveKind.CastleKingSide:
                MovePiece(move.From, move.To, us, PieceType.King);
                MovePiece(move.To + 1, move.To - 1, us, PieceType.Rook);    // Rook: h-file to f-file
                break;

            case MoveKind.CastleQueenSide:
                MovePiece(move.From, move.To, us, PieceType.King);
                MovePiece(move.To - 2, move.To + 1, us, PieceType.Rook);    // Rook: a-file to d-file
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
    /// Pass the turn without moving, for null-move pruning in the search. Unmade like any other move
    /// with <see cref="UnmakeMove"/>. Repetitions are not detected across a null move.
    /// </summary>
    public void MakeNullMove()
    {
        history.Add(new UndoInfo(Move.None, CastlingRights, EnPassantSquare, HalfMoveClock, Key, PawnKey));
        SetEnPassantSquare(Square.None);
        HalfMoveClock = Math.Min(HalfMoveClock + 1, MaxHalfMoveClock);
        SwitchSideToMove(SideToMove.Opponent());
    }

    /// <summary>
    /// True if the last move made was a null move.
    /// </summary>
    public bool LastMoveWasNull => history.Count != 0 && history[^1].Move.IsNone;

    /// <summary>
    /// Unmake the most recently made move (or null move).
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
        checkersKnown = false;
        if (move.IsNone)
        {
            RestoreState(undo);
            return;
        }
        if (us == Colour.Black)
        {
            FullMoveNumber--;
        }

        switch (move.Kind)
        {
            case MoveKind.Normal:
            case MoveKind.DoublePawnPush:
                MovePiece(move.To, move.From, us, move.Piece);
                if (move.IsCapture)
                {
                    PutPiece(move.To, them, move.Captured);
                }
                break;

            case MoveKind.EnPassant:
                MovePiece(move.To, move.From, us, PieceType.Pawn);
                PutPiece(EnPassantCaptureSquare(move), them, PieceType.Pawn);
                break;

            case MoveKind.Promotion:
                RemovePiece(move.To, us, move.Promotion);
                PutPiece(move.From, us, PieceType.Pawn);
                if (move.IsCapture)
                {
                    PutPiece(move.To, them, move.Captured);
                }
                break;

            case MoveKind.CastleKingSide:
                MovePiece(move.To, move.From, us, PieceType.King);
                MovePiece(move.To - 1, move.To + 1, us, PieceType.Rook);
                break;

            case MoveKind.CastleQueenSide:
                MovePiece(move.To, move.From, us, PieceType.King);
                MovePiece(move.To + 1, move.To - 2, us, PieceType.Rook);
                break;

            default:
                throw new InvalidOperationException("corrupt move history");
        }

        RestoreState(undo);
    }

    /// <summary>
    /// The keys were changed by the piece movements of the unmake; restore the saved values directly.
    /// </summary>
    private void RestoreState(in UndoInfo undo)
    {
        CastlingRights = undo.CastlingRights;
        EnPassantSquare = undo.EnPassantSquare;
        HalfMoveClock = undo.HalfMoveClock;
        Key = undo.Key;
        PawnKey = undo.PawnKey;
    }

    /// <summary>
    /// The square of the pawn captured by an en passant move: the destination file on the source rank.
    /// </summary>
    public static Square EnPassantCaptureSquare(Move move) => Square.At(move.To.File, move.From.Rank);
}
