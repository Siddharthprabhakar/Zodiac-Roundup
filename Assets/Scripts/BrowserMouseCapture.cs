using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Centralizes desktop and WebGL pointer-lock behavior.
/// The browser requires a user gesture, so gameplay captures on a click in the game view.
/// Escape releases the pointer and the next gameplay click captures it again.
/// </summary>
public static class BrowserMouseCapture
{
    private static bool configured;

    public static bool UpdateCapture(bool gameplayAllowsCapture)
    {
        ConfigureOnce();

        if (!gameplayAllowsCapture)
        {
            Release();
            return false;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Release();
            return false;
        }

        if (Cursor.lockState != CursorLockMode.Locked &&
            Input.GetMouseButtonDown(0) &&
            !IsPointerOverInteractiveUi())
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        return Cursor.lockState == CursorLockMode.Locked;
    }

    public static void Release()
    {
        if (Cursor.lockState != CursorLockMode.None)
            Cursor.lockState = CursorLockMode.None;

        Cursor.visible = true;
    }

    private static void ConfigureOnce()
    {
        if (configured) return;
        configured = true;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Keep Unity's cursor state synchronized when the browser releases pointer lock with Escape.
        WebGLInput.stickyCursorLock = false;
#endif
    }

    private static bool IsPointerOverInteractiveUi()
    {
        if (EventSystem.current == null)
            return false;

        var pointerData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };
        var raycastResults = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, raycastResults);

        foreach (var result in raycastResults)
        {
            // Full-screen decorative Images should not prevent gameplay pointer lock.
            // Only actual controls such as Button, Toggle and TMP input fields do.
            if (result.gameObject.GetComponentInParent<Selectable>() != null)
                return true;
        }

        return false;
    }
}
