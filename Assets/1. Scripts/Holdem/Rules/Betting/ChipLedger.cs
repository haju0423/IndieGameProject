using System;
using System.Collections.Generic;

namespace Poker.Foundation
{
    /// <summary>
    /// Immutable, rule-independent chip bookkeeping.
    /// The authority must validate betting legality and commit the returned candidate once.
    /// </summary>
    public sealed class ChipLedger
    {
        private readonly SeatChips[] seats;
        // Committed stays as the matched wager for side-pot eligibility, even after a payout.
        private readonly long[] removed;
        // False-accusation fines belong to the main pot, not the payer's betting cap.
        private readonly long deadMoney;

        private ChipLedger(SeatChips[] ownedSeats, long totalChips, long[] removed = null, long deadMoney = 0)
        {
            this.removed = removed ?? new long[ownedSeats.Length];
            this.deadMoney = deadMoney;
            long stacks = 0;
            long contributions = deadMoney;
            checked
            {
                for (int i = 0; i < ownedSeats.Length; i++)
                {
                    SeatChips seat = ownedSeats[i];
                    if (this.removed[i] < 0 || this.removed[i] > seat.Committed)
                        throw new InvalidOperationException("Invalid paid-out contribution.");
                    stacks += seat.Stack;
                    contributions += seat.Committed - this.removed[i];
                }
                if (stacks + contributions != totalChips)
                    throw new InvalidOperationException("A movement must conserve the ledger's chips.");
            }
            seats = ownedSeats;
            TotalChips = totalChips;
            TotalCommitted = contributions;
        }

        public int SeatCount => seats.Length;
        /// <summary>Constant initial total: remaining stacks plus outstanding contributions.</summary>
        public long TotalChips { get; }
        /// <summary>Unsettled hand contributions after refunds, not an already awarded pot.</summary>
        public long TotalCommitted { get; }
        internal long DeadMoney => deadMoney;
        internal bool HasAccusationPayments
        {
            get { if (deadMoney != 0) return true; foreach (long amount in removed) if (amount != 0) return true; return false; }
        }
        internal long RemovedContribution(SeatId seat) => removed[FindSeatIndex(seat)];

        internal ChipLedger PayContributionTo(SeatId from, SeatId recipient)
        {
            int source = FindSeatIndex(from), destination = FindSeatIndex(recipient);
            if (source == destination) throw new ArgumentException("The recipient must be another seat.");
            long amount = seats[source].Committed - removed[source];
            var next = (SeatChips[])seats.Clone();
            next[destination] = new SeatChips(recipient, checked(seats[destination].Stack + amount), seats[destination].Committed);
            var paid = (long[])removed.Clone();
            paid[source] = seats[source].Committed;
            return new ChipLedger(next, TotalChips, paid, deadMoney);
        }

        internal ChipLedger AddDeadMoney(SeatId seat, long amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            int index = FindSeatIndex(seat);
            if (amount > seats[index].Stack) throw new InvalidOperationException("Insufficient stack.");
            var next = (SeatChips[])seats.Clone();
            next[index] = new SeatChips(seat, seats[index].Stack - amount, seats[index].Committed);
            return new ChipLedger(next, TotalChips, removed, checked(deadMoney + amount));
        }

        /// <summary>
        /// Copies starting balances in the given order. Zero stacks are valid bookkeeping,
        /// not permission to participate. The caller must not mutate input during the copy.
        /// </summary>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        /// <exception cref="ArgumentException">Empty list, null entry, repeated seat or existing contribution.</exception>
        /// <exception cref="OverflowException">The total is greater than Int64.MaxValue.</exception>
        public static ChipLedger Create(IReadOnlyList<SeatChips> startingChips)
        {
            if (startingChips == null) throw new ArgumentNullException(nameof(startingChips));
            int count = startingChips.Count;
            if (count <= 0) throw new ArgumentException("At least one starting balance is required.", nameof(startingChips));
            var ownedSeats = new SeatChips[count];
            var seen = new HashSet<SeatId>();
            long total = 0;
            for (int index = 0; index < count; index++)
            {
                SeatChips entry = startingChips[index];
                if (entry == null || !entry.Owner.IsValid || !seen.Add(entry.Owner) || entry.Committed != 0)
                    throw new ArgumentException("Starting balances must have unique valid owners and no contributions.",
                        nameof(startingChips));
                ownedSeats[index] = entry; // Entries are sealed immutable values; the collection is owned here.
                total = checked(total + entry.Stack);
            }
            return new ChipLedger(ownedSeats, total);
        }

        public SeatId GetSeatAt(int index)
        {
            if (index < 0 || index >= seats.Length) throw new ArgumentOutOfRangeException(nameof(index));
            return seats[index].Owner;
        }

        /// <summary>Trusted internal lookup, not an authorization or visibility filter.</summary>
        public SeatChips GetChips(SeatId seat) => seats[FindSeatIndex(seat)];

        /// <summary>
        /// Returns a new candidate moving exactly amount from stack to hand contribution.
        /// Amount is an additional payment, NOT a BetTo/RaiseTo target. Never clamps to an all-in.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Amount is not positive.</exception>
        /// <exception cref="InvalidOperationException">The stack cannot cover the full amount.</exception>
        public ChipLedger Contribute(SeatId seat, long amount)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            int index = FindSeatIndex(seat);
            SeatChips current = seats[index];
            if (amount > current.Stack) throw new InvalidOperationException("The stack cannot cover the contribution.");
            return WithBalance(index, checked(current.Stack - amount), checked(current.Committed + amount));
        }

        /// <summary>
        /// Returns a new candidate refunding exactly amount from this seat's outstanding contribution.
        /// The caller decides whether a refund is legal and its size; this does not calculate uncalled bets.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Amount is not positive.</exception>
        /// <exception cref="InvalidOperationException">This seat has not contributed enough.</exception>
        public ChipLedger RefundContribution(SeatId seat, long amount)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            int index = FindSeatIndex(seat);
            SeatChips current = seats[index];
            if (amount > current.Committed - removed[index]) throw new InvalidOperationException("The refund exceeds this seat's contribution.");
            return WithBalance(index, checked(current.Stack + amount), checked(current.Committed - amount));
        }

        /// <summary>
        /// Authority bookkeeping: transfer an exact amount between uncommitted stacks, leaving the pot untouched.
        /// Does not choose a fine or a shortfall/elimination rule. Insufficient funds reject the whole movement.
        /// </summary>
        public ChipLedger TransferStack(SeatId from, SeatId to, long amount)
        {
            if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (from == to) throw new ArgumentException("A transfer requires different seats.", nameof(to));
            int source = FindSeatIndex(from), destination = FindSeatIndex(to);
            if (seats[source].Stack < amount) throw new InvalidOperationException("The stack cannot cover the transfer.");
            var next = (SeatChips[])seats.Clone();
            next[source] = new SeatChips(from, checked(seats[source].Stack - amount), seats[source].Committed);
            next[destination] = new SeatChips(to, checked(seats[destination].Stack + amount), seats[destination].Committed);
            return new ChipLedger(next, TotalChips, removed, deadMoney);
        }

        // Settlement supplies a complete award vector in ledger order; no public arbitrary-payout/reset API.
        internal ChipLedger Distribute(long[] payouts)
        {
            if (payouts == null || payouts.Length != seats.Length)
                throw new ArgumentException("One payout per ledger seat is required.", nameof(payouts));
            long total = 0;
            var next = new SeatChips[seats.Length];
            for (int i = 0; i < seats.Length; i++)
            {
                if (payouts[i] < 0) throw new ArgumentException("Payouts cannot be negative.", nameof(payouts));
                total = checked(total + payouts[i]);
                next[i] = new SeatChips(seats[i].Owner, checked(seats[i].Stack + payouts[i]), 0);
            }
            if (total != TotalCommitted) throw new InvalidOperationException("Awards must consume exactly the outstanding pot.");
            return new ChipLedger(next, TotalChips);
        }

        private ChipLedger WithBalance(int index, long stack, long committed)
        {
            var next = (SeatChips[])seats.Clone();
            next[index] = new SeatChips(seats[index].Owner, stack, committed);
            return new ChipLedger(next, TotalChips, removed, deadMoney);
        }

        private int FindSeatIndex(SeatId seat)
        {
            if (!seat.IsValid) throw new ArgumentException("A valid seat ID is required.", nameof(seat));
            for (int index = 0; index < seats.Length; index++)
                if (seats[index].Owner == seat) return index;
            throw new KeyNotFoundException("The seat is not part of this chip ledger.");
        }
    }
}
