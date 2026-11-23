using System;
using System.Collections.Generic;

namespace Poker.Foundation
{
    public static class HandEvaluator
    {
        public const int HandSize = 5;

        /// <summary>Evaluate exactly five distinct valid cards without modifying or retaining the input.</summary>
        /// <exception cref="ArgumentNullException">The input is null.</exception>
        /// <exception cref="ArgumentException">The input has the wrong size, invalid cards, or duplicates.</exception>
        public static HandValue Evaluate(IReadOnlyList<Card> cards)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            if (cards.Count != HandSize)
                throw new ArgumentException("A five-card hand must contain exactly five cards.", nameof(cards));

            Span<Card> five = stackalloc Card[HandSize];
            ulong seenCards = 0;
            for (int i = 0; i < HandSize; i++)
            {
                Card card = cards[i];
                if (!card.IsValid)
                    throw new ArgumentException("The hand contains an uninitialized card.", nameof(cards));
                ulong bit = 1UL << card.Id;
                if ((seenCards & bit) != 0)
                    throw new ArgumentException("The hand contains a duplicate card.", nameof(cards));
                seenCards |= bit;
                five[i] = card;
            }
            return RankFive(five);
        }

        // Both public entry points validate their input once before comparing subsets.
        internal static HandValue RankFive(ReadOnlySpan<Card> five)
        {
            Span<int> counts = stackalloc int[(int)Rank.Ace + 1];
            counts.Clear();
            bool flush = true;
            Suit firstSuit = five[0].Suit;
            for (int i = 0; i < HandSize; i++)
            {
                counts[(int)five[i].Rank]++;
                if (five[i].Suit != firstSuit) flush = false;
            }

            // Walk ranks, not the caller's list. All priority lists are descending.
            Span<int> descending = stackalloc int[HandSize];
            Span<int> singles = stackalloc int[HandSize];
            int sortedCount = 0, singleCount = 0, distinctCount = 0;
            int four = 0, three = 0, highPair = 0, lowPair = 0;
            for (int rank = (int)Rank.Ace; rank >= (int)Rank.Two; rank--)
            {
                int count = counts[rank];
                if (count == 0) continue;
                distinctCount++;
                for (int i = 0; i < count; i++) descending[sortedCount++] = rank;
                switch (count)
                {
                    case 4: four = rank; break;
                    case 3: three = rank; break;
                    case 2:
                        if (highPair == 0) highPair = rank;
                        else lowPair = rank;
                        break;
                    case 1: singles[singleCount++] = rank; break;
                }
            }

            int straightHigh = 0;
            if (distinctCount == HandSize)
            {
                if (descending[0] - descending[HandSize - 1] == HandSize - 1)
                    straightHigh = descending[0];
                // Ace is low only in A-2-3-4-5. There is no circular Q-K-A-2-3 straight.
                else if (descending[0] == (int)Rank.Ace && descending[1] == (int)Rank.Five
                    && descending[HandSize - 1] == (int)Rank.Two)
                    straightHigh = (int)Rank.Five;
            }

            if (flush && straightHigh != 0)
                return new HandValue(HandCategory.StraightFlush, straightHigh);
            if (four != 0) return new HandValue(HandCategory.FourOfAKind, four, singles[0]);
            if (three != 0 && highPair != 0)
                return new HandValue(HandCategory.FullHouse, three, highPair);
            if (flush) return new HandValue(HandCategory.Flush,
                descending[0], descending[1], descending[2], descending[3], descending[4]);
            if (straightHigh != 0) return new HandValue(HandCategory.Straight, straightHigh);
            if (three != 0) return new HandValue(HandCategory.ThreeOfAKind, three, singles[0], singles[1]);
            if (lowPair != 0) return new HandValue(HandCategory.TwoPair, highPair, lowPair, singles[0]);
            if (highPair != 0)
                return new HandValue(HandCategory.OnePair, highPair, singles[0], singles[1], singles[2]);
            return new HandValue(HandCategory.HighCard,
                descending[0], descending[1], descending[2], descending[3], descending[4]);
        }
    }
}
