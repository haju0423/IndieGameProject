using System;
using NUnit.Framework;
using Poker.Application;

namespace Poker.Foundation.Tests
{
    public class HoldemPracticeClueTests
    {
        private readonly HoldemPracticeClues reader = new();
        private static readonly int[] Empty = Array.Empty<int>();
        private static readonly int Heart = new Card(Rank.Three, Suit.Hearts).Id;
        private static readonly int Club = new Card(Rank.Four, Suit.Clubs).Id;
        private static HoldemChatEntryPacket Line(long n, int seat, string text) =>
            new HoldemChatEntryPacket { sequence = n, seat = seat, text = text };

        [TestCase("딜러, 하트 한 장 줘", 1)]
        [TestCase("클럽 한 장 부탁해", 0)]
        [TestCase("하트 말고 클로버 한 장", 0)]
        [TestCase("하트나 다이아 한 장 부탁해", 0)]
        [TestCase("하트는 지겨워. 그만 줘", 0)]
        public void OnlyEarlierUnambiguousPublicHintCanBecomeASuspect(string line, int expected)
        {
            reader.Observe("first", Empty, Array.Empty<HoldemChatEntryPacket>());
            var chat = new[] { Line(1, 1, line) };
            reader.Observe("first", Empty, chat);
            Assert.That(reader.Suspect, Is.Zero);
            reader.Observe("first", new[] { Heart }, chat);
            Assert.That(reader.Suspect, Is.EqualTo(expected));
        }

        [Test]
        public void TwoPossibleSpeakersOrACommentAfterTheCardGiveNoImmediateAccusation()
        {
            reader.Observe("one", Empty, Array.Empty<HoldemChatEntryPacket>());
            var chat = new[] { Line(1, 2, "하트 한 장"), Line(2, 4, "하트 주세요") };
            reader.Observe("one", Empty, chat);
            reader.Observe("one", new[] { Heart }, chat);
            Assert.That(reader.Suspect, Is.Zero);
            reader.Observe("one", new[] { Heart, Club }, new[] { Line(3, 1, "클럽 한 장") });
            Assert.That(reader.Suspect, Is.Zero);
        }

        [Test]
        public void OldMessagesCannotReappearAsCluesAfterPassingOrStartingAnotherHand()
        {
            reader.Observe("a", Empty, Array.Empty<HoldemChatEntryPacket>());
            var chat = new[] { Line(1, 4, "하트 한 장") };
            reader.Observe("a", Empty, chat);
            reader.Observe("a", new[] { Heart }, chat);
            Assert.That(reader.Suspect, Is.EqualTo(4));
            reader.PassOnClue();
            reader.Observe("a", new[] { Heart }, chat);
            Assert.That(reader.Suspect, Is.Zero);
            reader.Observe("b", Empty, chat);
            reader.Observe("b", new[] { Heart }, chat);
            Assert.That(reader.Suspect, Is.Zero);
        }
    }
}
