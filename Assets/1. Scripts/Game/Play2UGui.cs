using TMPro;
using UnityEngine;
using UnityEngine.Events;

internal static class Play2UGui
{
    internal static readonly Color Cream = Hex("F6F0E2"), Ink = Hex("1A1A1A"), Orange = Hex("E8833A"),
        Red = Hex("B3261E"), Dark = Hex("101B16");

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
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
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
