namespace StoopidChess
{
    /// <summary>
    /// Methods of the Board class for saving and restoring the board state.
    /// </summary>
    partial class Board
    {
        /// <summary>
        /// Capture the board state in a form which can be saved to disk.
        /// The board is temporarily rewound to the start of the move history
        /// so that the starting position can be recorded, then the moves are replayed.
        /// </summary>
        public SavedPosition GetSavedPosition()
        {
            List<Move> moves = new List<Move>(moveStack);
            for (int i = 0; i != moves.Count; i++)
            {
                UndoLastMove();
            }
            SavedPosition result = new SavedPosition
            {
                Squares = new int[64],
                CastlingFlags = castlingFlags,
                EnPassant = enPassant,
                // Stack enumerates top to bottom; record bottom to top so it can be pushed back in order
                PriorCaptures = [.. captureStack.Reverse().Select(SavedPosition.Encode)],
                Moves = [.. moves.Select(SavedMove.FromMove)],
            };
            for (int y = 0; y != 8; y++)
            {
                for (int x = 0; x != 8; x++)
                {
                    result.Squares[x + (y << 3)] = SavedPosition.Encode(this[Index(x, y)]);
                }
            }
            foreach (Move move in moves)
            {
                DoMove(move);
            }
            return result;
        }
        /// <summary>
        /// Replace the board state with a saved position, validating as we go.
        /// </summary>
        /// <returns>false if the saved position is invalid, in which case the board contents are undefined</returns>
        public bool LoadSavedPosition(SavedPosition saved)
        {
            if (saved.Squares.Length != 64)
            {
                return false;
            }
            // Clear the board (via the indexer so the hashes, score and piece lists are maintained)
            for (int i = 0; i != theBoard.Length; i++)
            {
                this[i] = Square.EMPTY;
            }
            flagStack.Clear();
            enPassantStack.Clear();
            captureStack.Clear();
            moveStack.Clear();
            positionStack.Clear();
            // Starting position
            for (int y = 0; y != 8; y++)
            {
                for (int x = 0; x != 8; x++)
                {
                    Square? square = SavedPosition.Decode(saved.Squares[x + (y << 3)]);
                    if (square == null)
                    {
                        return false;
                    }
                    this[Index(x, y)] = square;
                }
            }
            if ((saved.CastlingFlags & ~CastlingFlags.ALL_CASTLE_RIGHTS) != CastlingFlags.NONE)
            {
                return false;
            }
            castlingFlags = saved.CastlingFlags;
            if (saved.EnPassant != NO_EN_PASSANT && (saved.EnPassant & OFF_BOARD) != 0)
            {
                return false;
            }
            enPassant = saved.EnPassant;
            // NB: the capture stack holds an entry for every move made, EMPTY for non-captures
            foreach (int capture in saved.PriorCaptures)
            {
                Square? square = SavedPosition.Decode(capture);
                if (square == null)
                {
                    return false;
                }
                captureStack.Push(square);
            }
            // Replay the moves. Each must be legal from the position reached so far,
            // which requires both kings to be present.
            if (saved.Moves.Length != 0 &&
                (LocationsOf(Colour.WHITE, PieceType.KING).Count != 1 || LocationsOf(Colour.BLACK, PieceType.KING).Count != 1))
            {
                return false;
            }
            foreach (SavedMove savedMove in saved.Moves)
            {
                Move move = savedMove.ToMove();
                if ((move.from & OFF_BOARD) != 0 || (move.to & OFF_BOARD) != 0)
                {
                    return false;
                }
                Colour colour = this[move.from].colour;
                if (colour == Colour.NONE || !MoveGenerator.GenerateStrictlyLegalMoves(this, colour).Contains(move))
                {
                    return false;
                }
                DoMove(move);
            }
            return true;
        }
    }
}
