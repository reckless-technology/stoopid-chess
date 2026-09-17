using System;
using System.Collections.Generic;
using System.Text;

namespace StoopidChess
{
    /// <summary>
    /// Class to represent an entry in the transposition hash table
    /// </summary>
    class Transposition
    {
        /// <summary>
        /// Instance variables
        /// </summary>
        public readonly ulong hash1;            // Primary 64-bit Zobrist hash for this position
        public readonly ulong hash2;            // Secondary hash (to detect possible hash collisions)
        public readonly int depth;              // Depth to which node was search
        public readonly NodeType nodeType;      // Did this node cause a cutoff or did we determine an exact score?
        public readonly int value;              // Score (or bounding value) for the subtree from this node
        public readonly Move bestMove;          // Best move found (or move which caused beta cutoff)

        // For debug only
        public static int numberOfTranspositionsCreated = 0;

        public Transposition(ulong hash1, ulong hash2, int depth, NodeType nodeType, int value, Move bestMove)
        {
            this.hash1 = hash1;
            this.hash2 = hash2;        
            this.depth = depth;
            this.nodeType = nodeType;
            this.value = value;
            this.bestMove = bestMove;

            numberOfTranspositionsCreated++;
        }
    }
}
