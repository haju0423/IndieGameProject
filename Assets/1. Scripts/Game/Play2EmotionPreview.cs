using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class Play2EmotionPreview : MonoBehaviour
{
    [SerializeField] private Transform tabletop;
    [SerializeField] private Transform playerView;
    [SerializeField] private Transform localSeat;
    [SerializeField] private GameObject[] characters;
    [SerializeField] private CardVisual cardPrefab;
    [SerializeField] private TMP_FontAsset font;

    private static readonly (string Label, int State)[] Emotions =
    {
        ("중지", Animator.StringToHash("Base Layer.SittingMiddleFingerLeft")),
        ("가리키기", Animator.StringToHash("Base Layer.SittingPointLeft")),
        ("손짓", Animator.StringToHash("Base Layer.SittingGestureLeft")),
        ("질문", Animator.StringToHash("Base Layer.SittingAskQuestionLeft")),
        ("엄지척", Animator.StringToHash("Base Layer.SittingThumbsUp")),
        ("불신", Animator.StringToHash("Base Layer.SittingDisbelief")),
        ("승리", Animator.StringToHash("Base Layer.SittingVictory"))
    };

    private const float DeadZone = 44f;
    private static readonly Vector3 LeftArmViewOffset = new Vector3(-.115f, .23f, .43f);
    private static readonly int SittingIdle = Animator.StringToHash("Base Layer.SittingIdle");
    private readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[Emotions.Length];
    private Canvas canvas;
    private GameObject wheelOverlay;
    private Play2EmotionWheelGraphic wheel;
    private RectTransform pointer;
    private TextMeshProUGUI currentCharacter, centerLabel, centerHint;
    private CardVisual firstCard, secondCard, ownFirst, ownSecond;
    private PreviewCardGrip grip, localGrip;
    private GameObject localActor;
    private Mesh leftArmMesh;
    private SkinnedMeshRenderer leftArm;
    private Animator localAnimator, opponentAnimator;
    private InputAction middleButton;
    private Vector2 direction, lastMousePosition;
    private int selected = -1;
    private int opponentIndex = -1, localIndex = -1;
    private bool previewLocal = true;
    private bool choosing, cursorWasVisible, releasePending;
    private Mouse wheelMouse;

    private void Awake()
    {
        BuildInterface();
        middleButton = new InputAction(type: InputActionType.Button, binding: "<Mouse>/middleButton");
        middleButton.started += context =>
        {
            if (context.control.device is Mouse mouse) OpenWheel(mouse);
        };
        middleButton.canceled += _ => releasePending = choosing;
    }

    private void OnEnable() => middleButton.Enable();

    private void Start()
    {
        ownFirst = CreateCard(new Card(1, Suit.Spade), true);
        ownSecond = CreateCard(new Card(13, Suit.Heart), true);
        firstCard = CreateCard(new Card(11, Suit.Club), false);
        secondCard = CreateCard(new Card(7, Suit.Heart), false);
        SelectOpponent(0);
        SelectLocal(2);
        UpdateTargetLabel();
    }

    private CardVisual CreateCard(Card card, bool own)
    {
        var visual = Instantiate(cardPrefab, tabletop);
        visual.transform.localScale = Vector3.one * (own ? Play2RightHandGrip.PreviewViewCardScale : Play2RightHandGrip.PreviewOpponentCardScale);
        visual.SetCard(card);
        visual.transform.Find("Front").gameObject.SetActive(own);
        return visual;
    }

    private void SelectCharacter(int index)
    {
        if (index >= characters.Length) return;
        if (previewLocal) SelectLocal(index);
        else SelectOpponent(index);
        UpdateTargetLabel();
    }

    private void SelectOpponent(int index)
    {
        if (index == opponentIndex) return;
        grip?.Release();
        for (int i = 0; i < characters.Length; i++) characters[i].SetActive(i == index);
        opponentIndex = index;
        var character = characters[index];
        opponentAnimator = character.GetComponent<Animator>();
        SeatCharacter(opponentAnimator);
        grip = new PreviewCardGrip(character, null, tabletop, firstCard, secondCard);
        grip.PoseHeldCards();
    }

    private void SelectLocal(int index)
    {
        if (index == localIndex) return;
        ClearLocal();
        localIndex = index;
        var source = characters[index];
        localActor = Instantiate(source, localSeat, false);
        localActor.name = "First Person Left Arm";
        localActor.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        localActor.transform.localScale = source.transform.lossyScale;
        localActor.SetActive(true);
        localAnimator = localActor.GetComponent<Animator>();
        SeatCharacter(localAnimator);

        // Build the right arm from the full mesh before masking the left arm view.
        localGrip = new PreviewCardGrip(localActor, playerView, tabletop, ownFirst, ownSecond);
        localGrip.PoseHeldCards();
        var skin = localActor.GetComponentInChildren<SkinnedMeshRenderer>();
        var upper = System.Array.Find(skin.bones, bone => bone.name == "mixamorig:LeftArm");
        leftArmMesh = CopyArmMesh(skin, upper);
        leftArm = skin;
        localActor.transform.position += playerView.TransformVector(LeftArmViewOffset);
        leftArm.enabled = false;
    }

    private void ClearLocal()
    {
        localGrip?.Release();
        localGrip = null;
        if (localActor != null)
        {
            localActor.SetActive(false);
            Destroy(localActor);
        }
        if (leftArmMesh != null) Destroy(leftArmMesh);
    }

    private static void SeatCharacter(Animator animator)
    {
        animator.SetBool("IsSeated", true);
        animator.Play(SittingIdle, 0, 0f);
        animator.Update(0f);
    }

    private static Mesh CopyArmMesh(SkinnedMeshRenderer skin, Transform upperArm)
    {
        var mesh = Instantiate(skin.sharedMesh);
        skin.sharedMesh = mesh;
        Play2CardGrip.ShowArmOnly(skin, upperArm);
        return mesh;
    }

    private void UpdateTargetLabel()
    {
        string side = previewLocal ? "내 왼손" : "맞은편";
        currentCharacter.text = side + "  ·  " + characters[previewLocal ? localIndex : opponentIndex].name;
    }

    private void LateUpdate()
    {
        localGrip?.PoseHeldCards();
        grip?.PoseHeldCards();
        if (leftArm != null)
        {
            bool activeGesture = localAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash != SittingIdle ||
                (localAnimator.IsInTransition(0) && localAnimator.GetNextAnimatorStateInfo(0).fullPathHash != SittingIdle);
            leftArm.enabled = previewLocal && activeGesture;
        }
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.escapeKey.wasPressedThisFrame && choosing)
            {
                CloseWheel(false);
                return;
            }

            if (!choosing)
            {
                if (keyboard.tabKey.wasPressedThisFrame)
                {
                    previewLocal = !previewLocal;
                    UpdateTargetLabel();
                }
                else if (keyboard.digit1Key.wasPressedThisFrame) SelectCharacter(0);
                else if (keyboard.digit2Key.wasPressedThisFrame) SelectCharacter(1);
                else if (keyboard.digit3Key.wasPressedThisFrame) SelectCharacter(2);
                else if (keyboard.digit4Key.wasPressedThisFrame) SelectCharacter(3);
            }
        }

        if (!choosing) return;
        var mouse = wheelMouse;
        if (mouse == null || !mouse.added)
        {
            CloseWheel(false);
            return;
        }

        Vector2 position = mouse.position.ReadValue();
        Vector2 movement = mouse.delta.ReadValue();
        if (movement == Vector2.zero) movement = position - lastMousePosition;
        lastMousePosition = position;
        UpdateSelection(movement / canvas.scaleFactor);
        if (releasePending) CloseWheel(true);
        else if (!mouse.middleButton.isPressed) CloseWheel(false);
    }

    private void OpenWheel(Mouse mouse)
    {
        if (choosing) return;
        choosing = true;
        wheelMouse = mouse;
        lastMousePosition = mouse.position.ReadValue();
        releasePending = false;
        cursorWasVisible = Cursor.visible;
        Cursor.visible = false;
        direction = Vector2.zero;
        wheelOverlay.SetActive(true);
        SetSelection(-1);
        UpdateSelection(Vector2.zero);
    }

    private void UpdateSelection(Vector2 movement)
    {
        direction = Vector2.ClampMagnitude(direction + movement, Play2EmotionWheelGraphic.Radius - 14f);
        pointer.anchoredPosition = direction;
        pointer.gameObject.SetActive(direction.sqrMagnitude >= DeadZone * DeadZone);
        if (direction.sqrMagnitude < DeadZone * DeadZone)
        {
            SetSelection(-1);
            return;
        }

        float angle = Mathf.Repeat(Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg, 360f);
        float sector = 360f / Emotions.Length;
        SetSelection(Mathf.FloorToInt((angle + sector * .5f) / sector) % Emotions.Length);
    }

    private void SetSelection(int index)
    {
        selected = index;
        wheel.SetSelection(Emotions.Length, index);
        for (int i = 0; i < labels.Length; i++)
            labels[i].color = i == index ? Play2UGui.Dark : Play2UGui.Cream;
        centerLabel.text = index < 0 ? "이모트 선택" : Emotions[index].Label;
        centerHint.text = index < 0 ? "마우스를 움직이세요" : "휠 버튼을 떼면 재생";
    }

    private void CloseWheel(bool confirm)
    {
        if (!choosing) return;
        choosing = false;
        wheelMouse = null;
        releasePending = false;
        wheelOverlay.SetActive(false);
        Cursor.visible = cursorWasVisible;
        var animator = previewLocal ? localAnimator : opponentAnimator;
        if (!confirm || selected < 0 || animator == null) return;

        int state = Emotions[selected].State;
        if (animator.GetCurrentAnimatorStateInfo(0).fullPathHash == state)
            animator.Play(state, 0, 0f);
        else
            animator.CrossFadeInFixedTime(state, .12f, 0, 0f);
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) CloseWheel(false);
    }

    private void OnDisable()
    {
        CloseWheel(false);
        middleButton.Disable();
    }

    private void OnDestroy()
    {
        grip?.Release();
        ClearLocal();
        middleButton.Dispose();
    }

    private sealed class PreviewCardGrip
    {
        private readonly Play2RightHandGrip fingers;
        private readonly Transform view, tabletop;
        private readonly CardVisual first, second;
        private readonly GameObject viewModel;
        private readonly Mesh viewMesh;
        private readonly Quaternion wristRest;
        private readonly Vector3 outward;

        internal PreviewCardGrip(GameObject actor, Transform view, Transform tabletop, CardVisual first, CardVisual second)
        {
            this.view = view;
            this.tabletop = tabletop;
            this.first = first;
            this.second = second;
            outward = Vector3.ProjectOnPlane(actor.transform.position - tabletop.position, Vector3.up).normalized;

            if (view != null)
            {
                Vector3 scale = actor.transform.lossyScale;
                viewModel = Instantiate(actor, view, false);
                viewModel.name = "First Person Right Arm";
                viewModel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                viewModel.transform.localScale = scale;
                viewModel.GetComponent<Animator>().enabled = false;
                actor = viewModel;
            }

            fingers = new Play2RightHandGrip(actor);
            wristRest = fingers.Wrist.localRotation;
            if (viewModel != null)
                viewMesh = CopyArmMesh(viewModel.GetComponentInChildren<SkinnedMeshRenderer>(), fingers.UpperArm);
        }

        internal void PoseHeldCards()
        {
            Transform wrist = fingers.Wrist;
            Pose contact = fingers.Contact;
            Vector3 point;
            Quaternion facing;
            if (viewModel != null)
            {
                point = view.TransformPoint(Play2RightHandGrip.PreviewViewPoint);
                facing = view.rotation * Play2RightHandGrip.PreviewViewFacing;
            }
            else
            {
                Vector3 lateral = Vector3.Cross(outward, Vector3.up);
                point = tabletop.position + outward * .60f + lateral * .14f + Vector3.up * .11f;
                Vector3 normal = outward + Vector3.up * .58f;
                facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.up, normal), normal);
            }

            Quaternion wristRotation = facing * Quaternion.Inverse(contact.rotation);
            Vector3 wristTarget = point - wristRotation * Vector3.Scale(contact.position, wrist.lossyScale);
            if (viewModel != null)
            {
                Quaternion neutralForearm = wristRotation * Quaternion.Inverse(wristRest);
                Vector3 neutralDirection = neutralForearm * wrist.localPosition.normalized;
                Vector3 towardGrip = wristTarget - view.TransformPoint(new Vector3(.34f, -.40f, .20f));
                Vector3 armDirection = Vector3.RotateTowards(neutralDirection, towardGrip.normalized, 40f * Mathf.Deg2Rad, 0f);
                Quaternion armRotation = Quaternion.FromToRotation(neutralDirection, armDirection) * neutralForearm;
                Transform body = viewModel.transform;
                body.rotation = armRotation * Quaternion.Inverse(fingers.Forearm.rotation) * body.rotation;
                body.position += wristTarget - wrist.position;
            }
            else
            {
                Play2TableArm.Reach(fingers.UpperArm, fingers.Forearm, wrist, wristTarget);
            }

            wrist.rotation = wristRotation;
            fingers.SetClosure(1f);
            point = wrist.TransformPoint(contact.position);
            PoseCard(first.transform, point, facing, -1);
            PoseCard(second.transform, point, facing, 1);
        }

        private static void PoseCard(Transform card, Vector3 point, Quaternion facing, int side)
        {
            var pose = Play2RightHandGrip.FanCardPose(point, facing, Play2RightHandGrip.PreviewSpread, side, card.localScale.z, 1f);
            card.SetPositionAndRotation(pose.position, pose.rotation);
        }

        internal void Release()
        {
            if (viewModel != null)
            {
                viewModel.SetActive(false);
                Destroy(viewModel);
                Destroy(viewMesh);
            }
            else
            {
                fingers.SetClosure(0f);
            }
        }
    }

    private void BuildInterface()
    {
        var root = new GameObject("Emotion Preview UI", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;

        var header = Play2UGui.Frame("Preview Header", root.transform, new Vector2(22f, -22f), new Vector2(225f, 83f), null);
        Play2UGui.Anchor(header, Vector2.up);
        Play2UGui.Text("Title", header, "이모트 테스트", new Vector2(0f, 16f), new Vector2(202f, 32f), 23f,
            Play2UGui.Cream, font, style: FontStyles.Bold);
        currentCharacter = Play2UGui.Text("Character", header, "", new Vector2(0f, -17f), new Vector2(200f, 27f), 18f,
            Play2UGui.Gold, font);

        var controls = Play2UGui.Frame("Controls", root.transform, new Vector2(-22f, -22f), new Vector2(392f, 70f), null);
        Play2UGui.Anchor(controls, Vector2.one);
        Play2UGui.Text("Wheel Controls", controls, "휠 버튼 누르기  →  방향 선택  →  떼서 재생",
            new Vector2(0f, 13f), new Vector2(375f, 30f), 17f, Play2UGui.Cream, font);
        Play2UGui.Text("Character Controls", controls, "Tab 대상 바꾸기   ·   1~4 캐릭터 바꾸기",
            new Vector2(0f, -14f), new Vector2(375f, 24f), 15f, Play2UGui.Gold, font);

        var overlay = Play2UGui.Fill("Emotion Selection", root.transform);
        wheelOverlay = overlay.gameObject;
        Play2UGui.Image(overlay, Play2UGui.Hex("07120F49"));
        float size = (Play2EmotionWheelGraphic.Radius + 4f) * 2f;
        var rim = Play2UGui.Rect("Wheel", overlay, Vector2.zero, new Vector2(size, size));
        wheel = rim.gameObject.AddComponent<Play2EmotionWheelGraphic>();
        wheel.raycastTarget = false;
        for (int i = 0; i < Emotions.Length; i++)
        {
            var position = Play2EmotionWheelGraphic.Direction(i * 360f / Emotions.Length) * Play2EmotionWheelGraphic.LabelRadius;
            labels[i] = Play2UGui.Text("Emotion " + i, rim, Emotions[i].Label, position, new Vector2(112f, 32f), 21f,
                Play2UGui.Cream, font, style: FontStyles.Bold);
        }

        centerLabel = Play2UGui.Text("Selection", rim, "", new Vector2(0f, 17f), new Vector2(202f, 42f), 27f,
            Play2UGui.Cream, font, style: FontStyles.Bold);
        centerHint = Play2UGui.Text("Confirm", rim, "", new Vector2(0f, -20f), new Vector2(205f, 28f), 15f, Play2UGui.Gold, font);
        pointer = Play2UGui.Diamond("Mouse Direction", rim, Vector2.zero, Play2UGui.Cream);
        wheelOverlay.SetActive(false);
    }
}
