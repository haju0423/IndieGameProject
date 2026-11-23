using System;
using Poker.Application;
using Poker.Foundation;
using Poker.Transport;
using PokerCard = Poker.Foundation.Card;

// Local practice uses the same public conversation and private request receipt as a human seat.
internal sealed class Play2PracticeConversation
{
    private static readonly string[] Suits = { "클로버", "다이아", "하트", "스페이드" };
    private static readonly string[] Ranks =
        { "2", "3", "4", "5", "6", "7", "8", "9", "10", "잭", "퀸", "킹", "에이스" };
    private string handId;
    private int street = -1;
    private int speaker;
    private readonly HoldemPracticeClues clues = new HoldemPracticeClues();
    private int lastPhrase = -1;

    internal void Observe(HoldemRoomPacket view)
    {
        if (view.hasGame && view.hasChat) clues.Observe(view.game.handId, view.game.board, view.chat.entries);
    }

    internal bool TryAccuse(HoldemRoomPacket table, HoldemHostCommandBridge host, Guid connection,
        IRandomSource random, bool dealerAvailable)
    {
        int target = clues.Suspect;
        if (target == 0 || target == table.game.currentSeat || !dealerAvailable
            || table.hasTurnAccusation && table.turnAccusation.handId == table.game.handId) return false;
        if (!host.TryCreateProjection(connection, out var own) || !own.game.canAccuseOnTurn) return false;
        clues.PassOnClue();
        var suspect = Array.Find(own.game.seats, s => s.seat == target);
        if (suspect == null || !suspect.dealtIn || suspect.status == (int)HoldemSeatStatus.Folded
            || random.NextInt(4) != 0) return false;
        return host.Dispatch(connection, new HoldemWireRequest {
            protocol = HoldemRoomPacketMapper.ProtocolVersion, type = "turn-accusation",
            id = Guid.NewGuid().ToString("N"), sessionId = own.sessionId, handId = own.game.handId,
            version = own.game.version, street = own.game.street, accusationTarget = target
        }).Response.accepted;
    }

    internal void BeforeBet(HoldemRoomPacket table, HoldemHostCommandBridge host, Guid connection,
        IRandomSource random, bool dealerAvailable)
    {
        var game = table.game;
        if (handId != game.handId || street != game.street)
        {
            handId = game.handId;
            street = game.street;
            speaker = ChooseSpeaker(game, random);
        }
        if (game.currentSeat != speaker) return;
        speaker = 0;
        if (!host.TryCreateProjection(connection, out var own) || !own.hasChat || own.paused
            || !own.game.hasLegal || own.viewerSeat != own.game.currentSeat) return;

        var seat = Array.Find(own.game.seats, s => s.viewer);
        if (seat?.visibleCards == null || seat.visibleCards.Length != 2) return;
        bool moreCards = own.game.board.Length < 5 && street < (int)HoldemStreet.River;
        var window = own.ownUtterances;
        bool ask = moreCards && dealerAvailable && window != null && window.canSubmit
            && (window.unlimitedMessages || window.remaining > 0) && random.NextInt(100) < 60;
        string line;
        if (!moreCards) line = RiverTalk(own, random);
        else if (!ask && random.NextInt(3) == 0)
            line = own.game.legal.call ? $"그 {own.game.legal.callAmount}칩이 그렇게 자신 있어?" : "다들 다음 장 기다리는 눈치인데.";
        else line = CardTalk(own, seat, random);
        host.Dispatch(connection, new HoldemWireRequest {
            protocol = HoldemRoomPacketMapper.ProtocolVersion, type = "chat",
            id = Guid.NewGuid().ToString("N"), sessionId = own.sessionId, handId = handId,
            version = own.game.version, street = street, text = line, toDealer = ask,
            windowId = ask ? window.windowId : null
        });
    }

    private static int ChooseSpeaker(HoldemGamePacket game, IRandomSource random)
    {
        // Pick a seat once per betting street. A raise does not trigger another burst of lines.
        if (random.NextInt(100) >= (game.board.Length == 0 ? 90 : 65)) return 0;
        int count = 0, chosen = 0;
        foreach (var seat in game.seats)
        {
            if (seat.seat == 1 || seat.status != (int)HoldemSeatStatus.Active) continue;
            if (random.NextInt(++count) == 0) chosen = seat.seat;
        }
        return chosen;
    }

    private string CardTalk(HoldemRoomPacket own, HoldemSeatPacket seat, IRandomSource random)
    {
        var first = PokerCard.FromId(seat.visibleCards[0]);
        var second = PokerCard.FromId(seat.visibleCards[1]);
        var card = random.NextInt(2) == 0 ? first : second;
        // A public hint can also be a bluff. The line never certifies a hole card.
        if (random.NextInt(3) == 0) card = PokerCard.FromId(random.NextInt(PokerCard.DeckSize));
        string suit = Suits[(int)card.Suit - 1], rank = Ranks[(int)card.Rank - 2];
        // Count the speaker's cards and the public board only. The deck is hidden.
        int suited = (first.Suit == card.Suit ? 1 : 0) + (second.Suit == card.Suit ? 1 : 0);
        int ranked = (first.Rank == card.Rank ? 1 : 0) + (second.Rank == card.Rank ? 1 : 0);
        foreach (int id in own.game.board)
        {
            var board = PokerCard.FromId(id);
            if (board.Suit == card.Suit) suited++;
            if (board.Rank == card.Rank) ranked++;
        }

        int count = suited == 4 || ranked == 4 ? 3 : 6;
        int phrase = random.NextInt(count);
        if (phrase == lastPhrase) phrase = (phrase + 1) % count;
        lastPhrase = phrase;
        return phrase switch
        {
            0 => $"{suit} 한 장만 더 보고 싶은데.",
            1 => $"다음에 {suit} 나와도 다들 콜할 거야?",
            2 => $"나는 왠지 {suit} 쪽인데.",
            3 => $"{rank} 뜨면 판 재밌어지겠다.",
            4 => $"여기서 {rank} 한 장만 주면 좋겠네.",
            _ => $"다음 카드 {rank}에 한 표."
        };
    }

    private static string RiverTalk(HoldemRoomPacket own, IRandomSource random)
    {
        if (own.game.legal.call && random.NextInt(2) == 0)
            return $"여기서 {own.game.legal.callAmount}칩을 더 보자고?";
        var last = PokerCard.FromId(own.game.board[own.game.board.Length - 1]);
        return random.NextInt(2) == 0
            ? $"마지막 {Ranks[(int)last.Rank - 2]} 보고 표정 바뀐 사람?"
            : $"{Suits[(int)last.Suit - 1]} 기다리던 사람 있어?";
    }
}
