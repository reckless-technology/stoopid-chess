using StoopidChess.Core;
using Color = StoopidChess.Core.Color;   // The chess colour, not System.Drawing.Color

namespace StoopidChess.Gui;

/// <summary>
/// The piece bitmaps from the embedded resources, indexed by color and piece type.
/// </summary>
internal static class PieceImages
{
    private static readonly Dictionary<(Color, Piece), Bitmap> Images = new()
    {
        [(Color.White, Piece.Pawn)] = ChessPieceImages.WhitePawn,
        [(Color.White, Piece.Knight)] = ChessPieceImages.WhiteKnight,
        [(Color.White, Piece.Bishop)] = ChessPieceImages.WhiteBishop,
        [(Color.White, Piece.Rook)] = ChessPieceImages.WhiteRook,
        [(Color.White, Piece.Queen)] = ChessPieceImages.WhiteQueen,
        [(Color.White, Piece.King)] = ChessPieceImages.WhiteKing,
        [(Color.Black, Piece.Pawn)] = ChessPieceImages.BlackPawn,
        [(Color.Black, Piece.Knight)] = ChessPieceImages.BlackKnight,
        [(Color.Black, Piece.Bishop)] = ChessPieceImages.BlackBishop,
        [(Color.Black, Piece.Rook)] = ChessPieceImages.BlackRook,
        [(Color.Black, Piece.Queen)] = ChessPieceImages.BlackQueen,
        [(Color.Black, Piece.King)] = ChessPieceImages.BlackKing,
    };

    public static Bitmap Get(Color color, Piece type) => Images[(color, type)];
}
