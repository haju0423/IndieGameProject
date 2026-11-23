using System;
using System.Collections.Generic;

namespace Poker.Foundation
{
    /// <summary>A five-card value plus the exact five physical cards that produced it.</summary>
    public sealed class HoldemEvaluatedHand
    {
        private readonly Card[] bestCards;

        internal HoldemEvaluatedHand(HandValue value, Card[] ownedBestCards)
        {
            Value = value;
            bestCards = ownedBestCards;
        }

        public HandValue Value { get; }
        public int BestCardCount => bestCards.Length;

        public Card GetBestCard(int index)
        {
            if (index < 0 || index >= bestCards.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return bestCards[index];
        }
    }

    public static class HoldemBestHand
    {
        /// <summary>
        /// Chooses the strongest five cards from a three-to-five-card board and exactly two
        /// private cards. The selected best five may use zero, one, or both private cards.
        /// </summary>
        public static HoldemEvaluatedHand Evaluate(IReadOnlyList<Card> board, IReadOnlyList<Card> holeCards)
        {
            var bestCards = new Card[HandEvaluator.HandSize];
            var best = FindBest(board, holeCards, bestCards);
            return new HoldemEvaluatedHand(best, bestCards);
        }

        /// <summary>Compares the same combinations without retaining the displayed five cards.</summary>
        public static HandValue EvaluateValue(IReadOnlyList<Card> board, IReadOnlyList<Card> holeCards)
            => FindBest(board, holeCards, Span<Card>.Empty);

        private static HandValue FindBest(IReadOnlyList<Card> board, IReadOnlyList<Card> holeCards, Span<Card> bestCards)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (holeCards == null) throw new ArgumentNullException(nameof(holeCards));
            if (board.Count < 3 || board.Count > 5)
                throw new ArgumentException("The visible board must contain three through five cards.", nameof(board));
            if (holeCards.Count != 2)
                throw new ArgumentException("A Hold'em player must supply exactly two private cards.", nameof(holeCards));
            int count = board.Count + holeCards.Count;
            if (count < HandEvaluator.HandSize || count > 7)
                throw new ArgumentException("The combined cards must contain five through seven cards.");

            Span<Card> all = stackalloc Card[7];
            ulong seen = 0;
            for (int i = 0; i < board.Count; i++) CopyChecked(board[i], all, i, ref seen);
            for (int i = 0; i < holeCards.Count; i++) CopyChecked(holeCards[i], all, board.Count + i, ref seen);

            Span<Card> five = stackalloc Card[HandEvaluator.HandSize];
            HandValue best = default;
            for (int a = 0; a < count - 4; a++)
            for (int b = a + 1; b < count - 3; b++)
            for (int c = b + 1; c < count - 2; c++)
            for (int d = c + 1; d < count - 1; d++)
            for (int e = d + 1; e < count; e++)
            {
                five[0] = all[a];
                five[1] = all[b];
                five[2] = all[c];
                five[3] = all[d];
                five[4] = all[e];
                HandValue value = HandEvaluator.RankFive(five);
                if (best.IsValid && value <= best) continue;
                best = value;
                if (!bestCards.IsEmpty) five.CopyTo(bestCards);
            }
            return best;
        }

        private static void CopyChecked(Card card, Span<Card> destination, int index, ref ulong seen)
        {
            if (!card.IsValid) throw new ArgumentException("The cards contain an uninitialized card.");
            ulong bit = 1UL << card.Id;
            if ((seen & bit) != 0) throw new ArgumentException("The board and private cards contain a duplicate card.");
            seen |= bit;
            destination[index] = card;
        }
    }
}
