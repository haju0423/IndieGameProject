using UnityEngine;

public sealed class Play2TitleTable : MonoBehaviour
{
    [SerializeField] private Transform tabletop;
    [SerializeField] private GameObject[] players;
    [SerializeField] private CardVisual cardPrefab;

    private const float Cycle = 12f;
    private readonly CardVisual[] community = new CardVisual[5];
    private readonly CardVisual[] hands = new CardVisual[4];
    private Quaternion[] idleRotations;
    private Vector3[] idlePositions;

    private Transform[] upperArms, forearms, rightHands;
    private Play2FingerRig[] fingerRigs;
    private float started;

    private void Start()
    {
        upperArms = new Transform[players.Length];
        forearms = new Transform[players.Length];
        rightHands = new Transform[players.Length];
        fingerRigs = new Play2FingerRig[players.Length];
        idleRotations = new Quaternion[players.Length];
        idlePositions = new Vector3[players.Length];
        for (int i = 0; i < players.Length; i++)
        {
            idleRotations[i] = players[i].transform.localRotation;
            idlePositions[i] = players[i].transform.localPosition;
            if (players[i].TryGetComponent<Animator>(out var animator) && animator.isHuman)
            {
                upperArms[i] = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                forearms[i] = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
                rightHands[i] = animator.GetBoneTransform(HumanBodyBones.RightHand);
                fingerRigs[i] = Play2FingerRig.Attach(animator);
            }
        }

        for (int i = 0; i < community.Length; i++) community[i] = CreateCard(1.15f);
        for (int i = 0; i < hands.Length; i++)
        {
            hands[i] = CreateCard(1.05f);
            hands[i].transform.Find("Front").gameObject.SetActive(false);
        }

        SetCommunityCards();
        started = Time.unscaledTime;
    }

    private void LateUpdate() => Pose((Time.unscaledTime - started) % Cycle);

    private CardVisual CreateCard(float scale)
    {
        var card = Instantiate(cardPrefab, tabletop);
        card.transform.localScale = Vector3.one * scale;
        card.gameObject.SetActive(false);
        return card;
    }

    private void SetCommunityCards()
    {
        int[] ranks = { 1, 7, 11, 4, 13 };
        Suit[] suits = { Suit.Diamond, Suit.Club, Suit.Heart, Suit.Spade, Suit.Diamond };
        for (int i = 0; i < community.Length; i++)
            community[i].SetCard(new Card(ranks[i], suits[i]));
    }

    private void Pose(float time)
    {
        for (int i = 0; i < community.Length; i++)
        {
            float reveal = i < 3 ? 3.7f + i * 0.23f : 6.2f + (i - 3) * 1.8f;
            float slide = Ease((time - 2.8f - i * 0.16f) / 0.5f);
            float flip = Ease((time - reveal) / 0.35f);
            var card = community[i];
            card.gameObject.SetActive(slide > 0f && time < 11.2f);
            card.transform.localPosition = Vector3.Lerp(new Vector3(0.32f, 0.017f, 0.19f),
                new Vector3(-0.26f + i * 0.13f, 0.012f + Mathf.Sin(flip * Mathf.PI) * 0.07f, 0.02f), slide);
            card.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(180f, 0f, flip));
        }

        for (int i = 0; i < players.Length; i++)
        {
            Transform player = players[i].transform;
            float turn = Pulse(time, 1.2f + i * 0.7f, 1.45f) + Pulse(time, 8.55f + i * 0.34f, 1.5f);
            float breathing = Mathf.Sin(time * 2.1f + i * 1.7f);
            player.localPosition = idlePositions[i] + (idleRotations[i] * Vector3.forward) * (turn * 0.045f);
            player.localRotation = idleRotations[i] * Quaternion.Euler(turn * 8f, breathing * 3f, breathing * 1.5f);

            Vector3 outward = Vector3.ProjectOnPlane(tabletop.InverseTransformPoint(player.position), Vector3.up).normalized;
            float deal = Ease((time - 0.8f - i * 0.18f) / 0.5f);
            float lift = Pulse(time, 5.15f + i * 0.31f, 1.1f);
            fingerRigs[i]?.SetGrip(lift);
            var card = hands[i];
            card.gameObject.SetActive(deal > 0f && time < 11.2f);
            card.transform.localPosition = Vector3.Lerp(Vector3.up * 0.07f, outward * 0.60f + Vector3.up * 0.016f, deal);
            if (lift > 0.001f && upperArms[i] != null && forearms[i] != null && rightHands[i] != null)
            {
                var hand = rightHands[i];
                Vector3 target = tabletop.TransformPoint(outward * 0.73f + Vector3.up * 0.17f) + player.right * 0.10f;
                Play2TableArm.Reach(upperArms[i], forearms[i], hand, Vector3.Lerp(hand.position, target, lift));
                card.transform.position = Vector3.Lerp(card.transform.position,
                    hand.position + player.forward * 0.06f + Vector3.up * 0.035f, lift);
            }
            card.transform.localRotation = Quaternion.LookRotation(-outward) * Quaternion.Euler(lift * -58f, 0f, 180f);
        }
    }

    private static float Ease(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

    private static float Pulse(float time, float start, float duration)
    {
        float progress = Mathf.Clamp01((time - start) / duration);
        float wave = Mathf.Sin(Mathf.PI * progress);
        return wave * wave;
    }
}
