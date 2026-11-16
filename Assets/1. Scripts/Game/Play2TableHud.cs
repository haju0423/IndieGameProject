using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Poker.Application;
using Poker.Foundation;
using Poker.Transport;
using TMPro;
using UnityEngine;

internal sealed class Play2TableHud
{
    private readonly Play2TablePreview table;
    private readonly Play2FrontEnd owner;
    private readonly RectTransform root, sizing, accusation;
    private readonly TextMeshProUGUI street, pot, turn, notice, target, feedback, suspectLabel;
    private readonly TextMeshProUGUI[] seatLabels = new TextMeshProUGUI[4];
    private readonly UnityEngine.UI.Button fold, call, raise, accuse, next, allIn, suspect;
    private readonly UnityEngine.UI.Slider slider;
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
        var top = Play2UGui.Panel("Header", root, new Vector2(0, 326), new Vector2(1280, 68), new Color(.04f,.07f,.05f,.82f));
        Play2UGui.Button("Leave", top, "← 나가기", new Vector2(-548, 0), new Vector2(140, 44), Play2UGui.Dark,
            Play2UGui.Cream, font, table.Leave, 16);
        street = Play2UGui.Text("Street", top, "", new Vector2(0, 0), new Vector2(400, 48), 20, Play2UGui.Cream, font, style: FontStyles.Bold);
        pot = Play2UGui.Text("Pot and Ante", top, "", new Vector2(475, 0), new Vector2(290, 48), 19, Play2UGui.Cream, font, style: FontStyles.Bold);
        for (int i = 0; i < 4; i++)
        {
            var plate = Play2UGui.Panel("Player " + (i + 1), root, new Vector2((i - 1.5f) * 270, 257),
                new Vector2(250, 47), new Color(.04f,.07f,.05f,.8f));
            seatLabels[i] = Play2UGui.Text("Info", plate, "", Vector2.zero, new Vector2(236, 40), 15, Play2UGui.Cream, font);
        }
        var noticePanel = Play2UGui.Panel("Notice", root, new Vector2(0, 80), new Vector2(520, 150), new Color(.04f,.07f,.05f,.94f));
        notice = Play2UGui.Text("Text", noticePanel, "", Vector2.zero, new Vector2(480, 120), 29, Play2UGui.Cream, font, style: FontStyles.Bold);
        noticePanel.gameObject.SetActive(false);
        var controls = Play2UGui.Panel("Actions", root, new Vector2(442, -275), new Vector2(350, 145), new Color(.04f,.07f,.05f,.9f));
        turn = Play2UGui.Text("Turn", controls, "", new Vector2(0, 48), new Vector2(330, 32), 17, Play2UGui.Cream, font, style: FontStyles.Bold);
        fold = Play2UGui.Button("Fold", controls, "폴드", new Vector2(-114, 3), new Vector2(100, 48), Play2UGui.Hex("66594A"), Play2UGui.Cream, font, () => Act(BettingActionKind.Fold), 17);
        call = Play2UGui.Button("Call", controls, "콜", new Vector2(0, 3), new Vector2(100, 48), Play2UGui.Hex("2F8A57"), Play2UGui.Cream, font,
            () => Act(packet.game.legal.check ? BettingActionKind.Check : BettingActionKind.Call), 17);
        raise = Play2UGui.Button("Raise", controls, "레이즈", new Vector2(114, 3), new Vector2(100, 48), Play2UGui.Orange, Play2UGui.Ink, font,
            () => { accusation.gameObject.SetActive(false); sizing.gameObject.SetActive(!sizing.gameObject.activeSelf); }, 17);
        accuse = Play2UGui.Button("Accuse", controls, "고발", new Vector2(-82, -47), new Vector2(150, 40), Play2UGui.Hex("733125"), Play2UGui.Cream, font,
            () => { sizing.gameObject.SetActive(false); accusation.gameObject.SetActive(!accusation.gameObject.activeSelf); }, 17);
        next = Play2UGui.Button("Next", controls, "다음 판", new Vector2(82, -47), new Vector2(150, 40), Play2UGui.Orange, Play2UGui.Ink, font,
            () => { if (packet.game.settlementState == (int)HoldemSettlementState.AwaitingOddChipPriority)
                { var cmd = Command("settle"); cmd.oddChipRule = (int)HoldemOddChipRule.ClockwiseFromButton; table.Submit(cmd); }
                else table.StartHand(); }, 17);
        feedback = Play2UGui.Text("Feedback", root, "", new Vector2(445, -184), new Vector2(350, 30), 14, Play2UGui.Cream, font);

        sizing = Play2UGui.Panel("Bet Sizing", root, new Vector2(442, -94), new Vector2(350, 190), new Color(.04f,.07f,.05f,.95f), true);
        target = Play2UGui.Text("Amount", sizing, "", new Vector2(0, 65), new Vector2(310, 30), 18, Play2UGui.Cream, font, style: FontStyles.Bold);
        var track = Play2UGui.Panel("Slider", sizing, new Vector2(0, 20), new Vector2(290, 14), Play2UGui.Hex("776D5C"), true);
        slider = track.gameObject.AddComponent<UnityEngine.UI.Slider>();
        slider.wholeNumbers = true;
        var fillArea = Play2UGui.Fill("Fill Area", track);
        fillArea.offsetMin = new Vector2(0, 0); fillArea.offsetMax = new Vector2(0, 0);
        var fill = Play2UGui.Fill("Fill", fillArea); Play2UGui.Image(fill, Play2UGui.Orange);
        slider.fillRect = fill;
        var handleArea = Play2UGui.Fill("Handle Area", track);
        handleArea.offsetMin = new Vector2(10, 0); handleArea.offsetMax = new Vector2(-10, 0);
        var handle = Play2UGui.Panel("Handle", handleArea, Vector2.zero, new Vector2(22, 28), Play2UGui.Cream, true);
        slider.handleRect = handle; slider.targetGraphic = handle.GetComponent<UnityEngine.UI.Image>();
        slider.onValueChanged.AddListener(value => target.text = $"이번 베팅 총 {(int)value} 칩");
        Play2UGui.Button("Minimum", sizing, "최소", new Vector2(-108, -25), new Vector2(92, 38), Play2UGui.Hex("66594A"), Play2UGui.Cream, font, () => slider.value = slider.minValue, 15);
        Play2UGui.Button("Half", sizing, "하프", new Vector2(0, -25), new Vector2(92, 38), Play2UGui.Hex("66594A"), Play2UGui.Cream, font,
            () => { long current = packet.game.seats.Max(s => s.streetContribution); slider.value = Mathf.Clamp((float)(current + packet.game.pot / 2), slider.minValue, slider.maxValue); }, 15);
        allIn = Play2UGui.Button("All In", sizing, "올인", new Vector2(108, -25), new Vector2(92, 38), Play2UGui.Red, Play2UGui.Cream, font,
            () => RaiseTo(packet.game.legal.allInTarget), 15);
        Play2UGui.Button("Commit", sizing, "베팅 확정", new Vector2(0, -70), new Vector2(310, 40), Play2UGui.Orange, Play2UGui.Ink, font, () => RaiseTo((long)slider.value), 17);
        sizing.gameObject.SetActive(false);

        accusation = Play2UGui.Panel("Accusation", root, new Vector2(442, -85), new Vector2(350, 172), new Color(.04f,.07f,.05f,.95f), true);
        Play2UGui.Text("Heading", accusation, "누구를 고발할까요?", new Vector2(0, 55), new Vector2(320, 30), 18, Play2UGui.Cream, font, style: FontStyles.Bold);
        suspect = Play2UGui.Button("Suspect", accusation, "", new Vector2(0, 12), new Vector2(310, 44), Play2UGui.Cream, Play2UGui.Ink, font,
            () => { if (suspects.Length == 0) return; suspectIndex = (suspectIndex + 1) % suspects.Length; UpdateSuspect(); }, 17);
        suspectLabel = suspect.GetComponentInChildren<TextMeshProUGUI>();
        Play2UGui.Button("Confirm Accusation", accusation, "고발하기", new Vector2(0, -43), new Vector2(310, 48), Play2UGui.Red, Play2UGui.Cream, font,
            () => { if (suspects.Length == 0) return; var command = Command("turn-accusation"); command.accusationTarget = suspects[suspectIndex];
                if (table.Submit(command)) accusation.gameObject.SetActive(false); }, 18);
        accusation.gameObject.SetActive(false);
        root.gameObject.SetActive(false);
    }

    public void Present(HoldemRoomPacket value, string status)
    {
        if (value?.hasGame != true)
        {
            packet = null; noticeKey = null; shownNotices.Clear(); root.gameObject.SetActive(false); return;
        }
        bool dealing = table.IsDealing;
        if (ReferenceEquals(packet, value) && lastStatus == status && wasDealing == dealing) return;
        var previous = packet?.game;
        packet = value; lastStatus = status;
        bool dealFinished = wasDealing && !dealing; wasDealing = dealing;
        root.gameObject.SetActive(true);
        var game = packet.game;
        if (previous?.handId != game.handId) shownNotices.Clear();
        street.text = game.hasResult ? "결과" : game.board.Length == 0 ? "카드 공개 전" : $"공용 카드 {game.board.Length}/5장 공개";
        pot.text = $"팟 {game.pot}  ·  입장료 {game.postedAnte}";
        for (int i = 0; i < seatLabels.Length; i++)
        {
            var seat = game.seats.FirstOrDefault(s => s.seat == i + 1);
            if (seat == null) { seatLabels[i].text = "빈자리"; continue; }
            string label = seat.status == (int)HoldemSeatStatus.Folded ? " · 폴드"
                : seat.status == (int)HoldemSeatStatus.AllIn ? " · 올인" : seat.status == (int)HoldemSeatStatus.Busted ? " · 탈락" : "";
            seatLabels[i].text = $"{Name(seat.seat)}   {seat.stack}칩{label}";
            seatLabels[i].color = seat.currentActor ? Play2UGui.Orange : Play2UGui.Cream;
        }
        bool acting = game.hasLegal && game.currentSeat == packet.viewerSeat && !packet.paused
            && !game.hasResult && !game.dealPending && !game.revealPending && !dealing;
        var legal = game.legal;
        fold.interactable = acting && legal.fold;
        call.interactable = acting && (legal.check || legal.call);
        call.GetComponentInChildren<TextMeshProUGUI>().text = acting && legal.call ? $"콜 {legal.callAmount}" : "체크";
        raise.interactable = acting && (legal.bet || legal.raise || legal.hasAllInTarget);
        raise.GetComponentInChildren<TextMeshProUGUI>().text = acting && legal.bet ? "베팅" : "레이즈";
        if (acting && (legal.bet || legal.raise || legal.hasAllInTarget)
            && (previous == null || previous.version != game.version || dealFinished || slider.maxValue != legal.maximumTarget))
        {
            float min = legal.minimumTarget > 0 ? legal.minimumTarget : legal.allInTarget;
            slider.minValue = min; slider.maxValue = Mathf.Max(min, legal.maximumTarget); slider.SetValueWithoutNotify(min);
            target.text = $"이번 베팅 총 {(long)min} 칩";
        }
        allIn.interactable = acting && legal.hasAllInTarget;
        accuse.interactable = acting && game.canAccuseOnTurn;
        if (!acting || previous?.version != game.version) { sizing.gameObject.SetActive(false); accusation.gameObject.SetActive(false); }
        if (acting && game.canAccuseOnTurn && (previous?.version != game.version || suspects.Length == 0))
        {
            suspects = game.seats.Where(s => s.dealtIn && s.seat != packet.viewerSeat && s.status != (int)HoldemSeatStatus.Folded).Select(s => s.seat).ToArray();
            suspectIndex = 0; UpdateSuspect();
        }
        bool settlement = game.settlementState == (int)HoldemSettlementState.AwaitingOddChipPriority;
        next.gameObject.SetActive(table.IsHost && !packet.paused && (settlement || (game.hasResult && game.canContinue)));
        next.GetComponentInChildren<TextMeshProUGUI>().text = settlement ? "정산하기" : "다음 판";
        feedback.text = status ?? "";
        turn.text = packet.paused ? "연결 대기 중" : settlement ? "정산 대기" : game.hasResult ? (game.isOver ? "경기 종료" : "이번 판 종료")
            : game.dealPending ? "딜러가 다음 카드를 준비 중입니다." : game.revealPending || dealing ? "카드 공개 중"
            : acting ? "내 차례" : $"{Name(game.currentSeat)} 차례";
        if (packet.hasTurnAccusation)
        {
            var result = packet.turnAccusation;
            ShowNotice($"a:{result.handId}:{result.street}:{result.accuser}:{result.target}",
                $"{Name(result.accuser)} → {Name(result.target)}  ·  {(result.caught ? "고발 적중" : "고발 실패")}");
        }
        if (game.hasResult && (game.result.kind != (int)HoldemResultKind.Accusation || !packet.hasTurnAccusation))
        {
            string winners = string.Join(", ", game.seats.Where(s => s.awarded > 0).Select(s => $"{Name(s.seat)} +{s.awarded}"));
            ShowNotice("result:" + game.handId, winners.Length > 0 ? "정산  ·  " + winners : "이번 판 종료");
        }
        else if (!game.hasResult && packet.hasOwnCardChange)
            ShowNotice($"dealer:{game.handId}:{packet.ownCardChange.street}", "딜러가 내 요청을 반영했습니다.");
        else if (!game.hasResult && previous != null && previous.handId == game.handId
            && previous.board.Length != game.board.Length && game.board.Length > 0)
            ShowNotice($"street:{game.handId}:{game.board.Length}", $"공용 카드 {game.board.Length}장 공개");
    }

    private void UpdateSuspect() => suspectLabel.text = suspects.Length == 0 ? "선택할 대상이 없습니다" : $"{Name(suspects[suspectIndex])}  · 변경";
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
        yield return new WaitForSecondsRealtime(2.35f);
        if (noticeKey == key) notice.transform.parent.gameObject.SetActive(false);
    }
    private void RaiseTo(long amount) => Act(packet.game.legal.bet || (!packet.game.legal.raise
        && packet.game.seats.All(s => s.streetContribution == 0)) ? BettingActionKind.BetTo : BettingActionKind.RaiseTo, amount);
    private void Act(BettingActionKind kind, long amount = 0)
    {
        var request = Command("act"); request.action = (int)kind; request.target = amount;
        if (table.Submit(request)) sizing.gameObject.SetActive(false);
    }
    private HoldemWireRequest Command(string type) => new HoldemWireRequest
    { type = type, handId = packet.game.handId, version = packet.game.version, street = packet.game.street };
    private string Name(int seat) => packet.members.FirstOrDefault(member => member.seat == seat)?.name ?? $"{seat}번";
}
