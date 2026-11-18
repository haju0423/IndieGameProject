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
    internal enum ShowdownBeat { None, Waiting, Reveal, Award, Finished }
    internal ShowdownBeat Showdown { get; private set; }
    internal int AwardIndex { get; private set; }
    internal Play2SettlementSummary.ShowdownAward CurrentAward => awards.Length == 0 ? null : awards[AwardIndex];
    internal int[] HighlightSeats => CurrentAward?.Seats ?? Array.Empty<int>();
    internal int HighlightSeat => HighlightSeats.Length == 0 ? 0 : HighlightSeats[0];
    private Play2SettlementSummary.ShowdownAward[] awards = Array.Empty<Play2SettlementSummary.ShowdownAward>();
    private Play2ShowdownCards showdownCards;
    private float beatUntil;
    private readonly Play2AllInPosture[] postures = new Play2AllInPosture[4];
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
    private Play2TableAudio tableAudio;
    private Play2TableReactions reactions;
    private Play2TableAtmosphere atmosphere;
    private Material cardFaceMaterial;
    private SkinnedMeshRenderer[][] actorSkins;
    internal Vector3 TableCenter => tabletop.position;
    internal Vector3 PlayerLabelViewport(int seat)
    {
        var head = players[seat - 1].GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
        var point = head.position;
        point.y = actorSkins[seat - 1].Max(skin => skin.bounds.max.y) + .055f;
        var label = seatCamera.WorldToViewportPoint(point);
        label.x = seatCamera.WorldToViewportPoint(head.position).x;
        return label;
    }
    internal Vector3 PotLabelViewport => seatCamera.WorldToViewportPoint(tabletop.position +
        Vector3.ProjectOnPlane(seatCamera.transform.forward, Vector3.up).normalized * .20f + Vector3.up * .025f);
    private Transform deck;
    private Vector3 DealOrigin => deck.TransformPoint(Vector3.up * .022f);
    private float animationUntil;
    private float nextPreviewAction;

    private void LateUpdate()
    {
        float standFraming = 0f;
        if (viewer > 0 && postures[viewer - 1] != null)
        {
            for (int i = 0; i < postures.Length; i++)
            {
                postures[i].Animate(i + 1 == viewer, chips.WaitToStand(i));
                if (i + 1 != viewer) standFraming = Mathf.Max(standFraming, postures[i].Weight);
            }
            var eye = eyes[viewer - 1];
            float lift = postures[viewer - 1].Weight;
            Vector3 behind = -Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
            seatCamera.transform.SetPositionAndRotation(eye.position + behind * .20f + Vector3.up * (.035f + .11f * lift),
                eye.rotation * Quaternion.Euler(3.5f * lift, 0f, 0f));
        }
        float aspect = Mathf.Min(seatCamera.aspect, 16f / 9f);
        seatCamera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(
            Camera.VerticalToHorizontalFieldOfView(65f + standFraming * 4f, 16f / 9f), aspect);
        for (int i = 0; i < grips.Length; i++)
        {
            float push = chips.PushTime(i);
            grips[i]?.UpdatePose(postures[i].Rise, push, push >= 0f ? chips.PushPalm(push, true) : default);
        }
        if (Showdown is ShowdownBeat.Award or ShowdownBeat.Finished) LiftWinningCards();
        reactions?.Animate();
        chips?.UpdateHands();
    }

    private void Start()
    {
        session = Play2RoomSession.Current;
        if (session == null)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(Play2RoomSession.TitleScene);
            return;
        }

        // The scene was laid out for heads-up first, with seat 2 opposite seat 1.
        // Keep the avatars assigned in the lobby and move their whole poses around the table.
        if (session.State?.rules?.seatCapacity >= 3)
        {
            Vector3 centre = tabletop.position;
            Vector3 from = Vector3.ProjectOnPlane(eyes[1].position - centre, Vector3.up);
            Vector3 to = Vector3.ProjectOnPlane(eyes[2].position - centre, Vector3.up);
            float angle = Vector3.SignedAngle(from, to, Vector3.up);
            players[1].transform.RotateAround(centre, Vector3.up, angle);
            players[2].transform.RotateAround(centre, Vector3.up, -angle);
            (eyes[1], eyes[2]) = (eyes[2], eyes[1]);
        }
        showdownCards = new Play2ShowdownCards();
        actorSkins = players.Select(player => player.GetComponentsInChildren<SkinnedMeshRenderer>(true)).ToArray();
        for (int i = 0; i < players.Length; i++) postures[i] = new Play2AllInPosture(players[i].GetComponent<Animator>());
        chips = new GameObject("Table pot").AddComponent<Play2TableChips>();
        chips.transform.SetParent(tabletop, false);
        tableAudio = gameObject.AddComponent<Play2TableAudio>();
        atmosphere = gameObject.AddComponent<Play2TableAtmosphere>();
        atmosphere.Initialize(tabletop, players, eyes);
        cardFaceMaterial = Resources.Load<Material>("TablePresentation/CardFace");
        chips.Initialize(eyes, chipMaterial, players, seatCamera.transform, tableAudio);
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
        reactions = new Play2TableReactions(this, players, eyes, seatCamera, frontEnd.HudRoot, frontEnd.Font, chips);
        SetState(session.State);
    }

    internal bool IsHost => session != null && session.IsHost;
    internal bool ActionPending => session != null && session.ActionPending;
    internal bool IsDealing => Time.time < animationUntil;
    internal bool IsBetting => chips.IsAnimating;
    internal void PlayTurnSound() => tableAudio.YourTurn();
    internal void PlayResultSound() => tableAudio.HandEnd();
    internal void PlayAccusationSound(bool caught) => tableAudio.Accusation(caught);

    private void Update()
    {
        if (session == null) return;
        chips?.RestoreFlinch();
        foreach (var pose in postures) pose?.Restore();
        SetState(session.State);
        AdvanceShowdown();
        if (!IsDealing && Time.time >= nextPreviewAction && session.PlayPreviewOpponent())
            nextPreviewAction = Time.time + 0.65f;
        bool reactionOpen = reactions.HandlePointer();
        frontEnd.Present(state, session.Status, reactionOpen);
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (!IsDealing && state?.game?.hasResult != true && mouse?.leftButton.wasPressedThisFrame == true
            && UnityEngine.EventSystems.EventSystem.current?.IsPointerOverGameObject() != true
            && viewer > 0 && grips[viewer - 1] != null && grips[viewer - 1].Contains(mouse.position.ReadValue(), seatCamera))
            grips[viewer - 1].Inspect(!grips[viewer - 1].IsInspecting);
    }

    internal bool Submit(HoldemWireRequest request)
    {
        if (request.type == "act") request.chipAimSeat = reactions?.AimSeat ?? 0;
        if (session == null || !session.Submit(request)) return false;
        if (request.type == "act") reactions?.ClearAim();
        return true;
    }
    internal void StartHand() => session?.StartHand();
    internal void ToggleRematchReady() => session?.ToggleRematchReady();
    internal void RestartMatch() => session?.RestartMatch();
    internal void Leave() => session?.Leave();

    private void SetState(HoldemRoomPacket packet)
    {
        if (ReferenceEquals(state, packet) && viewer != 0) return;
        var previous = state;
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
            seatCamera.transform.position += -Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized * .20f + Vector3.up * .035f;
            seatCamera.fieldOfView = 65;
        }
        for (int i = 0; i < players.Length; i++)
            players[i].SetActive(i + 1 != viewer && (state == null || state.members.Any(m => m.seat == i + 1 && m.connected)));

        var game = state?.hasGame == true ? state.game : null;
        bool sameHand = game != null && previous?.hasGame == true && previous.sessionId == packet.sessionId
            && game.handId == previous.game.handId && viewerSeat == previous.viewerSeat;
        bool showdown = game?.hasResult == true && game.result?.kind == (int)Poker.Foundation.HoldemResultKind.Showdown;
        if (!showdown)
        {
            Showdown = ShowdownBeat.None;
            awards = Array.Empty<Play2SettlementSummary.ShowdownAward>();
            AwardIndex = 0;
        }
        else if (!sameHand || !previous.game.hasResult)
        {
            Showdown = sameHand ? ShowdownBeat.Waiting : ShowdownBeat.Finished;
            awards = Play2SettlementSummary.ShowdownAwards(game);
            AwardIndex = 0;
        }
        atmosphere.Present(packet, Showdown, HighlightSeats);
        for (int i = 0; i < postures.Length; i++)
        {
            var seat = game?.seats.FirstOrDefault(s => s.seat == i + 1);
            bool paidAll = packet?.hasHistory == true && packet.history?.entries?.Any(e => e.seat == i + 1 && e.allIn) == true;
            postures[i].AllIn = seat?.dealtIn == true && seat.status != (int)HoldemSeatStatus.Folded &&
                (seat.status == (int)HoldemSeatStatus.AllIn || showdown && (paidAll || sameHand && postures[i].AllIn));
        }
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
            bool revealed = Showdown != ShowdownBeat.Waiting && Play2SettlementSummary.IsPublicHand(seat);
            bool showFaces = own && !folded;
            if (showFaces && seat.visibleCards.Length < 2) continue;
            if (!dealtSeats.Add(seat.seat))
            {
                if (folded) grips[player].Fold();
                else if (revealed) grips[player].Reveal(seat.visibleCards[0], seat.visibleCards[1]);
                continue;
            }

            float delay = i * 0.10f;
            float gap = seatsInHand.Length * 0.10f;
            CardVisual firstCard = CreateHoleCard(showFaces, showFaces ? seat.visibleCards[0] : 0);
            CardVisual secondCard = CreateHoleCard(showFaces, showFaces ? seat.visibleCards[1] : 0);
            grips[player] = new Play2CardGrip(players[player], own ? seatCamera.transform : null,
                tabletop, eyes[player], DealOrigin, firstCard, secondCard, delay, gap,
                showFaces && game.board.Length == 0 && !game.hasResult, tableAudio.CardSlide);
            if (folded) grips[player].Fold();
            else if (revealed) grips[player].Reveal(seat.visibleCards[0], seat.visibleCards[1]);
            else animationUntil = Mathf.Max(animationUntil, Time.time + delay + Play2CardGrip.Duration(gap));
        }
        int first = board;
        while (board < game.board.Length)
        {
            StartCoroutine(DealBoardCard(game.board[board], board, (board - first) * 0.11f));
            board++;
        }
        RefreshRequestMarkers();
        reactions.Present(packet);
    }

    private void AdvanceShowdown()
    {
        switch (Showdown)
        {
            case ShowdownBeat.Waiting:
                if (IsDealing || IsBetting) return;
                foreach (var seat in state.game.seats)
                    if (Play2SettlementSummary.IsPublicHand(seat))
                        grips[seat.seat - 1]?.Reveal(seat.visibleCards[0], seat.visibleCards[1]);
                Showdown = ShowdownBeat.Reveal;
                beatUntil = Time.unscaledTime + 1.05f;
                break;
            case ShowdownBeat.Reveal when Time.unscaledTime >= beatUntil:
                Showdown = ShowdownBeat.Award;
                beatUntil = Time.unscaledTime + 1.6f;
                reactions.Celebrate(state);
                break;
            case ShowdownBeat.Award when Time.unscaledTime >= beatUntil:
                if (AwardIndex + 1 < awards.Length)
                {
                    AwardIndex++;
                    beatUntil = Time.unscaledTime + 1.45f;
                }
                else
                {
                    Showdown = ShowdownBeat.Finished;
                    AwardIndex = 0;
                }
                break;
            default:
                return;
        }
        atmosphere.Present(state, Showdown, HighlightSeats);
    }

    private void LiftWinningCards()
    {
        if (HighlightSeat == 0 || IsDealing) return;
        var game = state.game;
        if (!showdownCards.Ready)
        {
            for (int i = 0; i < game.board.Length; i++) if (boardCards[i] == null) return;
            foreach (var seat in game.seats)
                if (Play2SettlementSummary.IsPublicHand(seat) && grips[seat.seat - 1]?.RestingFaceUp != true) return;
            for (int i = 0; i < game.board.Length; i++) showdownCards.Add(boardCards[i], game.board[i]);
            foreach (var seat in game.seats)
                if (Play2SettlementSummary.IsPublicHand(seat)) grips[seat.seat - 1].AddRevealedCards(showdownCards, seat.visibleCards);
        }
        foreach (var seat in game.seats)
            if (seat.seat == HighlightSeat) { showdownCards.Animate(seat.revealedBestCards); break; }
    }

    private CardVisual CreateHoleCard(bool own, int cardId)
    {
        var card = Instantiate(cardPrefab, tabletop);
        card.transform.Find("Front").GetComponent<MeshRenderer>().sharedMaterial = cardFaceMaterial;
        cards.Add(card);
        if (own) SetCardValue(card, cardId);
        else card.transform.Find("Front").gameObject.SetActive(false);
        card.transform.localScale = Vector3.one * (own ? 1.40f : 1.10f);
        card.gameObject.SetActive(false);
        return card;
    }

    internal static void SetCardValue(CardVisual card, int cardId)
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
        card.transform.Find("Front").GetComponent<MeshRenderer>().sharedMaterial = cardFaceMaterial;
        cards.Add(card);
        tableAudio.CardSlide();
        SetCardValue(card, cardId);

        var pose = card.transform;
        Quaternion boardFacing = Quaternion.Euler(0f, seatCamera.transform.eulerAngles.y, 0f);
        Vector3 end = boardFacing * new Vector3((index - 2) * .164f, 0.012f, -0.045f);
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

    private void OnDestroy() => reactions?.Release();

    private void ClearCards()
    {
        showdownCards.Clear();
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
