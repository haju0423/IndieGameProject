using System;
using System.Linq;
using Poker.Application;
using Poker.Foundation;
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
        var panel = Play2UGui.Rect("Bet Sizing", parent, Vector2.zero, new Vector2(370, 190));
        panel.anchorMin = panel.anchorMax = new Vector2(1, 0);
        panel.anchoredPosition = new Vector2(-200, 109);
        var body = Play2HudArt.Plate("Bet Card", panel, Vector2.zero, panel.sizeDelta, "PinePanel", true);
        var heading = Play2UGui.Text("Heading", body, "베팅 금액", new Vector2(-108, 59), new Vector2(117, 28), 17,
            Play2UGui.Gold, font, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        var hand = Play2UGui.Text("Current Hand", body, "", new Vector2(-99, 37), new Vector2(135, 24), 13,
            Play2UGui.Cream, font, TextAlignmentOptions.MidlineLeft);
        Play2HudArt.Fit(hand, 10);
        var amount = Play2UGui.Text("Chips", body, "", new Vector2(100, 53), new Vector2(135, 38), 30,
            Play2UGui.Cream, font, style: FontStyles.Bold);
        Play2HudArt.Fit(amount, 15);
        var adjustments = Play2UGui.Fill("Adjustments", body);
        Play2HudArt.Button("Decrease", adjustments, "−", new Vector2(-153, 10), new Vector2(36, 39), "ActionRow", Play2UGui.Cream, font, () => StepBet(-1));
        Play2HudArt.Button("Increase", adjustments, "+", new Vector2(153, 10), new Vector2(36, 39), "ActionRow", Play2UGui.Cream, font, () => StepBet(1));
        var track = Play2UGui.Panel("Bet Slider", adjustments, new Vector2(0, 10), new Vector2(248, 38), Color.clear, true);
        amountSlider = track.gameObject.AddComponent<UnityEngine.UI.Slider>();
        amountSlider.wholeNumbers = true;
        amountSlider.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        var rail = Play2UGui.Panel("Rail", track, Vector2.zero, new Vector2(232, 6), Play2UGui.Hex("776D5C"));
        var fill = Play2UGui.Fill("Fill", rail);
        Play2UGui.Image(fill, Play2UGui.Gold);
        amountSlider.fillRect = fill;
        var grip = Play2UGui.Rect("Grip Area", track, Vector2.zero, new Vector2(232, 26));
        var handle = Play2HudArt.Plate("Grip", grip, Vector2.zero, new Vector2(18, 0), "Keycap", true);
        amountSlider.handleRect = handle;
        amountSlider.targetGraphic = handle.GetComponent<UnityEngine.UI.Image>();
        amountSlider.onValueChanged.AddListener(value => SelectBet(value > maximumBet ? packet.game.legal.allInTarget : (long)value));
        Play2UGui.Text("Pot Basis", adjustments, "팟 기준", new Vector2(-145, -27), new Vector2(57, 28), 14, Play2UGui.Cream, font);
        for (int i = 0; i < potPercents.Length; i++)
        {
            int percent = potPercents[i];
            potButtons[i] = Play2HudArt.Button("Pot " + percent, adjustments, percent + "%", new Vector2(-91 + 57*i, -27),
                new Vector2(54, 31), "ActionRow", Play2UGui.Cream, font, () => SelectPot(percent), 15);
            var label = potButtons[i].GetComponentInChildren<TextMeshProUGUI>();
            label.rectTransform.sizeDelta = new Vector2(52, 27);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            Play2HudArt.Fit(label, 12);
        }
        var allIn = (RectTransform)Play2HudArt.Button("All In", adjustments, "올인", new Vector2(140, -27), new Vector2(53, 31),
            "ActionRow", Play2UGui.Cream, font, () => SelectBet(WholeStack)).transform;
        betCommit = Play2HudArt.Button("Commit", body, "", new Vector2(-52, -67), new Vector2(231, 37),
            "GoldButton", PaperInk, font, () => RaiseTo(betAmount), 18).GetComponentInChildren<TextMeshProUGUI>();
        Play2HudArt.Button("Cancel Bet", body, "취소", new Vector2(123, -67), new Vector2(91, 37),
            "HeaderBadge", Play2UGui.Cream, font, CloseActionPanels);
        panel.gameObject.SetActive(false);
        return (panel, hand, amount, heading, adjustments, allIn);
    }



    private (RectTransform panel, TextMeshProUGUI hand, UnityEngine.UI.Image[] plates, TextMeshProUGUI[] names)
        BuildAccusationPicker(RectTransform parent, TMP_FontAsset font)
    {
        var panel = MakePicker("Accusation", parent, font, out var hand);
        var body = Play2HudArt.Plate("Accusation Card", panel, new Vector2(0, -18), new Vector2(440, 230), "PinePanel", true);
        PickerHeading(body, "고발 대상", 85, font);
        Play2HudArt.Clickable(Arrow(body, new Vector2(-183, 22), true), () => StepSuspect(-1));
        Play2HudArt.Clickable(Arrow(body, new Vector2(183, 22), false), () => StepSuspect(1));
        var plates = new UnityEngine.UI.Image[3];
        var names = new TextMeshProUGUI[plates.Length];
        for (int i = 0; i < plates.Length; i++)
        {
            var choice = Play2HudArt.Plate("Target " + (i + 1), body, Vector2.zero, new Vector2(94, 49), "HeaderBadge");
            int index = i;
            Play2HudArt.Clickable(choice, () => { suspectIndex = index; UpdateSuspect(); });
            plates[i] = choice.GetComponent<UnityEngine.UI.Image>();
            names[i] = Play2UGui.Text("Player", choice, "", Vector2.zero, new Vector2(82, 38), 20,
                Play2UGui.Cream, font, style: FontStyles.Bold);
            names[i].textWrappingMode = TextWrappingModes.NoWrap;
            names[i].overflowMode = TextOverflowModes.Ellipsis;
            names[i].richText = false;
            Play2HudArt.Fit(names[i], 12);
        }

        Play2HudArt.Button("Confirm Accusation", body, "고발 확정", new Vector2(-96, -74), new Vector2(215, 46), "RedButton", Play2UGui.Cream, font, ConfirmAccusation);
        Play2HudArt.Clickable(KeyAction(body, font, "Esc", "취소", new Vector2(120, -74), new Vector2(169, 46), "HeaderBadge"), CloseActionPanels);
        return (panel, hand, plates, names);
    }

}
