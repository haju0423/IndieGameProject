using System;
using System.Linq;
using Poker.Application;
using Poker.Foundation;

internal static class Play2SettlementSummary
{
    internal static bool IsPublicHand(HoldemSeatPacket seat) =>
        seat.status != (int)HoldemSeatStatus.Folded && seat.visibleCards?.Length == 2 && seat.revealedBestCards?.Length == 5;

    internal static HandValue Value(HoldemSeatPacket seat) =>
        HandEvaluator.Evaluate(seat.revealedBestCards.Select(Poker.Foundation.Card.FromId).ToArray());

    internal static int[] OrderedBestCards(HoldemSeatPacket seat)
    {
        var cards = seat.revealedBestCards.Select(Poker.Foundation.Card.FromId).ToArray();
        var value = Value(seat);
        bool wheel = value.Category is HandCategory.Straight or HandCategory.StraightFlush && value.GetTieBreaker(0) == 5;
        var groups = cards.GroupBy(card => card.Rank).ToDictionary(group => group.Key, group => group.Count());
        return cards.OrderByDescending(card => groups[card.Rank])
            .ThenByDescending(card => wheel && card.Rank == Poker.Foundation.Rank.Ace ? 1 : (int)card.Rank)
            .ThenBy(card => card.Suit).Select(card => card.Id).ToArray();
    }

    internal static string HandName(HandValue value)
    {
        string high = Rank(value.GetTieBreaker(0));
        return value.Category switch
        {
            HandCategory.HighCard => high + " 하이 카드",
            HandCategory.OnePair => high + " 원페어",
            HandCategory.TwoPair => high + ", " + Rank(value.GetTieBreaker(1)) + " 투페어",
            HandCategory.ThreeOfAKind => high + " 트리플",
            HandCategory.Straight => high + " 하이 스트레이트",
            HandCategory.Flush => high + " 하이 플러시",
            HandCategory.FullHouse => high + ", " + Rank(value.GetTieBreaker(1)) + " 풀하우스",
            HandCategory.FourOfAKind => high + " 포카드",
            HandCategory.StraightFlush => value.GetTieBreaker(0) == 14 ? "로열 플러시" : high + " 하이 스트레이트 플러시",
            _ => ""
        };
    }

    internal static string Heading(HoldemGamePacket game, Func<int, string> name, int viewerSeat)
    {
        if (!game.hasResult) return "남은 칩 정산";
        var winners = game.seats.Where(seat => seat.awarded > 0).ToArray();
        if (winners.Length == 0) return "이번 판 결과";
        if (winners.Length == 1) return winners[0].seat == viewerSeat ? "승리" : name(winners[0].seat) + " 승리";
        if (game.result?.pots?.Length > 1) return "팟별 정산";
        return winners.Length + "명 공동 승리";
    }

    internal static string Reason(HoldemRoomPacket packet, Func<int, string> name)
    {
        var game = packet.game;
        if (!game.hasResult) return "동률인 플레이어에게 나머지 칩을 배분합니다.";
        var result = game.result;
        if (result == null) return "";
        if (result.kind == (int)HoldemResultKind.Fold)
            return "다른 플레이어가 모두 폴드해 팟을 획득했습니다.";
        if (result.kind == (int)HoldemResultKind.Accusation)
        {
            int target = result.accusedSeat;
            if (target == 0 && packet.hasTurnAccusation && packet.turnAccusation.handId == game.handId && packet.turnAccusation.caught)
                target = packet.turnAccusation.target;
            return target > 0 ? name(target) + "의 카드 조작을 고발해 팟을 획득했습니다." : "고발로 팟을 획득했습니다.";
        }
        if (result.kind != (int)HoldemResultKind.Showdown) return "이번 판 결과를 정산했습니다.";
        var pots = (result.pots ?? Array.Empty<HoldemPotPacket>()).Where(pot => pot != null).ToArray();
        if (pots.Length > 1) return "각 팟에 참여한 플레이어끼리 족보를 비교해 정산했습니다.";
        return pots.Length == 1 ? PotReason(game, pots[0]) : "공개된 최종 5장으로 승부를 결정했습니다.";
    }

    internal static string PotReason(HoldemGamePacket game, HoldemPotPacket pot)
    {
        var payouts = (pot?.payouts ?? Array.Empty<HoldemPayoutPacket>()).Where(payout => payout != null && payout.amount > 0).ToArray();
        if (payouts.Length > 1) return "최종 5장의 우열이 같아 팟을 나눴습니다.";
        if (payouts.Length == 0) return "";
        int winnerSeat = payouts[0].seat;
        if (pot.eligibleSeats?.Contains(winnerSeat) != true) return "팟을 배분했습니다.";
        var winner = game.seats.FirstOrDefault(seat => seat.seat == winnerSeat && IsPublicHand(seat));
        if (winner == null) return "";
        var contenders = game.seats.Where(seat => seat.seat != winnerSeat && IsPublicHand(seat)
            && (pot.eligibleSeats ?? Array.Empty<int>()).Contains(seat.seat)).Select(Value).OrderByDescending(value => value).ToArray();
        var winning = Value(winner);
        if (contenders.Length == 0) return HandName(winning) + "로 팟을 획득했습니다.";
        var runnerUp = contenders[0];
        if (winning.Category != runnerUp.Category)
            return HandName(winning) + "로 " + Play2HandGuide.CategoryLabel(runnerUp) + "보다 높은 족보를 완성했습니다.";
        for (int i = 0; i < winning.TieBreakerCount; i++)
        {
            if (winning.GetTieBreaker(i) == runnerUp.GetTieBreaker(i)) continue;
            int rank = winning.GetTieBreaker(i);
            string particle = rank is 1 or 3 or 6 or 7 or 8 or 10 ? "이 " : "가 ";
            string category = Play2HandGuide.CategoryLabel(winning);
            return category + "끼리 비교해 " + DifferenceLabel(winning.Category, i) + " " + Rank(rank)
                + particle + Rank(runnerUp.GetTieBreaker(i)) + "보다 높아 승리했습니다.";
        }
        return "최종 5장의 우열이 같습니다.";
    }

    private static string DifferenceLabel(HandCategory category, int index) => category switch
    {
        HandCategory.OnePair => index == 0 ? "페어 숫자" : index + "번째 키커",
        HandCategory.TwoPair => index == 0 ? "높은 페어" : index == 1 ? "낮은 페어" : "키커",
        HandCategory.ThreeOfAKind => index == 0 ? "트리플 숫자" : index + "번째 키커",
        HandCategory.FullHouse => index == 0 ? "트리플 숫자" : "페어 숫자",
        HandCategory.FourOfAKind => index == 0 ? "포카드 숫자" : "키커",
        HandCategory.Straight or HandCategory.StraightFlush => "스트레이트의 가장 높은 카드",
        _ => index == 0 ? "가장 높은 카드" : (index + 1) + "번째로 높은 카드"
    };

    private static string Rank(int rank) => rank switch { 14 => "A", 13 => "K", 12 => "Q", 11 => "J", _ => rank.ToString() };
}
