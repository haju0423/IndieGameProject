using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using Poker.Application;
using Poker.Transport;
using UnityEngine;
using HoldemSeatStatus = Poker.Foundation.HoldemSeatStatus;

public sealed class Play2TablePreview : MonoBehaviour
{
    [SerializeField] private Camera seatCamera;
    [SerializeField] private Transform tabletop;
    [SerializeField] private GameObject[] players;
    [SerializeField] private Transform[] eyes;
    [SerializeField] private CardVisual cardPrefab;
    [SerializeField] private Material requestMarkerMaterial;
    [SerializeField] private Material chipMaterial;
    [SerializeField] private Play2FrontEnd frontEnd;

    private const float BoardDealDuration = 0.95f;
    private readonly List<CardVisual> cards = new List<CardVisual>(13);
    private readonly CardVisual[] boardCards = new CardVisual[5];
    private readonly LineRenderer[] requestMarkers = new LineRenderer[5];
    private readonly HashSet<int> dealtSeats = new HashSet<int>();
    private readonly Play2CardGrip[] grips = new Play2CardGrip[4];
    private Play2RoomSession session;
    private HoldemRoomPacket state;
    private string handId;
    private int viewer, board;
    private Play2TableChips chips;
    private Transform deck;
    private Vector3 DealOrigin => deck.TransformPoint(Vector3.up * .022f);
    private float animationUntil;
    private float nextPreviewAction;

    private void LateUpdate()
    {
        foreach (var grip in grips) grip?.UpdatePose();
    }

    private void Start()
    {
        session = Play2RoomSession.Current;
        if (session == null)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(Play2RoomSession.TitleScene);
            return;
        }

        chips = new GameObject("Table pot").AddComponent<Play2TableChips>();
        chips.transform.SetParent(tabletop, false);
        chips.Initialize(eyes, chipMaterial);
        deck = new GameObject("Dealer deck").transform;
        deck.SetParent(tabletop, false);
        deck.localPosition = new Vector3(.29f, .016f, .29f);
        var template = cardPrefab.transform.Find("Back");
        for (int i = 0; i < 4; i++)
        {
            var back = Instantiate(template, deck, false);
            back.localPosition = new Vector3(i * .0008f, i * .0035f, 0);
            back.localRotation = Quaternion.Euler(0, -16f, 180f) * template.localRotation;
            back.localScale = template.localScale * 1.45f;
        }
        frontEnd.Initialize(this);
        SetState(session.State);
    }

    internal bool IsHost => session != null && session.IsHost;
    internal bool IsDealing => Time.time < animationUntil;

    private void Update()
    {
        if (session == null) return;
        SetState(session.State);
        if (!IsDealing && Time.time >= nextPreviewAction && session.PlayPreviewOpponent())
            nextPreviewAction = Time.time + 0.65f;
        frontEnd.Present(state, session.Status);
    }

    internal bool Submit(HoldemWireRequest request) => session != null && session.Submit(request);
    internal void StartHand() => session?.StartHand();
    internal void Leave() => session?.Leave();

    private void SetState(HoldemRoomPacket packet)
    {
        if (ReferenceEquals(state, packet) && viewer != 0) return;
        chips.Present(state, packet);
        deck.gameObject.SetActive(packet?.hasGame == true);
        state = packet;
        int viewerSeat = state?.viewerSeat ?? 1;
        if (viewerSeat != viewer)
        {
            ClearCards();
            viewer = viewerSeat;
            var eye = eyes[viewer - 1];
            seatCamera.transform.SetPositionAndRotation(eye.position, eye.rotation);
        }
        for (int i = 0; i < players.Length; i++)
            players[i].SetActive(i + 1 != viewer && (state == null || state.members.Any(m => m.seat == i + 1 && m.connected)));

        var game = state?.hasGame == true ? state.game : null;
        if (handId != game?.handId)
        {
            ClearCards();
            handId = game?.handId;
        }
        if (game == null) return;
        var seatsInHand = game.seats.Where(seat => seat.dealtIn).ToArray();
        for (int i = 0; i < seatsInHand.Length; i++)
        {
            var seat = seatsInHand[i];
            bool own = seat.seat == viewer;
            int player = seat.seat - 1;
            bool folded = seat.status == (int)HoldemSeatStatus.Folded;
            bool showFaces = own && !folded;
            if (showFaces && seat.visibleCards.Length < 2) continue;
            if (!dealtSeats.Add(seat.seat))
            {
                if (folded) grips[player].Fold();
                continue;
            }

            float delay = i * 0.10f;
            float gap = seatsInHand.Length * 0.10f;
            CardVisual firstCard = CreateHoleCard(showFaces, showFaces ? seat.visibleCards[0] : 0);
            CardVisual secondCard = CreateHoleCard(showFaces, showFaces ? seat.visibleCards[1] : 0);
            grips[player] = new Play2CardGrip(players[player], own ? seatCamera.transform : null,
                tabletop, eyes[player], DealOrigin, firstCard, secondCard, delay, gap);
            if (folded) grips[player].Fold();
            else animationUntil = Mathf.Max(animationUntil, Time.time + delay + Play2CardGrip.Duration(gap));
        }
        int first = board;
        while (board < game.board.Length)
        {
            StartCoroutine(DealBoardCard(game.board[board], board, (board - first) * 0.11f));
            board++;
        }
        RefreshRequestMarkers();
    }

    private CardVisual CreateHoleCard(bool own, int cardId)
    {
        var card = Instantiate(cardPrefab, tabletop);
        cards.Add(card);
        if (own) SetCardValue(card, cardId);
        else card.transform.Find("Front").gameObject.SetActive(false);
        card.transform.localScale = Vector3.one * (own ? 1.17f : 1.10f);
        card.gameObject.SetActive(false);
        return card;
    }

    private static void SetCardValue(CardVisual card, int cardId)
    {
        var value = Poker.Foundation.Card.FromId(cardId);
        Suit suit = value.Suit switch
        {
            Poker.Foundation.Suit.Clubs => Suit.Club,
            Poker.Foundation.Suit.Diamonds => Suit.Diamond,
            Poker.Foundation.Suit.Hearts => Suit.Heart,
            _ => Suit.Spade
        };
        card.SetCard(new Card(value.Rank == Poker.Foundation.Rank.Ace ? 1 : (int)value.Rank, suit));
    }

    private IEnumerator DealBoardCard(int cardId, int index, float delay)
    {
        animationUntil = Mathf.Max(animationUntil, Time.time + delay + BoardDealDuration);
        if (delay > 0f) yield return new WaitForSeconds(delay);
        var card = Instantiate(cardPrefab, tabletop);
        cards.Add(card);
        SetCardValue(card, cardId);

        var pose = card.transform;
        Quaternion boardFacing = Quaternion.Euler(0f, seatCamera.transform.eulerAngles.y, 0f);
        Vector3 end = boardFacing * new Vector3(-0.35f + index * 0.175f, 0.012f, -0.055f);
        Vector3 start = tabletop.InverseTransformPoint(DealOrigin);
        pose.localScale = Vector3.one * 1.65f;
        pose.localPosition = start;
        pose.localRotation = boardFacing * Quaternion.Euler(0f, 0f, 179.9f);

        var motion = AddDealMotion(card, start, end);
        motion.Initialization();
        motion.PlayFeedbacks();

        yield return new WaitForSeconds(.54f);
        var back = pose.localRotation;
        for (float t = 0; t < 1; t += Time.deltaTime / .27f)
        {
            pose.localRotation = Quaternion.Slerp(back, boardFacing, Mathf.SmoothStep(0, 1, t));
            yield return null;
        }
        pose.localRotation = boardFacing;
        yield return new WaitForSeconds(BoardDealDuration - .81f);
        boardCards[index] = card;
        UpdateRequestMarker(index);
    }

    private static MMF_Player AddDealMotion(CardVisual card, Vector3 start, Vector3 end)
    {
        var motion = card.gameObject.AddComponent<MMF_Player>();
        motion.AddFeedback(new MMF_Position
        {
            AnimatePositionTarget = card.gameObject, Space = MMF_Position.Spaces.Local,
            Mode = MMF_Position.Modes.AtoB, RelativePosition = false, InitialPosition = start,
            DestinationPosition = end, AnimatePositionDuration = 0.36f,
            AnimatePositionTween = new MMTweenType(AnimationCurve.EaseInOut(0, 0, 1, 1))
        });
        return motion;
    }

    private void RefreshRequestMarkers()
    {
        for (int i = 0; i < boardCards.Length; i++) UpdateRequestMarker(i);
    }

    private void UpdateRequestMarker(int index)
    {
        if (boardCards[index] == null || requestMarkerMaterial == null) return;
        bool selected = HoldemDealerSelectionPacketReader.FindOwnAppliedCard(state, index) != null;
        if (selected && requestMarkers[index] == null)
        {
            var marker = new GameObject("Own Request Card Outline");
            marker.transform.SetParent(boardCards[index].transform, false);
            marker.transform.SetLocalPositionAndRotation(new Vector3(0f, .003f, 0f),
                Quaternion.Euler(90f, 0f, 0f));
            var line = marker.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.alignment = LineAlignment.TransformZ;
            line.loop = true;
            line.positionCount = 4;
            line.SetPositions(new[] { new Vector3(-.045f, -.063f, 0f), new Vector3(-.045f, .063f, 0f),
                new Vector3(.045f, .063f, 0f), new Vector3(.045f, -.063f, 0f) });
            line.startWidth = line.endWidth = .0045f;
            line.sharedMaterial = requestMarkerMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            requestMarkers[index] = line;
        }
        if (requestMarkers[index] != null) requestMarkers[index].gameObject.SetActive(selected);
    }

    private void ClearCards()
    {
        StopAllCoroutines();
        foreach (var card in cards) Destroy(card.gameObject);
        cards.Clear();
        Array.Clear(boardCards, 0, boardCards.Length);
        Array.Clear(requestMarkers, 0, requestMarkers.Length);
        for (int i = 0; i < grips.Length; i++)
        {
            grips[i]?.Release();
            grips[i] = null;
        }
        dealtSeats.Clear();
        board = 0;
        animationUntil = 0f;
    }

}
