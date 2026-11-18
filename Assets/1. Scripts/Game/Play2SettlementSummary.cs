using System;
using System.Linq;
using Poker.Application;
using Poker.Foundation;

internal static class Play2SettlementSummary
{
    internal static bool IsPublicHand(HoldemSeatPacket seat) =>
        seat.status != (int)HoldemSeatStatus.Folded && seat.visibleCards?.Length == 2 && seat.revealedBestCards?.Length == 5;

    internal sealed class ShowdownAward
    {
        internal int PotIndex;
        internal HoldemPotPacket Pot;
        internal int[] Seats;

        internal long PayoutThroughThisPot(HoldemGamePacket game, int seat)
        {
            var pots = game.result.pots;
            if (pots == null || pots.Length == 0) return Pot.payouts.Where(p => p.seat == seat).Sum(p => p.amount);
            return pots.Take(PotIndex + 1).Where(p => p?.payouts != null)
                .SelectMany(p => p.payouts).Where(p => p != null && p.seat == seat).Sum(p => p.amount);
        }

        internal string Caption(HoldemGamePacket game, Func<int, string> name)
        {
            var group = Seats;
            var payouts = Pot.payouts.Where(p => p != null && p.amount > 0).ToArray();
            var shown = payouts.Where(p => Array.IndexOf(group, p.seat) >= 0).ToArray();
            string players = group.Length == 1 ? name(group[0])
                : group.Length == game.seats.Count(seat => seat.dealtIn) ? $"{group.Length}명"
                : string.Join(", ", group.Select(name));
            string potName = game.result.pots?.Length > 1
                ? PotIndex == 0 ? "메인 팟 " : $"사이드 팟 {PotIndex} " : "";
            string outcome = payouts.Length > 1 ? "공동 승리" : "승리";
            string share = shown.Length == 1 ? $"+{shown[0].amount}칩"
                : shown.All(p => p.amount == shown[0].amount) ? $"각 +{shown[0].amount}칩"
                : $"{shown.Sum(p => p.amount)}칩 분배";
            return $"{potName}{players} {outcome}   {share}";
        }
    }

    internal static int[] RevealOrder(HoldemRoomPacket packet)
    {
        var game = packet.game;
        var eligible = game.seats.Where(IsPublicHand).Select(seat => seat.seat).ToArray();
        var river = packet.history?.entries?.Where(entry => entry != null && entry.hasAction
            && entry.street == (int)HoldemStreet.River && eligible.Contains(entry.seat)).ToArray();
        var aggressor = river?.LastOrDefault(entry => entry.action == (int)BettingActionKind.BetTo
            || entry.action == (int)BettingActionKind.RaiseTo);
        int first = aggressor?.seat ?? river?.FirstOrDefault()?.seat ?? (game.button % 4 + 1);
        return eligible.OrderBy(seat => (seat - first + 4) % 4).ToArray();
    }

    internal static ShowdownAward[] ShowdownAwards(HoldemGamePacket game)
    {
        var hands = game.seats.Where(IsPublicHand).ToDictionary(seat => seat.seat);
        var pots = game.result.pots ?? Array.Empty<HoldemPotPacket>();
        if (pots.Length == 0)
        {
            // Earlier snapshots may contain only the total awarded to each seat.
            pots = new[] { new HoldemPotPacket { amount = game.pot, payouts = game.seats.Where(s => s.awarded > 0)
                .Select(s => new HoldemPayoutPacket { seat = s.seat, amount = s.awarded }).ToArray() } };
        }

        var awards = new System.Collections.Generic.List<ShowdownAward>();
        for (int i = 0; i < pots.Length; i++)
        {
            var pot = pots[i];
            if (pot?.payouts == null) continue;
            var winners = pot.payouts.Where(p => p != null && p.amount > 0 && hands.ContainsKey(p.seat));
            // Physical cards may differ in a tie. Show each distinct five within its own pot.
            foreach (var group in winners.GroupBy(p => hands[p.seat].revealedBestCards.Aggregate(0UL, (mask, card) => mask | 1UL << card)))
                awards.Add(new ShowdownAward { PotIndex = i, Pot = pot, Seats = group.Select(p => p.seat).ToArray() });
        }
        return awards.ToArray();
    }

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
