using System.Collections;
using System;
using Poker.Application;
using Poker.Foundation;
using UnityEngine;

// The authoritative pot is displayed in the HUD. A bounded stack gives it a place on the table.
internal sealed class Play2TableChips : MonoBehaviour
{
    private readonly Transform[] pile = new Transform[7];
    private readonly Transform[] moving = new Transform[4];
    private readonly Coroutine[] flights = new Coroutine[4];
    private static readonly Vector3 PotPosition = new Vector3(-.27f, .014f, .27f);
    private Transform[] eyes;
    private Material material;

    public void Initialize(Transform[] seatEyes, Material chipMaterial)
    {
        eyes = seatEyes;
        material = chipMaterial;
        for (int i = 0; i < pile.Length; i++)
        {
            pile[i] = MakeChip("Pot chip", i % 2 == 0 ? Play2UGui.Hex("C05C42") : Play2UGui.Hex("405D71"));
            pile[i].localPosition = PotPosition + Vector3.up * (i * .009f);
        }
        for (int i = 0; i < moving.Length; i++)
            moving[i] = MakeChip("Bet from seat " + (i + 1), Play2UGui.Hex("C05C42"));
    }

    private Transform MakeChip(string label, Color color)
    {
        var chip = new GameObject(label).transform;
        chip.SetParent(transform, false);
        Disc(chip, "Edge", 0f, .079f, .0045f, color);
        Disc(chip, "Ivory inset", .0045f, .060f, .0006f, Play2UGui.Cream);
        Disc(chip, "Face", .0053f, .048f, .0005f, color);
        chip.gameObject.SetActive(false);
        return chip;
    }

    private void Disc(Transform chip, string label, float y, float size, float height, Color color)
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = label;
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(chip, false);
        disc.transform.localScale = new Vector3(size, height, size);
        disc.transform.localPosition = Vector3.up * y;
        var tint = new MaterialPropertyBlock();
        tint.SetColor("_BaseColor", color);
        tint.SetColor("_Color", color);
        var renderer = disc.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.SetPropertyBlock(tint);
    }

    public void Present(HoldemRoomPacket previous, HoldemRoomPacket next)
    {
        var game = next?.hasGame == true ? next.game : null;
        var old = previous?.hasGame == true ? previous.game : null;
        int count = game == null || game.hasResult || game.pot <= 0 ? 0
            : Mathf.Clamp(1 + (int)Mathf.Log(game.pot, 2f), 1, pile.Length);
        for (int i = 0; i < pile.Length; i++) pile[i].gameObject.SetActive(i < count);
        if (game == null || old == null || game.handId != old.handId || game.hasResult)
        {
            StopAllCoroutines();
            for (int i = 0; i < moving.Length; i++) { moving[i].gameObject.SetActive(false); flights[i] = null; }
            return;
        }

        var history = next.history;
        if (history?.entries == null || previous.history?.entries == null) return;
        long previousCount = previous.history.omittedCount + previous.history.entries.Length;
        int first = (int)Math.Max(0, Math.Min(history.entries.Length, previousCount - history.omittedCount));
        for (int i = first; i < history.entries.Length; i++)
        {
            var entry = history.entries[i];
            if (!entry.hasAction || entry.amount <= 0 || entry.seat < 1 || entry.seat > moving.Length) continue;
            if (entry.action != (int)BettingActionKind.Call && entry.action != (int)BettingActionKind.BetTo
                && entry.action != (int)BettingActionKind.RaiseTo) continue;
            int index = entry.seat - 1;
            if (flights[index] != null) StopCoroutine(flights[index]);
            flights[index] = StartCoroutine(Throw(index, count));
        }
    }

    private IEnumerator Throw(int index, int count)
    {
        var chip = moving[index];
        Vector3 seat = transform.InverseTransformPoint(eyes[index].position);
        var start = new Vector3(seat.x, 0, seat.z).normalized * .54f + Vector3.up * .075f;
        var end = PotPosition + Vector3.up * (count * .009f);
        chip.gameObject.SetActive(true);
        for (float t = 0; t < 1; t += Time.deltaTime / .38f)
        {
            chip.localPosition = Vector3.Lerp(start, end, t) + Vector3.up * (.13f * 4f * t * (1 - t));
            chip.localRotation = Quaternion.Euler(32f * Mathf.Sin(t * Mathf.PI), 150f * t, 0);
            yield return null;
        }
        chip.gameObject.SetActive(false);
        flights[index] = null;
    }
}
