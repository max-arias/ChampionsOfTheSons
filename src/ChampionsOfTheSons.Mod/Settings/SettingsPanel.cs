using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Audio;
using ChampionsOfTheSons.Mod.HostRules;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ChampionsOfTheSons.Mod.Settings;

/// <summary>
/// COS-104: the mod settings overlay. Every mod key with click-to-rebind (the next key pressed; Esc cancels), a warning when the key is
/// also a mod key or a game key, reset to defaults, and the mod sound volume. Further settings go below as more sections.
/// </summary>
internal sealed class SettingsPanel : ModalPanel
{
    public const string PanelId = "cots-mod-settings";
    private const float W = 1100, RowH = 48, RowTop = 112;

    private sealed class RowUi
    {
        public ModKey Key = null!;
        public SBgButtonOptions Button = null!;
        public SLabelOptions Warning = null!;
    }

    private readonly List<RowUi> _rows = new();
    private SLabelOptions _volume = null!;
    private SLabelOptions _message = null!;
    private int _openedFrame = -1;

    /// <summary>The key id waiting for a key press, or null.</summary>
    public string? Listening { get; private set; }

    public IReadOnlyList<string> RowIds => _rows.Select(r => r.Key.Id).ToList();

    private static float Height => RowTop + ModKeys.All.Count * RowH + 150;

    public void Build()
    {
        float h = Height;
        BuildShell(PanelId, W, h, UiText.T("ui.settings.title", "Champions settings"));
        Window.Add(Button("X", 24, Close, "#2B333E", "#4A3036").TopLeft(W - 64, 16).Size(40, 40));

        Window.Add(Label(UiText.T("ui.settings.section.keys", "Keys"), 22, UiTheme.Gold, TextAlignmentOptions.MidlineLeft).TopLeft(28, 66).Size(300, 36));
        var hint = Label(UiText.T("ui.settings.keys_hint", "Click a key, then press the new one. Hold Alt, Ctrl or Shift with it for a combo. Esc cancels."), 16,
            UiTheme.TextDim, TextAlignmentOptions.MidlineLeft);
        hint.TextObject.enableAutoSizing = true;
        hint.TextObject.fontSizeMin = 11;
        hint.TextObject.fontSizeMax = 16;
        Window.Add(hint.TopLeft(340, 60).Size(W - 380, 48));

        for (int i = 0; i < ModKeys.All.Count; i++)
        {
            var key = ModKeys.All[i];
            float y = RowTop + i * RowH;
            Window.Add(Label(ModKeys.Label(key), 19, UiTheme.Text, TextAlignmentOptions.MidlineLeft).TopLeft(28, y).Size(300, 40));
            var row = new RowUi { Key = key };
            row.Button = Button("", 20, () => StartListening(key.Id));
            row.Button.TopLeft(340, y).Size(200, 40);
            Window.Add(row.Button);
            row.Warning = Label("", 16, UiTheme.Bad, TextAlignmentOptions.MidlineLeft);
            Window.Add(row.Warning.TopLeft(556, y).Size(W - 584, 40));
            _rows.Add(row);
        }

        float by = RowTop + ModKeys.All.Count * RowH + 6;
        Window.Add(Button(UiText.T("ui.settings.reset", "Reset keys to defaults"), 18, ResetKeys).TopLeft(340, by).Size(280, 40));
        _message = Label("", 16, UiTheme.TextDim, TextAlignmentOptions.MidlineLeft);
        Window.Add(_message.TopLeft(636, by).Size(W - 664, 40));

        float sy = by + 64;
        Window.Add(Label(UiText.T("ui.settings.section.sound", "Sound"), 22, UiTheme.Gold, TextAlignmentOptions.MidlineLeft).TopLeft(28, sy).Size(300, 36));
        Window.Add(Label(UiText.T("ui.settings.volume", "Mod sound volume"), 19, UiTheme.Text, TextAlignmentOptions.MidlineLeft).TopLeft(160, sy).Size(180, 36));
        Window.Add(Button("-", 22, () => StepVolume(-0.1f)).TopLeft(340, sy).Size(44, 36));
        _volume = Label("", 20, UiTheme.Gold, TextAlignmentOptions.Midline);
        Window.Add(_volume.TopLeft(388, sy).Size(108, 36));
        Window.Add(Button("+", 22, () => StepVolume(0.1f)).TopLeft(500, sy).Size(44, 36));
        Window.Add(Label(UiText.T("ui.settings.volume_hint", "On top of the game's sound effects volume."), 16, UiTheme.TextDim, TextAlignmentOptions.MidlineLeft)
            .TopLeft(556, sy).Size(W - 584, 36));
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else
        {
            ModKeys.RefreshGameKeys();
            _message.Text("");
            _openedFrame = Time.frameCount;
            Open();
        }
    }

    public new void Close()
    {
        Listening = null;
        base.Close();
    }

    /// <summary>Esc or the pad's back button: stops listening first, then closes.</summary>
    public void Back()
    {
        if (Listening != null) CancelListening();
        else Close();
    }

    public void StartListening(string id)
    {
        Listening = id;
        Refresh();
    }

    public void CancelListening()
    {
        Listening = null;
        Refresh();
    }

    /// <summary>The key press that ends listening (from <see cref="Tick"/> or a self-test).</summary>
    public void Accept(string key)
    {
        if (Listening == null) return;
        var id = Listening;
        Listening = null;
        ModKeys.Rebind(id, key);
        Refresh();
    }

    public void ResetKeys()
    {
        Listening = null;
        ModKeys.ResetAll();
        _message.Text(UiText.T("ui.settings.reset_done", "Keys reset to defaults."));
        Refresh();
    }

    public void StepVolume(float delta)
    {
        float v = Mathf.Clamp01(Mathf.Round((Config.SoundVolume.Value + delta) * 10f) / 10f);
        Config.SoundVolume.Value = v;
        ModKeys.Save();
        Refresh();
        ModAudio.Play(SoundCues.UiClick);
    }

    /// <summary>Each frame while open: Esc and the next key press while listening.</summary>
    public void Tick()
    {
        if (!IsOpen) return;
        var kb = Keyboard.current;
        if (Listening == null)
        {
            // The keybind only fires while the player is controllable, which a panel prevents: close on its key here.
            bool own = ModKeys.PressedThisFrame(Config.SettingsKey);
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (own && Time.frameCount != _openedFrame)) Close();
            return;
        }
        if (kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame) { CancelListening(); return; }
        foreach (Key k in Enum.GetValues(typeof(Key)))
        {
            // Alt, Ctrl and Shift alone do not end listening: held with the next key they make a combo (COS-133).
            if (k is Key.None or Key.Escape or Key.IMESelected or Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
                or Key.LeftShift or Key.RightShift or Key.LeftMeta or Key.RightMeta) continue;
            KeyControl? c;
            try { c = kb[k]; } catch { continue; }
            if (c == null || !c.wasPressedThisFrame) continue;
            Accept(Combine(c.name, kb.ctrlKey.isPressed, kb.shiftKey.isPressed, kb.altKey.isPressed));
            return;
        }
    }

    /// <summary>The key pressed while listening, with the modifiers held at that moment ("alt+1"); no modifiers gives the plain key.</summary>
    public static string Combine(string key, bool ctrl, bool shift, bool alt) => new KeyCombo(key, ctrl, shift, alt).ToString();

    public string WarningText(string id) => _rows.FirstOrDefault(r => r.Key.Id == id)?.Warning.TextObject.text ?? "";

    public string ButtonText(string id) => _rows.FirstOrDefault(r => r.Key.Id == id)?.Button.TextObject.text ?? "";

    protected override void Refresh()
    {
        foreach (var row in _rows)
        {
            string id = row.Key.Id;
            if (Listening == id)
            {
                row.Button.Text($"<color={UiTheme.Gold}>{UiText.T("ui.settings.press_key", "Press a key...")}</color>");
                row.Warning.Text("");
                continue;
            }
            string value = ModKeys.Value(id);
            row.Button.Text(KeyNames.Short(value));
            var parts = new List<string>();
            var mod = ModKeys.ModClashes(id);
            if (mod.Count > 0) parts.Add(string.Format(UiText.T("ui.settings.clash_mod", "Also used by {0}"), string.Join(", ", mod.Select(ModKeys.Label))));
            var game = ModKeys.GameClashes(value, id);
            if (game.Count > 0) parts.Add(string.Format(UiText.T("ui.settings.clash_game", "Game key: {0}"), string.Join(", ", game.Take(3))));
            row.Warning.Text(string.Join("  ", parts));
        }
        _volume.Text($"{Mathf.RoundToInt(Config.SoundVolume.Value * 100)}%");
    }
}
