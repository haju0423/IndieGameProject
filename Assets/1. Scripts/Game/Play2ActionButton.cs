using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

internal sealed class Play2ActionButton : Button
{
    // Return opens the table chat even when a bet button still has keyboard focus.
    public override void OnSubmit(BaseEventData eventData)
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed)) return;
        base.OnSubmit(eventData);
    }
}
