using System;
using Poker.Foundation;

namespace Poker.Application
{
    /// <summary>No accuser identity or verdict is accepted from a client.</summary>
    public sealed class HoldemRoomLegacyTurnAccusation
    {
        public HoldemRoomLegacyTurnAccusation(Guid sessionId, Guid handId, Guid commandId, long expectedVersion, SeatId target)
        {
            if (sessionId == Guid.Empty || handId == Guid.Empty || commandId == Guid.Empty)
                throw new ArgumentException("Session, hand and command IDs are required.");
            if (expectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
            if (!target.IsValid) throw new ArgumentException("Invalid target.", nameof(target));
            SessionId = sessionId; HandId = handId; CommandId = commandId;
            ExpectedVersion = expectedVersion; Target = target;
        }
        public Guid SessionId { get; }
        public Guid HandId { get; }
        public Guid CommandId { get; }
        public long ExpectedVersion { get; }
        public SeatId Target { get; }
    }

    /// <summary>Public outcome of the latest turn accusation in the current hand. No dealer evidence.</summary>
    public sealed class HoldemTurnAccusationNotice
    {
        internal HoldemTurnAccusationNotice(Guid handId, HoldemStreet street, SeatId accuser, SeatId target, bool caught)
        { HandId = handId; Street = street; Accuser = accuser; Target = target; Caught = caught; }
        public Guid HandId { get; }
        public HoldemStreet Street { get; }
        public SeatId Accuser { get; }
        public SeatId Target { get; }
        public bool Caught { get; }
    }

    public sealed partial class HoldemRoom
    {
        private HoldemTurnAccusationNotice lastTurnAccusation;

        public HoldemRoomReceipt SubmitLegacyTurnAccusation(Guid connection, HoldemRoomLegacyTurnAccusation input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            lock (gate)
            {
                var error = Guard(connection, false, out var member);
                if (error != HoldemRoomError.None) return Reject(error);
                HoldemSnapshot before = session.CurrentHandId.HasValue ? session.GetSnapshot(member.Seat) : null;
                var command = new HoldemTurnAccusationCommand(input.SessionId, input.HandId, input.CommandId,
                    member.Seat, input.ExpectedVersion, input.Target);
                var receipt = session.SubmitTurnAccusation(member.Seat, command);
                if (receipt.Accepted && before != null && receipt.Version > before.SessionVersion)
                {
                    var after = session.GetSnapshot(member.Seat);
                    bool caught = after.GetSeat(input.Target).Status == HoldemSeatStatus.Folded;
                    lastTurnAccusation = new HoldemTurnAccusationNotice(before.HandId, before.Street,
                        member.Seat, input.Target, caught);
                    long movement = caught ? before.GetSeat(input.Target).Committed
                        : Math.Min(before.PostedAnte, before.GetSeat(member.Seat).Stack);
                    if (movement > 0) AppendHistory(new HoldemHistoryEntry(caught ? HoldemHistoryKind.AccusationPayout : HoldemHistoryKind.AccusationFine,
                        before.Street, member.Seat, null, movement, 0, false));
                    if (!caught)
                    {
                        lastAction = new HoldemActionNotice(member.Seat, BettingAction.Fold(), 0, before.Street);
                        seatActions[member.Seat.Value - 1] = lastAction;
                        AppendHistory(new HoldemHistoryEntry(HoldemHistoryKind.Action, before.Street, member.Seat,
                            BettingActionKind.Fold, 0, before.OwnStreetContribution, false));
                    }
                    RecordHistoryReturns(before, after, member.Seat, caught ? 0 : movement,
                        caught ? member.Seat : (SeatId?)null, caught ? movement : 0);
                    SynchronizeUtterances();
                    revision++;
                }
                return Wrap(receipt);
            }
        }

        private HoldemTurnAccusationNotice CurrentTurnAccusation(HoldemSnapshot game)
            => game != null && lastTurnAccusation != null && lastTurnAccusation.HandId == game.HandId
                ? lastTurnAccusation : null;
    }
}
