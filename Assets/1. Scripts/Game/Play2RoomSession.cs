using System;
using Poker.Application;
using Poker.Foundation;
using Poker.Online;
using Poker.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Play2RoomSession : MonoBehaviour
{
    internal const string TitleScene = "Title";
    internal const string LobbyScene = "Lobby";
    internal const string GameScene = "InGame";

    internal static Play2RoomSession Current { get; private set; }

    private readonly Guid[] previewSeats = new Guid[4];
    private FusionClient online;
    private HoldemHostCommandBridge preview;
    private CryptographicRandomSource previewRandom;
    private HoldemLlmTableDealer previewDealer;
    private Poker.Llm.Unity.LlmUnityMentAnalyzer roomAnalyzer;
    private AsyncOperation sceneLoad;
    private HoldemRoomPacket state;
    private string status = "";
    private bool connecting;

    internal HoldemRoomPacket State => state;
    internal string Status => status;
    internal bool IsConnecting => connecting || (online != null && state == null);
    internal bool IsLocal => preview != null;
    internal bool IsHost => IsLocal || (online != null && online.IsHost);
    internal string RoomCode => online?.RoomName ?? "";

    internal static Play2RoomSession GetOrCreate()
    {
        if (Current != null) return Current;
        Current = new GameObject("Holdem Room Session").AddComponent<Play2RoomSession>();
        DontDestroyOnLoad(Current.gameObject);
        return Current;
    }

    private void Update()
    {
        if (online != null)
        {
            online.Poll();
            while (online.TryReadResponse(out var response))
                ReadReceipt(response);
            if (online.IsClosed)
            {
                status = online.ConnectionError ?? online.AdmissionError
                    ?? (string.IsNullOrEmpty(status) ? "Disconnected" : status);
                Destroy(online);
                online = null;
                DestroyRoomAnalyzer();
                state = null;
            }
            else state = online.Latest;
        }
        else if (preview != null)
        {
            previewDealer.Tick();
            if (preview.TryCreateProjection(previewSeats[0], out var packet, state?.revision ?? -1))
                state = packet;
        }

        string scene = state == null ? TitleScene : state.hasGame ? GameScene : LobbyScene;
        if ((sceneLoad == null || sceneLoad.isDone) && SceneManager.GetActiveScene().name != scene)
            sceneLoad = SceneManager.LoadSceneAsync(scene);
    }

    internal void ToggleReady()
    {
        if (online == null || state == null) return;
        foreach (var member in state.members)
        {
            if (member.seat != state.viewerSeat) continue;
            online.Send(new HoldemWireRequest { type = "ready", ready = !member.ready });
            break;
        }
    }

    internal bool PlayPreviewOpponent()
    {
        var game = state?.game;
        if (preview == null || game == null || game.currentSeat <= 1 || game.dealPending
            || game.revealPending || game.hasResult) return false;
        Guid actor = previewSeats[game.currentSeat - 1];
        if (!preview.TryCreateProjection(actor, out var packet) || !packet.game.hasLegal) return false;
        var legal = packet.game.legal;
        if (!legal.check && !legal.call) return false;

        var request = PreviewRequest("act");
        request.handId = game.handId;
        request.version = game.version;
        request.action = (int)(legal.check ? BettingActionKind.Check : BettingActionKind.Call);
        preview.Dispatch(actor, request);
        return true;
    }

    internal bool Submit(HoldemWireRequest request)
    {
        if (state == null) return false;
        status = "";
        if (preview == null) return online?.Send(request) != null;
        request.protocol = HoldemRoomPacketMapper.ProtocolVersion;
        request.id = Guid.NewGuid().ToString("N");
        request.sessionId = state.sessionId;
        var reply = preview.Dispatch(previewSeats[0], request).Response;
        ReadReceipt(reply);
        return reply.accepted;
    }

    private void ReadReceipt(HoldemWireResponse response)
    {
        if (response.type == "chat-receipt")
        {
            status = !response.accepted ? "채팅 전송 실패 · " + response.chatError
                : !response.hasUtteranceReceipt ? ""
                : response.utteranceError == "None" ? "딜러에게 요청을 전달했습니다."
                : "채팅 전송 완료 · 딜러 요청 거절 (" + response.utteranceError + ")";
        }
        else if (response.type == "receipt" && !response.accepted)
            status = "행동 거절 · " + response.error;
    }

    private Poker.Llm.Unity.LlmUnityMentAnalyzer CreateRoomAnalyzer(Poker.Llm.Unity.LlmUnityMentAnalyzer hostAnalyzer)
    {
        if (hostAnalyzer == null) return null;
        string model = hostAnalyzer.GetComponent<LLMUnity.LLM>().model;
        if (string.IsNullOrEmpty(model) || !System.IO.File.Exists(LLMUnity.LLM.GetLLMManagerAssetRuntime(model)))
            return null;
        roomAnalyzer = Instantiate(hostAnalyzer, transform);
        return roomAnalyzer;
    }

    private void DestroyRoomAnalyzer()
    {
        if (roomAnalyzer != null) Destroy(roomAnalyzer.gameObject);
        roomAnalyzer = null;
    }

    internal async void Connect(bool host, string room, string nickname, int capacity,
        Poker.Llm.Unity.LlmUnityMentAnalyzer hostAnalyzer)
    {
        if (online != null || preview != null || connecting) return;
        connecting = true;
        status = "";
        var client = gameObject.AddComponent<FusionClient>();
        online = client;
        try
        {
            await client.Connect(host, room.Trim().ToUpperInvariant(), new HoldemClientIdentity(nickname.Trim()),
                capacity: capacity, llmDealer: host, hostAnalyzer: host ? CreateRoomAnalyzer(hostAnalyzer) : null);
        }
        catch (ArgumentException error)
        {
            status = error.Message;
            if (client != null) client.Dispose();
        }
        finally { connecting = false; }
    }

    internal void StartPreview(Poker.Llm.Unity.LlmUnityMentAnalyzer hostAnalyzer)
    {
        if (preview != null || online != null || connecting) return;
        status = "";
        previewRandom = new CryptographicRandomSource();
        var nickname = PlayerPrefs.GetString("Play2.Nickname", "").Trim();
        var host = new HoldemClientIdentity(nickname.Length == 0 ? "플레이어" : nickname);
        preview = new HoldemHostCommandBridge(host, HoldemLlmTableDealer.CreateConfig(),
            new SeatId(1), previewRandom, utterancePolicy: HoldemLlmTableDealer.CreateUtterancePolicy(), chatEnabled: true);
        var analyzer = CreateRoomAnalyzer(hostAnalyzer);
        if (analyzer != null) analyzer.gameObject.SetActive(true);
        previewDealer = new HoldemLlmTableDealer(preview, analyzer);
        for (int i = 0; i < previewSeats.Length; i++)
        {
            var identity = i == 0 ? host : new HoldemClientIdentity("Preview " + (i + 1));
            previewSeats[i] = Guid.NewGuid();
            preview.Dispatch(previewSeats[i], identity.AdmissionRequest());
            preview.Dispatch(previewSeats[i], PreviewRequest("ready"));
        }
    }

    private HoldemWireRequest PreviewRequest(string type) => new HoldemWireRequest
    {
        protocol = HoldemRoomPacketMapper.ProtocolVersion, type = type, ready = true,
        id = Guid.NewGuid().ToString("N"), sessionId = preview.SessionId.ToString("N")
    };

    internal void StartHand()
    {
        if (!IsHost) return;
        Submit(new HoldemWireRequest { type = "start", handId = Guid.NewGuid().ToString("N"),
            version = state?.hasGame == true ? state.game.version : 0 });
    }

    internal void Leave()
    {
        status = "";
        CloseRoom();
    }

    private void CloseRoom()
    {
        if (online != null)
        {
            online.Dispose();
            Destroy(online);
            online = null;
        }

        previewDealer?.Dispose();
        previewDealer = null;
        DestroyRoomAnalyzer();
        preview?.Dispose();
        preview = null;
        previewRandom?.Dispose();
        previewRandom = null;
        state = null;
    }

    private void OnDestroy()
    {
        if (Current != this) return;
        CloseRoom();
        Current = null;
    }
}
