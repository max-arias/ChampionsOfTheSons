using UnityEngine;

namespace ChampionsOfTheSons.Mod.Pad;

/// <summary>
/// The pointer the panels read for hover, click, right-click and drag. With a mouse it is the real mouse; while the gamepad
/// drives the UI it is a virtual pointer parked on the focused control (so tooltips and slot hit-tests work unchanged) with
/// clicks made by the pad. Moving the real mouse hands control back.
/// </summary>
internal static class PadPointer
{
    private static Vector2 _pos;
    private static Vector2 _mouseAtMove;
    private static int _downFrame = -10, _button;

    public static bool Active { get; private set; }

    public static Vector2 Position => Active ? _pos : (Vector2)Input.mousePosition;

    public static bool ButtonDown(int button) => Input.GetMouseButtonDown(button) || (Active && Time.frameCount == _downFrame && _button == button);

    public static bool Button(int button) => Input.GetMouseButton(button) || (Active && Time.frameCount == _downFrame && _button == button);

    public static bool ButtonUp(int button) => Input.GetMouseButtonUp(button) || (Active && Time.frameCount == _downFrame + 1 && _button == button);

    /// <summary>Parks the virtual pointer at a screen point (pixels, origin bottom-left) and takes over from the mouse.</summary>
    public static void MoveTo(Vector2 screen)
    {
        Active = true;
        _pos = screen;
        _mouseAtMove = Input.mousePosition;
    }

    /// <summary>A click with the virtual pointer: the button is down next frame and up the frame after, whichever panel polls first.</summary>
    public static void Click(int button)
    {
        if (!Active) return;
        _button = button;
        _downFrame = Time.frameCount + 1;
    }

    /// <summary>Hands control back to the mouse when it moves or clicks.</summary>
    public static void Tick()
    {
        if (!Active) return;
        if (Vector2.Distance(Input.mousePosition, _mouseAtMove) > 4f || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) Release();
    }

    public static void Release() => Active = false;
}
