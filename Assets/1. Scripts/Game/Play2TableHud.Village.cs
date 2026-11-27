using Poker.Foundation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed partial class Play2TableHud
{
    internal const float VillageBarHeight = 64f;
    private static readonly Color SeatIdle = Play2UGui.Hex("10191C9E");
    private static readonly Color SeatActing = Play2UGui.Hex("14292FEF");
    private static readonly Color SeatAwarded = Play2UGui.Hex("473F2BEA");
    private static readonly Color VillageTurn = Play2UGui.Hex("6E793A");

    private bool village, themeChanged;
    private RectTransform topBar, potPanel, actionTray, phaseBadge;
    private Button leaveButton, settingsButton;
    private TextMeshProUGUI phase;
    private Shadow potShadow;
    private readonly RectTransform[] actionKeys = new RectTransform[4];
    private readonly Outline[] marketActionEdges = new Outline[4];

    private void BuildVillageChrome(TMP_FontAsset font)
    {
        topBar = Play2UGui.Panel("Market Table Header", root, Vector2.zero, Vector2.zero, Play2UGui.Hex("F0E3C1"));
        topBar.SetAsFirstSibling();
        topBar.anchorMin = Vector2.up;
        topBar.anchorMax = Vector2.one;
        var rule = Play2UGui.Panel("Header Baseline", topBar, Vector2.zero, Vector2.zero, Play2HudArt.MarketEdge);
        rule.anchorMin = Vector2.zero;
        rule.anchorMax = Vector2.right;
        rule.offsetMin = Vector2.zero;
        rule.offsetMax = new Vector2(0, 1);

        phaseBadge = Play2HudArt.Plate("Table Phase", potPanel, new Vector2(-145, 0), new Vector2(105, 30), "MarketMoss");
        phase = Play2UGui.Text("Phase", phaseBadge, "", Vector2.zero, new Vector2(95, 29), 16,
            Play2HudArt.MarketLightText, font, style: FontStyles.Bold);
        Play2HudArt.Fit(phase, 12);

        actionTray = Play2HudArt.Plate("Market Action Tray", controls, new Vector2(0, 9), new Vector2(370, 110), "MarketPanel");
        actionTray.SetAsFirstSibling();
        var rim = AddMarketEdge(actionTray);
        rim.enabled = true;
        var shadow = actionTray.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(.16f, .22f, .11f, .6f);
        shadow.effectDistance = new Vector2(0, -4);
        for (int i = 0; i < actionRows.Length; i++)
        {
            var row = actionRows[i].rectTransform;
            actionKeys[i] = Play2HudArt.Plate("Shortcut", row, new Vector2(i == 3 ? -26 : -39, 0),
                new Vector2(20, 21), "MarketSeat");
            AddMarketEdge(actionKeys[i]).enabled = true;
            Play2UGui.Text("Key", actionKeys[i], "FERX"[i].ToString(), Vector2.zero, new Vector2(19, 20), 12,
                Play2HudArt.MarketInk, font, style: FontStyles.Bold);
            marketActionEdges[i] = AddMarketEdge(row);
        }

        AddMarketEdge((RectTransform)leaveButton.transform);
        AddMarketEdge((RectTransform)settingsButton.transform);
        AddMarketEdge(handGuide.HelpButton);
    }

    internal void SetVillageTheme(bool value)
    {
        village = value;
        themeChanged = true;
        topBar.gameObject.SetActive(village);
        phaseBadge.gameObject.SetActive(village);
        actionTray.gameObject.SetActive(village && acting);
        potShadow.enabled = !village;
        potPanel.anchorMin = potPanel.anchorMax = village ? new Vector2(.5f, 1f) : Vector2.up;
        potPanel.sizeDelta = new Vector2(village ? 443 : 216, 52);
        potPanel.GetComponent<Image>().color = village ? Color.clear : Play2UGui.Hex("142023E8");
        StyleText(potAndAnte, village ? new Vector2(152, 36) : new Vector2(194, 30),
            TextAlignmentOptions.MidlineLeft, village ? Play2HudArt.MarketBrown : Play2UGui.Gold);
        potAndAnte.textWrappingMode = village ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
        potAndAnte.rectTransform.anchoredPosition = village ? new Vector2(-5, 0) : new Vector2(0, 6);
        StyleText(street, village ? new Vector2(200, 25) : new Vector2(194, 18),
            TextAlignmentOptions.MidlineLeft, village ? Play2HudArt.MarketMuted : Play2UGui.Cream);
        street.rectTransform.anchoredPosition = village ? new Vector2(169, 0) : new Vector2(0, -13);
        StyleHeaderButton((RectTransform)leaveButton.transform);
        StyleHeaderButton((RectTransform)settingsButton.transform);
        StyleHeaderButton(handGuide.HelpButton);
        ((RectTransform)feedback.transform.parent).anchoredPosition = new Vector2(0, village ? -91 : -32);
        ((RectTransform)notice.transform.parent).anchoredPosition = new Vector2(0, village ? -141 : -85);

        Play2HudArt.Apply(ownTurn.GetComponent<Image>(), village ? "MarketMoss" : "PlayingBadge");
        ownTurn.GetComponentInChildren<TMP_Text>(true).color = village ? Play2HudArt.MarketLightText : Play2UGui.Gold;
        for (int i = 0; i < actionKeys.Length; i++)
        {
            actionKeys[i].gameObject.SetActive(village);
            marketActionEdges[i].enabled = village;
            var label = actionLabels[i].rectTransform;
            label.anchoredPosition = village ? new Vector2(9, 0) : Vector2.zero;
            label.sizeDelta = actionRows[i].rectTransform.sizeDelta - new Vector2(village ? 37 : 10, 4);
        }

        StylePicker(sizing);
        StylePicker(accusation);
        Play2HudArt.Apply(showdownCaption.GetComponent<Image>(), village ? "MarketSeat" : "PlayingBadge");
        showdownTitle.color = village ? Play2HudArt.MarketBrown : Play2UGui.Gold;
        showdownDetail.color = village ? Play2HudArt.MarketInk : Play2UGui.Cream;
        handGuide.SetVillageTheme(village);
        settlementCard.SetVillageTheme(village);
        AlignVillageChrome();
    }

    private void AlignVillageChrome()
    {
        var leave = (RectTransform)leaveButton.transform;
        if (!village)
        {
            leave.anchoredPosition = new Vector2(60, -32);
            potPanel.anchoredPosition = new Vector2(222, -35);
            return;
        }

        float leftDock = ((RectTransform)root.parent).rect.width - root.rect.width;
        topBar.offsetMin = new Vector2(-leftDock, -VillageBarHeight);
        topBar.offsetMax = Vector2.zero;
        leave.anchoredPosition = new Vector2(60 - leftDock, -32);
        potPanel.anchoredPosition = new Vector2(-leftDock / 2f, -32);
    }

    private void StyleHeaderButton(RectTransform button)
    {
        Play2HudArt.Apply(button.GetComponent<Image>(), village ? "MarketButton" : "PlayingBadge");
        button.GetComponent<Outline>().enabled = village;
        button.GetComponentInChildren<TMP_Text>(true).color = village ? Play2HudArt.MarketInk : Play2UGui.Cream;
    }

    private void DrawSeatTheme(int i, bool own, bool hasTurn, bool won)
    {
        var label = seatLabels[i];
        var stack = seatStacks[i];
        var state = seatStates[i];
        var fill = seatPanels[i].GetComponent<Image>();
        var outline = seatOutlines[i];
        bool withStatus = hasTurn || won || !string.IsNullOrEmpty(state.text);
        if (!village)
        {
            seatPanels[i].sizeDelta = new Vector2(152, 56);
            StyleText(label, new Vector2(136, 20), TextAlignmentOptions.Center,
                hasTurn || won ? Play2UGui.Gold : Play2UGui.Cream);
            StyleText(stack, new Vector2(144, 24), TextAlignmentOptions.Center, Play2UGui.Cream);
            label.rectTransform.anchoredPosition = new Vector2(0, withStatus ? 16 : 12);
            stack.rectTransform.anchoredPosition = new Vector2(0, own ? 5 : withStatus ? -1 : -8);
            fill.color = own ? Color.clear : won ? SeatAwarded : hasTurn ? SeatActing : SeatIdle;
            state.rectTransform.anchoredPosition = new Vector2(0, -19);
            state.color = Play2UGui.Gold;
            seatTurns[i].color = Play2UGui.Gold;
            outline.effectColor = Play2UGui.Gold;
            outline.enabled = hasTurn && !own;
            return;
        }

        seatPanels[i].sizeDelta = new Vector2(166, 52);
        Play2HudArt.Apply(fill, won ? "MarketPrimary" : "MarketSeat");
        StyleText(label, new Vector2(82, 23), TextAlignmentOptions.MidlineLeft, Play2HudArt.MarketInk);
        StyleText(stack, new Vector2(59, 23), TextAlignmentOptions.MidlineRight, Play2HudArt.MarketMuted);
        label.rectTransform.anchoredPosition = new Vector2(-29, withStatus ? 9 : 0);
        stack.rectTransform.anchoredPosition = new Vector2(44, withStatus ? 9 : 0);
        state.rectTransform.anchoredPosition = new Vector2(0, -14);
        state.color = won ? Play2HudArt.MarketBrown : Play2HudArt.MarketInk;
        seatTurns[i].color = VillageTurn;
        seatTurns[i].rectTransform.anchoredPosition = new Vector2(-77, 0);
        outline.effectColor = hasTurn ? VillageTurn : Play2HudArt.MarketEdge;
        outline.enabled = !own;
    }

    private void DrawActionTheme(int index, bool enabled)
    {
        if (village)
        {
            Play2HudArt.Apply(actionRows[index], enabled && (index == 1 || index == 2 && !packet.game.legal.check && !packet.game.legal.call)
                ? "MarketPrimary" : "MarketButton");
            if (!enabled) actionRows[index].color = Play2UGui.Hex("DDD0AC");
            actionLabels[index].color = enabled ? Play2HudArt.MarketInk : Play2HudArt.MarketMuted;
            return;
        }

        Play2HudArt.Apply(actionRows[index], enabled && index == 2 ? "GoldButton" : "PlayingAction");
        if (!enabled) actionRows[index].color = Play2UGui.Hex("1A282BFF");
        actionLabels[index].color = !enabled ? Play2UGui.Hex("8FA19FFF") : index == 2 ? PaperInk : Play2UGui.Cream;
    }

    private static Outline AddMarketEdge(RectTransform target)
    {
        var edge = target.gameObject.AddComponent<Outline>();
        edge.effectColor = Play2HudArt.MarketEdge;
        edge.effectDistance = new Vector2(1, -1);
        edge.enabled = false;
        return edge;
    }

    private static void StyleText(TMP_Text text, Vector2 size, TextAlignmentOptions alignment, Color color)
    {
        text.rectTransform.sizeDelta = size;
        text.alignment = alignment;
        text.color = color;
    }

    private void StylePicker(RectTransform picker)
    {
        var body = picker.Find(picker == sizing ? "Bet Card" : "Accusation Card");
        Play2HudArt.Apply(body.GetComponent<Image>(), village ? "MarketPanel" : "PinePanel");
        foreach (var button in body.GetComponentsInChildren<Button>(true))
        {
            if (button.name.StartsWith("Target ") || button.name == "Previous" || button.name == "Next"
                || button.name == "Confirm Accusation" || button.name == "취소") continue;
            Play2HudArt.Apply((Image)button.targetGraphic, button.name == "Commit"
                ? village ? "MarketPrimary" : "GoldButton"
                : village ? "MarketButton" : button.name == "Cancel Bet" ? "HeaderBadge" : "ActionRow");
            button.GetComponentInChildren<TMP_Text>(true).color = button.name == "Commit" ? PaperInk
                : village ? Play2HudArt.MarketInk : Play2UGui.Cream;
        }

        foreach (var label in body.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label.GetComponentInParent<Button>() != null) continue;
            label.color = village ? Play2HudArt.MarketInk
                : label == betHeading ? Play2UGui.Gold : Play2UGui.Cream;
        }
        if (suspects.Length > 0) UpdateSuspect();
    }

    private static string PhaseName(int value) => ((HoldemStreet)value) switch
    {
        HoldemStreet.Preflop => "프리플랍",
        HoldemStreet.Flop => "플랍",
        HoldemStreet.Turn => "턴",
        HoldemStreet.River => "리버",
        _ => "정산"
    };
}
