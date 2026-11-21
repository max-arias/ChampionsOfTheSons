using ChampionsOfTheSons.Mod.Hud;
using ChampionsOfTheSons.Mod.Icons;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.ActionBar;

/// <summary>
/// One slot of the action bar (COS-151): hide inset, icon (or name text), cooldown ash, bone frame, glow, key plate and a count.
/// Draw order is the build order, so the bone frame overlaps the icon's edges and the plate sits on the frame.
/// </summary>
internal sealed class BarSlot
{
    public readonly SContainerOptions Root, Inset, Cooldown, Glow, Plate;
    public readonly SImageOptions Icon;
    public readonly SLabelOptions Name, Center, Key, Count;
    private readonly Image? _glowImg;
    private string _glowShown = "";
    private float _cooldownShown = -1;

    public BarSlot(SContainerOptions parent, float x, float y, float size, float plateW)
    {
        Root = SContainer.TopLeft(x, y).Size(size, size);
        parent.Add(Root);
        float frameScale = size / 128f * 0.9f;

        Inset = HudArt.Piece(HudArt.SlotInset, 1, UiTheme.Hide).Anchor(AnchorType.Fill).Margin(size * 0.11f);
        Root.Add(Inset);
        Icon = IconView.Create().Anchor(AnchorType.Fill).Margin(size * 0.06f);
        Inset.Add(Icon);
        Name = HudKit.Label("", 12, UiTheme.Bone, TextAlignmentOptions.Center, wrap: true).Anchor(AnchorType.Fill);
        Name.TextObject.margin = new Vector4(2, 2, 2, 8);
        Name.TextObject.enableAutoSizing = true;
        Name.TextObject.fontSizeMin = 7;
        Name.TextObject.fontSizeMax = 12;
        Name.TextObject.overflowMode = TextOverflowModes.Truncate;
        Inset.Add(Name);
        // The cooldown drains downward: anchored to the top, its height is the remaining fraction.
        Cooldown = SContainer.Background(HudKit.C(UiTheme.Ash), EBackground.None);
        Inset.Add(Cooldown);

        Root.Add(HudArt.Piece(HudArt.SlotFrame, frameScale, UiTheme.BoneDark, UiTheme.BoneTint).Anchor(AnchorType.Fill));
        Glow = HudArt.Piece(HudArt.SlotGlow, frameScale, "#00000000").Anchor(AnchorType.Fill).Margin(-size * 0.04f);
        Root.Add(Glow);
        _glowImg = Glow.Root.GetComponent<Image>();
        Glow.Active(false);

        Center = HudKit.Label("", Mathf.RoundToInt(size * 0.32f), UiTheme.Bone, TextAlignmentOptions.Center).Anchor(AnchorType.Fill);
        Outline(Center, 0.25f);
        Root.Add(Center);

        float plateH = 15;
        Plate = HudArt.Piece(HudArt.KeyPlate, 0.6f, UiTheme.Bone).TopLeft((size - plateW) / 2, size - plateH + 5).Size(plateW, plateH);
        Root.Add(Plate);
        Key = HudKit.Label("", 11, UiTheme.Ink, TextAlignmentOptions.Center).Anchor(AnchorType.Fill);
        Key.TextObject.fontStyle = FontStyles.Bold;
        Plate.Add(Key);

        Count = HudKit.Label("", 13, UiTheme.Bone, TextAlignmentOptions.TopRight).Anchor(AnchorType.Fill);
        Count.TextObject.margin = new Vector4(2, size * 0.1f, size * 0.12f, 2);
        Count.TextObject.fontStyle = FontStyles.Bold;
        Outline(Count, 0.3f);
        Root.Add(Count);

        SetCooldown(0);
        HudKit.NoRaycast(Root.Root);
    }

    public static void Outline(SLabelOptions l, float width)
    {
        l.TextObject.outlineWidth = width;
        l.TextObject.outlineColor = new Color32(12, 8, 5, 255);
    }

    /// <summary>A content icon (spells) by id; false when there is none, so the caller shows the name.</summary>
    public bool ShowContentIcon(string? id) => IconView.Show(Icon, id);

    /// <summary>A game texture (the game's item icons).</summary>
    public bool ShowTexture(Texture? tex)
    {
        Icon.ImageObject.texture = tex;
        Icon.Active(tex != null);
        return tex != null;
    }

    public void SetIconColor(string hex) => Icon.ImageObject.color = HudKit.C(hex);

    /// <summary>Glow tint (#RRGGBBAA) or null for none.</summary>
    public void SetGlow(string? hex)
    {
        string v = hex ?? "";
        if (v == _glowShown) return;
        _glowShown = v;
        Glow.Active(hex != null);
        if (hex != null && _glowImg != null) _glowImg.color = HudKit.C(hex);
    }

    public string GlowHex => _glowShown;

    public void SetCooldown(float remainingFraction)
    {
        remainingFraction = Mathf.Clamp01(remainingFraction);
        if (Mathf.Approximately(remainingFraction, _cooldownShown)) return;
        _cooldownShown = remainingFraction;
        var rt = Cooldown.RectTransform;
        rt.anchorMin = new Vector2(0, 1 - remainingFraction);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Cooldown.Root.SetActive(remainingFraction > 0.001f);
    }

    public float CooldownFraction => Cooldown.Root.activeSelf ? 1 - Cooldown.RectTransform.anchorMin.y : 0;

    public void SetPlateDim(bool dim) => Plate.Root.GetComponent<Image>().color = HudKit.C(dim ? "#FFFFFF80" : "#FFFFFF");
}
