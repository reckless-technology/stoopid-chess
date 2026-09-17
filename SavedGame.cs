using System.Text.Json;
using System.Text.Json.Serialization;

namespace StoopidChess
{
    /// <summary>
    /// A move in a form suitable for saving to disk
    /// </summary>
    class SavedMove
    {
        public int From { get; set; }
        public int To { get; set; }
        public PieceType MovingPiece { get; set; }
        public MoveType MoveType { get; set; }
        public PieceType CapturedPiece { get; set; }

        public static SavedMove FromMove(Move move)
        {
            return new SavedMove
            {
                From = move.from,
                To = move.to,
                MovingPiece = move.movingPiece,
                MoveType = move.moveType,
                CapturedPiece = move.capturedPiece,
            };
        }

        public Move ToMove()
        {
            return new Move(From, To, MovingPiece, MoveType, CapturedPiece);
        }
    }
    /// <summary>
    /// The engine state needed to reconstruct a Board: the position at the start
    /// of the recorded move history, any pieces captured before that point (following
    /// a positional edit) and the moves made since. The board is rebuilt by replaying
    /// the moves, which regenerates all the incremental state (hashes, score, piece
    /// location lists and the undo stacks).
    /// </summary>
    class SavedPosition
    {
        /// <summary>
        /// Squares a1..h8 (file fastest) each encoded as (colour &lt;&lt; 3) | piece
        /// </summary>
        public int[] Squares { get; set; } = [];
        public CastlingFlags CastlingFlags { get; set; }
        public int EnPassant { get; set; } = Board.NO_EN_PASSANT;
        public int[] PriorCaptures { get; set; } = [];
        public SavedMove[] Moves { get; set; } = [];

        public static int Encode(Square square)
        {
            return ((int)square.colour << 3) | (int)square.piece;
        }
        /// <summary>
        /// Decode a square, returning null if the value is not a valid square
        /// </summary>
        public static Square? Decode(int value)
        {
            Colour colour = (Colour)(value >> 3);
            PieceType piece = (PieceType)(value & 0x07);
            if (colour is not (Colour.NONE or Colour.WHITE or Colour.BLACK) || piece > PieceType.KING)
            {
                return null;
            }
            if ((colour == Colour.NONE) != (piece == PieceType.NONE))
            {
                return null;
            }
            return colour == Colour.NONE ? Square.EMPTY : new Square(colour, piece);
        }
    }
    /// <summary>
    /// The contents of a Stoopid Chess saved game file (JSON)
    /// </summary>
    class SavedGame
    {
        public const string SIGNATURE = "Stoopid Chess saved game";
        public const int CURRENT_VERSION = 1;

        public string Signature { get; set; } = SIGNATURE;
        public int Version { get; set; } = CURRENT_VERSION;
        public SavedPosition Position { get; set; } = new SavedPosition();
        public Colour HumanColour { get; set; }
        public Colour ColourToMove { get; set; }
        public bool IsBoardFlipped { get; set; }

        private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
        /// <summary>
        /// Write the saved game to a stream
        /// </summary>
        public void Save(Stream stream)
        {
            JsonSerializer.Serialize(stream, this, jsonOptions);
        }
        /// <summary>
        /// Read a saved game from a stream
        /// </summary>
        /// <returns>null if the stream does not contain a valid Stoopid Chess saved game</returns>
        public static SavedGame? Load(Stream stream)
        {
            try
            {
                SavedGame? savedGame = JsonSerializer.Deserialize<SavedGame>(stream, jsonOptions);
                if (savedGame == null || savedGame.Signature != SIGNATURE || savedGame.Version != CURRENT_VERSION)
                {
                    return null;
                }
                if (savedGame.HumanColour is not (Colour.WHITE or Colour.BLACK) ||
                    savedGame.ColourToMove is not (Colour.WHITE or Colour.BLACK))
                {
                    return null;
                }
                return savedGame;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
