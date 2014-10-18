using System;
using System.Collections.Generic;
using System.Text;

namespace StoopidChess
{
    class PawnHashEntry
    {
        public readonly ulong hash;
        public readonly int score;
        public PawnHashEntry(ulong hash, int score)
        {
            this.hash = hash;
            this.score = score;
        }
    }
}
