using System;
using System.Linq;
using NUnit.Framework;
using Poker.Application;
using Poker.Online;
using Poker.Transport;
using UnityEngine;

namespace Poker.Foundation.Tests
{
    public sealed partial class HoldemRoomChatTests
    {
        // Seat 1 receives AA, seat 4 receives 23; the five-card board cannot tie them.
        // Later hands use the ordered deck for the separate request-application assertion.
        private sealed class OpeningHandRandom : IRandomSource
        {
            private readonly System.Collections.Generic.Queue<int> swaps = new System.Collections.Generic.Queue<int>();
            public OpeningHandRandom(int[] secondHand = null)
            {
                int[] prefix = { 0, 1, 2, 48, 4, 5, 6, 49, 8, 12, 21, 30, 39, 44 };
                QueueDeck(prefix);
                if (secondHand == null) return;
                swaps.Enqueue(10); // Reveal all five on the first board.
                QueueDeck(secondHand);
            }
            private void QueueDeck(int[] prefix)
            {
                var desired = prefix.Concat(Enumerable.Range(0, 52).Except(prefix)).ToArray();
                var deck = Enumerable.Range(0, 52).ToArray();
                for (int i = 51; i > 0; i--)
                {
                    int at = Array.IndexOf(deck, desired[i]);
                    swaps.Enqueue(at);
                    int old = deck[i]; deck[i] = deck[at]; deck[at] = old;
                }
            }
            public int NextInt(int maximum) => swaps.Count > 0 ? swaps.Dequeue() : maximum - 1;
        }
        [Test]
        public void FinalPlacesRetainEarlierKnockoutShareTheLastPlaceGroupAndResetOnRematch()
        {
            var roster = Enumerable.Range(1, 4).Select(i => new SeatId(i)).ToArray();
            var stacks = new long[] { 8, 1, 5, 5 };
            var ledger = ChipLedger.Create(roster.Select((seat, i) => new SeatChips(seat, stacks[i])).ToArray());
            var config = new HoldemConfig(10, 1, 2, ante: 1, anteSteps: new long[] { 1, 8 });
            // After seat 2 busts, seats 4, 1, 3 receive the next deal in that order.
            var random = new OpeningHandRandom(new[] { 0, 48, 1, 4, 49, 5, 8, 12, 21, 30, 39, 44 });
            var match = new HoldemSession(Guid.NewGuid(), config, ledger, roster, roster[0], random);
            HoldemSnapshot PlayHand()
            {
                Assert.That(match.StartNextHand(Guid.NewGuid(), Guid.NewGuid(), match.Version).Accepted, Is.True);
                var view = match.GetSnapshot(roster[0]);
                for (int step = 0; step < 20 && view.Result == null; step++)
                {
                    var actor = view.CurrentSeat.Value;
                    var action = match.GetSnapshot(actor).LegalActions.CanCheck ? BettingAction.Check() : BettingAction.Call();
                    Assert.That(match.Submit(actor, HoldemCommand.Act(match.SessionId, view.HandId,
                        Guid.NewGuid(), actor, match.Version, action)).Accepted, Is.True);
                    view = match.GetSnapshot(roster[0]);
                }
                Assert.That(view.Result, Is.Not.Null);
                return view;
            }
            var first = PlayHand();
            Assert.That(first.IsOver, Is.False);
            Assert.That(match.IsEliminatedAfterSettlement(roster[1]), Is.True);
            var final = PlayHand();
            Assert.That(final.IsOver, Is.True);
            foreach (var observer in roster)
            {
                var view = match.GetSnapshot(observer);
                Assert.That(Enumerable.Range(0, 4).Select(view.GetFinalPlaceAt), Is.EqualTo(new[] { 1, 4, 2, 2 }));
            }
            Assert.That(match.RestartMatch(new HoldemStartCommand(match.SessionId, Guid.NewGuid(), Guid.NewGuid(),
                match.Version), final.HandId).Accepted, Is.True);
            var rematch = match.GetSnapshot(roster[0]);
            Assert.That(rematch.RoundInMatch, Is.EqualTo(1));
            Assert.That(Enumerable.Range(0, 4).Select(rematch.GetFinalPlaceAt), Is.EqualTo(new int[4]));
            Assert.That(Enumerable.Range(0, 4).Select(final.GetFinalPlaceAt), Is.EqualTo(new[] { 1, 4, 2, 2 }));
        }

        private HoldemSnapshot ChatState => room.Read(peers[0]).Game;
        private void ChatAction(BettingAction action)
        {
            var state = ChatState;
            Assert.That(room.Submit(peers[state.CurrentSeat.Value.Value - 1], new HoldemRoomAction(state.SessionId,
                state.HandId, Guid.NewGuid(), state.SessionVersion, action)).Accepted, Is.True);
        }
        private void ChatRunout()
        {
            for (int step = 0; ChatState.Result == null && step < 10; step++)
            {
                var state = ChatState;
                if (state.IsDealPending)
                    Assert.That(room.DealUnchanged(peers[0], room.ReadPendingDealerTurn(peers[0]).Turn
                        .CreateUnchangedCommand(Guid.NewGuid())).Accepted, Is.True);
                else if (state.IsRevealPending)
                    Assert.That(room.ResumeAfterReveal(peers[0], new HoldemRevealCommand(state.SessionId,
                        state.HandId, Guid.NewGuid(), state.SessionVersion, state.Street)).Accepted, Is.True);
                else if (state.IsSettlementPending)
                    Assert.That(room.ResolveSettlement(peers[0], Guid.NewGuid(), state.SessionVersion,
                        HoldemOddChipRule.ClockwiseFromButton).Accepted, Is.True);
                else Assert.Fail($"Unexpected runout phase: street={state.Street}, settlement={state.IsSettlementPending}, actor={state.CurrentSeat}");
            }
            Assert.That(ChatState.Result, Is.Not.Null);
        }
        private void AssertSpectatorCanRequestAndAffectCards(int seat, bool busted)
        {
            var before = room.Read(peers[seat - 1]);
            var ownSeat = Enumerable.Range(0, before.Game.SeatCount).Select(before.Game.GetSeatAt).Single(s => s.Seat.Value == seat);
            Assert.That(ownSeat.Status, Is.EqualTo(busted ? HoldemSeatStatus.Busted : HoldemSeatStatus.Folded));
            Assert.That(before.Game.CanAccuseOnTurn, Is.False);
            Assert.That(before.Game.CurrentSeat?.Value, Is.Not.EqualTo(seat));
            string text = "<b>럭키 세븐</b>";
            Assert.That(before.OwnUtterances.CanSubmit, Is.True);
            var packet = JsonUtility.FromJson<HoldemRoomPacket>(JsonUtility.ToJson(HoldemRoomPacketMapper.Create(before)));
            Assert.That(HoldemUtterancePacketReader.Read(packet).CanSubmit, Is.True);
            if (busted)
            {
                Assert.That(packet.game.seats.Single(s => s.seat == seat).visibleCards, Is.Empty);
                Assert.That(ownSeat.WasDealtIn, Is.False);
            }
            Assert.That(room.SubmitChat(peers[seat - 1], ToDealer(seat, text)).Accepted, Is.True);
            Assert.That(room.SubmitChat(peers[seat - 1], ToDealer(seat, "두 번째 요청")).Accepted, Is.False, "Existing per-street quota stays in force");
            CloseBetting();
            var turn = room.ReadPendingDealerTurn(peers[0]).Turn;
            var entry = turn.SourceUtterances.GetEntry(0);
            Assert.That(entry.Speaker.Value, Is.EqualTo(seat));
            var deal = turn.TargetDeal;
            var command = new HoldemDealCommand(deal.SessionId, deal.HandId, deal.WindowId, Guid.NewGuid(),
                turn.ExpectedVersion, deal.Street, new HoldemCardChange(entry.WindowId, entry.CommandId, entry.Speaker,
                    deal.BoardStart + deal.CardCount - 1, Card.FromId(45), HoldemCardSourceScope.UndealtOutsideCurrentHandRunout));
            Assert.That(room.ApplyDealerDeal(peers[0], command).Accepted, Is.True);
            Assert.That(ChatState.GetBoardCard(ChatState.BoardCount - 1).Id, Is.EqualTo(45));
            for (int i = 0; i < peers.Length; i++)
            {
                var view = room.Read(peers[i]);
                Assert.That(view.DealerSelectionCount, Is.EqualTo(i == seat - 1 ? 1 : 0));
                Assert.That(Lines(view), Does.Contain(text), "The original sentence remains public");
                var afterPacket = JsonUtility.FromJson<HoldemRoomPacket>(JsonUtility.ToJson(HoldemRoomPacketMapper.Create(view)));
                Assert.DoesNotThrow(() => new SpeechHistoryAssembler().Accept(afterPacket));
                Assert.DoesNotThrow(() => HoldemUtterancePacketReader.Read(afterPacket));
                Assert.That(view.Game.LegalActions, Is.Null);
                if (i == seat - 1)
                {
                    Assert.That(view.Game.CanAccuseOnTurn, Is.False);
                    Assert.That(view.OwnCardChange, Is.Not.Null);
                }
            }
        }

        [Test]
        public void FoldedPlayerRequestsThroughPublicChatWithoutRegainingPlayRights()
        {
            Create(policy: HoldemLlmTableDealer.CreateUtterancePolicy()); Start();
            int folded = ChatState.CurrentSeat.Value.Value;
            ChatAction(BettingAction.Fold());
            AssertSpectatorCanRequestAndAffectCards(folded, false);
        }

        [Test]
        public void BustedPlayerCanRequestInNextHandAndChangeItsBoard()
        {
            Create(policy: HoldemLlmTableDealer.CreateUtterancePolicy(), random: new OpeningHandRandom()); Start();
            ChatAction(BettingAction.RaiseTo(100));
            ChatAction(BettingAction.Call());
            while (ChatState.CurrentSeat.HasValue) ChatAction(BettingAction.Fold());
            ChatRunout();
            var busted = Enumerable.Range(0, ChatState.SeatCount).Select(ChatState.GetSeatAt).Where(s => s.Stack == 0).ToArray();
            Assert.That(busted.Length, Is.EqualTo(1), string.Join(", ", Enumerable.Range(0, ChatState.SeatCount).Select(i => $"{ChatState.GetSeatAt(i).Seat}:{ChatState.GetSeatAt(i).Stack}")));
            int seat = busted[0].Seat.Value;
            Start();
            AssertSpectatorCanRequestAndAffectCards(seat, true);
        }
    }
}
