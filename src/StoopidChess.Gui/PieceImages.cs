using StoopidChess.Core;

namespace StoopidChess.Gui;

/// <summary>
/// The piece bitmaps from the embedded resources, indexed by piece.
/// </summary>
internal static class PieceImages
{
    private static readonly Dictionary<Piece, Bitmap> Images = new()
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

    public static Bitmap Get(Piece piece) => Images[piece];
}
