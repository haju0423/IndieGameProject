using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class Play2ShowdownCards
{
    private sealed class Face
    {
        internal Transform card;
        internal Renderer front;
        internal Vector3 rest;
        internal Color paper;
        internal int id;
        internal float lift;
        internal bool shaded;
    }

    private readonly List<Face> faces = new(13);
    private readonly MaterialPropertyBlock tint = new();
    internal bool Ready => faces.Count > 0;

    internal void Add(CardVisual card, int id)
    {
        var front = card.transform.Find("Front").GetComponent<Renderer>();
        faces.Add(new Face { card = card.transform, front = front, rest = card.transform.localPosition,
            paper = front.sharedMaterial.GetColor("_BaseColor"), id = id });
    }

    internal void Animate(int[] bestFive)
    {
        if (bestFive?.Length != 5) return;
        foreach (var face in faces)
        {
            float target = Array.IndexOf(bestFive, face.id) >= 0 ? 1f : 0f;
            if (face.shaded && face.lift == target) continue;
            float next = Mathf.MoveTowards(face.lift, target, Time.deltaTime / .24f);
            float offset = Mathf.SmoothStep(0, 1, next) * .036f;
            face.card.localPosition = face.rest + Vector3.up * offset;
            face.front.GetPropertyBlock(tint);
            tint.SetColor("_BaseColor", Color.Lerp(face.paper * new Color(.68f, .68f, .68f, 1f), face.paper, next));
            face.front.SetPropertyBlock(tint);
            face.lift = next;
            face.shaded = true;
        }
    }

    internal void Clear() => faces.Clear();
}
