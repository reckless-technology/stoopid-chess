using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace StoopidChess
{
    // Callback from engine to UI to allow user to select whether to promote 
    // a pawn to a queen or a knight upon reaching the back rank
    delegate PieceType PawnPromotionCallback(Colour colour);
    // Handler delegate for the computer thinking completed event
    delegate void ComputerThinkingCompletedHandler(Move move);
    // Struct for holding move co-ordinates in 2D (i.e. non 0x88) format
    // as used by the UI and interface
    struct MoveFromTo
    {
        public int xfrom;
        public int xto;
        public int yfrom;
        public int yto;
    }
    /// <summary>
    /// Interface between the chess engine and GUI
    /// </summary>
    interface IChessEngine
    {
        // Get both sides' captured pieces
        List<Square>[] GetCaptureBins();
        // Get the current board (8 x 8 array of squares)
        Square[,] GetBoard();     
        // Make a move
        void DoMove(Move move);
        // Undo the last move made - returns false if there was no move available to undo
        bool UndoLastMove();
        // Determine legality of a move for the colour to play
        bool IsMoveLegal(Move move, Colour colour);
        // Is colour in check?
        bool IsInCheck(Colour colour);
        // Is colour checkmated?
        bool IsCheckmated(Colour colour);
        // Is the game stalemate (with colour the side to play)?
        bool IsDrawn(Colour colour);
        // Determine a Move type from start and end square locations (used for human move generation)
        // Requires a delegate to return the human's choice of pawn promotion in the event of
        // moving a pawn to the back rank
        Move DetermineMove(MoveFromTo coords, Colour colour, PawnPromotionCallback callback);
        // Start the computer move search on a worker thread, for the colour specified
        void StartComputerThinking(Colour colour);
        // Set limit on the computer move search time
        void SetMaxThinkingTime(int milliseconds);
        // Set limit on the computer move search depth
        void SetMaxDepth(int depth);
        // Serialize the current game state (save game)
        void Serialize(Stream stream);
        // Deserialize the current game state (load game)
        IChessEngine Deserialize(Stream stream);
        // Event which is fired when the computer move worker thread has completed
        event ComputerThinkingCompletedHandler ComputerThinkingCompletedEvent;
        // Get a string containing the moves so far in the game in standard algebraic notation
        string GetMoveHistory();
        // Place a piece (or an empty square) at a specified location
        void EditSquare(Square square, int x, int y);
        // Determine if both kings are still present (following a position edit)
        bool IsPositionLegal();
        // Start a new game
        void NewGame();
    }
}
