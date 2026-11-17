using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Poker.Application;
using Poker.Foundation;
using Poker.Transport;
using TMPro;
using UnityEngine;

internal sealed partial class Play2TableHud
{
    private readonly Play2TablePreview table;
    private readonly Play2FrontEnd owner;
    private readonly RectTransform root, controls, sizing, accusation, betAdjustments, allInOption;
    private readonly TextMeshProUGUI street, potAndAnte, turn, notice, target, feedback, betHeading, betHand, accusationHand;
    private readonly UnityEngine.UI.Image turnMark;
    private readonly UnityEngine.UI.Image[] suspectPlates;
    private readonly TextMeshProUGUI[] suspectNames;
    private readonly SettlementCard settlementCard;
    private readonly Play2InGameSettings settings;
    private readonly TextMeshProUGUI[] seatLabels = new TextMeshProUGUI[4];
    private readonly UnityEngine.UI.Image[] actionRows = new UnityEngine.UI.Image[4];
    private readonly CanvasGroup[] actionOpacity = new CanvasGroup[4];
    private readonly TextMeshProUGUI[] actionLabels = new TextMeshProUGUI[4];
    private readonly Play2HandGuide handGuide;
    private bool acting;
    private long minimumBet, maximumBet, betAmount;
    private HoldemRoomPacket packet;
    private string lastStatus, noticeKey;
    private readonly HashSet<string> shownNotices = new HashSet<string>();
    private bool wasDealing;
    private int[] suspects = Array.Empty<int>();
    private int suspectIndex;

    public Play2TableHud(Transform parent, Play2TablePreview table, TMP_FontAsset font, Play2FrontEnd owner)
    {
        this.table = table;
        this.owner = owner;
        root = Play2UGui.Fill("Game HUD", parent);
        var top = Play2UGui.Rect("Header", root, Vector2.zero, Vector2.zero);
        top.anchorMin = new Vector2(0f, 1f);
        top.anchorMax = Vector2.one;
        top.offsetMin = new Vector2(15, -54);
        top.offsetMax = new Vector2(-11, -17);

        var leave = Play2HudArt.Button("Leave", top, "나가기", new Vector2(53, 0), new Vector2(106, 37),
            "PlayingBadge", Play2UGui.Cream, font, table.Leave, 18);
        var leaveRect = (RectTransform)leave.transform;
        leaveRect.anchorMin = leaveRect.anchorMax = new Vector2(0, .5f);
        leave.GetComponentInChildren<TextMeshProUGUI>().rectTransform.anchoredPosition = new Vector2(9, 0);
        Play2HudArt.Plate("Back", leaveRect, new Vector2(-30, 0), new Vector2(11, 15), "PlayingBack");

        var stage = Play2HudArt.Plate("Stage Plate", top, new Vector2(-14, -4), new Vector2(270, 38), "PlayingStage");
        Play2HudArt.Plate("Stage Diamond", stage, new Vector2(-86, 0), new Vector2(14, 17), "PlayingDiamond");
        street = Play2UGui.Text("Street", stage, "", new Vector2(18, 0), new Vector2(190, 34), 18,
            Play2UGui.Ink, font, style: FontStyles.Bold);
        street.textWrappingMode = TextWrappingModes.NoWrap;
        street.characterSpacing = -2;
        Play2HudArt.Fit(street, 13);

        var score = Play2HudArt.Plate("Pot and Ante", top, new Vector2(-88, 0), new Vector2(176, 37), "PlayingBadge");
        score.anchorMin = score.anchorMax = new Vector2(1, .5f);
        potAndAnte = Play2UGui.Text("Amounts", score, "", Vector2.zero, new Vector2(162, 31), 17,
            Play2UGui.Cream, font, style: FontStyles.Bold);
        potAndAnte.textWrappingMode = TextWrappingModes.NoWrap;
        Play2HudArt.Fit(potAndAnte, 11);

        var seats = Play2UGui.Rect("Seat Status", root, Vector2.zero, Vector2.zero);
        seats.anchorMin = new Vector2(.269f, 1f);
        seats.anchorMax = new Vector2(.724f, 1f);
        seats.offsetMin = new Vector2(0, -104);
        seats.offsetMax = new Vector2(0, -72);
        for (int i = 0; i < seatLabels.Length; i++)
        {
            var plate = Play2HudArt.Plate("Player " + (i + 1), seats, Vector2.zero, Vector2.zero, "PlayingBadge");
            plate.anchorMin = new Vector2(i / 4f, 0);
            plate.anchorMax = new Vector2((i + 1) / 4f, 1);
            plate.offsetMin = new Vector2(8, 0);
            plate.offsetMax = new Vector2(-8, 0);

            var label = Play2UGui.Text("Info", plate, "", Vector2.zero, Vector2.zero, 16,
                Play2UGui.Cream, font, style: FontStyles.Bold);
            Play2HudArt.Fit(label, 10);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(7, 2);
            label.rectTransform.offsetMax = new Vector2(-7, -2);
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.richText = false;
            seatLabels[i] = label;
        }
        var noticePanel = Play2HudArt.Plate("Action Notice", root, new Vector2(0, -130), new Vector2(334, 53), "CreamPanel");
        noticePanel.anchorMin = noticePanel.anchorMax = new Vector2(.5f, 1);
        noticePanel.anchoredPosition = new Vector2(0, -130);
        Play2HudArt.Plate("Diamond", noticePanel, new Vector2(-140, 0), new Vector2(14, 17), "Diamond");
        notice = Play2UGui.Text("Text", noticePanel, "", new Vector2(10, 0), new Vector2(269, 42), 22, PaperInk, font,
            style: FontStyles.Bold);
        notice.richText = false;
        Play2HudArt.Fit(notice, 11);
        notice.overflowMode = TextOverflowModes.Ellipsis;
        noticePanel.gameObject.SetActive(false);
        var sidebar = Play2HudArt.Plate("Actions and Hand", root, Vector2.zero, Vector2.zero, "PlayingPanel");
        controls = Play2UGui.Rect("Action Keys", sidebar, Vector2.zero, Vector2.zero);
        controls.anchorMin = new Vector2(0, 1);
        controls.anchorMax = Vector2.one;
        controls.offsetMin = new Vector2(0, -Play2UGui.ActionAreaHeight);
        controls.offsetMax = Vector2.zero;
        turnMark = Play2HudArt.Plate("Turn Diamond", controls, new Vector2(-106, 55), new Vector2(16, 19), "PlayingDiamond")
            .GetComponent<UnityEngine.UI.Image>();
        turn = Play2UGui.Text("Turn", controls, "", new Vector2(28, 55), new Vector2(231, 33), 24,
            Play2UGui.Cream, font, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        turn.textWrappingMode = TextWrappingModes.NoWrap;
        Play2HudArt.Fit(turn, 13);
        Play2UGui.Panel("Action Divider", controls, new Vector2(0, -80.5f), new Vector2(235, 1.25f), Play2UGui.Hex("D6C094"));

        string[] keys = { "F", "E", "R", "X" };
        for (int i = 0; i < keys.Length; i++)
        {
            float x = i % 2 == 0 ? -60.5f : 60.5f;
            float y = i < 2 ? 6 : -45;
            var row = Play2HudArt.Plate(keys[i] + " Action", controls, new Vector2(x, y), new Vector2(115, 44), "PlayingAction");
            actionRows[i] = row.GetComponent<UnityEngine.UI.Image>();
            actionOpacity[i] = row.gameObject.AddComponent<CanvasGroup>();
            actionLabels[i] = Play2UGui.Text("Action", row, "", new Vector2(-14, 0), new Vector2(66, 29), 18,
                Play2UGui.Cream, font, style: FontStyles.Bold);
            actionLabels[i].textWrappingMode = TextWrappingModes.NoWrap;
            Play2HudArt.Fit(actionLabels[i], 13);
            Play2HudArt.Plate(keys[i] + " Keycap", row, new Vector2(31, 0), new Vector2(23, 21), "PlayingKey" + keys[i]);
        }

        feedback = Play2UGui.Text("Feedback", root, "", new Vector2(0, 14), new Vector2(390, 32), 14,
            Play2UGui.Cream, font);
        Play2UGui.Anchor(feedback.rectTransform, new Vector2(.5f, 0));
        handGuide = new Play2HandGuide(sidebar, font);
        (sizing, betHand, target, betHeading, betAdjustments, allInOption) = BuildBetPicker(root, font);
        (accusation, accusationHand, suspectPlates, suspectNames) = BuildAccusationPicker(root, font);
        settlementCard = new SettlementCard(root, font, AdvanceHand);
        settings = new Play2InGameSettings(root, font);
        root.gameObject.SetActive(false);
    }

    public void Present(HoldemRoomPacket value, string status)
    {
        if (value?.hasGame != true)
        {
            packet = null;
            acting = false;
            noticeKey = null;
            notice.transform.parent.gameObject.SetActive(false);
            shownNotices.Clear();
            if (settings.IsOpen) settings.Close();
            settlementCard.Hide();
            CloseActionPanels();
            root.gameObject.SetActive(false);
            return;
        }
        bool dealing = table.IsDealing;
        if (ReferenceEquals(packet, value) && lastStatus == status && wasDealing == dealing) return;
        var previousPacket = packet;
        var previous = previousPacket?.game;
        packet = value; lastStatus = status;
        wasDealing = dealing;
        root.gameObject.SetActive(true);
        var game = packet.game;
        if (previous?.handId != game.handId)
        {
            shownNotices.Clear();
            noticeKey = null;
            notice.transform.parent.gameObject.SetActive(false);
        }
        street.text = game.hasResult ? "결과" : game.board.Length == 0 ? "카드 공개 전" : $"공용 카드 {game.board.Length}/5장 공개";
        potAndAnte.text = $"팟 {game.pot}  ·  입장료 {game.postedAnte}";
        for (int i = 0; i < seatLabels.Length; i++)
        {
            var seat = game.seats.FirstOrDefault(s => s.seat == i + 1);
            if (seat == null) { seatLabels[i].text = "빈자리"; seatLabels[i].color = Play2UGui.Cream; continue; }
            string label = seat.status == (int)HoldemSeatStatus.Folded ? " · 폴드"
                : seat.status == (int)HoldemSeatStatus.AllIn ? " · 올인" : seat.status == (int)HoldemSeatStatus.Busted ? " · 탈락" : "";
            string name = seat.seat == packet.viewerSeat ? "나" : $"{seat.seat}번";
            seatLabels[i].text = $"{name}   {seat.stack}칩{label}";
            seatLabels[i].color = Play2UGui.Cream;
        }
        acting = game.hasLegal && game.currentSeat == packet.viewerSeat && !packet.paused
            && !game.hasResult && !game.dealPending && !game.revealPending && !dealing;
        var legal = game.legal;
        bool settlement = game.settlementState == (int)HoldemSettlementState.AwaitingOddChipPriority;
        bool showActions = !game.hasResult && !settlement;
        controls.gameObject.SetActive(showActions);
        handGuide.SetActionsVisible(showActions);
        SetAction(0, "폴드", showActions, acting && legal.fold);
        SetAction(1, legal?.call == true ? $"콜 {legal.callAmount}" : "체크", showActions, acting && (legal.check || legal.call));
        SetAction(2, legal?.bet == true ? "베팅" : legal?.raise != true && legal?.hasAllInTarget == true ? "올인" : "레이즈",
            showActions, acting && (legal.bet || legal.raise || legal.hasAllInTarget));
        SetAction(3, "고발", showActions, acting && game.canAccuseOnTurn);
        if (!acting || previous?.handId != game.handId || previous?.version != game.version)
            CloseActionPanels();
        if (acting && game.canAccuseOnTurn && (previous?.version != game.version || suspects.Length == 0))
        {
            suspects = game.seats.Where(s => s.dealtIn && s.seat != packet.viewerSeat && s.status != (int)HoldemSeatStatus.Folded).Select(s => s.seat).ToArray();
            suspectIndex = 0;
            UpdateSuspect();
        }
        handGuide.Present(packet, dealing);
        betHand.text = accusationHand.text = $"내 족보 · {handGuide.CurrentRank}";
        settlementCard.Present(packet, settlement, table.IsHost, DisplayName);
        feedback.text = status ?? "";
        turn.text = packet.paused ? "연결 대기 중" : settlement ? "정산 대기" : game.hasResult ? (game.isOver ? "경기 종료" : "이번 판 종료")
            : game.dealPending ? "딜러가 다음 카드를 준비 중입니다." : game.revealPending || dealing ? "카드 공개 중"
            : acting ? "내 차례" : $"{Name(game.currentSeat)} 차례";
        turn.color = Play2UGui.Cream;
        turnMark.color = acting ? Color.white : new Color(1, 1, 1, .52f);
        if (packet.hasTurnAccusation)
        {
            var result = packet.turnAccusation;
            ShowNotice($"a:{result.handId}:{result.street}:{result.accuser}:{result.target}",
                $"{Name(result.accuser)} → {Name(result.target)}  ·  {(result.caught ? "고발 적중" : "고발 실패")}");
        }
        if (!game.hasResult && packet.dealerSelections != null)
        {
            var selection = Enumerable.Range(0, game.board.Length)
                .Select(index => HoldemDealerSelectionPacketReader.FindOwnAppliedCard(packet, index))
                .LastOrDefault(item => item != null && item.street == game.street);
            if (selection != null)
            {
                if (!dealing)
                {
                    string card = Play2HandGuide.CardText(Poker.Foundation.Card.FromId(selection.card));
                    string message = $"요청 채택 · {card} · 왼쪽 {selection.boardIndex + 1}번째";
                    if (!string.IsNullOrWhiteSpace(selection.interpretation))
                        message += $"\nAI 해석: {Brief(selection.interpretation, 120)}";
                    ShowNotice($"dealer:{game.handId}:{selection.dealWindowId}", message);
                }
            }
            else if (previous != null && previous.handId == game.handId
                && previous.board.Length != game.board.Length && game.board.Length > 0)
                ShowNotice($"street:{game.handId}:{game.board.Length}", $"공용 카드 {game.board.Length}장 공개");
        }
        else if (!game.hasResult && previous != null && previous.handId == game.handId
            && previous.board.Length != game.board.Length && game.board.Length > 0)
            ShowNotice($"street:{game.handId}:{game.board.Length}", $"공용 카드 {game.board.Length}장 공개");

        if (!game.hasResult && !settlement) ShowAcceptedBet(previousPacket, value);
    }

    private void OpenSettings()
    {
        ClearSelection();
        settings.Open();
    }

    private void ShowAcceptedBet(HoldemRoomPacket previous, HoldemRoomPacket current)
    {
        if (previous?.game.handId != current.game.handId || current.history?.entries == null) return;

        long previousCount = previous.history?.entries == null ? 0 : previous.history.omittedCount + previous.history.entries.Length;
        var history = current.history;
        for (int i = history.entries.Length - 1; i >= 0 && history.omittedCount + i >= previousCount; i--)
        {
            var entry = history.entries[i];
            if (!entry.hasAction || entry.seat != current.viewerSeat || entry.amount <= 0) continue;
            string action = entry.action == (int)BettingActionKind.BetTo ? "베팅"
                : entry.action == (int)BettingActionKind.RaiseTo ? "레이즈" : null;
            if (action == null) continue;
            if (entry.allIn) action = "올인";
            ShowNotice($"bet:{current.game.handId}:{history.omittedCount + i}", $"나 · {entry.amount}칩 {action}");
            break;
        }
    }

    public void HandleKeys(bool chatFocused)
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (packet == null || keyboard == null || !Application.isFocused) return;
        if (settings.IsOpen)
        {
            if (keyboard.escapeKey.wasPressedThisFrame) settings.Close();
            return;
        }
        if (chatFocused) return;

        bool selecting = sizing.gameObject.activeSelf || accusation.gameObject.activeSelf;
        if (!selecting && keyboard.escapeKey.wasPressedThisFrame)
        {
            OpenSettings();
            return;
        }
        if (keyboard.hKey.wasPressedThisFrame && !selecting) handGuide.Toggle();
        if (!acting) return;

        if (sizing.gameObject.activeSelf)
        {
            HandleSizing(keyboard);
            return;
        }
        if (accusation.gameObject.activeSelf)
        {
            HandleAccusation(keyboard);
            return;
        }

        var legal = packet.game.legal;
        if (keyboard.fKey.wasPressedThisFrame && legal.fold) Act(BettingActionKind.Fold);
        else if (keyboard.eKey.wasPressedThisFrame && (legal.check || legal.call))
            Act(legal.check ? BettingActionKind.Check : BettingActionKind.Call);
        else if (keyboard.rKey.wasPressedThisFrame && (legal.bet || legal.raise || legal.hasAllInTarget))
            OpenSizing();
        else if (keyboard.xKey.wasPressedThisFrame && packet.game.canAccuseOnTurn && suspects.Length > 0)
        {
            ClearSelection();
            accusation.gameObject.SetActive(true);
            handGuide.SetActionPickerOpen(true);
        }
    }

    private void SetAction(int index, string label, bool visible, bool enabled)
    {
        actionRows[index].gameObject.SetActive(visible);
        actionOpacity[index].alpha = enabled ? 1f : .57f;
        actionLabels[index].text = label;
    }

    private void OpenSizing()
    {
        ClearSelection();
        var legal = packet.game.legal;
        bool adjustable = legal.bet || legal.raise;
        minimumBet = adjustable ? legal.minimumTarget : legal.allInTarget;
        maximumBet = adjustable ? Math.Max(minimumBet, legal.maximumTarget) : minimumBet;
        betAmount = minimumBet;
        betAdjustments.gameObject.SetActive(adjustable);
        allInOption.gameObject.SetActive(legal.hasAllInTarget);
        UpdateBetAmount();
        sizing.gameObject.SetActive(true);
        handGuide.SetActionPickerOpen(true);
    }

    private void HandleSizing(UnityEngine.InputSystem.Keyboard keyboard)
    {
        if (keyboard.escapeKey.wasPressedThisFrame || keyboard.rKey.wasPressedThisFrame)
        {
            CloseActionPanels();
            return;
        }

        var legal = packet.game.legal;
        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            RaiseTo(betAmount);
            return;
        }
        if (keyboard.aKey.wasPressedThisFrame && legal.hasAllInTarget)
            betAmount = legal.allInTarget;
        else if (keyboard.homeKey.wasPressedThisFrame)
            betAmount = minimumBet;
        else if (keyboard.pKey.wasPressedThisFrame && (legal.bet || legal.raise))
        {
            long committed = packet.game.seats.Max(s => s.streetContribution);
            long available = Math.Max(0, maximumBet - committed);
            betAmount = Math.Max(minimumBet, Math.Min(maximumBet, committed + Math.Min(available, packet.game.pot / 2)));
        }
        else if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
        {
            betAmount = Math.Max(minimumBet, Math.Min(maximumBet, betAmount));
            long step = keyboard.shiftKey.isPressed ? 10 : 1;
            betAmount += keyboard.leftArrowKey.wasPressedThisFrame
                ? -Math.Min(step, betAmount - minimumBet) : Math.Min(step, maximumBet - betAmount);
        }
        else return;

        UpdateBetAmount();
    }

    private void HandleAccusation(UnityEngine.InputSystem.Keyboard keyboard)
    {
        if (keyboard.escapeKey.wasPressedThisFrame || keyboard.xKey.wasPressedThisFrame)
        {
            CloseActionPanels();
            return;
        }
        if (!packet.game.canAccuseOnTurn || suspects.Length == 0)
        {
            CloseActionPanels();
            return;
        }

        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            var command = Command("turn-accusation");
            command.accusationTarget = suspects[suspectIndex];
            if (table.Submit(command)) CloseActionPanels();
        }
        else if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
        {
            int step = keyboard.leftArrowKey.wasPressedThisFrame ? -1 : 1;
            suspectIndex = (suspectIndex + suspects.Length + step) % suspects.Length;
            UpdateSuspect();
        }
    }

    private void UpdateBetAmount()
    {
        var legal = packet.game.legal;
        betHeading.text = legal.hasAllInTarget && betAmount == legal.allInTarget ? "올인 금액"
            : legal.raise ? "레이즈 금액" : "베팅 금액";
        target.text = $"{betAmount}칩";
    }

    private void UpdateSuspect()
    {
        for (int i = 0; i < suspectPlates.Length; i++)
        {
            var plate = suspectPlates[i];
            plate.gameObject.SetActive(i < suspects.Length);
            if (i >= suspects.Length) continue;
            bool selected = i == suspectIndex;
            plate.rectTransform.anchoredPosition = new Vector2((i - (suspects.Length - 1) * .5f) * 106, 22);
            Play2HudArt.Apply(plate, selected ? "SelectedTarget" : "HeaderBadge");
            suspectNames[i].color = selected ? PaperRed : Play2UGui.Cream;
            suspectNames[i].text = Name(suspects[i]);
        }
    }

    private void AdvanceHand()
    {
        if (packet?.game.settlementState == (int)HoldemSettlementState.AwaitingOddChipPriority)
        {
            var command = Command("settle");
            command.oddChipRule = (int)HoldemOddChipRule.ClockwiseFromButton;
            table.Submit(command);
        }
        else table.StartHand();
    }

    private void CloseActionPanels()
    {
        sizing.gameObject.SetActive(false);
        accusation.gameObject.SetActive(false);
        handGuide.SetActionPickerOpen(false);
    }

    private static void ClearSelection() => UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);

    private static string Brief(string value, int limit) => string.IsNullOrWhiteSpace(value) ? "해석 정보 없음"
        : value.Length <= limit ? value : value.Substring(0, limit) + "…";
    private void ShowNotice(string key, string message)
    {
        if (!shownNotices.Add(key)) return;
        noticeKey = key; notice.text = message; notice.transform.parent.gameObject.SetActive(true);
        owner.StartCoroutine(HideNotice(key));
    }
    private IEnumerator HideNotice(string key)
    {
        var panel = notice.transform.parent;
        float elapsed = 0f;
        while (elapsed < .25f && noticeKey == key)
        {
            elapsed += Time.unscaledDeltaTime;
            panel.localScale = Vector3.one * Mathf.Lerp(.85f, 1f, Mathf.Clamp01(elapsed / .25f));
            yield return null;
        }
        yield return new WaitForSecondsRealtime(key.StartsWith("dealer:") ? 6.75f : 2.35f);
        if (noticeKey == key) notice.transform.parent.gameObject.SetActive(false);
    }
    private void RaiseTo(long amount) => Act(packet.game.legal.bet || (!packet.game.legal.raise
        && packet.game.seats.All(s => s.streetContribution == 0)) ? BettingActionKind.BetTo : BettingActionKind.RaiseTo, amount);
    private void Act(BettingActionKind kind, long amount = 0)
    {
        var request = Command("act"); request.action = (int)kind; request.target = amount;
        if (table.Submit(request)) CloseActionPanels();
    }
    private HoldemWireRequest Command(string type) => new HoldemWireRequest
    { type = type, handId = packet.game.handId, version = packet.game.version, street = packet.game.street };
    private string DisplayName(int seat) => seat == packet.viewerSeat ? "나" : Name(seat);
    private string Name(int seat) => packet.members.FirstOrDefault(member => member.seat == seat)?.name ?? $"{seat}번";
}
