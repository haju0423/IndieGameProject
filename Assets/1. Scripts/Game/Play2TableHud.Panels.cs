using System;
using System.Linq;
using Poker.Application;
using TMPro;
using UnityEngine;

internal sealed partial class Play2TableHud
{
    private static readonly Color PaperInk = Play2UGui.Hex("241E15");
    private static readonly Color ResultInk = Play2UGui.Hex("173226");
    private static readonly Color PaperRed = Play2UGui.Hex("9C2922");

    private static RectTransform MakePicker(string name, RectTransform root, TMP_FontAsset font, out TextMeshProUGUI hand)
    {
        var picker = Play2UGui.Rect(name, root, new Vector2(0, -27), new Vector2(452, 278));
        var summary = Play2HudArt.Plate("Hand Summary", picker, new Vector2(0, 117), new Vector2(430, 40), "HeaderBadge");
        hand = Play2UGui.Text("Current Hand", summary, "", Vector2.zero, new Vector2(412, 29), 21,
            Play2UGui.Cream, font, style: FontStyles.Bold);
        Play2HudArt.Fit(hand, 13);
        picker.gameObject.SetActive(false);
        return picker;
    }

    private static void PickerHeading(RectTransform body, string title, float y, TMP_FontAsset font)
    {
        Play2HudArt.Plate("Diamond", body, new Vector2(-195, y), new Vector2(15, 18), "Diamond");
        Play2UGui.Text("Heading", body, title, new Vector2(-86, y), new Vector2(185, 27), 21,
            Play2UGui.Cream, font, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
    }

    private static RectTransform Arrow(Transform parent, Vector2 position, bool left)
    {
        var cap = Play2HudArt.Plate(left ? "Previous" : "Next", parent, position, new Vector2(50, 37), "Keycap");
        Play2HudArt.Plate("Arrow", cap, Vector2.zero, new Vector2(17, 19), left ? "ArrowLeft" : "ArrowRight");
        return cap;
    }

    private static RectTransform KeyAction(Transform parent, TMP_FontAsset font, string key, string label,
        Vector2 position, Vector2 size, string asset)
    {
        var plate = Play2HudArt.Plate(label, parent, position, size, asset);
        float capWidth = key == "Home" || key == "Enter" ? 50 : key.Length == 1 ? 25 : 33;
        float contentWidth = capWidth + 48;
        Play2HudArt.Keycap(plate, font, key, new Vector2((capWidth - contentWidth) / 2f, 0), capWidth);
        Play2UGui.Text("Label", plate, label, new Vector2(contentWidth / 2f - 20, 0), new Vector2(48, 24),
            16, Play2UGui.Cream, font, style: FontStyles.Bold);
        return plate;
    }

    private (RectTransform panel, TextMeshProUGUI hand, TextMeshProUGUI amount, TextMeshProUGUI heading,
        RectTransform adjustments, RectTransform allIn) BuildBetPicker(RectTransform parent, TMP_FontAsset font)
    {
        var panel = MakePicker("Bet Sizing", parent, font, out var hand);
        var body = Play2HudArt.Plate("Bet Card", panel, new Vector2(0, -24), new Vector2(440, 242), "PinePanel");
        Play2HudArt.Plate("Diamond", body, new Vector2(-195, 91), new Vector2(15, 18), "Diamond");
        var heading = Play2UGui.Text("Heading", body, "베팅 금액", new Vector2(-80, 91), new Vector2(202, 28), 21,
            Play2UGui.Cream, font, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        var amount = Play2UGui.Text("Chips", body, "", new Vector2(0, 44), new Vector2(212, 55), 43,
            Play2UGui.Cream, font, style: FontStyles.Bold);
        Play2HudArt.Fit(amount, 22);
        var adjustments = Play2UGui.Fill("Adjustments", body);
        Arrow(adjustments, new Vector2(-138, 43), true);
        Arrow(adjustments, new Vector2(138, 43), false);
        Play2UGui.Text("Adjustment Step", adjustments, "Shift + ← / →   10칩", new Vector2(0, 11), new Vector2(242, 20), 12,
            Play2UGui.Gold, font);
        KeyAction(adjustments, font, "Home", "최소", new Vector2(-140, -23), new Vector2(126, 40), "ActionRow");
        KeyAction(adjustments, font, "P", "하프", new Vector2(0, -23), new Vector2(126, 40), "ActionRow");
        var allIn = KeyAction(adjustments, font, "A", "올인", new Vector2(140, -23), new Vector2(126, 40), "ActionRow");
        KeyAction(body, font, "Enter", "확정", new Vector2(-96, -83), new Vector2(215, 46), "GoldButton");
        KeyAction(body, font, "Esc", "취소", new Vector2(120, -83), new Vector2(169, 46), "HeaderBadge");
        return (panel, hand, amount, heading, adjustments, allIn);
    }

    private (RectTransform panel, TextMeshProUGUI hand, UnityEngine.UI.Image[] plates, TextMeshProUGUI[] names)
        BuildAccusationPicker(RectTransform parent, TMP_FontAsset font)
    {
        var panel = MakePicker("Accusation", parent, font, out var hand);
        var body = Play2HudArt.Plate("Accusation Card", panel, new Vector2(0, -18), new Vector2(440, 230), "PinePanel");
        PickerHeading(body, "고발 대상", 85, font);
        Arrow(body, new Vector2(-183, 22), true);
        Arrow(body, new Vector2(183, 22), false);
        var plates = new UnityEngine.UI.Image[3];
        var names = new TextMeshProUGUI[plates.Length];
        for (int i = 0; i < plates.Length; i++)
        {
            var choice = Play2HudArt.Plate("Target " + (i + 1), body, Vector2.zero, new Vector2(94, 49), "HeaderBadge");
            plates[i] = choice.GetComponent<UnityEngine.UI.Image>();
            names[i] = Play2UGui.Text("Player", choice, "", Vector2.zero, new Vector2(82, 38), 20,
                Play2UGui.Cream, font, style: FontStyles.Bold);
            names[i].textWrappingMode = TextWrappingModes.NoWrap;
            names[i].overflowMode = TextOverflowModes.Ellipsis;
            names[i].richText = false;
            Play2HudArt.Fit(names[i], 12);
        }

        KeyAction(body, font, "Enter", "고발", new Vector2(-96, -74), new Vector2(215, 46), "RedButton");
        KeyAction(body, font, "Esc", "취소", new Vector2(120, -74), new Vector2(169, 46), "HeaderBadge");
        return (panel, hand, plates, names);
    }

    private sealed class SettlementCard
    {
        private readonly RectTransform card;
        private readonly TextMeshProUGUI title, winners, requests;
        private readonly UnityEngine.UI.Button next;
        private readonly TextMeshProUGUI nextLabel;
        private readonly (RectTransform frame, TextMeshProUGUI name, TextMeshProUGUI message,
            TextMeshProUGUI interpretation, TextMeshProUGUI result, TextMeshProUGUI position)[] rows = new
            (RectTransform, TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI)[3];

        internal SettlementCard(RectTransform parent, TMP_FontAsset font, UnityEngine.Events.UnityAction advance)
        {
            card = Play2HudArt.Plate("Settlement Card", parent, new Vector2(0, -10), new Vector2(526, 220), "CreamPanel");
            var mark = Play2HudArt.Plate("Diamond", card, Vector2.zero, new Vector2(16, 18), "Diamond");
            Top(mark, -87, -34);
            title = TextAtTop("Heading", "이번 판 종료", 4, -34, 152, 31, 24, PaperInk);
            winners = TextAtTop("Winners", "", 0, -83, 480, 58, 38, ResultInk);
            Play2HudArt.Fit(winners, 20);
            winners.richText = false;
            var rule = Play2HudArt.Plate("Header Rule", card, Vector2.zero, new Vector2(468, 2), "Divider");
            Top(rule, 0, -121);
            requests = TextAtTop("Selected Requests", "채택된 딜러 요청", -117, -145, 232, 29, 18, PaperInk,
                TextAlignmentOptions.MidlineLeft);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = Play2HudArt.Plate("Request " + (i + 1), card, Vector2.zero, new Vector2(470, 51), "PaperRow");
                Top(row, 0, -183 - i * 54);
                var name = RowText(row, "Player", -190, 0, 70, 29, 14, TextAlignmentOptions.Center);
                var message = RowText(row, "Message", -19, 9, 255, 20, 15, TextAlignmentOptions.MidlineLeft);
                var interpretation = RowText(row, "Interpretation", -19, -10, 255, 18, 12, TextAlignmentOptions.MidlineLeft);
                var result = RowText(row, "Card", 166, 8, 108, 24, 18, TextAlignmentOptions.Center);
                var position = RowText(row, "Card Position", 166, -12, 108, 18, 11, TextAlignmentOptions.Center);
                rows[i] = (row, name, message, interpretation, result, position);
            }

            next = Play2HudArt.Button("Next Hand", card, "다음 판", Vector2.zero, new Vector2(153, 43),
                "GoldButton", PaperInk, font, advance, 19);
            var nextRect = (RectTransform)next.transform;
            nextRect.anchorMin = nextRect.anchorMax = new Vector2(1, 0);
            nextRect.anchoredPosition = new Vector2(-101, 36);
            nextLabel = next.GetComponentInChildren<TextMeshProUGUI>();
            card.gameObject.SetActive(false);

            TextMeshProUGUI TextAtTop(string name, string text, float x, float y, float w, float h, int size, Color color,
                TextAlignmentOptions alignment = TextAlignmentOptions.Center)
            {
                var label = Play2UGui.Text(name, card, text, Vector2.zero, new Vector2(w, h), size, color, font, alignment, FontStyles.Bold);
                Top(label.rectTransform, x, y);
                return label;
            }

            TextMeshProUGUI RowText(RectTransform row, string name, float x, float y, float w, float h, int size,
                TextAlignmentOptions alignment)
            {
                var label = Play2UGui.Text(name, row, "", new Vector2(x, y), new Vector2(w, h), size, PaperInk, font, alignment,
                    name == "Interpretation" ? FontStyles.Normal : FontStyles.Bold);
                label.richText = false;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;
                return label;
            }
        }

        private static void Top(RectTransform rect, float x, float y)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
        }

        internal void Hide() => card.gameObject.SetActive(false);

        internal void Present(HoldemRoomPacket packet, bool oddChips, bool isHost, Func<int, string> name)
        {
            var game = packet.game;
            bool show = game.hasResult || oddChips;
            card.gameObject.SetActive(show);
            if (!show) return;

            bool canAdvance = isHost && !packet.paused && (oddChips || game.canContinue);
            next.gameObject.SetActive(canAdvance);
            nextLabel.text = oddChips ? "정산하기" : "다음 판";
            title.text = game.isOver ? "경기 종료" : oddChips ? "칩 정산" : "이번 판 종료";
            var awards = game.seats.Where(seat => seat.awarded > 0)
                .Select(seat => $"{name(seat.seat)} +{seat.awarded}칩").ToArray();
            winners.text = oddChips ? "남은 칩 분배 대기" : awards.Length > 0 ? string.Join("  ·  ", awards) : "획득한 칩 없음";

            var selections = packet.dealerSelections ?? Array.Empty<HoldemDealerSelectionPacket>();
            int count = game.hasResult ? Mathf.Min(rows.Length, selections.Length) : 0;
            requests.gameObject.SetActive(count > 0);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                row.frame.gameObject.SetActive(i < count);
                if (i >= count) continue;

                var item = selections[i];
                row.name.text = name(item.speaker);
                row.message.text = item.text;
                row.interpretation.text = Brief(item.interpretation, 120);
                row.position.text = item.hasAppliedCard ? $"왼쪽 {item.boardIndex + 1}번째" : "";
                row.result.color = PaperInk;
                row.result.text = "정보 없음";
                if (!item.hasAppliedCard) continue;

                var revealed = Poker.Foundation.Card.FromId(item.card);
                row.result.color = revealed.Suit == Poker.Foundation.Suit.Hearts || revealed.Suit == Poker.Foundation.Suit.Diamonds
                    ? PaperRed : PaperInk;
                row.result.text = Play2HandGuide.CardText(revealed);
            }

            float height = (count > 0 ? 158 + count * 54 : 135) + (canAdvance ? 64 : 18);
            card.sizeDelta = new Vector2(card.sizeDelta.x, height);
        }
    }
}
