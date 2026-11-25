using RedLoader;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.TitleLogo;

/// <summary>
/// Shows the "Champions of the Sons" logo wherever the game shows its own: the title menu and the loading screen.
/// The loading screen stacks three images of the logo (dark backing, red logo, orange wipe line), so each game texture
/// maps to ours: the red logo to <c>logo/cots_logo.png</c>, the white silhouettes to <c>logo/cots_logo_white.png</c>.
/// Our logo is wider than the game's, so each image's rect is widened to keep its height.
/// With the launch option <c>--cots-dump-logo</c> it instead logs the logo objects and writes the game's textures to
/// <c>%TEMP%\cots-ui\logo\</c> (the input of tools/logo/compose_logo.py).
/// </summary>
public sealed class TitleLogoFeature : IModFeature
{
    private const string RedSource = "SonsOfTheForestLogoGrunge";
    private const string WhiteSource = "SonsOfTheForestLogoGrungeWhite"; // also matches ...White1pxBoundry

    /// <summary>Size of our logo on the title menu relative to the game's logo box.</summary>
    private const float TitleMenuScale = 0.85f;

    private static readonly bool Dump = Environment.GetCommandLineArgs().Any(a => a == "--cots-dump-logo");
    private static readonly string DumpDir = Path.Combine(Path.GetTempPath(), "cots-ui", "logo");

    /// <summary>Images swapped so far, by object path (for the self-test).</summary>
    public static readonly HashSet<string> Swapped = new();

    private static Sprite? _red, _white;
    private static bool _loadTried;
    private readonly HashSet<int> _seen = new();
    private float _nextScan;

    /// <summary>Self-test runs only: screenshots of the swapped screens (title menu, loading screen) to %TEMP%\cots-ui\.</summary>
    private static readonly List<(float At, string Name)> _captures = new();
    private static readonly HashSet<string> _captured = new();

    public void Update(float dt, bool inWorld)
    {
        for (int i = _captures.Count - 1; i >= 0; i--)
        {
            if (Time.realtimeSinceStartup < _captures[i].At) continue;
            var name = _captures[i].Name;
            _captures.RemoveAt(i);
            if (!inWorld && _captured.Add(name))
                RLog.Msg($"Champions of the Sons: title logo: screenshot {UI.UiCapture.SaveScreen(name) ?? "failed: " + UI.UiCapture.LastError}");
        }
        if (inWorld || Time.realtimeSinceStartup < _nextScan) return;
        _nextScan = Time.realtimeSinceStartup + 0.25f;
        if (Dump) { DumpAll(); return; }
        if (!LoadSprites()) return;
        foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (img == null || !img.gameObject.scene.IsValid()) continue;
            var s = img.sprite;
            if (s == null || s == _red || s == _white) continue;
            var name = s.name;
            if (name == RedSource) Swap(img, _red!);
            else if (name.StartsWith(WhiteSource, StringComparison.Ordinal)) Swap(img, _white!);
        }
    }

    private static void Swap(Image img, Sprite ours)
    {
        var rt = img.rectTransform;
        float oldW = rt.rect.width, h = rt.rect.height;
        // The height the game's logo is drawn at (preserveAspect fits it into the rect); ours is drawn at the same height.
        var old = img.sprite;
        float shown = Math.Min(h, oldW * old.rect.height / old.rect.width);
        img.sprite = ours;
        img.preserveAspect = true;
        // Widen to our aspect at that height, so ours shows as tall as the game's. Not on the title menu (pinned to the
        // right edge): the menu buttons sit right up to the logo's box, so there it stays in the box and shows a little smaller.
        float newW = shown * ours.rect.width / ours.rect.height;
        if (newW > oldW + 0.5f && rt.anchorMin.x < 0.99f) rt.sizeDelta += new Vector2(newW - oldW, 0);
        if (rt.anchorMin.x >= 0.99f)
        {
            // Title menu: our big SONS reads heavier than the game's logo, so show it smaller, keeping the bottom-right corner in place.
            var d = rt.sizeDelta * (1 - TitleMenuScale);
            rt.sizeDelta -= d;
            rt.anchoredPosition += new Vector2(d.x * (1 - rt.pivot.x), -d.y * rt.pivot.y);
        }
        var path = PathOf(img.transform);
        Swapped.Add(path);
        if (SelfTest.Enabled)
        {
            var shot = path.StartsWith("TitleMenu/") ? "title-logo-menu" : "title-logo-loading";
            foreach (var delay in new[] { 8f, 20f }) _captures.Add((Time.realtimeSinceStartup + delay, $"{shot}-{delay:0}s"));
        }
        RLog.Msg($"Champions of the Sons: title logo: replaced {path} ({oldW:0}x{h:0} -> {rt.rect.width:0}x{rt.rect.height:0})");
    }

    private static bool LoadSprites()
    {
        if (_loadTried) return _red != null && _white != null;
        _loadTried = true;
        _red = LoadSprite("logo/cots_logo.png");
        _white = LoadSprite("logo/cots_logo_white.png");
        return _red != null && _white != null;
    }

    private static Sprite? LoadSprite(string resource)
    {
        try
        {
            using var stream = typeof(TitleLogoFeature).Assembly.GetManifestResourceStream(resource);
            if (stream == null) { RLog.Warning($"Champions of the Sons: title logo: {resource} not embedded"); return null; }
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, ms.ToArray())) { RLog.Warning($"Champions of the Sons: title logo: {resource} unreadable"); return null; }
            tex.name = "ChampionsOfTheSonsLogo";
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "ChampionsOfTheSonsLogo";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: title logo: {resource}: {e.Message}");
            return null;
        }
    }

    public void RegisterSelfTests()
    {
        SelfTest.Add(0f, "title logo", () =>
        {
            SelfTest.Check("title logo: art embedded and loaded", _red != null && _white != null);
            SelfTest.Check("title logo: title-menu logo replaced", Swapped.Any(p => p.StartsWith("TitleMenu/")), string.Join(", ", Swapped));
            SelfTest.Check("title logo: loading-screen logo layers replaced", Swapped.Count(p => p.StartsWith("TitleGui/")) >= 3, string.Join(", ", Swapped));
        });
    }

    private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

    private void DumpAll()
    {
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t == null || !t.gameObject.scene.IsValid() || !t.name.Contains("Logo", StringComparison.OrdinalIgnoreCase)) continue;
            if (!_seen.Add(t.GetInstanceID())) continue;
            try { DumpObject(t); }
            catch (Exception e) { RLog.Warning($"TitleLogo dump {t.name}: {e}"); }
        }
    }

    private static void DumpObject(Transform t)
    {
        var img = t.GetComponent<Image>();
        if (img == null || img.sprite == null) return;
        Directory.CreateDirectory(DumpDir);
        var rt = img.rectTransform;
        var s = img.sprite;
        RLog.Msg($"TitleLogo: '{PathOf(t)}' scene={t.gameObject.scene.name} active={t.gameObject.activeInHierarchy} rect={rt.rect} "
            + $"anchors={rt.anchorMin}-{rt.anchorMax} pivot={rt.pivot} sprite={s.name} tex={s.texture?.width}x{s.texture?.height} color={img.color} type={img.type}");
        Save(s.texture, $"{t.name}__{s.name}");
    }

    /// <summary>Copies any texture (readable or not) through a render texture and writes it as PNG.</summary>
    private static void Save(Texture? tex, string name)
    {
        if (tex == null) return;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;
        var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
        copy.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var file = Path.Combine(DumpDir, string.Concat(name.Split(Path.GetInvalidFileNameChars())) + ".png");
        File.WriteAllBytes(file, ImageConversion.EncodeToPNG(copy));
        UnityEngine.Object.Destroy(copy);
        RLog.Msg($"TitleLogo:   saved {file}");
    }
}
