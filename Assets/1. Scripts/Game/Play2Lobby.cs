using System.Collections.Generic;
using System.Linq;
using MoreMountains.Tools;
using Poker.Application;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public sealed class Play2Lobby : MonoBehaviour
{
    [SerializeField] private GameObject[] models;

    private Play2RoomSession session;
    private VisualElement lobby;
    private Label roomLabel, lobbyDetail;
    private Button ready, start, copy;
    private readonly Label[] seatNames = new Label[4], seatStates = new Label[4];
    private readonly GameObject[] actors = new GameObject[4];
    private readonly bool[] wasReady = new bool[4];
    private readonly List<Material> stageMaterials = new List<Material>();
    private GameObject stage;
    private HoldemRoomPacket lastPacket;
    private string lastStatus;

    private void Start()
    {
        session = Play2RoomSession.Current;
        if (session == null)
        {
            SceneManager.LoadScene(Play2RoomSession.TitleScene);
            return;
        }

        var root = GetComponent<UIDocument>().rootVisualElement;
        root.pickingMode = PickingMode.Ignore;
        lobby = root.Q("lobby");
        roomLabel = root.Q<Label>("room-label");
        lobbyDetail = root.Q<Label>("lobby-detail");
        ready = root.Q<Button>("ready");
        start = root.Q<Button>("start");
        copy = root.Q<Button>("copy");
        root.Q<Button>("leave").clicked += session.Leave;
        start.clicked += session.StartHand;
        ready.clicked += session.ToggleReady;
        copy.clicked += () =>
        {
            GUIUtility.systemCopyBuffer = session.RoomCode;
            lobbyDetail.text = "방 코드를 복사했습니다.";
            Animate(lobbyDetail, 0.35f, 6f, 0f);
        };

        var seats = root.Q("seats");
        for (int i = 0; i < seatNames.Length; i++)
        {
            var seat = new VisualElement();
            seat.AddToClassList("seat");
            seatNames[i] = new Label();
            seatNames[i].AddToClassList("seat-name");
            seatStates[i] = new Label();
            seatStates[i].AddToClassList("seat-state");
            seat.Add(seatNames[i]);
            seat.Add(seatStates[i]);
            seats.Add(seat);
        }

        BuildStage(models);
        Animate(lobby, 0f, 14f, 0f);
        Present(session.State, session.Status);
    }

    private void Update()
    {
        if (session != null) Present(session.State, session.Status);
    }

    private void Present(HoldemRoomPacket packet, string status)
    {
        if (packet == null || packet.hasGame) return;
        if (ReferenceEquals(packet, lastPacket) && status == lastStatus) return;
        lastPacket = packet;
        lastStatus = status;

        int count = packet.rules.seatCapacity;
        roomLabel.text = session.IsLocal ? "로컬 프리뷰" : session.RoomCode + "  ·  " + packet.members.Length + "/" + count;
        Show(copy, !session.IsLocal);
        bool allReady = packet.members.Length == count && packet.members.All(member => member.connected && member.ready);
        var mine = packet.members.FirstOrDefault(member => member.seat == packet.viewerSeat);
        Show(ready, !session.IsLocal);
        ready.text = mine?.ready == true ? "준비 취소" : "준비";
        Show(start, session.IsHost);
        start.SetEnabled(allReady);
        lobbyDetail.text = !string.IsNullOrEmpty(status) ? status
            : session.IsLocal ? "네 좌석의 카드 배분을 연습합니다."
            : allReady ? "모두 준비되었습니다."
            : packet.members.Length < count ? "플레이어가 들어오기를 기다리는 중"
            : "모든 플레이어가 준비하면 방장이 시작할 수 있습니다.";

        for (int i = 0; i < actors.Length; i++)
        {
            var member = packet.members.FirstOrDefault(m => m.seat == i + 1);
            var plate = seatNames[i].parent;
            bool occupied = member?.connected == true;
            bool isReady = occupied && member.ready;
            bool entering = occupied && !actors[i].activeSelf;
            actors[i].SetActive(occupied);
            if (entering)
            {
                Vector3 end = actors[i].transform.localPosition;
                StartCoroutine(RevealActor(actors[i].transform, end));
            }
            seatNames[i].text = member == null ? "빈자리" : member.name + (member.host ? "  ·  방장" : "");
            seatStates[i].text = i >= count ? "닫힌 자리" : member == null ? "입장 대기"
                : !occupied ? "연결 끊김" : isReady ? "✓ 준비 완료" : "준비 중";
            plate.EnableInClassList("mine", i + 1 == packet.viewerSeat);
            seatStates[i].EnableInClassList("ready", isReady);
            if (isReady != wasReady[i]) Animate(plate, 0.45f, 7f, 0f);
            wasReady[i] = isReady;
        }
    }

    private System.Collections.IEnumerator RevealActor(Transform actor, Vector3 end)
    {
        float started = Time.unscaledTime;
        Vector3 from = end + Vector3.back * 0.18f + Vector3.down * 0.16f;
        while (Time.unscaledTime - started < 0.35f)
        {
            actor.localPosition = MMTween.Tween(Time.unscaledTime - started, 0f, 0.35f, from, end, MMTween.MMTweenCurve.EaseOutCubic);
            yield return null;
        }
        actor.localPosition = end;
    }

    private static void Animate(VisualElement element, float initialOpacity, float offset, float delay)
    {
        float started = Time.unscaledTime + delay;
        element.style.opacity = initialOpacity;
        element.style.translate = new Translate(0, offset);
        IVisualElementScheduledItem tick = null;
        tick = element.schedule.Execute(() =>
        {
            float elapsed = Mathf.Clamp(Time.unscaledTime - started, 0f, 0.36f);
            float progress = MMTween.Tween(elapsed, 0f, 0.36f, 0f, 1f, MMTween.MMTweenCurve.EaseOutCubic);
            element.style.opacity = Mathf.Lerp(initialOpacity, 1f, progress);
            element.style.translate = new Translate(0, offset * (1f - progress));
            if (elapsed >= 0.36f)
            {
                tick.Pause();
            }
        }).Every(16);
    }

    private static void Show(VisualElement element, bool visible) => element.EnableInClassList("hidden", !visible);

    private void BuildStage(GameObject[] models)
    {
        stage = new GameObject("Lobby Stage");
        stage.transform.SetParent(transform, false);
        stage.transform.position = new Vector3(0f, 1000f, 0f);
        var cameraObject = new GameObject("Lobby Camera");
        cameraObject.transform.SetParent(stage.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.62f, -6f);
        cameraObject.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
        var lobbyCamera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();
        lobbyCamera.orthographic = true;
        lobbyCamera.orthographicSize = 1.45f;
        lobbyCamera.clearFlags = CameraClearFlags.SolidColor;
        lobbyCamera.backgroundColor = new Color(0.045f, 0.032f, 0.026f);
        lobbyCamera.farClipPlane = 14f;
        var wood = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        wood.color = new Color(0.28f, 0.14f, 0.079f);
        wood.SetFloat("_Smoothness", 0.06f);
        wood.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        stageMaterials.Add(wood);
        var darkerWood = new Material(wood) { color = new Color(0.21f, 0.10f, 0.055f) };
        stageMaterials.Add(darkerWood);
        for (int i = 0; i < 24; i++)
            Block("Stage plank", new Vector3((i - 11.5f) * 0.38f, -0.065f, 0.5f),
                new Vector3(0.375f, 0.13f, 13f), i % 3 == 0 ? darkerWood : wood);
        for (int i = 0; i < actors.Length; i++)
        {
            float x = (i - 1.5f) * 1.22f;
            Block("Seat riser", new Vector3(x, 0.19f, 0.29f), new Vector3(0.58f, 0.38f, 0.48f), darkerWood);
            actors[i] = Instantiate(models[i], stage.transform);
            actors[i].transform.localPosition = new Vector3(x, 0.19f, 0f);
            actors[i].transform.localRotation = Quaternion.Euler(0f, 180f + (i - 1.5f) * 8f, 0f);
            actors[i].SetActive(false);
        }
        StageLight(new Vector3(-2f, 3f, -3f), new Color(1f, 0.72f, 0.46f), 7f);
        StageLight(new Vector3(2f, 2f, 1f), new Color(0.96f, 0.57f, 0.26f), 4f);

    }

    private void Block(string label, Vector3 position, Vector3 size, Material material)
    {
        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = label;
        Destroy(block.GetComponent<Collider>());
        block.transform.SetParent(stage.transform, false);
        block.transform.localPosition = position;
        block.transform.localScale = size;
        block.GetComponent<Renderer>().sharedMaterial = material;
    }

    private void StageLight(Vector3 position, Color color, float intensity)
    {
        var light = new GameObject("Stage light").AddComponent<Light>();
        light.transform.SetParent(stage.transform, false);
        light.transform.localPosition = position;
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = 9f;
    }

    private void OnDestroy()
    {
        foreach (var material in stageMaterials) Destroy(material);
    }
}
