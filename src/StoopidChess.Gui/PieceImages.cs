using StoopidChess.Core;

namespace StoopidChess.Gui;

/// <summary>
/// The piece bitmaps from the embedded resources, indexed by colour and piece type.
/// </summary>
internal static class PieceImages
{
    private static readonly Dictionary<(Colour, PieceType), Bitmap> Images = new()
    {
        [(Colour.White, PieceType.Pawn)] = ChessPieceImages.WhitePawn,
        [(Colour.White, PieceType.Knight)] = ChessPieceImages.WhiteKnight,
        [(Colour.White, PieceType.Bishop)] = ChessPieceImages.WhiteBishop,
        [(Colour.White, PieceType.Rook)] = ChessPieceImages.WhiteRook,
        [(Colour.White, PieceType.Queen)] = ChessPieceImages.WhiteQueen,
        [(Colour.White, PieceType.King)] = ChessPieceImages.WhiteKing,
        [(Colour.Black, PieceType.Pawn)] = ChessPieceImages.BlackPawn,
        [(Colour.Black, PieceType.Knight)] = ChessPieceImages.BlackKnight,
        [(Colour.Black, PieceType.Bishop)] = ChessPieceImages.BlackBishop,
        [(Colour.Black, PieceType.Rook)] = ChessPieceImages.BlackRook,
        [(Colour.Black, PieceType.Queen)] = ChessPieceImages.BlackQueen,
        [(Colour.Black, PieceType.King)] = ChessPieceImages.BlackKing,
    };

    public static Bitmap Get(Colour colour, PieceType type) => Images[(colour, type)];
}
