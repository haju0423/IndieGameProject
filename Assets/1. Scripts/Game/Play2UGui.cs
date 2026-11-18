using TMPro;
using UnityEngine;
using UnityEngine.Events;

internal static class Play2UGui
{
    internal static readonly Color Cream = Hex("F6F0E2"), Ink = Hex("1A1A1A"), Orange = Hex("E8833A"),
        Red = Hex("B3261E"), Dark = Hex("101B16");

    internal static readonly Color Pine = Hex("10251BEC"), PineInset = Hex("31493CBE"),
        Trim = Hex("BFA77C88"), Gold = Hex("F2CD8B"), Coral = Hex("F18673"), Accent = Hex("D54B40");
    internal const float HudMargin = 14f;
    internal const float SidePanelTop = 70f;
    internal const float SidePanelWidth = 258f;
    internal const float SidePanelMargin = 8f;
    internal const float ActionAreaHeight = 161f;
    internal const float HandSummaryHeight = 88f;

    internal static Color Hex(string value)
    {
        ColorUtility.TryParseHtmlString("#" + value, out var color);
        return color;
    }

    internal static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    internal static void Anchor(RectTransform rect, Vector2 anchor)
    {
        var position = rect.anchoredPosition;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
    }

    internal static RectTransform Fill(string name, Transform parent)
    {
        var rect = Rect(name, parent, Vector2.zero, Vector2.zero);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    internal static UnityEngine.UI.Image Image(RectTransform rect, Color color, bool raycast = false)
    {
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    internal static RectTransform Panel(string name, Transform parent, Vector2 position, Vector2 size, Color color, bool raycast = false)
    {
        var rect = Rect(name, parent, position, size);
        Image(rect, color, raycast);
        return rect;
    }

    internal static RectTransform Frame(string name, Transform parent, Vector2 position, Vector2 size, Texture2D felt)
    {
        var rect = Panel(name, parent, position, size, Pine);
        ApplyFrame(rect.GetComponent<UnityEngine.UI.Image>(), felt);
        return rect;
    }

    internal static void ApplyFrame(UnityEngine.UI.Graphic image, Texture2D felt)
    {
        var parent = image.rectTransform;
        image.color = felt != null ? new Color(Pine.r, Pine.g, Pine.b, .46f) : Pine;
        if (felt != null)
        {
            var surface = Fill("Felt Surface", parent);
            surface.SetAsFirstSibling();
            var texture = surface.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            texture.texture = felt;
            texture.color = new Color(1, 1, 1, .95f);
            texture.raycastTarget = false;
        }
        AddFrameEdge("Top Edge", parent, Vector2.up, Vector2.one, new Vector2(0, -1), Vector2.zero);
        AddFrameEdge("Bottom Edge", parent, Vector2.zero, Vector2.right, Vector2.zero, new Vector2(0, 1));
        AddFrameEdge("Left Edge", parent, Vector2.zero, Vector2.up, new Vector2(0, 1), new Vector2(1, -1));
        AddFrameEdge("Right Edge", parent, Vector2.right, Vector2.one, new Vector2(-1, 1), new Vector2(0, -1));
    }

    private static void AddFrameEdge(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        var edge = Panel(name, parent, Vector2.zero, Vector2.zero, Trim);
        edge.anchorMin = anchorMin;
        edge.anchorMax = anchorMax;
        edge.offsetMin = offsetMin;
        edge.offsetMax = offsetMax;
    }

    internal static RectTransform Paper(string name, Transform parent, Vector2 position, Vector2 size, Texture2D texture, Color? tint = null)
    {
        var rect = Rect(name, parent, position, size);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        image.texture = texture;
        image.color = tint ?? Cream;
        image.raycastTarget = false;
        return rect;
    }

    internal static RectTransform Diamond(string name, Transform parent, Vector2 position, Color color)
    {
        var rect = Panel(name, parent, position, new Vector2(7, 7), color);
        rect.localRotation = Quaternion.Euler(0, 0, 45);
        return rect;
    }

    internal static TextMeshProUGUI Text(string name, Transform parent, string text, Vector2 position, Vector2 size,
        float fontSize, Color color, TMP_FontAsset font, TextAlignmentOptions alignment = TextAlignmentOptions.Center,
        FontStyles style = FontStyles.Normal)
    {
        var rect = Rect(name, parent, position, size);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = alignment;
        label.fontStyle = style;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    internal static UnityEngine.UI.Button Button(string name, Transform parent, string text, Vector2 position, Vector2 size,
        Color background, Color foreground, TMP_FontAsset font, UnityAction action, float fontSize = 20)
    {
        var rect = Panel(name, parent, position, size, background, true);
        var button = rect.gameObject.AddComponent<Play2ActionButton>();
        button.targetGraphic = rect.GetComponent<UnityEngine.UI.Image>();
        var colors = button.colors;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1);
        colors.pressedColor = new Color(.8f, .8f, .8f, 1);
        colors.disabledColor = new Color(.55f, .55f, .55f, .7f);
        button.colors = colors;
        if (action != null) button.onClick.AddListener(action);
        Text("Label", rect, text, Vector2.zero, size - new Vector2(12, 6), fontSize, foreground, font,
            TextAlignmentOptions.Center, FontStyles.Bold);
        return button;
    }

}
