using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>
/// Screenshots of the SUI canvas into <c>%TEMP%\cots-ui\</c>, for the self-test only. They grab the back buffer, so they need the
/// windowed run (<c>tools/run-selftest.ps1</c> without <c>-Headless</c>): in batchmode there is no back buffer and no file is written.
/// </summary>
internal static class UiCapture
{
    public static readonly string Dir = Path.Combine(Path.GetTempPath(), "cots-ui");

    private const int W = 1920, H = 1080;

    public static string LastError { get; private set; } = "";

    public static void Prepare()
    {
        // Headless runs take no screenshots, so they leave the last windowed run's pictures alone.
        if (!Application.isBatchMode)
        {
            Directory.CreateDirectory(Dir);
            foreach (var old in Directory.GetFiles(Dir, "*.png")) File.Delete(old);
            // The windowed self-test run opens a tiny window; the canvas scales with it, so make it full HD before shooting.
            Screen.SetResolution(W, H, false);
        }
        DismissDialogs();
        SelfTest.Log($"capture: screen {Screen.width}x{Screen.height}, batchmode={Application.isBatchMode}, canvas mode {SUI.SUI.SUIViewport.renderMode}");
    }

    public static void LogScreen()
    {
        DismissDialogs();
        SelfTest.Log($"capture: screen is now {Screen.width}x{Screen.height}, canvas scale {SUI.SUI.SUIViewport.scaleFactor:0.###}");
    }

    /// <summary>The test save is an old early-access save, so the game greets it with a modal that covers everything: click its OK.</summary>
    public static void DismissDialogs()
    {
        foreach (var b in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (!b.gameObject.activeInHierarchy) continue;
            var label = b.GetComponentInChildren<TMP_Text>();
            if (label == null || !string.Equals(label.text?.Trim(), "OK", StringComparison.OrdinalIgnoreCase)) continue;
            SelfTest.Log($"capture: clicking OK on dialog button '{b.name}' under '{b.transform.parent?.name}'");
            b.onClick.Invoke();
        }
    }

    public static string? SaveScreen(string name)
    {
        var path = Path.Combine(Dir, name + ".png");
        try
        {
            ScreenCapture.CaptureScreenshot(path);
            return path;
        }
        catch (Exception e)
        {
            LastError = e.GetType().Name + ": " + e.Message;
            return null;
        }
    }

}
