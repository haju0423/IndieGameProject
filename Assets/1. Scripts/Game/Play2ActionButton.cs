using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

internal sealed class Play2ActionButton : Button
{
    internal string Intent { get; set; }
    private string pressedIntent;
    private bool trackingPress;

    public override void OnPointerDown(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Left)
        {
            pressedIntent = Intent;
            trackingPress = true;
        }
        base.OnPointerDown(data);
    }

    public override void OnPointerClick(PointerEventData data)
    {
        bool unchanged = !trackingPress || pressedIntent == Intent;
        trackingPress = false;
        if (unchanged) base.OnPointerClick(data);
    }

    // Return opens the table chat even when a bet button still has keyboard focus.
    public override void OnSubmit(BaseEventData eventData)
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed)) return;
        base.OnSubmit(eventData);
    }
}
