using System;
using System.Linq;
using Poker.Application;
using Poker.Foundation;
using TMPro;
using UnityEngine;
using PokerCard = Poker.Foundation.Card;

internal sealed class Play2HandGuide
{
    private static readonly (string name, string cards)[] Ranks =
    {
        ("로열 플러시", "10♠ J♠ Q♠ K♠ A♠"),
        ("스트레이트 플러시", "5♥ 6♥ 7♥ 8♥ 9♥"),
        ("포카드", "7♠ 7♦ 7♥ 7♣ 2♠"),
        ("풀하우스", "K♣ K♦ K♠ 5♥ 5♣"),
        ("플러시", "2♦ 4♦ 5♦ 9♦ J♦"),
        ("스트레이트", "8♦ 9♠ 10♦ J♠ Q♣"),
        ("트리플", "3♠ 3♥ 3♣ 9♦ J♣"),
        ("투페어", "A♣ A♥ 3♠ 3♦ K♣"),
        ("원페어", "9♠ 9♣ 3♦ 5♥ J♠"),
        ("하이 카드", "A♦ J♣ 9♥ 6♣ 2♠")
    };

    private const float SummaryHeight = Play2UGui.HandSummaryHeight;

    private readonly TextMeshProUGUI summary, cards, toggle;
    private readonly RectTransform sidebar, panel, legend;
    private bool expanded = true;
    private bool showActions = true;
    private bool actionPickerOpen;
    private readonly UnityEngine.UI.Image[] rankBackgrounds = new UnityEngine.UI.Image[Ranks.Length];
    private string handId;
    private int[] holeCards = Array.Empty<int>(), board = Array.Empty<int>();
    private int rankIndex = -1;

    internal Play2HandGuide(RectTransform sidebar, TMP_FontAsset font)
    {
        this.sidebar = sidebar;
        panel = Play2UGui.Fill("My Hand", sidebar);
        var header = Play2UGui.Rect("Current Hand", panel, Vector2.zero, Vector2.zero);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = Vector2.one;
        header.offsetMin = new Vector2(11, -SummaryHeight);
        header.offsetMax = new Vector2(-11, 0);
        Play2UGui.Text("Heading", header, "내 족보", new Vector2(-70, 21), new Vector2(96, 27), 19,
            Play2UGui.Cream, font, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        toggle = Play2UGui.Text("Toggle Hint", header, "", new Vector2(69, 21), new Vector2(96, 25), 14,
            Play2UGui.Hex("DEC694"), font, TextAlignmentOptions.MidlineRight);
        summary = Play2UGui.Text("Current Rank", header, "", new Vector2(0, -15), new Vector2(226, 40), 31,
            Play2UGui.Hex("EED3A1"), font, style: FontStyles.Bold);
        summary.textWrappingMode = TextWrappingModes.NoWrap;
        Play2HudArt.Fit(summary, 19);
        cards = Play2UGui.Text("Rank Detail", header, "", new Vector2(0, -46), new Vector2(229, 18), 12,
            Play2UGui.Hex("DED0B4"), font);
        Play2UGui.Panel("Hand Divider", header, new Vector2(0, -41), new Vector2(235, 1.25f),
            Play2UGui.Hex("D6C094"));

        legend = Play2UGui.Fill("Hand Rankings", panel);
        legend.offsetMin = new Vector2(9, 14);
        legend.offsetMax = new Vector2(-7, -SummaryHeight - 7);
        for (int i = 0; i < Ranks.Length; i++)
        {
            var entry = Ranks[i];
            var row = Play2UGui.Rect(entry.name, legend, Vector2.zero, Vector2.zero);
            row.anchorMin = new Vector2(0, 1f - (i + 1f) / Ranks.Length);
            row.anchorMax = new Vector2(1, 1f - (float)i / Ranks.Length);
            row.offsetMin = new Vector2(0, 1);
            row.offsetMax = Vector2.zero;
            var emphasis = Play2UGui.Fill("Current Rank Highlight", row);
            emphasis.offsetMin = new Vector2(0, -1);
            emphasis.offsetMax = new Vector2(0, 2);
            rankBackgrounds[i] = Play2UGui.Image(emphasis, Color.white);
            Play2HudArt.Apply(rankBackgrounds[i], "PlayingSelection");
            emphasis.gameObject.SetActive(false);

            if (i < Ranks.Length - 1)
            {
                var divider = Play2UGui.Panel("Row Divider", row, Vector2.zero, Vector2.zero, Play2UGui.Hex("C6AE797A"));
                divider.anchorMin = Vector2.zero;
                divider.anchorMax = Vector2.right;
                divider.offsetMin = new Vector2(157, 0);
                divider.offsetMax = new Vector2(-4, 1);
            }

            var name = Play2UGui.Text("Rank", row, entry.name, Vector2.zero, new Vector2(84, 28), 14,
                Play2UGui.Cream, font, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(1f, .5f);
            name.rectTransform.anchoredPosition = new Vector2(-43, 0);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.characterSpacing = -2;
            name.overflowMode = TextOverflowModes.Ellipsis;
            Play2HudArt.Fit(name, 8);

            string[] examples = entry.cards.Split(' ');
            for (int card = 0; card < examples.Length; card++)
                AddExampleCard(row, examples[card], card, font);
        }
        UpdateLayout();
        ShowRank(-1, "카드 배분 대기", "");
    }

    private static void AddExampleCard(RectTransform row, string face, int index, TMP_FontAsset font)
    {
        var card = Play2HudArt.Plate(face, row, Vector2.zero, Vector2.zero, "PlayingCard");
        card.anchorMin = Vector2.zero;
        card.anchorMax = Vector2.up;
        float x = 2 + index * 30;
        card.offsetMin = new Vector2(x, 2);
        card.offsetMax = new Vector2(x + 28, -2);

        string rank = face.Substring(0, face.Length - 1);
        char suit = face[face.Length - 1];
        var ink = suit == '♦' || suit == '♥' ? Play2UGui.Hex("8D1F1A") : Play2UGui.Ink;
        var rankLabel = Play2UGui.Text("Card Rank", card, rank, Vector2.zero, new Vector2(26, 16), 15,
            ink, font, style: FontStyles.Bold);
        SetCardTextBand(rankLabel, .43f, .96f);
        var suitLabel = Play2UGui.Text("Card Suit", card, suit.ToString(), Vector2.zero, new Vector2(26, 16), 14,
            ink, font, style: FontStyles.Bold);
        SetCardTextBand(suitLabel, .06f, .53f);
    }

    private static void SetCardTextBand(TextMeshProUGUI label, float bottom, float top)
    {
        label.rectTransform.anchorMin = new Vector2(0, bottom);
        label.rectTransform.anchorMax = new Vector2(1, top);
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        Play2HudArt.Fit(label, 6);
    }

    internal void Toggle()
    {
        expanded = !expanded;
        UpdateLayout();
    }

    internal void SetActionPickerOpen(bool value)
    {
        if (actionPickerOpen == value) return;
        actionPickerOpen = value;
        UpdateLayout();
    }

    internal string CurrentRank => summary.text;

    internal void SetActionsVisible(bool visible)
    {
        if (showActions == visible) return;
        showActions = visible;
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        bool showLegend = expanded && !actionPickerOpen;
        bool showDetails = !expanded && !actionPickerOpen;
        float actionHeight = showActions ? Play2UGui.ActionAreaHeight : 0;
        float height = actionHeight + (actionPickerOpen ? 0 : SummaryHeight + (showDetails ? 18 : 0));
        const float edge = Play2UGui.SidePanelMargin;
        const float top = Play2UGui.SidePanelTop;
        sidebar.anchorMin = new Vector2(1, showLegend ? 0 : 1);
        sidebar.anchorMax = Vector2.one;
        sidebar.offsetMin = new Vector2(-edge - Play2UGui.SidePanelWidth, showLegend ? 15 : -top - height);
        sidebar.offsetMax = new Vector2(-edge, -top);
        sidebar.gameObject.SetActive(height > 0);
        panel.offsetMax = new Vector2(0, -actionHeight);
        panel.gameObject.SetActive(!actionPickerOpen);
        legend.gameObject.SetActive(showLegend);
        cards.gameObject.SetActive(showDetails);
        toggle.text = expanded ? "[H] 접기" : "[H] 펼치기";
    }

    internal void Present(HoldemRoomPacket packet, bool dealing)
    {
        var game = packet.game;
        if (handId != game.handId)
        {
            handId = game.handId;
            holeCards = board = Array.Empty<int>();
            ShowRank(-1, "카드 배분 대기", "");
        }

        if (dealing) return;
        var viewer = game.seats.FirstOrDefault(seat => seat.seat == packet.viewerSeat);
        var myCards = viewer?.dealtIn == true ? viewer.visibleCards : null;
        if (myCards?.Length != 2)
        {
            string label = viewer?.dealtIn == true ? "카드 배분 대기" : "이번 판 내 카드 없음";
            if (summary.text != label) ShowRank(-1, label, "");
            holeCards = board = Array.Empty<int>();
            return;
        }
        if (holeCards.SequenceEqual(myCards) && board.SequenceEqual(game.board)) return;
        holeCards = myCards;
        board = game.board;

        if (board.Length >= 3)
        {
            var best = HoldemBestHand.Evaluate(board.Select(PokerCard.FromId).ToArray(), myCards.Select(PokerCard.FromId).ToArray());
            var value = best.Value;
            int index = value.Category == HandCategory.StraightFlush && value.GetTieBreaker(0) == (int)Rank.Ace
                ? 0 : 10 - (int)value.Category;
            string bestCards = string.Join("   ", Enumerable.Range(0, best.BestCardCount).Select(best.GetBestCard)
                .OrderByDescending(card => card.Rank).Select(CardText));
            ShowRank(index, RankName(index, value.GetTieBreaker(0), index == 7 ? value.GetTieBreaker(1) : 0), bestCards);
            return;
        }

        var groups = myCards.Concat(board).Select(PokerCard.FromId).GroupBy(card => (int)card.Rank)
            .OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key).ToArray();
        int count = groups[0].Count();
        bool twoPairs = count == 2 && groups.Length > 1 && groups[1].Count() == 2;
        int partialIndex = count == 4 ? 2 : count == 3 ? 6 : twoPairs ? 7 : count == 2 ? 8 : 9;
        ShowRank(partialIndex, RankName(partialIndex, groups[0].Key, twoPairs ? groups[1].Key : 0),
            $"현재 보이는 카드 {myCards.Length + board.Length}장");
    }

    private void ShowRank(int index, string label, string detail)
    {
        if (rankIndex >= 0)
        {
            rankBackgrounds[rankIndex].gameObject.SetActive(false);
        }
        rankIndex = index;
        if (index >= 0)
        {
            rankBackgrounds[index].gameObject.SetActive(true);
        }
        summary.text = label;
        cards.text = detail;
    }

    private static string RankName(int index, int high, int low)
    {
        if (index == 7) return $"{RankText(high)}·{RankText(low)} 투페어";
        if (index == 2 || index == 6 || index == 8 || index == 9) return $"{RankText(high)} {Ranks[index].name}";
        return Ranks[index].name;
    }

    internal static string CardText(PokerCard card) => RankText((int)card.Rank) + "♣♦♥♠"[(int)card.Suit - 1];

    private static string RankText(int rank) => rank switch
    {
        (int)Rank.Ace => "A",
        (int)Rank.King => "K",
        (int)Rank.Queen => "Q",
        (int)Rank.Jack => "J",
        _ => rank.ToString()
    };
}
