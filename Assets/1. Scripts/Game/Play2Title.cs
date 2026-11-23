using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public sealed class Play2Title : MonoBehaviour
{
    [SerializeField] private Poker.Llm.Unity.LlmUnityMentAnalyzer hostAnalyzer;
    private const string DealerWaitMessage = "이전 딜러 요청을 마무리하고 있습니다.";

    private enum Form { Closed, Host, Join, Practice, Settings, Quit }

    private Play2RoomSession session;
    private Form openForm;
    private VisualElement menu, modal, fields, settings;
    private TextField nickname, room;
    private DropdownField capacity;
    private Label heading, detail, notice, volumeValue;
    private Button confirm, cancel, reconnect;
    private bool wasConnecting, wasReconnectAvailable;
    private string lastStatus;
    private Texture2D scrim;

    private void Start()
    {
        session = Play2RoomSession.GetOrCreate();
        Play2AudioVideoSettings.Restore();

        var root = GetComponent<UIDocument>().rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        scrim = CreateScrim();
        root.Q("scrim").style.backgroundImage = new StyleBackground(scrim);
        menu = root.Q("menu");
        reconnect = new Button(() => session.Reconnect()) { name = "reconnect", text = "다시 연결" };
        reconnect.AddToClassList("menu-button");
        reconnect.AddToClassList("featured");
        menu.Insert(1, reconnect);
        Show(reconnect, session.CanReconnect);
        modal = root.Q("form");
        fields = root.Q("room-fields");
        settings = root.Q("settings-fields");
        heading = root.Q<Label>("form-title");
        detail = root.Q<Label>("form-detail");
        notice = root.Q<Label>("notice");
        nickname = root.Q<TextField>("nickname");
        nickname.value = PlayerPrefs.GetString("Play2.Nickname", "");
        room = root.Q<TextField>("room-code");
        capacity = root.Q<DropdownField>("capacity");
        capacity.choices = new System.Collections.Generic.List<string> { "2명", "3명", "4명" };
        capacity.index = capacity.choices.IndexOf("4명");

        var volume = root.Q<Slider>("master-volume");
        volumeValue = root.Q<Label>("volume-value");
        volume.SetValueWithoutNotify(AudioListener.volume);
        SetVolumeLabel(AudioListener.volume);
        volume.RegisterValueChangedCallback(change =>
        {
            Play2AudioVideoSettings.SetVolume(change.newValue);
            SetVolumeLabel(change.newValue);
        });

        var fullscreen = root.Q<Toggle>("fullscreen");
        fullscreen.SetValueWithoutNotify(Play2AudioVideoSettings.Fullscreen);
        fullscreen.RegisterValueChangedCallback(change => Play2AudioVideoSettings.SetFullscreen(change.newValue));

        root.Q<Button>("host").clicked += () => OpenForm(Form.Host);
        root.Q<Button>("join").clicked += () => OpenForm(Form.Join);
        root.Q<Button>("settings").clicked += () => OpenForm(Form.Settings);
        root.Q<Button>("quit").clicked += () => OpenForm(Form.Quit);
        root.Q<Button>("local").clicked += () =>
        {
            session.StartPreview(hostAnalyzer);
            if (session.WaitingForDealer) OpenForm(Form.Practice);
        };
        confirm = root.Q<Button>("confirm");
        cancel = root.Q<Button>("cancel");
        confirm.clicked += Confirm;
        cancel.clicked += () =>
        {
            session.CancelPendingRoomStart();
            OpenForm(Form.Closed);
        };
        Update();
    }

    private void Update()
    {
        if (session == null) return;
        bool connecting = session.IsConnecting;
        string status = session.WaitingForDealer ? DealerWaitMessage : session.Status;
        if (connecting == wasConnecting && status == lastStatus && wasReconnectAvailable == session.CanReconnect) return;
        wasConnecting = connecting;
        wasReconnectAvailable = session.CanReconnect;
        Show(reconnect, session.CanReconnect);
        lastStatus = status;
        menu.SetEnabled(!connecting && session.State == null && openForm == Form.Closed);
        confirm.SetEnabled(!connecting && session.State == null);
        cancel.SetEnabled((!connecting || session.WaitingForDealer) && session.State == null);
        if (openForm == Form.Host || openForm == Form.Join)
        {
            if (session.WaitingForDealer) detail.text = status;
            else if (connecting) detail.text = "방에 연결하는 중...";
            else if (!string.IsNullOrEmpty(status)) detail.text = Poker.Online.HoldemOnlineText.ConnectionFailure(status);
        }
        notice.text = session.WaitingForDealer ? status : session.CanReconnect
            ? connecting ? "다시 연결하는 중..." : session.ReconnectRoom + " 연결이 끊겼습니다. 다시 연결할 수 있습니다."
            : status == "HostEnded" ? "방장이 나가 방이 종료됐어요."
            : string.IsNullOrEmpty(status) ? "" : Poker.Online.HoldemOnlineText.ConnectionFailure(status);
    }

    private void OpenForm(Form next)
    {
        if (openForm == Form.Settings) PlayerPrefs.Save();
        openForm = next;
        Show(modal, next != Form.Closed);
        Show(notice, next == Form.Closed);
        menu.SetEnabled(next == Form.Closed);
        if (next == Form.Closed) return;

        bool roomForm = next == Form.Host || next == Form.Join;
        Show(fields, roomForm);
        Show(capacity, next == Form.Host);
        Show(settings, next == Form.Settings);
        Show(confirm, next != Form.Settings && next != Form.Practice);
        heading.text = next switch
        {
            Form.Host => "방 만들기",
            Form.Join => "방 참여",
            Form.Practice => "혼자 연습하기",
            Form.Settings => "설정",
            _ => "게임을 종료할까요?"
        };
        confirm.text = next == Form.Host ? "방 열기" : next == Form.Join ? "참가하기" : "종료";
        cancel.text = next == Form.Settings ? "닫기" : next == Form.Practice ? "취소" : "뒤로";
        detail.text = next == Form.Practice ? DealerWaitMessage
            : next == Form.Host ? "만든 방 코드를 친구에게 알려주세요." : "";
        if (roomForm)
        {
            room.value = next == Form.Host ? "OMC-" + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant() : "";
            nickname.Focus();
        }
        else cancel.Focus();
    }

    private void Confirm()
    {
        if (openForm == Form.Quit) { Application.Quit(); return; }
        if (openForm != Form.Host && openForm != Form.Join) return;
        string name = nickname.value.Trim();
        string code = room.value.Trim().ToUpperInvariant();
        if (name.Length == 0) { detail.text = "닉네임을 입력해 주세요."; return; }
        if (!Poker.Application.HoldemPlayerText.IsValidSingleLine(name))
        { detail.text = "닉네임은 줄바꿈 없이 입력해 주세요."; return; }
        if (code.Length < 6 || code.Length > 24 || code.Any(c => !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-'))
        {
            detail.text = "방 코드는 영문, 숫자, 하이픈으로 6~24자를 입력해 주세요.";
            return;
        }
        PlayerPrefs.SetString("Play2.Nickname", name);
        session.Connect(openForm == Form.Host, code, name, capacity.index + 2, hostAnalyzer);
    }

    private void SetVolumeLabel(float value) => volumeValue.text = Mathf.RoundToInt(value * 100f) + "%";
    private static void Show(VisualElement element, bool visible) => element.EnableInClassList("title-hidden", !visible);

    private static Texture2D CreateScrim()
    {
        const int width = 128;
        var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
        {
            name = "Title contrast gradient",
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color[width];
        for (int i = 0; i < width; i++)
        {
            float opacity = Mathf.SmoothStep(0.88f, 0f, (float)i / (width - 1));
            pixels[i] = new Color(0.025f, 0.060f, 0.038f, opacity);
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void OnDestroy()
    {
        if (scrim != null) Destroy(scrim);
    }
}
