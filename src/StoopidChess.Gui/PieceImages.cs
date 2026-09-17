using StoopidChess.Core;

namespace StoopidChess.Gui;

/// <summary>
/// The piece bitmaps from the embedded resources, indexed by piece.
/// </summary>
internal sealed class PieceImages
{
    private readonly Dictionary<Piece, Bitmap> images = new()
    {
        [Piece.WhitePawn] = ChessPieceImages.WhitePawn,
        [Piece.WhiteKnight] = ChessPieceImages.WhiteKnight,
        [Piece.WhiteBishop] = ChessPieceImages.WhiteBishop,
        [Piece.WhiteRook] = ChessPieceImages.WhiteRook,
        [Piece.WhiteQueen] = ChessPieceImages.WhiteQueen,
        [Piece.WhiteKing] = ChessPieceImages.WhiteKing,
        [Piece.BlackPawn] = ChessPieceImages.BlackPawn,
        [Piece.BlackKnight] = ChessPieceImages.BlackKnight,
        [Piece.BlackBishop] = ChessPieceImages.BlackBishop,
        [Piece.BlackRook] = ChessPieceImages.BlackRook,
        [Piece.BlackQueen] = ChessPieceImages.BlackQueen,
        [Piece.BlackKing] = ChessPieceImages.BlackKing,
    };

    public Bitmap this[Piece piece] => images[piece];
}
