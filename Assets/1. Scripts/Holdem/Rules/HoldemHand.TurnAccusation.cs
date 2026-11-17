using System;

namespace Poker.Foundation
{
    public sealed partial class HoldemHand
    {
        /// <summary>
        /// The current seat on its first action of a post-reveal street. A raise that brings the action
        /// back to a seat that already acted does not reopen the accusation.
        /// </summary>
        private bool CanAccuseLegacyOnTurn(SeatId seat)
            => Config.TurnAccusations && CurrentSeat == seat
                && (Street == HoldemStreet.Flop || Street == HoldemStreet.Turn || Street == HoldemStreet.River)
                && !CurrentBetting.HasActed(seat);

        public bool IsTurnAccusationTarget(SeatId accuser, SeatId target)
            => target.IsValid && target != accuser && WasDealtIn(target) && !IsFolded(target);

        /// <summary>The caller supplies the verdict from the host's committed deal record.</summary>
        internal HoldemHand ResolveTurnAccusation(SeatId accuser, SeatId target, bool caught)
        {
            if (!CanAccuseLegacyOnTurn(accuser)) throw new InvalidOperationException("This seat cannot accuse now.");
            if (!IsTurnAccusationTarget(accuser, target)) throw new ArgumentException("Invalid accusation target.", nameof(target));
            long fine = caught ? 0 : Math.Min(Config.Ante, Ledger.GetChips(accuser).Stack);
            BettingRound next = CurrentBetting.ResolveAccusation(accuser, target, caught, fine);
            SeatId folded = caught ? target : accuser;
            long version = checked(Version + 1);
            var candidate = NewState(version, AddFolded(folded), board, deck.Copy(), Street, next, null, null, null);
            return next.IsComplete ? candidate.AdvanceAfterCompletedBetting(folded, version) : candidate;
        }
    }
}
