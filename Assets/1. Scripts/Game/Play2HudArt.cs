using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

internal static class Play2HudArt
{
    private static readonly Dictionary<string, Sprite> Sprites = new();

    internal static void Apply(UnityEngine.UI.Image image, string asset)
    {
        if (!Sprites.TryGetValue(asset, out var sprite))
        {
            sprite = Resources.Load<Sprite>("InGameUI/" + asset);
            Sprites.Add(asset, sprite);
            if (sprite == null) Debug.LogError($"Missing InGame UI sprite: {asset}");
        }

        image.sprite = sprite;
        image.type = sprite != null && sprite.border != Vector4.zero
            ? UnityEngine.UI.Image.Type.Sliced
            : UnityEngine.UI.Image.Type.Simple;
        image.color = Color.white;
    }

    internal static UnityEngine.UI.Image Skin(UnityEngine.UI.Graphic graphic, string asset)
    {
        if (graphic is UnityEngine.UI.Image image)
        {
            Apply(image, asset);
            return image;
        }

        graphic.color = Color.clear;
        var background = Play2UGui.Fill("Sprite Background", graphic.rectTransform);
        background.SetAsFirstSibling();
        var spriteImage = Play2UGui.Image(background, Color.white);
        Apply(spriteImage, asset);
        return spriteImage;
    }

    internal static RectTransform Plate(string name, Transform parent, Vector2 position, Vector2 size,
        string asset, bool raycast = false)
    {
        var rect = Play2UGui.Rect(name, parent, position, size);
        Apply(Play2UGui.Image(rect, Color.white, raycast), asset);
        return rect;
    }

    internal static UnityEngine.UI.Button Button(string name, Transform parent, string label, Vector2 position,
        Vector2 size, string asset, Color textColor, TMP_FontAsset font, UnityAction click, float fontSize = 16)
    {
        var button = Play2UGui.Button(name, parent, label, position, size, Color.white, textColor, font, click, fontSize);
        Apply((UnityEngine.UI.Image)button.targetGraphic, asset);
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        return button;
    }

    internal static UnityEngine.UI.Button Clickable(RectTransform plate, UnityAction click)
    {
        var image = plate.GetComponent<UnityEngine.UI.Image>();
        image.raycastTarget = true;
        var button = plate.gameObject.AddComponent<Play2ActionButton>();
        button.targetGraphic = image;
        button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        button.onClick.AddListener(click);
        return button;
    }

    internal static void Keycap(Transform parent, TMP_FontAsset font, string key, Vector2 position, float width = 38)
    {
        var cap = Plate(key + " Keycap", parent, position, new Vector2(width, 23), "Keycap");
        Play2UGui.Text("Key", cap, key, Vector2.zero, cap.sizeDelta, 13, Play2UGui.Ink, font, style: FontStyles.Bold);
    }

    internal static void Fit(TextMeshProUGUI label, float min)
    {
        label.enableAutoSizing = true;
        label.fontSizeMin = min;
        label.fontSizeMax = label.fontSize;
    }
}
