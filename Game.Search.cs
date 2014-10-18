using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;
using System.Diagnostics;

namespace StoopidChess
{
    /// <summary>
    /// Methods for searching the game tree
    /// </summary>
    partial class Game
    {
        /// <summary>
        /// Determine the computer's best move.
        /// Uses iterative deepening:
        /// a) When time runs out we will have found a reasonable move to the best depth available in the time allowed
        /// b) Earlier, shallower searches will seed the transposition hash table and (hopefully!) improve
        ///    move ordering during later and deeper searches
        /// </summary>
        private void GetComputerMove(object obj)
        {
            Debug.Assert(obj is Colour);
            Colour colour = (Colour)obj;
            doCancelSearch = false;
            debug = new GameDebugVariables();
            Move computerMove = Move.NONE;           
            int finalDepth = maxSearchDepth;
            if (board.pieceCount < 16)
            {
                finalDepth++;
            }
            if (board.pieceCount < 13)
            {
                finalDepth++;
            }
            if (board.pieceCount < 10)
            {
                finalDepth++;
            }
            if (board.pieceCount < 7)
            {
                finalDepth++;
            }
            Move move = Move.NONE;
            timer.Start();
            for (int depth = 0; depth <= finalDepth; depth++)
            {
                int score = AlphaBeta(colour, ALPHA, BETA, depth, 0, out move);
                if (!doCancelSearch)
                {
                    // Only trust the value of this move if the 
                    // search was not prematurely cancelled
                    computerMove = move;
                    debug.computerScore = score;
                    debug.bestDepthAchieved = depth;
                }
                else
                {
                    break;
                }
            }
            // If we finished searching to the required depth, stop the timer to prevent the
            // elapsed event from occurring in a future search
            timer.Stop();
            Debug.Assert(computerMove.moveType != MoveType.NONE);
            ComputerThinkingCompletedEvent(computerMove);
        }
        /// <summary>
        /// Negamax recursive search algorithm with alpha beta pruning
        /// Calls quiescence search at non terminal leaf nodes to minimize horizon effect
        /// See also:
        /// http://chessprogramming.wikispaces.com/Alpha-Beta
        /// </summary>
        private int AlphaBeta(Colour colour, int alpha, int beta, int depth, int depthFromRoot, out Move bestMove)
        {
            debug.callsToAlphaBeta++;
            Colour enemyColour = colour ^ Colour.XOR;
            Debug.Assert(alpha < beta);
            bestMove = Move.NONE;
            // If our time is up, return current value of alpha
            if (doCancelSearch)
            {
                return alpha;
            }
            bool isTransposition = false;
            ulong hash1 = board.GetHash1(colour);
            ulong hash2 = board.GetHash2(colour);
            int tableIndex = (int)(hash1 >> SHIFT_HASH);
            // Check to see if there is an entry in the hash tables for this position
            Transposition transposition = replaceByDepthHashTable[tableIndex];
            if (transposition != null && transposition.hash1 == hash1 && transposition.hash2 == hash2)
            {
                debug.replaceDeeperTranspositionHits++;
                isTransposition = true;
            }
            else
            {
                transposition = alwaysReplaceHashTable[tableIndex];
                if (transposition != null && transposition.hash1 == hash1 && transposition.hash2 == hash2)
                {
                    debug.alwaysReplaceTranspositionHits++;
                    isTransposition = true;
                }
            }
            if (isTransposition)
            {
                // Is this entry's depth sufficient for us to avoid searching altogether?
                if (transposition.depth >= depth)
                {
                    if (transposition.nodeType == NodeType.BETA && transposition.value >= beta)
                    {
                        // Check the hash table best move will not result in draw by repetition
                        board.DoMove(transposition.bestMove);
                        bool isDraw = board.IsDrawByRepetition(enemyColour);
                        board.UndoLastMove();
                        if (!isDraw)
                        {
                            debug.transpositionsBeta++;
                            bestMove = transposition.bestMove;
                            return transposition.value;
                        }
                        else
                        {
                            Debug.WriteLine("Draw detected in transposition table beta move");
                        }
                    }
                    else if (transposition.nodeType == NodeType.EXACT)
                    {
                        // Check the hash table best move will not result in draw by repetition
                        board.DoMove(transposition.bestMove);
                        bool isDraw = board.IsDrawByRepetition(enemyColour);
                        board.UndoLastMove();
                        if (!isDraw)
                        {
                            debug.transpositionsExact++;
                            bestMove = transposition.bestMove;
                            return transposition.value;
                        }
                        else
                        {
                            Debug.WriteLine("Draw detected in transposition table exact move");
                        }
                    }
                }
            }
            bool areWeInCheck = MoveGenerator.IsInCheck(board, colour);
            int successfulMoveCount = 0;
            int currentScore = ALPHA;
            int bestScore = ALPHA;
            Move currentMove = Move.NONE;
            SortedDictionary<int, Move> moves = null;
            SortedDictionary<int, Move>.Enumerator moveEnumerator = new SortedDictionary<int, Move>.Enumerator();
            // If there is a best move in the transposition table entry, try it before we generate any moves as it
            // might cause beta cutoff and save us the time and effort of move generation altogether
            if (isTransposition)
            {
                debug.transpositionMoves++;
                currentMove = transposition.bestMove;
                Debug.Assert(board[currentMove.from].piece == currentMove.movingPiece);
                Debug.Assert(board[currentMove.from].colour == colour);
                Debug.Assert(board[currentMove.to].colour != colour);
            }
            else
            {
                // No transposition entry found - generate moves for this position
                moves = MoveGenerator.GenerateAllPseudoLegalMoves(board, colour, !areWeInCheck);
                // Check to see if killer moves are legal from this position and
                // move them up to the front of the move list if so
                Move killer;
                int killerHash;
                if (depthFromRoot >= 2)
                {
                    // Killer move from 2 plies ago
                    killer = killerMoves[depthFromRoot - 2];
                    killerHash = killer.GetHashCode();
                    if (moves.ContainsKey(killerHash))
                    {
                        moves.Remove(killerHash);
                        // Add killer move from 2 plies ago into second position within move dictionary
                        moves.Add(int.MinValue + 1, killer);
                        debug.killersFound++;
                    }
                }
                // Killer from this ply
                killer = killerMoves[depthFromRoot];
                killerHash = killer.GetHashCode();
                if (moves.ContainsKey(killerHash))
                {
                    moves.Remove(killerHash);
                    // Add killer move into first position within move dictionary
                    moves.Add(int.MinValue, killer);
                    debug.killersFound++;
                }
                moveEnumerator = moves.GetEnumerator();
                moveEnumerator.MoveNext();
                currentMove = moveEnumerator.Current.Value;
            }

            // Iterate over moves, trying to achieve "best first" ordering 
            // to improve probability of beta cutoff
            // 1st: try the move from the transposition table (if any)
            // 2nd: try the killer move from this ply (if legal)
            // 3rd: try the killer move from 2 plies ago (if legal)
            // 4th: try each pseudo-legal move in turn, sorted by most valuable victim / least valuable attacker
            while (true)
            {
                board.DoMove(currentMove);
                if (MoveGenerator.IsInCheck(board, colour))
                {
                    // We are not allowed to put ourselves in check, so 
                    // don't bother to continue searching any further here
                    board.UndoLastMove();
                    goto NextMove;
                }
                successfulMoveCount++;                
                // Check to see if this is a draw by repetition move
                if (board.IsDrawByRepetition(enemyColour))
                {
                    Debug.WriteLine("Draw by repetition 3 times detected in search tree");
                    currentScore = Evaluator.DRAW_SCORE + depthFromRoot;
                }
                else if (board.IsDrawByFiftyMoveRule())
                {
                    Debug.WriteLine("Draw by 50 move rule detected in search tree");
                    currentScore = Evaluator.DRAW_SCORE + depthFromRoot;
                }
                else if (board.IsDrawByInsufficientMaterial())
                {
                    Debug.WriteLine("Draw by insufficient material detected in search tree");
                    currentScore = Evaluator.DRAW_SCORE + depthFromRoot;
                }
                else
                {
                    // Extend depth of search if we are in check (don't go into quiescence search when in check)
                    if (depth > 0 || areWeInCheck)
                    {
                        Move dummy = Move.NONE;
                        // Don't pass the best move reference down from the root node
                        currentScore = -AlphaBeta(enemyColour, -beta, -alpha, depth - 1, depthFromRoot + 1, out dummy);
                    }
                    else
                    {
                        currentScore = -QuiescenceSearch(enemyColour, -beta, -alpha, depthFromRoot + 1);
                    }
                }
                board.UndoLastMove();
                if (currentScore > bestScore)
                {
                    bestScore = currentScore;
                    bestMove = currentMove;
                }
                if (currentScore >= beta)
                {
                    // Beta cutoff
                    debug.alphaBetaCutoffs++;
                    // Store the move which caused the beta cutoff in the transposition table
                    // We don't know the true value of this node as it caused a beta cutoff, so simply store 
                    // the lower bound value in the transposition table.
                    Transposition temp = replaceByDepthHashTable[tableIndex];
                    if (temp == null || temp.depth <= depth)
                    {
                        replaceByDepthHashTable[tableIndex] = new Transposition(hash1, hash2, depth, NodeType.BETA, currentScore, currentMove);
                    }
                    else
                    {
                        alwaysReplaceHashTable[tableIndex] = new Transposition(hash1, hash2, depth, NodeType.BETA, currentScore, currentMove);
                    }
                    // Store this move in the killer moves table
                    killerMoves[depthFromRoot] = currentMove;
                    return currentScore;
                }
                if (currentScore > alpha)
                {
                    alpha = currentScore;
                }

            NextMove:

                if (moves != null)
                {
                    if (!moveEnumerator.MoveNext())
                    {
                        break;  // No more moves to examine
                    }
                    currentMove = moveEnumerator.Current.Value;
                }
                else
                {
                    // Last iteration was from the transposition table
                    // We now need to generate moves as the transposition table move did not cause cutoff
                    moves = MoveGenerator.GenerateAllPseudoLegalMoves(board, colour, !areWeInCheck);
                    Move killer;
                    int killerHash;
                    if (depthFromRoot >= 2)
                    {
                        killer = killerMoves[depthFromRoot - 2];
                        killerHash = killer.GetHashCode();
                        if (moves.ContainsKey(killerHash))
                        {
                            moves.Remove(killerHash);
                            moves.Add(int.MinValue + 1, killer);
                            debug.killersFound++;
                        }
                    }
                    killer = killerMoves[depthFromRoot];
                    killerHash = killer.GetHashCode();
                    if (moves.ContainsKey(killerHash))
                    {
                        moves.Remove(killerHash);
                        moves.Add(int.MinValue, killer);
                        debug.killersFound++;
                    }
                    moveEnumerator = moves.GetEnumerator();
                    moveEnumerator.MoveNext();
                    currentMove = moveEnumerator.Current.Value;
                }
            }
            // Finished examining all moves
            if (successfulMoveCount != 0)
            {
                // Store the best move we found in the transposition table
                Transposition temp = replaceByDepthHashTable[tableIndex];
                if (temp == null || temp.depth <= depth)
                {
                    replaceByDepthHashTable[tableIndex] = new Transposition(hash1, hash2, depth, NodeType.EXACT, bestScore, bestMove);
                }
                else
                {
                    alwaysReplaceHashTable[tableIndex] = new Transposition(hash1, hash2, depth, NodeType.EXACT, bestScore, bestMove);
                }
                return bestScore;
            }
            else
            {
                // We were unable to find any moves that did not leave us in check,
                // i.e. this is a terminal node
                // Add depth from root to terminal score to encourage fastest route to end of game where
                // more than one path may exist 
                return areWeInCheck ? Evaluator.LOSING_SCORE + depthFromRoot : Evaluator.DRAW_SCORE + depthFromRoot;
            }
        }
        /// <summary>
        /// The quiescence search is conducted once alpha beta is complete and attempts to minimize the horizon effect
        /// by seeking quiet nodes before calling the static evaluation function. Only capture moves are considered
        /// unless we are in check. There is no limit to depth so move ordering is important to avoid a search explosion.
        /// Captures are evaluated in the sequence most valuable victim, least valuable attacker
        /// See also:
        /// http://chessprogramming.wikispaces.com/Quiescence+Search
        /// </summary>
        private int QuiescenceSearch(Colour colour, int alpha, int beta, int depthFromRoot)
        {
            debug.callsToQuiescentSearch++;
            Debug.Assert(alpha < beta);        
            if (doCancelSearch)
            {
                // If we are out of time, return the current value of alpha
                return alpha;
            }
            int score = Evaluator.EvaluatePosition(board, colour);
            bool areWeInCheck = MoveGenerator.IsInCheck(board, colour);
            // If the stand pat score is sufficient to cause a beta cutoff, terminate the search
            if (!areWeInCheck && score >= beta)
            {
                debug.quiescentBetaCutoffs++;
                return score;
            }
            if (!areWeInCheck && score > alpha)
            {
                alpha = score;
            }            
            // If we are in check we should consider all possible moves
            // otherwise only consider captures
            SortedDictionary<int, Move> moves = areWeInCheck ? MoveGenerator.GenerateAllPseudoLegalMoves(board, colour, false) : MoveGenerator.GeneratePseudoLegalCaptures(board, colour);
            int successfulMoveCount = 0;
            Colour enemyColour = colour ^ Colour.XOR;
            foreach (KeyValuePair<int, Move> move in moves)
            {
                board.DoMove(move.Value);
                if (MoveGenerator.IsInCheck(board, colour))
                {
                    // We can't put ourselves in check
                    board.UndoLastMove();
                    continue;
                }
                successfulMoveCount++;
                score = -QuiescenceSearch(enemyColour, -beta, -alpha, depthFromRoot + 1);
                board.UndoLastMove();
                if (score >= beta)
                {
                    debug.quiescentBetaCutoffs++;
                    return score;
                }
                if (score > alpha)
                {
                    alpha = score;
                }
            }
            if (areWeInCheck && successfulMoveCount == 0)
            {
                // We were unable to get ourselves out of check
                return Evaluator.LOSING_SCORE + depthFromRoot;
            }
            return alpha;
        }
    }
}
