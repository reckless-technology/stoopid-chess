using System;
using System.Collections.Generic;
using System.Text;

namespace StoopidChess
{
    /// <summary>
    /// Class to represent an entry in check status hash table
    /// </summary>
    class CheckHashEntry
    {
        /// <summary>
        /// Instance variables
        /// </summary>
        public readonly ulong hash1;    // Zobrist hash for board position (includes colour)
        public readonly bool isInCheck; // Check status
        /// <summary>
        /// Constructor
        /// </summary>
        public CheckHashEntry(ulong hash1, bool isInCheck)
        {
            this.hash1 = hash1;
            this.isInCheck = isInCheck;
        }
        /// <summary>
        /// Disallow default constructor
        /// </summary>
        private CheckHashEntry()
        {
        }
    }
}
