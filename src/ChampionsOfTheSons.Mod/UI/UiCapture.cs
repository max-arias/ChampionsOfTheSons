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

    /// <summary>Loads the PNG and checks that it is neither missing nor a flat color.</summary>
    public static (bool Ok, string Detail) Verify(string path)
    {
        try
        {
            if (!File.Exists(path)) return (false, "file missing");
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) return (false, "not a PNG");
            var px = tex.GetPixels32();
            double sum = 0, sum2 = 0;
            int n = 0;
            for (int i = 0; i < px.Length; i += 53)
            {
                double l = (px[i].r + px[i].g + px[i].b) / 3.0;
                sum += l;
                sum2 += l * l;
                n++;
            }
            double mean = sum / n, std = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
            int w = tex.width, h = tex.height;
            UnityEngine.Object.Destroy(tex);
            return (std > 15, $"{w}x{h} brightness mean {mean:0} stddev {std:0}");
        }
        catch (Exception e)
        {
            return (false, e.GetType().Name + ": " + e.Message);
        }
    }
}
