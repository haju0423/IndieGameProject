using Poker.Application;
using UnityEngine;

public sealed class Play2FrontEnd : MonoBehaviour
{
    [SerializeField] private Play2ChatCanvas chatCanvas;

    private Play2TableHud hud;

    public void Initialize(Play2TablePreview table)
    {
        chatCanvas.Initialize(table);
        var canvasObject = new GameObject("Play2 Game HUD", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
        chatCanvas.GetComponent<UnityEngine.UI.CanvasScaler>().screenMatchMode = scaler.screenMatchMode;
        chatCanvas.GetComponent<Canvas>().sortingOrder = 10;
        hud = new Play2TableHud(canvasObject.transform, table, chatCanvas.Font, this);
    }

    public void Present(HoldemRoomPacket packet, string status)
    {
        hud.Present(packet, status);
        chatCanvas.Present(packet);
        if (!hud.CanOpenChat) chatCanvas.DiscardFocusRequest();
        else if (chatCanvas.HandleEnter()) return;
        hud.HandleKeys(chatCanvas.IsTyping);
    }
}
