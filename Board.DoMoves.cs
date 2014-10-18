using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

namespace StoopidChess
{
    /// <summary>
    /// Methods of the Board class for making and unmaking moves.
    /// It is essential that after making and unmaking a move, the
    /// board state is identical to what it was before the move was made.
    /// 
    /// Board state comprises the locations of all pieces, the castling rights
    /// flags, the en passant location, the hashes & the score.
    /// 
    /// Multiple moves may be made before any is unmade during the depth-first
    /// recursive search. This must be done in a strictly last-in first-out 
    /// sequence in order to correctly maintain board state.
    /// </summary>
    partial class Board
    {
        public void DoMove(Move move)
        {
            Debug.Assert(move.moveType != MoveType.NONE);
            Debug.Assert(this[move.from].colour != Colour.NONE);
            // Push the position history
            positionStack.Add(GetHash1(this[move.from].colour));
            // Push the move onto the move stack
            moveStack.Add(move);
            // Push the flags & last en passant location
            flagStack.Push(castlingFlags);
            enPassantStack.Push(enPassant);
            // Reset en passant location
            enPassant = NO_EN_PASSANT;

            switch (move.moveType)
            {
                case MoveType.NORMAL:
                    Debug.Assert(move.movingPiece == this[move.from].piece);
                    Debug.Assert(move.capturedPiece == this[move.to].piece);
                    Debug.Assert(move.movingPiece != PieceType.NONE);
                    Debug.Assert(this[move.to].colour != this[move.from].colour);
                    Debug.Assert(move.movingPiece != PieceType.KING);
                    Debug.Assert(move.movingPiece != PieceType.ROOK);

                    captureStack.Push(this[move.to]);
                    this[move.to] = this[move.from];
                    this[move.from] = Square.EMPTY;
                    break;

                case MoveType.EN_PASSANT:
                    Debug.Assert(move.movingPiece == this[move.from].piece);
                    Debug.Assert(move.capturedPiece == this[move.to].piece);
                    Debug.Assert(move.movingPiece == PieceType.PAWN);
                    Debug.Assert(move.capturedPiece == PieceType.NONE);
                    Debug.Assert(Math.Abs(move.to - move.from) == 0x20);
                    Debug.Assert(this[move.to] == Square.EMPTY);
                    Debug.Assert(this[(move.to + move.from) >> 1] == Square.EMPTY);

                    captureStack.Push(this[move.to]);
                    this[move.to] = this[move.from];
                    this[move.from] = Square.EMPTY;
                    enPassant = (move.to + move.from) >> 1;
                    break;

                case MoveType.EN_PASSANT_CAPTURE:
                    Debug.Assert(move.movingPiece == PieceType.PAWN);
                    Debug.Assert(move.capturedPiece == PieceType.PAWN);
                    Debug.Assert(this[(move.from & Board.RANK) + (move.to & Board.FILE)].piece == PieceType.PAWN);
                    Debug.Assert(this[(move.from & Board.RANK) + (move.to & Board.FILE)].colour == (this[move.from].colour ^ Colour.XOR));
                    Debug.Assert(this[move.from].piece == PieceType.PAWN);
                    Debug.Assert(this[move.to] == Square.EMPTY);

                    int captureLocation = (move.from & Board.RANK) + (move.to & Board.FILE);
                    captureStack.Push(this[captureLocation]);
                    this[move.to] = this[move.from];
                    this[move.from] = Square.EMPTY;
                    this[captureLocation] = Square.EMPTY;
                    break;

                case MoveType.CASTLE_KING_SIDE_WHITE:
                    Debug.Assert(move.movingPiece == PieceType.KING);
                    Debug.Assert(move.capturedPiece == PieceType.NONE);
                    Debug.Assert(move.from == 0x04);
                    Debug.Assert(move.to == 0x06);
                    Debug.Assert(this[0x04] == new Square(Colour.WHITE, PieceType.KING));
                    Debug.Assert(this[0x07] == new Square(Colour.WHITE, PieceType.ROOK));
                    Debug.Assert(this[0x05] == Square.EMPTY);
                    Debug.Assert(this[0x06] == Square.EMPTY);
                    Debug.Assert((castlingFlags & CastlingFlags.MAY_WHITE_CASTLE_KING_SIDE) != CastlingFlags.NONE);

                    this[0x06] = Square.WHITE_KING;
                    this[0x05] = Square.WHITE_ROOK;
                    this[0x04] = Square.EMPTY;
                    this[0x07] = Square.EMPTY;
                    castlingFlags &= ~(CastlingFlags.MAY_WHITE_CASTLE_KING_SIDE | CastlingFlags.MAY_WHITE_CASTLE_QUEEN_SIDE);
                    break;

                case MoveType.CASTLE_QUEEN_SIDE_WHITE:
                    Debug.Assert(move.movingPiece == PieceType.KING);
                    Debug.Assert(move.capturedPiece == PieceType.NONE);
                    Debug.Assert(move.from == 0x04);
                    Debug.Assert(move.to == 0x02);
                    Debug.Assert(this[0x04] == new Square(Colour.WHITE, PieceType.KING));
                    Debug.Assert(this[0x00] == new Square(Colour.WHITE, PieceType.ROOK));
                    Debug.Assert(this[0x01] == Square.EMPTY);
                    Debug.Assert(this[0x02] == Square.EMPTY);
                    Debug.Assert(this[0x03] == Square.EMPTY);
                    Debug.Assert((castlingFlags & CastlingFlags.MAY_WHITE_CASTLE_QUEEN_SIDE) != CastlingFlags.NONE);

                    this[0x02] = Square.WHITE_KING;
                    this[0x03] = Square.WHITE_ROOK;
                    this[0x00] = Square.EMPTY;
                    this[0x04] = Square.EMPTY;
                    castlingFlags &= ~(CastlingFlags.MAY_WHITE_CASTLE_KING_SIDE | CastlingFlags.MAY_WHITE_CASTLE_QUEEN_SIDE);
                    break;

                case MoveType.CASTLE_KING_SIDE_BLACK:
                    Debug.Assert(move.movingPiece == PieceType.KING);
                    Debug.Assert(move.capturedPiece == PieceType.NONE);
                    Debug.Assert(move.from == 0x74);
                    Debug.Assert(move.to == 0x76);
                    Debug.Assert(this[0x74] == new Square(Colour.BLACK, PieceType.KING));
                    Debug.Assert(this[0x77] == new Square(Colour.BLACK, PieceType.ROOK));
                    Debug.Assert(this[0x75] == Square.EMPTY);
                    Debug.Assert(this[0x76] == Square.EMPTY);
                    Debug.Assert((castlingFlags & CastlingFlags.MAY_BLACK_CASTLE_KING_SIDE) != CastlingFlags.NONE);

                    this[0x76] = Square.BLACK_KING;
                    this[0x75] = Square.BLACK_ROOK;
                    this[0x74] = Square.EMPTY;
                    this[0x77] = Square.EMPTY;
                    castlingFlags &= ~(CastlingFlags.MAY_BLACK_CASTLE_KING_SIDE | CastlingFlags.MAY_BLACK_CASTLE_QUEEN_SIDE);
                    break;

                case MoveType.CASTLE_QUEEN_SIDE_BLACK:
                    Debug.Assert(move.movingPiece == PieceType.KING);
                    Debug.Assert(move.capturedPiece == PieceType.NONE);
                    Debug.Assert(move.from == 0x74);
                    Debug.Assert(move.to == 0x72);
                    Debug.Assert(this[0x74] == new Square(Colour.BLACK, PieceType.KING));
                    Debug.Assert(this[0x70] == new Square(Colour.BLACK, PieceType.ROOK));
                    Debug.Assert(this[0x71] == Square.EMPTY);
                    Debug.Assert(this[0x72] == Square.EMPTY);
                    Debug.Assert(this[0x73] == Square.EMPTY);
                    Debug.Assert((castlingFlags & CastlingFlags.MAY_BLACK_CASTLE_QUEEN_SIDE) != CastlingFlags.NONE);

                    this[0x72] = Square.BLACK_KING;
                    this[0x73] = Square.BLACK_ROOK;
                    this[0x70] = Square.EMPTY;
                    this[0x74] = Square.EMPTY;
                    castlingFlags &= ~(CastlingFlags.MAY_BLACK_CASTLE_KING_SIDE | CastlingFlags.MAY_BLACK_CASTLE_QUEEN_SIDE);
                    break;

                case MoveType.PAWN_PROMOTION_TO_KNIGHT:
                    Debug.Assert(move.movingPiece == PieceType.PAWN);
                    Debug.Assert(this[move.to].piece == move.capturedPiece);
                    Debug.Assert(this[move.from].piece == PieceType.PAWN);
                    Debug.Assert(this[move.to].colour != this[move.from].colour);
                    Debug.Assert((move.to & Board.RANK) == (this[move.from].colour == Colour.WHITE ? 0x70 : 0x00));

                    captureStack.Push(this[move.to]);
                    this[move.to] = new Square(this[move.from].colour, PieceType.KNIGHT);
                    this[move.from] = Square.EMPTY;
                    break;

                case MoveType.PAWN_PROMOTION_TO_QUEEN:
                    Debug.Assert(move.movingPiece == PieceType.PAWN);
                    Debug.Assert(this[move.from].piece == PieceType.PAWN);
                    Debug.Assert(this[move.to].piece == move.capturedPiece);
                    Debug.Assert(this[move.to].colour != this[move.from].colour);
                    Debug.Assert((move.to & Board.RANK) == (this[move.from].colour == Colour.WHITE ? 0x70 : 0x00));

                    captureStack.Push(this[move.to]);
                    this[move.to] = new Square(this[move.from].colour, PieceType.QUEEN);
                    this[move.from] = Square.EMPTY;
                    break;

                case MoveType.KING:
                    Debug.Assert(this[move.from].piece == PieceType.KING);
                    Debug.Assert(move.movingPiece == PieceType.KING);
                    Debug.Assert(move.capturedPiece == this[move.to].piece);
                    Debug.Assert(move.capturedPiece != PieceType.KING);
                    Debug.Assert(this[move.to].colour != this[move.from].colour);

                    captureStack.Push(this[move.to]);
                    if (this[move.from].colour == Colour.WHITE)
                    {
                        castlingFlags &= ~(CastlingFlags.MAY_WHITE_CASTLE_KING_SIDE | CastlingFlags.MAY_WHITE_CASTLE_QUEEN_SIDE);
                    }
                    else
                    {
                        castlingFlags &= ~(CastlingFlags.MAY_BLACK_CASTLE_KING_SIDE | CastlingFlags.MAY_BLACK_CASTLE_QUEEN_SIDE);
                    }
                    this[move.to] = this[move.from];
                    this[move.from] = Square.EMPTY;
                    break;

                case MoveType.ROOK:
                    Debug.Assert(this[move.from].piece == PieceType.ROOK);
                    Debug.Assert(move.movingPiece == PieceType.ROOK);
                    Debug.Assert(move.capturedPiece == this[move.to].piece);
                    Debug.Assert(this[move.to].colour != this[move.from].colour);

                    captureStack.Push(this[move.to]);
                    if (this[move.from].colour == Colour.WHITE)
                    {
                        if (move.from == 0x07)
                        {
                            castlingFlags &= ~CastlingFlags.MAY_WHITE_CASTLE_KING_SIDE;
                        }
                        else if (move.from == 0x00)
                        {
                            castlingFlags &= ~CastlingFlags.MAY_WHITE_CASTLE_QUEEN_SIDE;
                        }
                    }
                    else
                    {
                        if (move.from == 0x77)
                        {
                            castlingFlags &= ~CastlingFlags.MAY_BLACK_CASTLE_KING_SIDE;
                        }
                        else if (move.from == 0x70)
                        {
                            castlingFlags &= ~CastlingFlags.MAY_BLACK_CASTLE_QUEEN_SIDE;
                        }
                    }
                    this[move.to] = this[move.from];
                    this[move.from] = Square.EMPTY;
                    break;

                default:
                    throw new ApplicationException("Switch missed");
            }
        }

        public bool UndoLastMove()
        {
            if (moveStack.Count == 0)
            {
                return false;
            }
            Move move = moveStack[moveStack.Count - 1];
            moveStack.RemoveAt(moveStack.Count - 1);
            Debug.Assert(move.moveType != MoveType.NONE);
            switch (move.moveType)
            {
                case MoveType.NORMAL:
                case MoveType.EN_PASSANT:
                case MoveType.KING:
                case MoveType.ROOK:
                    Debug.Assert(this[move.from] == Square.EMPTY);
                    Debug.Assert(captureStack.Peek().colour != this[move.to].colour);

                    this[move.from] = this[move.to];
                    this[move.to] = captureStack.Pop();
                    break;

                case MoveType.EN_PASSANT_CAPTURE:
                    Debug.Assert(captureStack.Peek().piece == PieceType.PAWN);
                    Debug.Assert(captureStack.Peek().colour == (this[move.to].colour ^ Colour.XOR));
                    Debug.Assert(this[(move.from & Board.RANK) + (move.to & Board.FILE)] == Square.EMPTY);
                    Debug.Assert(this[move.from] == Square.EMPTY);

                    int captureLocation = (move.from & Board.RANK) + (move.to & Board.FILE);
                    this[captureLocation] = captureStack.Pop();
                    this[move.from] = this[move.to];
                    this[move.to] = Square.EMPTY;
                    break;

                case MoveType.CASTLE_KING_SIDE_WHITE:
                    Debug.Assert(this[0x06] == new Square(Colour.WHITE, PieceType.KING));
                    Debug.Assert(this[0x05] == new Square(Colour.WHITE, PieceType.ROOK));
                    Debug.Assert(this[0x04] == Square.EMPTY);
                    Debug.Assert(this[0x07] == Square.EMPTY);
                    Debug.Assert((castlingFlags & CastlingFlags.MAY_WHITE_CASTLE_KING_SIDE) == CastlingFlags.NONE);
                    Debug.Assert((castlingFlags & CastlingFlags.MAY_WHITE_CASTLE_QUEEN_SIDE) == CastlingFlags.NONE);

                    this[0x04] = Square.WHITE_KING;
                    this[0x07] = Square.WHITE_ROOK;
                    this[0x05] = Square.EMPTY;
                    this[0x06] = Square.EMPTY;
                    break;

                case MoveType.CASTLE_QUEEN_SIDE_WHITE:
                    Debug.Assert(this[0x02] == new Square(Colour.WHITE, PieceType.KING));
                    Debug.Assert(this[0x03] == new Square(Colour.WHITE, PieceType.ROOK));
                    Debug.Assert(this[0x00] == Square.EMPTY);
                    Debug.Assert(this[0x01] == Square.EMPTY);
                    Debug.Assert(this[0x04] == Square.EMPTY);
                    Debug.Assert((castlingFlags & CastlingFlags.MAY_WHITE_CASTLE_KING_SIDE) == CastlingFlags.NONE);
                    Debug.Assert((castlingFlags & CastlingFlags.MAY_WHITE_CASTLE_QUEEN_SIDE) == CastlingFlags.NONE);

                    this[0x04] = Square.WHITE_KING;
                    this[0x00] = Square.WHITE_ROOK;
                    this[0x02] = Square.EMPTY;
                    this[0x03] = Square.EMPTY;
                    break;

                case MoveType.CASTLE_KING_SIDE_BLACK:
                    Debug.Assert(this[0x76] == new Square(Colour.BLACK, PieceType.KING));
                    Debug.Assert(this[0x75] == new Square(Colour.BLACK, PieceType.ROOK));
                    Debug.Assert(this[0x74] == Square.EMPTY);
                    Debug.Assert(this[0x77] == Square.EMPTY);
                    Debug.Assert((castlingFlags & CastlingFlags.MAY_BLACK_CASTLE_KING_SIDE) == CastlingFlags.NONE);
                    Debug.Assert((castlingFlags & CastlingFlags.MAY_BLACK_CASTLE_QUEEN_SIDE) == CastlingFlags.NONE);

                    this[0x74] = Square.BLACK_KING;
                    this[0x77] = Square.BLACK_ROOK;
                    this[0x75] = Square.EMPTY;
                    this[0x76] = Square.EMPTY;
                    break;

                case MoveType.CASTLE_QUEEN_SIDE_BLACK:
                    Debug.Assert(this[0x72] == new Square(Colour.BLACK, PieceType.KING));
                    Debug.Assert(this[0x73] == new Square(Colour.BLACK, PieceType.ROOK));
                    Debug.Assert(this[0x70] == Square.EMPTY);
                    Debug.Assert(this[0x71] == Square.EMPTY);
                    Debug.Assert(this[0x74] == Square.EMPTY);
                    Debug.Assert((castlingFlags & CastlingFlags.MAY_BLACK_CASTLE_KING_SIDE) == CastlingFlags.NONE);
                    Debug.Assert((castlingFlags & CastlingFlags.MAY_BLACK_CASTLE_QUEEN_SIDE) == CastlingFlags.NONE);

                    this[0x74] = Square.BLACK_KING;
                    this[0x70] = Square.BLACK_ROOK;
                    this[0x72] = Square.EMPTY;
                    this[0x73] = Square.EMPTY;
                    break;

                case MoveType.PAWN_PROMOTION_TO_KNIGHT:
                case MoveType.PAWN_PROMOTION_TO_QUEEN:
                    Debug.Assert(this[move.to].piece == PieceType.KNIGHT || this[move.to].piece == PieceType.QUEEN);
                    Debug.Assert((move.from & Board.RANK) == (this[move.to].colour == Colour.WHITE ? 0x60 : 0x10));
                    Debug.Assert(this[move.from] == Square.EMPTY);

                    this[move.from] = new Square(this[move.to].colour, PieceType.PAWN);
                    this[move.to] = captureStack.Pop();
                    break;

                default:
                    throw new ApplicationException("Switch missed");
            }
            // Restore previous en passant location and board flags
            enPassant = enPassantStack.Pop();
            castlingFlags = flagStack.Pop();
            // Pop the position history
            positionStack.RemoveAt(positionStack.Count - 1);    // This should be an O(1) operation
            return true;
        }
    }
}