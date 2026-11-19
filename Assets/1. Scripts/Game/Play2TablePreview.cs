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
    // Includes the time it takes to lay the cards down. Leave the final hand in view before announcing the pot.
    private const float ShowdownLeadIn = .35f;
    private const float BetweenHandReveals = 1.35f;
    private const float LastHandReadTime = 1.65f;
    private float beatUntil;
    private int[] revealOrder = Array.Empty<int>();
    private int openedHands;
    internal int RevealingSeat => Showdown == ShowdownBeat.Reveal && openedHands > 0 ? revealOrder[openedHands - 1] : 0;
    internal bool IsHandShown(int seat)
    {
        if (Showdown is ShowdownBeat.Award or ShowdownBeat.Finished) return true;
        return Array.IndexOf(revealOrder, seat, 0, openedHands) >= 0;
    }
    private readonly MeshRenderer[] chairs = new MeshRenderer[4];
    private MeshRenderer firstPersonChair;
    private UnityEngine.Rendering.ShadowCastingMode chairShadows;
    private readonly Play2AllInPosture[] postures = new Play2AllInPosture[4];
    [SerializeField] private Camera seatCamera;
    [SerializeField] private Transform tabletop;
    [SerializeField] private GameObject[] players;
    [SerializeField] private Transform[] eyes;
    [SerializeField] private CardVisual cardPrefab;
    [SerializeField] private Material requestMarkerMaterial;
    [SerializeField] private Material chipMaterial;
    [SerializeField] private Play2FrontEnd frontEnd;

    private const float BoardScale = 1.82f, BoardTilt = 18f, BoardZ = -.045f;
    private static float BoardEdgeRise => .060f * BoardScale * Mathf.Sin(BoardTilt * Mathf.Deg2Rad);
    private const float BoardDealDuration = 0.95f;
    private const float ReadBoardBetweenStreets = .46f;
    private readonly float[] boardLandedAt = new float[5];
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
    private Play2MatchFinale finale;
    private bool liveFinalHand, groupStarted, tookPhoto;
    internal Play2MatchFinale.Beat FinaleBeat => finale?.Stage ?? Play2MatchFinale.Beat.None;
    internal void BeginFinale()
    {
        if (finale.Active || state?.game?.isOver != true || state.game.sessionWinner < 1
            || state.game.sessionWinner > players.Length) return;
        finale.Begin(state, liveFinalHand);
        // The fold winner may still be holding an unrevealed hand. Put it down with its back up.
        foreach (var grip in grips) grip?.Fold(liveFinalHand);
    }

    internal void CyclePhotoPose()
    {
        if (finale?.Stage >= Play2MatchFinale.Beat.Portrait && state.hasChat && !ActionPending)
            Submit(new HoldemWireRequest { type = "chat", text = finale.NextPoseMessage(viewer) });
    }
    private Play2TableReactions reactions;
    private Play2TableAtmosphere atmosphere;
    private Material cardFaceMaterial, boardFaceMaterial, curvedPaper;
    private readonly Play2FaceContact[] faces = new Play2FaceContact[4];
    internal float BoardTopViewportY => seatCamera.WorldToViewportPoint(tabletop.position
        + Vector3.ProjectOnPlane(seatCamera.transform.forward, Vector3.up).normalized
            * (BoardZ + .060f * BoardScale * Mathf.Cos(BoardTilt * Mathf.Deg2Rad))
        + Vector3.up * (.004f + 2f * BoardEdgeRise)).y;
    internal Vector3 TableCenter => tabletop.position;
    internal Vector3 PlayerLabelViewport(int seat)
    {
        if (faces[seat - 1].TryLabelPoint(seatCamera, out var point)) return point;
        var head = players[seat - 1].GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
        return seatCamera.WorldToViewportPoint(head.position + Vector3.up * .23f);
    }
    private Transform deck;
    private Vector3 DealOrigin => deck.TransformPoint(Vector3.up * .022f);
    private float animationUntil;
    private float nextPreviewAction;

    internal void SetViewportLeft(float left) => seatCamera.rect = new Rect(left, 0, 1f - left, 1);

    private void LateUpdate()
    {
        if (finale?.InGroup == true)
        {
            if (!groupStarted)
            {
                groupStarted = true;
                reactions.Release();
            }
            if (seatCamera.pixelHeight > 0) seatCamera.aspect = (float)seatCamera.pixelWidth / seatCamera.pixelHeight;
            atmosphere.LightPortrait(state.game.sessionWinner);
            finale.Frame(state);
            if (liveFinalHand && !tookPhoto && finale.Stage >= Play2MatchFinale.Beat.Portrait)
            {
                tookPhoto = true;
                tableAudio.TakePhoto();
            }
            if (firstPersonChair != null && players[viewer - 1].activeSelf) SetFirstPersonChair(null);
            return;
        }
        float standFraming = 0f;
        float peek = viewer > 0 ? grips[viewer - 1]?.PeekFocus ?? 0f : 0f;
        if (viewer > 0 && postures[viewer - 1] != null)
        {
            for (int i = 0; i < postures.Length; i++)
            {
                postures[i].Animate(i + 1 == viewer, chips.WaitToStand(i));
                if (i + 1 != viewer)
                {
                    postures[i].LeanIntoPush(chips.PushTime(i), tabletop.position);
                    standFraming = Mathf.Max(standFraming, postures[i].Weight);
                }
            }
            chips.FitPushToArms(grips);
            var eye = eyes[viewer - 1];
            float lift = postures[viewer - 1].Weight;
            Vector3 behind = -Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
            float pushTime = chips.PushTime(viewer - 1);
            float lean = grips[viewer - 1]?.ReachOverTable ?? 0f;
            if (pushTime >= 0f) lean = Mathf.Max(lean, Mathf.SmoothStep(0, 1, pushTime / .36f)
                * (1f - Mathf.SmoothStep(0, 1, (pushTime - .64f) / .58f)));
            seatCamera.transform.SetPositionAndRotation(eye.position + behind * (.12f - .21f * lean - .090f * peek)
                + Vector3.up * (.035f + .11f * lift + .045f * lean - .055f * peek),
                eye.rotation * Quaternion.Euler(3.5f * lift - lean + 6f * peek, 0f, 0f));
        }
        if (seatCamera.pixelHeight > 0) seatCamera.aspect = (float)seatCamera.pixelWidth / seatCamera.pixelHeight;
        float aspect = Mathf.Min(seatCamera.aspect, 16f / 9f);
        seatCamera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(
            Camera.VerticalToHorizontalFieldOfView(69f + standFraming * 4f - 8f * peek, 16f / 9f), aspect);
        for (int i = 0; i < grips.Length; i++)
        {
            float push = chips.PushTime(i);
            grips[i]?.UpdatePose(postures[i].Rise, push, push >= 0f ? chips.PushPalm(push, true) : default);
            if (i + 1 != viewer && players[i].activeInHierarchy)
                postures[i].RestEmptyArms(grips[i]?.RestingFaceUp == true && push < 0 && !reactions.PlayingReaction(i), tabletop.position);
        }
        if (Showdown is ShowdownBeat.Award or ShowdownBeat.Finished) LiftWinningCards();
        reactions?.Animate();
        chips?.UpdateHands();
        frontEnd.AlignTableLabels();
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
        finale = new Play2MatchFinale(players, eyes, tabletop, seatCamera);
        for (int i = 0; i < players.Length; i++)
        {
            var animator = players[i].GetComponent<Animator>();
            faces[i] = new Play2FaceContact(players[i].GetComponentInChildren<SkinnedMeshRenderer>(true),
                animator.GetBoneTransform(HumanBodyBones.Head));
            postures[i] = new Play2AllInPosture(animator);
            chairs[i] = GameObject.Find("Table Chair " + (i + 1))?.GetComponent<MeshRenderer>();
        }
        chips = new GameObject("Table pot").AddComponent<Play2TableChips>();
        chips.transform.SetParent(tabletop, false);
        tableAudio = gameObject.AddComponent<Play2TableAudio>();
        atmosphere = gameObject.AddComponent<Play2TableAtmosphere>();
        atmosphere.Initialize(tabletop, players, eyes);
        cardFaceMaterial = Resources.Load<Material>("TablePresentation/CardFace");
        boardFaceMaterial = Resources.Load<Material>("TablePresentation/CommunityCardFace");
        curvedPaper = Resources.Load<Material>("TablePresentation/PeekPaper");
        chips.Initialize(eyes, chipMaterial, players, faces, seatCamera.transform, tableAudio);
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

    internal bool WatchingPractice => session != null && session.WatchingPractice;
    internal void WatchRemainingPractice() => session?.WatchRemainingPractice();
    internal bool IsPractice => session != null && session.IsLocal;
    internal void NewPracticeAfterBust(string hand) => session?.NewPracticeAfterBust(hand);
    internal bool IsHost => session != null && session.IsHost;
    internal bool ActionPending => session != null && session.ActionPending;
    private int pendingBoard, waitingForSweep;
    internal bool WaitingToReveal => waitingForSweep > 0;
    internal bool IsDealing => pendingBoard > 0 || Time.time < animationUntil;
    internal bool IsBetting => chips.IsAnimating;
    internal void ShowPayout(int seat, long amount, bool animate)
    {
        chips.ShowPayout(seat, amount, animate);
        if (Showdown == ShowdownBeat.None) reactions.ShowWinner(seat, false, animate);
    }
    internal void PlayTurnSound() => tableAudio.YourTurn();
    internal void PlayResultSound() => tableAudio.HandEnd();
    internal void PlayAccusationSound(bool caught) => tableAudio.Accusation(caught);

    private void Update()
    {
        if (session == null) return;
        finale?.RestoreGaze();
        chips?.RestoreFlinch();
        foreach (var pose in postures) pose?.Restore();
        SetState(session.State);
        AdvanceShowdown();
        if (IsPractice && !IsDealing && Time.time >= nextPreviewAction
            && !frontEnd.WaitingForSpectatorRequest() && session.PlayPreviewOpponent())
            nextPreviewAction = Time.time + 0.65f;
        bool chatHasInput = frontEnd.CaptureChatInput();
        bool reactionOpen = !chatHasInput && !finale.InGroup && reactions.HandlePointer();
        frontEnd.Present(state, session.Status, chatHasInput || reactionOpen);
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (!chatHasInput && !IsDealing && state?.game?.hasResult != true && mouse?.leftButton.wasPressedThisFrame == true
            && UnityEngine.EventSystems.EventSystem.current?.IsPointerOverGameObject() != true
            && seatCamera.pixelRect.Contains(mouse.position.ReadValue())
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
        var game = state?.hasGame == true ? state.game : null;
        bool sameHand = game != null && previous?.hasGame == true && previous.sessionId == packet.sessionId
            && game.handId == previous.game.handId && viewerSeat == previous.viewerSeat;
        if (!sameHand)
        {
            finale.Reset();
            groupStarted = liveFinalHand = tookPhoto = false;
        }
        if (game?.isOver == true && sameHand && !previous.game.hasResult) liveFinalHand = true;
        if (!groupStarted)
        {
            SetFirstPersonChair(chairs[viewer - 1]);
            for (int i = 0; i < players.Length; i++)
                players[i].SetActive(i + 1 != viewer && (state == null || state.members.Any(m => m.seat == i + 1 && m.connected)));
        }
        bool showdown = game?.hasResult == true && game.result?.kind == (int)Poker.Foundation.HoldemResultKind.Showdown;
        if (!showdown)
        {
            Showdown = ShowdownBeat.None;
            openedHands = 0;
            revealOrder = Array.Empty<int>();
            awards = Array.Empty<Play2SettlementSummary.ShowdownAward>();
            AwardIndex = 0;
        }
        else if (!sameHand || !previous.game.hasResult)
        {
            Showdown = sameHand ? ShowdownBeat.Waiting : ShowdownBeat.Finished;
            beatUntil = Time.unscaledTime + ShowdownLeadIn;
            awards = Play2SettlementSummary.ShowdownAwards(game);
            revealOrder = Play2SettlementSummary.RevealOrder(packet);
            openedHands = sameHand ? 0 : revealOrder.Length;
            AwardIndex = 0;
        }
        atmosphere.Present(packet, Showdown, RevealingSeat, HighlightSeats);
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
            bool revealed = IsHandShown(seat.seat) && Play2SettlementSummary.IsPublicHand(seat);
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
                showFaces && game.board.Length == 0 && !game.hasResult, curvedPaper, tableAudio.CardSlide);
            if (folded) grips[player].Fold(false);
            else if (revealed) grips[player].Reveal(seat.visibleCards[0], seat.visibleCards[1], false);
            else animationUntil = Mathf.Max(animationUntil, Time.time + delay + Play2CardGrip.Duration(gap, showFaces && game.board.Length == 0 && !game.hasResult));
        }
        int first = board;
        while (board < game.board.Length)
        {
            StartCoroutine(DealBoardCard(game.board[board], board, first, sameHand || !game.hasResult));
            board++;
        }
        RefreshRequestMarkers();
        reactions.Present(packet);
        if (!sameHand && Showdown == ShowdownBeat.Finished) reactions.FinishShowdown(game, false);
    }

    private void AdvanceShowdown()
    {
        switch (Showdown)
        {
            case ShowdownBeat.Waiting:
                if (IsDealing || IsBetting)
                {
                    beatUntil = Time.unscaledTime + ShowdownLeadIn;
                    return;
                }
                if (Time.unscaledTime < beatUntil) return;
                Showdown = ShowdownBeat.Reveal;
                RevealNextHand();
                break;
            case ShowdownBeat.Reveal when Time.unscaledTime >= beatUntil:
                if (openedHands < revealOrder.Length)
                {
                    RevealNextHand();
                    break;
                }
                Showdown = ShowdownBeat.Award;
                beatUntil = Time.unscaledTime + 1.6f;
                ShowAwardWinners();
                break;
            case ShowdownBeat.Award when Time.unscaledTime >= beatUntil:
                if (AwardIndex + 1 < awards.Length)
                {
                    AwardIndex++;
                    ShowAwardWinners();
                    beatUntil = Time.unscaledTime + 1.45f;
                }
                else
                {
                    Showdown = ShowdownBeat.Finished;
                    reactions.FinishShowdown(state.game, true);
                    AwardIndex = 0;
                }
                break;
            default:
                return;
        }
        atmosphere.Present(state, Showdown, RevealingSeat, HighlightSeats);
    }

    private void ShowAwardWinners()
    {
        foreach (int seat in HighlightSeats) reactions.ShowWinner(seat, true, true);
    }

    private void RevealNextHand()
    {
        if (openedHands >= revealOrder.Length) { beatUntil = Time.unscaledTime; return; }
        int next = revealOrder[openedHands++];
        var seat = state.game.seats.First(s => s.seat == next);
        grips[next - 1]?.Reveal(seat.visibleCards[0], seat.visibleCards[1]);
        tableAudio.CardSlide();
        beatUntil = Time.unscaledTime + (openedHands == revealOrder.Length ? LastHandReadTime : BetweenHandReveals);
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
        card.transform.Find("Front").gameObject.SetActive(false);
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

    private IEnumerator DealBoardCard(int cardId, int index, int groupStart, bool animate)
    {
        if (animate)
        {
            int ticket = chips.RequestedSweep;
            pendingBoard++;
            // Each new authoritative group gets a reading beat, even if an all-in
            // delivers several streets before the previous group has landed.
            if (groupStart > 0)
                while (boardCards[groupStart - 1] == null || Time.time < boardLandedAt[groupStart - 1] + ReadBoardBetweenStreets)
                    yield return null;
            if (chips.FinishedSweep < ticket)
            {
                waitingForSweep++;
                while (chips.FinishedSweep < ticket) yield return null;
                waitingForSweep--;
            }
            float delay = (index - groupStart) * .11f;
            if (delay > 0f) yield return new WaitForSeconds(delay);
        }
        var card = Instantiate(cardPrefab, tabletop);
        card.transform.Find("Front").GetComponent<MeshRenderer>().sharedMaterial = boardFaceMaterial;
        cards.Add(card);
        SetCardValue(card, cardId);

        var pose = card.transform;
        Quaternion boardFacing = Quaternion.Euler(0f, seatCamera.transform.eulerAngles.y, 0f);
        // Rest the near edge on the felt and raise the far edge towards this seat.
        Quaternion shown = boardFacing * Quaternion.Euler(-BoardTilt, 0, 0);
        Vector3 end = boardFacing * new Vector3((index - 2) * .164f, .004f + BoardEdgeRise, BoardZ);
        pose.localScale = Vector3.one * BoardScale;
        if (animate)
        {
            tableAudio.CardSlide();
            Vector3 start = tabletop.InverseTransformPoint(DealOrigin);
            pose.localPosition = start;
            pose.localRotation = boardFacing * Quaternion.Euler(0f, 0f, 179.9f);

            var motion = AddDealMotion(card, start, end);
            motion.Initialization();
            motion.PlayFeedbacks();

            yield return new WaitForSeconds(.54f);
            var back = pose.localRotation;
            for (float t = 0; t < 1; t += Time.deltaTime / .27f)
            {
                pose.localRotation = Quaternion.Slerp(back, shown, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            pose.localRotation = shown;
            yield return new WaitForSeconds(BoardDealDuration - .81f);
        }
        pose.SetLocalPositionAndRotation(end, shown);
        boardCards[index] = card;
        boardLandedAt[index] = Time.time;
        if (animate) pendingBoard--;
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

    private void SetFirstPersonChair(MeshRenderer chair)
    {
        if (firstPersonChair == chair) return;
        if (firstPersonChair != null) firstPersonChair.shadowCastingMode = chairShadows;
        firstPersonChair = chair;
        if (chair == null) return;
        chairShadows = chair.shadowCastingMode;
        chair.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    }

    private void OnDestroy()
    {
        SetFirstPersonChair(null);
        reactions?.Release();
    }

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
        board = pendingBoard = waitingForSweep = 0;
        animationUntil = 0f;
    }

}
