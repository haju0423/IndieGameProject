using TMPro;
using UnityEngine;

internal static class Play2AudioVideoSettings
{
    private const string VolumeKey = "Play2.MasterVolume";
    private const string FullscreenKey = "Play2.Fullscreen";

    internal static void Restore()
    {
        AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, 1f));
        if (PlayerPrefs.HasKey(FullscreenKey)) Screen.fullScreen = PlayerPrefs.GetInt(FullscreenKey) == 1;
    }

    internal static void SetVolume(float value)
    {
        AudioListener.volume = value;
        PlayerPrefs.SetFloat(VolumeKey, value);
    }

    internal static void SetFullscreen(bool value)
    {
        Screen.fullScreen = value;
        PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
    }

    internal static bool Fullscreen => PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;
}

internal sealed class Play2InGameSettings
{
    private readonly RectTransform overlay;
    private readonly UnityEngine.UI.Slider volume;
    private readonly UnityEngine.UI.Image fullscreenIcon;
    private readonly TextMeshProUGUI volumeValue;
    private bool fullscreen;

    internal bool IsOpen => overlay.gameObject.activeSelf;

    internal Play2InGameSettings(RectTransform parent, TMP_FontAsset font)
    {
        overlay = Play2UGui.Fill("Settings Overlay", parent);
        Play2HudArt.Apply(Play2UGui.Image(overlay, Color.white, true), "Scrim");
        var panel = Play2HudArt.Plate("Settings Card", overlay, Vector2.zero, new Vector2(524, 268), "CreamPanel", true);
        Play2HudArt.Plate("Settings Diamond", panel, new Vector2(-33, 96), new Vector2(17, 20), "Diamond");
        Play2UGui.Text("Heading", panel, "설정", new Vector2(13, 96), new Vector2(64, 32), 27,
            Play2UGui.Ink, font, style: FontStyles.Bold);
        Play2HudArt.Plate("Header Rule", panel, new Vector2(0, 67), new Vector2(470, 2), "Divider");
        AddLabel("전체 음량", 30);
        AddLabel("전체 화면", -28);
        Play2HudArt.Plate("Volume Rule", panel, new Vector2(0, 0), new Vector2(470, 1), "Divider")
            .GetComponent<UnityEngine.UI.Image>().color = new Color(1, 1, 1, .4f);
        volumeValue = Play2UGui.Text("Volume Percentage", panel, "", new Vector2(205, 29), new Vector2(66, 30), 20,
            Play2UGui.Ink, font, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
        volumeValue.textWrappingMode = TextWrappingModes.NoWrap;
        Play2HudArt.Fit(volumeValue, 15);

        var slider = Play2UGui.Rect("Volume Slider", panel, new Vector2(35, 30), new Vector2(260, 36));
        Play2HudArt.Plate("Track", slider, Vector2.zero, new Vector2(260, 13), "Track");
        var fillArea = Play2UGui.Fill("Volume Fill Area", slider);
        fillArea.offsetMin = new Vector2(0, 11);
        fillArea.offsetMax = new Vector2(0, -11);
        var fill = Play2HudArt.Plate("Volume Fill", fillArea, Vector2.zero, Vector2.zero, "TrackFill");
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        var handleArea = Play2UGui.Fill("Volume Handle Area", slider);
        handleArea.offsetMin = new Vector2(0, 3.5f);
        handleArea.offsetMax = new Vector2(0, -3.5f);
        var handle = Play2HudArt.Plate("Volume Knob", handleArea, Vector2.zero, new Vector2(29, 0), "Knob");
        volume = slider.gameObject.AddComponent<UnityEngine.UI.Slider>();
        volume.fillRect = fill;
        volume.handleRect = handle;
        volume.targetGraphic = handle.GetComponent<UnityEngine.UI.Image>();
        volume.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        Play2UGui.Image(slider, Color.clear, true);
        volume.onValueChanged.AddListener(value =>
        {
            Play2AudioVideoSettings.SetVolume(value);
            UpdateVolumeLabel(value);
        });

        var screen = Play2HudArt.Button("Fullscreen Switch", panel, "", new Vector2(207, -28), new Vector2(61, 32),
            "SwitchOff", Play2UGui.Ink, font, ToggleFullscreen);
        fullscreenIcon = (UnityEngine.UI.Image)screen.targetGraphic;
        Play2HudArt.Plate("Footer Rule", panel, new Vector2(0, -56), new Vector2(470, 1), "Divider")
            .GetComponent<UnityEngine.UI.Image>().color = new Color(1, 1, 1, .4f);
        Play2HudArt.Keycap(panel, font, "Esc", new Vector2(97, -94), 44);
        Play2HudArt.Button("Close Settings", panel, "닫기", new Vector2(177, -94), new Vector2(120, 42),
            "HeaderBadge", Play2UGui.Cream, font, Close, 20);
        overlay.gameObject.SetActive(false);

        void AddLabel(string label, float y)
        {
            Play2UGui.Text(label, panel, label, new Vector2(-174, y), new Vector2(120, 34), 19,
                Play2UGui.Ink, font, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
        }
    }

    internal void Open()
    {
        fullscreen = Play2AudioVideoSettings.Fullscreen;
        UpdateFullscreenIcon();
        volume.SetValueWithoutNotify(AudioListener.volume);
        UpdateVolumeLabel(volume.value);
        overlay.SetAsLastSibling();
        overlay.gameObject.SetActive(true);
    }

    internal void Close()
    {
        overlay.gameObject.SetActive(false);
        PlayerPrefs.Save();
    }

    private void ToggleFullscreen()
    {
        fullscreen = !fullscreen;
        Play2AudioVideoSettings.SetFullscreen(fullscreen);
        UpdateFullscreenIcon();
    }

    private void UpdateFullscreenIcon() => Play2HudArt.Apply(fullscreenIcon, fullscreen ? "SwitchOn" : "SwitchOff");
    private void UpdateVolumeLabel(float value) => volumeValue.text = $"{Mathf.RoundToInt(value * 100f)}%";
}
