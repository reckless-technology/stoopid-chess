using System;
namespace StoopidChess
{
    // This file contains various enumerations used by the application
    [Flags]
    enum CastlingFlags
    {
        // Bit field for castling rights - also part of board hash
        NONE                        = 0x00,
        MAY_WHITE_CASTLE_QUEEN_SIDE = 0x01,
        MAY_WHITE_CASTLE_KING_SIDE  = 0x02,
        MAY_BLACK_CASTLE_QUEEN_SIDE = 0x04,
        MAY_BLACK_CASTLE_KING_SIDE  = 0x08,
        ALL_CASTLE_RIGHTS           = 0x0F
    }
    enum Colour 
    {
        NONE  = 0,
        WHITE = 1,
        BLACK = 2,
        XOR   = WHITE ^ BLACK // Used to switch sides
    }
    enum PieceType 
    {
        NONE     = 0,
        PAWN     = 1,
        KNIGHT   = 2,
        BISHOP   = 3,
        ROOK     = 4,
        QUEEN    = 5,
        KING     = 6,
        KING_END = 7        // Never placed in the board; used for piece-square evaluation only
    }
    enum NodeType           // Used to determine the type of entry in transposition hash table
    {
        NONE  = 0,
        EXACT = 1,
        BETA  = 3
    }
    enum MoveType           // Required by the DoMove() and UndoMove() methods of the Board class
    {                      
        NONE                     = 0,
        NORMAL                   = 1,
        EN_PASSANT               = 2,    // Pawn moves forward by 2 squares - affects en passant status
        EN_PASSANT_CAPTURE       = 3,    // Pawn takes pawn en passant
        CASTLE_KING_SIDE_WHITE   = 4,
        CASTLE_QUEEN_SIDE_WHITE  = 5,
        CASTLE_KING_SIDE_BLACK   = 6,
        CASTLE_QUEEN_SIDE_BLACK  = 7,
        PAWN_PROMOTION_TO_KNIGHT = 8,
        PAWN_PROMOTION_TO_QUEEN  = 9, 
        KING                     = 10,   // Affects castling flags
        ROOK                     = 11    // Affects castling flags
    }
}
