using System.Collections.Generic;
using TMPro;
using Poker.Application;
using Poker.Foundation;
using Poker.Transport;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public sealed class Play2ChatCanvas : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private UnityEngine.UI.ScrollRect scroll;
    [SerializeField] private TMPro.TMP_Text messages;
    [SerializeField] private TMPro.TMP_InputField input;
    [SerializeField] private UnityEngine.UI.Toggle dealer;
    [SerializeField] private UnityEngine.UI.Button send;

    private const float PanelWidth = 251f;
    private const float CompactHeight = 164f;
    private static readonly Color ChatText = Play2UGui.Hex("F0E6CB");

    private Play2TablePreview table;
    private HoldemRoomPacket packet;
    private RectTransform panelRect, scrollRect, scrollbarRail;
    private UnityEngine.UI.Image inputBackground;
    private readonly List<(RectTransform root, TMP_Text speaker, TMP_Text text)> rows = new();
    private UnityEngine.UI.Outline inputFocus;
    private GameObject closeHint;
    private long firstMessage = -1, lastMessage = -1;
    private bool expanded;
    internal TMPro.TMP_FontAsset Font => messages.font;
    internal bool IsTyping => panel.activeInHierarchy && (expanded || input.isFocused);

    internal void Initialize(Play2TablePreview owner)
    {
        table = owner;
        BuildLayout();
        send.onClick.AddListener(Send);
        input.onSelect.AddListener(_ => SetExpanded(true));
        input.onSubmit.AddListener(_ => Send());
        input.restoreOriginalTextOnEscape = false;
        panel.SetActive(false);
    }

    private void BuildLayout()
    {
        panelRect = (RectTransform)panel.transform;
        panelRect.pivot = Vector2.zero;
        Play2HudArt.Skin(panel.GetComponent<UnityEngine.UI.Graphic>(), "PlayingChat");

        scrollRect = (RectTransform)scroll.transform;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        messages.enabled = false;
        var chatLayout = scroll.content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        chatLayout.childAlignment = TextAnchor.UpperLeft;
        chatLayout.childControlWidth = chatLayout.childControlHeight = true;
        chatLayout.childForceExpandWidth = true;
        chatLayout.childForceExpandHeight = false;
        chatLayout.spacing = 4;

        var label = Play2UGui.Text("Chat Heading", panelRect, "채팅", new Vector2(21, -4), new Vector2(61, 30),
            18, ChatText, Font, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = Vector2.up;
        label.rectTransform.anchoredPosition = new Vector2(21, -4);
        var hint = Play2UGui.Text("Chat Close Hint", panelRect, "Esc 접기", new Vector2(-53, -23), new Vector2(79, 20),
            13, Play2UGui.Gold, Font, TextAlignmentOptions.MidlineRight);
        hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = Vector2.one;
        closeHint = hint.gameObject;
        var divider = Play2UGui.Panel("Chat Divider", panelRect, new Vector2(0, -35), new Vector2(227, 1.2f),
            Play2UGui.Hex("D6C094"));
        divider.anchorMin = divider.anchorMax = new Vector2(.5f, 1f);

        scrollbarRail = Play2UGui.Panel("Scrollbar Rail", scrollRect, Vector2.zero, Vector2.zero, Play2UGui.PineInset);
        scrollbarRail.anchorMin = Vector2.right;
        scrollbarRail.anchorMax = Vector2.one;
        scrollbarRail.offsetMin = new Vector2(6, 0);
        scrollbarRail.offsetMax = new Vector2(12, 0);
        var track = Play2UGui.Fill("Scrollbar", scrollbarRail);
        Play2UGui.Image(track, Color.clear, true);
        var thumb = Play2UGui.Fill("Thumb", track);
        var thumbImage = Play2UGui.Image(thumb, Play2UGui.Gold);
        var bar = track.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
        bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        bar.handleRect = thumb;
        bar.targetGraphic = thumbImage;
        bar.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;

        var inputRect = (RectTransform)input.transform;
        inputRect.offsetMin = new Vector2(10, 12);
        inputRect.offsetMax = new Vector2(-65, 46);
        if (input.targetGraphic != null)
            input.targetGraphic = inputBackground = Play2HudArt.Skin(input.targetGraphic, "PlayingInput");
        input.textComponent.color = ChatText;
        input.textComponent.fontSize = 14;
        if (input.placeholder is TMP_Text placeholder)
        {
            placeholder.color = Play2UGui.Hex("ABAFA1");
            placeholder.fontStyle = FontStyles.Normal;
            placeholder.fontSize = 14;
        }
        inputFocus = inputRect.gameObject.AddComponent<UnityEngine.UI.Outline>();
        inputFocus.effectColor = Play2UGui.Gold;
        inputFocus.effectDistance = new Vector2(1, 1);
        var dealerRect = (RectTransform)dealer.transform;
        dealerRect.offsetMin = new Vector2(14, 54);
        dealerRect.offsetMax = new Vector2(-12, 81);
        if (dealer.targetGraphic != null) dealer.targetGraphic = Play2HudArt.Skin(dealer.targetGraphic, "PlayingCheck");
        if (dealer.graphic != null) dealer.graphic = Play2HudArt.Skin(dealer.graphic, "PlayingTick");
        var dealerLabel = dealer.GetComponentInChildren<TMP_Text>();
        dealerLabel.text = "딜러에게 요청";
        dealerLabel.color = ChatText;
        dealerLabel.fontSize = 15;
        var sendRect = (RectTransform)send.transform;
        sendRect.anchorMin = sendRect.anchorMax = sendRect.pivot = Vector2.right;
        sendRect.anchoredPosition = new Vector2(-8, 12);
        sendRect.sizeDelta = new Vector2(52, 34);
        send.targetGraphic = Play2HudArt.Skin(send.targetGraphic, "PlayingSend");
        var sendLabel = send.GetComponentInChildren<TMP_Text>();
        sendLabel.color = ChatText;
        sendLabel.fontSize = 15;
        ApplyLayout();
    }

    private void LateUpdate()
    {
        if (!IsTyping) return;
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        bool clickedOutside = mouse != null && mouse.leftButton.wasPressedThisFrame
            && !RectTransformUtility.RectangleContainsScreenPoint(panelRect, mouse.position.ReadValue());
        if (keyboard?.escapeKey.wasPressedThisFrame == true || clickedOutside)
        {
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null
                && events.currentSelectedGameObject.transform.IsChildOf(panelRect))
                events.SetSelectedGameObject(null);
            input.DeactivateInputField();
            SetExpanded(false);
        }
    }

    private void SetExpanded(bool value)
    {
        if (expanded == value) return;
        bool keepScroll = value && scroll.verticalNormalizedPosition > .01f;
        float offset = scroll.content.anchoredPosition.y;
        expanded = value;
        ApplyLayout();
        RestoreScroll(keepScroll, offset);
    }

    private void ApplyLayout()
    {
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = expanded ? Vector2.up : Vector2.zero;
        const float left = 11f;
        const float bottom = 15f;
        panelRect.offsetMin = new Vector2(left, bottom);
        panelRect.offsetMax = new Vector2(left + PanelWidth, expanded ? -Play2UGui.SidePanelTop : bottom + CompactHeight);
        scrollRect.offsetMin = new Vector2(12, 84);
        scrollRect.offsetMax = new Vector2(expanded ? -25 : -11, -47);
        closeHint.SetActive(expanded);
        inputFocus.enabled = expanded;
        scrollbarRail.gameObject.SetActive(expanded);
        if (inputBackground != null)
            inputBackground.color = expanded ? Play2UGui.Hex("E6ECDF") : Color.white;
    }

    private void RestoreScroll(bool keepOffset, float offset)
    {
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        scroll.StopMovement();
        float maxOffset = scroll.content.rect.height - scroll.viewport.rect.height;
        scroll.verticalNormalizedPosition = keepOffset && maxOffset > 0 ? 1f - Mathf.Clamp01(offset / maxOffset) : 0f;
    }

    internal void Present(HoldemRoomPacket value)
    {
        if (ReferenceEquals(packet, value)) return;
        packet = value;
        bool visible = packet?.hasGame == true && packet.hasChat;
        panel.SetActive(visible);
        if (!visible)
        {
            firstMessage = lastMessage = -1;
            foreach (var row in rows) row.root.gameObject.SetActive(false);
            input.SetTextWithoutNotify("");
            dealer.SetIsOnWithoutNotify(false);
            SetExpanded(false);
            return;
        }

        bool canChat = !packet.paused;
        bool canRequest = canChat && packet.hasOwnUtterances && packet.ownUtterances.canSubmit
            && packet.ownUtterances.remaining > 0 && packet.game.street < (int)HoldemStreet.River;
        input.interactable = send.interactable = canChat;
        dealer.interactable = canRequest;
        if (!canRequest) dealer.SetIsOnWithoutNotify(false);

        var entries = packet.chat.entries;
        long oldest = entries.Length == 0 ? -1 : entries[0].sequence;
        long newest = entries.Length == 0 ? -1 : entries[entries.Length - 1].sequence;
        if (oldest == firstMessage && newest == lastMessage) return;
        firstMessage = oldest;
        lastMessage = newest;
        bool keepScroll = expanded && scroll.content.rect.height > scroll.viewport.rect.height
            && scroll.verticalNormalizedPosition > .01f;
        float offset = scroll.content.anchoredPosition.y;
        for (int i = 0; i < entries.Length; i++)
        {
            if (i == rows.Count) rows.Add(CreateMessageRow());
            var entry = entries[i];
            var row = rows[i];
            row.speaker.text = entry.seat == packet.viewerSeat ? "나" : $"{entry.seat}번";
            row.text.text = entry.text;
            row.root.gameObject.SetActive(true);
        }
        for (int i = entries.Length; i < rows.Count; i++) rows[i].root.gameObject.SetActive(false);
        RestoreScroll(keepScroll, offset);
    }

    private (RectTransform root, TMP_Text speaker, TMP_Text text) CreateMessageRow()
    {
        var root = Play2UGui.Rect("Chat Message", scroll.content, Vector2.zero, Vector2.zero);
        var line = root.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        line.childAlignment = TextAnchor.UpperLeft;
        line.childControlWidth = line.childControlHeight = true;
        line.childForceExpandWidth = line.childForceExpandHeight = false;
        line.spacing = 9;

        var badge = Play2HudArt.Plate("Speaker Badge", root, Vector2.zero, new Vector2(48, 25), "PlayingKey");
        var badgeLayout = badge.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        badgeLayout.preferredWidth = 48;
        badgeLayout.preferredHeight = 25;
        var speaker = Play2UGui.Text("Speaker", badge, "", Vector2.zero, new Vector2(46, 24), 16,
            Play2UGui.Ink, Font, style: FontStyles.Bold);

        var text = Play2UGui.Text("Message", root, "", Vector2.zero, Vector2.zero, 15, ChatText, Font,
            TextAlignmentOptions.Left);
        text.margin = new Vector4(0, 3, 0, 0);
        text.richText = false;
        var textLayout = text.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        textLayout.minHeight = 25;
        textLayout.flexibleWidth = 1;
        return (root, speaker, text);
    }

    private void Send()
    {
        if (packet?.hasGame != true || !send.interactable || string.IsNullOrWhiteSpace(input.text)) return;
        bool toDealer = dealer.interactable && dealer.isOn;
        if (!table.Submit(new HoldemWireRequest
        {
            type = "chat", text = input.text.Trim(), toDealer = toDealer,
            handId = packet.game.handId, street = packet.game.street, version = packet.game.version,
            windowId = toDealer ? packet.ownUtterances.windowId : null
        })) return;
        input.SetTextWithoutNotify("");
        dealer.SetIsOnWithoutNotify(false);
        SetExpanded(true);
        input.ActivateInputField();
    }
}
