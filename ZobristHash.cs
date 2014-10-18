using System;
using System.Collections.Generic;
using System.Text;

namespace StoopidChess
{
    /// <summary>
    /// Static class to implement Zobrist hash keys for a chess position
    /// The positional hash is used to index into the transposition table
    /// Refer to http://en.wikipedia.org/wiki/Zobrist_hashing for details
    /// </summary>
    static class ZobristHash
    {
        /// <summary>
        /// Coefficients for the 64 bit linear congruential generators
        /// used to synthesize hash keys. There are 2 independent
        /// generators to reduce the risk of hash collisions.
        /// </summary>
        private const ulong MULTIPLIER_1    = 0x27BB2EE687B0B0FD;
        private const ulong MULTILPIER_2    = 0x5851F42D4C957F2D;
        private const ulong INCREMENT_1     = 0x00000000B504F32D;
        private const ulong INCREMENT_2     = 0x14057B7EF767814F;
        private const ulong SEED_1          = 0x72B32F6D517CD491;
        private const ulong SEED_2          = 0xF3ABD1C72371D8D3;

        public static readonly ulong isBlackToMoveHash1;
        public static readonly ulong isBlackToMoveHash2;

        private static readonly ulong[, ,] piecesHash1;
        private static readonly ulong[, ,] piecesHash2;
        private static readonly ulong[] castlingRightsHash1;
        private static readonly ulong[] castlingRightsHash2;
        private static readonly ulong[] enPassantLocationHash1;
        private static readonly ulong[] enPassantLocationHash2;
        private static ulong lcg1;
        private static ulong lcg2;

        static ZobristHash()
        {
            lcg1 = SEED_1;
            lcg2 = SEED_2;
            piecesHash1 = new ulong[3, 7, 128];
            piecesHash2 = new ulong[3, 7, 128];
            castlingRightsHash1 = new ulong[16];
            castlingRightsHash2 = new ulong[16];
            enPassantLocationHash1 = new ulong[128];
            enPassantLocationHash2 = new ulong[128];
            isBlackToMoveHash1 = NextKey1();
            isBlackToMoveHash2 = NextKey2();
            for (Colour colour = Colour.NONE; colour <= Colour.BLACK; colour++)
            {
                for (PieceType piece = PieceType.NONE; piece <= PieceType.KING; piece++)
                {
                    for (int location = 0; location != 128; location++)
                    {
                        piecesHash1[(int)colour, (int)piece, location] = NextKey1();
                        piecesHash2[(int)colour, (int)piece, location] = NextKey2();
                    }
                }
            }
            for (int i = 0; i != 16; i++)
            {
                castlingRightsHash1[i] = NextKey1();
                castlingRightsHash2[i] = NextKey2();
            }
            for (int i = 0; i != 128; i++)
            {
                enPassantLocationHash1[i] = NextKey1();
                enPassantLocationHash2[i] = NextKey2();
            }
        }
        /// <summary>
        /// 64 bit linear congruential generators for synthesizing Zobrist keys
        /// </summary>
        /// <returns></returns>
        private static ulong NextKey1()
        {
            lcg1 = unchecked(MULTIPLIER_1 * lcg1 + INCREMENT_1);
            return lcg1;
        }

        private static ulong NextKey2()
        {
            lcg2 = unchecked(MULTILPIER_2 * lcg2 + INCREMENT_2);
            return lcg2;
        }

        public static ulong GetHashValueForSquare1(Square square, int location)
        {
            return piecesHash1[(int)square.colour, (int)square.piece, location];
        }

        public static ulong GetHashValueForCastlingRights1(CastlingFlags flags)
        {
            return castlingRightsHash1[(int)flags];
        }

        public static ulong GetHashValueForEnPassantLocation1(int location)
        {
            return enPassantLocationHash1[location];
        }

        public static ulong GetHashValueForSquare2(Square square, int location)
        {
            return piecesHash2[(int)square.colour, (int)square.piece, location];
        }

        public static ulong GetHashValueForCastlingRights2(CastlingFlags flags)
        {
            return castlingRightsHash2[(int)flags];
        }

        public static ulong GetHashValueForEnPassantLocation2(int location)
        {
            return enPassantLocationHash2[location];
        }
    }
}
