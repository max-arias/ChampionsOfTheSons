using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Spells;
using TheForest.Utils;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ChampionsOfTheSons.Mod.Settings;

/// <summary>COS-104 and COS-133 in game: the overlay lists every key, rebinds live, binds, shows and clears combos, Esc cancels, clashes
/// are flagged (combos too), reset restores the defaults, and real key events: Alt+1 casts and holds the game's hotkey 1 back, a plain 1
/// does not cast. The player's own key choices are put back at the end (the cfg file is shared).</summary>
internal static class SettingsSelfTest
{
    private const string Slot1 = "spell_slot_1", Slot2 = "spell_slot_2", TestKey = "backslash";

    private static readonly Dictionary<string, string> Saved = new();
    private static int _casts;
    private static Keyboard? _kb;
    private static bool _addedKeyboard;

    private static SettingsPanel Panel => SettingsFeature.Panel;
    private static PlayerCharacter Pc => PlayerState.Character;

    public static void Register()
    {
        SelfTest.Add(2f, "settings: open the overlay", OpenOverlay);
        SelfTest.Add(2.5f, "settings: defaults clash with no game key", Defaults);
        SelfTest.Add(3f, "settings: rebind, combos, cancel, clashes", Rebind);
        SelfTest.Add(3.5f, "settings: shot the overlay", () => TestKit.Screenshot("60-settings-overlay"));
        SelfTest.Add(4f, "settings: hold Alt", HoldAlt);
        SelfTest.Add(4.3f, "settings: press 1 with Alt", () => Send(Key.Digit1, true));
        SelfTest.Add(4.6f, "settings: Alt+1 aims the spell", AltOneAims);
        SelfTest.Add(4.75f, "settings: releasing Alt+1 casts", AltOneCast);
        SelfTest.Add(4.9f, "settings: press a plain 1", PlainOne);
        SelfTest.Add(5.2f, "settings: plain 1 did not cast", PlainOneChecked);
        SelfTest.Add(5.6f, "settings: reset to defaults", Reset);
        SelfTest.Add(6f, "settings: player keys restored", Restore);
    }

    private static void OpenOverlay()
    {
        Saved.Clear();
        foreach (var k in ModKeys.All) Saved[k.Id] = ModKeys.Value(k.Id);
        Panel.Toggle();
        SelfTest.Check("settings: overlay opens", Panel.IsOpen);
        SelfTest.Check("settings: every mod key listed", Panel.RowIds.Count == ModKeys.All.Count && ModKeys.All.All(k => ModKeys.Entry(k.Id) != null),
            string.Join(",", ModKeys.All.Where(k => ModKeys.Entry(k.Id) == null).Select(k => k.Id)));
        SelfTest.Check("settings: overlay is a pad panel", Pad.GamepadFeature.OpenPanel?.Name == "settings", Pad.GamepadFeature.OpenPanel?.Name ?? "none");
        SelfTest.Log($"settings: game keys read: {ModKeys.GameKeys().Count} keys");
    }

    private static void Defaults()
    {
        foreach (var id in new[] { "perks_key", "settings_key" }.Concat(Enumerable.Range(1, SpellBook.Slots).Select(i => $"spell_slot_{i}")))
        {
            var d = ModKeys.Default(id);
            var game = ModKeys.GameClashes(d);
            SelfTest.Check($"settings: default {id}={d} is no game key", game.Count == 0, string.Join(", ", game));
        }
        SelfTest.Check("settings: spell defaults are Alt+1..Alt+6", Enumerable.Range(1, SpellBook.Slots).All(i => ModKeys.Default($"spell_slot_{i}") == $"alt+{i}"),
            string.Join(",", Enumerable.Range(1, SpellBook.Slots).Select(i => ModKeys.Default($"spell_slot_{i}"))));
        SelfTest.Check("settings: game binds Alt+1 (set hotkey)", ModKeys.GameKeys().ContainsKey("alt+1"),
            string.Join(" | ", ModKeys.GameKeys().Keys.Where(k => k.Contains('+'))));
        SelfTest.Check("settings: V is flagged as a game key", ModKeys.GameClashes("v").Count > 0);
        SelfTest.Check("settings: a plain 1 is flagged as a game key", ModKeys.GameClashes("1").Count > 0);
        SelfTest.Check("settings: Alt+E is flagged (the game's E ignores Alt)", ModKeys.GameClashes("alt+e").Count > 0);
    }

    private static void Rebind()
    {
        // A combo: bind, show, then clear it by binding a plain key.
        Panel.StartListening(Slot1);
        Panel.Accept(SettingsPanel.Combine("7", ctrl: true, shift: false, alt: false));
        SelfTest.Check("settings: a combo is bound", ModKeys.Value(Slot1) == "ctrl+7", ModKeys.Value(Slot1));
        SelfTest.Check("settings: a combo binds its base key", string.Equals(ModKeys.BoundPath(Slot1), "<Keyboard>/7", StringComparison.OrdinalIgnoreCase),
            ModKeys.BoundPath(Slot1) ?? "none");
        SelfTest.Check("settings: the overlay shows the combo", Panel.ButtonText(Slot1) == "Ctrl+7", Panel.ButtonText(Slot1));
        SelfTest.Check("settings: the bar shows the combo", SpellKeys.Label(0) == "Ctrl+7", SpellKeys.Label(0));

        Panel.StartListening(Slot1);
        SelfTest.Check("settings: row waits for a key", Panel.Listening == Slot1);
        Panel.Accept(TestKey);
        SelfTest.Check("settings: a plain key clears the combo", ModKeys.Value(Slot1) == TestKey, ModKeys.Value(Slot1));
        SelfTest.Check("settings: input action follows at once", string.Equals(ModKeys.BoundPath(Slot1), "<Keyboard>/" + TestKey, StringComparison.OrdinalIgnoreCase),
            ModKeys.BoundPath(Slot1) ?? "none");
        SelfTest.Check("settings: bar label follows", SpellKeys.Label(0) == "\\", SpellKeys.Label(0));

        Panel.StartListening(Slot1);
        Panel.Back();
        SelfTest.Check("settings: Esc cancels and keeps the key", Panel.Listening == null && Panel.IsOpen && ModKeys.Value(Slot1) == TestKey);

        Panel.StartListening(Slot2);
        Panel.Accept(TestKey);
        SelfTest.Check("settings: a key used twice is flagged", Panel.WarningText(Slot2).Contains(ModKeys.Label(ModKeys.All.First(k => k.Id == Slot1))), Panel.WarningText(Slot2));
        Panel.StartListening(Slot2);
        Panel.Accept("v");
        SelfTest.Check("settings: a game key is flagged", Panel.WarningText(Slot2).Length > 0 && Panel.WarningText(Slot1).Length == 0,
            $"slot2 '{Panel.WarningText(Slot2)}', slot1 '{Panel.WarningText(Slot1)}'");

        // Combos clash only with the same combo.
        ModKeys.Rebind(Slot1, "alt+backslash", save: false);
        ModKeys.Rebind(Slot2, TestKey, save: false);
        Panel.StartListening(Slot1);
        Panel.Back();
        SelfTest.Check("settings: Alt+key and the plain key are no mod clash", ModKeys.ModClashes(Slot1).Count == 0 && ModKeys.ModClashes(Slot2).Count == 0);
        ModKeys.Rebind(Slot2, "leftAlt+backslash", save: false);
        SelfTest.Check("settings: the same combo twice is a mod clash", ModKeys.ModClashes(Slot1).Count == 1, ModKeys.Value(Slot2));
    }

    private static void HoldAlt()
    {
        Panel.Close();
        ModKeys.Rebind(Slot1, "alt+1", save: false);
        ModKeys.Rebind(Slot2, "alt+2", save: false);
        PlayerState.FromSave(new Core.Saving.PlayerSave { Level = 12, Xp = 0 });
        Pc.Perks.UnlockSpell("spell.blink", Pc.Progress);
        Pc.Spells.Equip(0, "spell.blink");
        ReadySpells();
        _casts = 0;
        SpellCaster.Casted += OnCast;
        _kb = Keyboard.current;
        _addedKeyboard = false;
        if (_kb == null)
        {
            try
            {
                _kb = InputSystem.AddDevice("Keyboard").TryCast<Keyboard>();
                _kb?.MakeCurrent();
                _addedKeyboard = _kb != null;
            }
            catch (Exception e)
            {
                SelfTest.Log("settings: could not add a test keyboard: " + e.Message);
            }
        }
        SelfTest.Log($"settings: keyboard {(_kb == null ? "none" : _addedKeyboard ? "added for the test" : "present")}, " +
            $"player controllable {SonsSdk.GameState.IsPlayerControllable}");
        Send(Key.LeftAlt, true);
    }

    private static void ReadySpells()
    {
        Pc.Spells.Tick(1000, Pc.Stats);
        var stamina = LocalPlayer.Vitals.Stamina;
        stamina.SetCurrentValue(stamina.GetMax());
    }

    /// <summary>Blink casts on release (COS-82): holding Alt+1 aims it, letting go casts it.</summary>
    private static void AltOneAims()
    {
        if (_kb == null) { SelfTest.Log("settings: no keyboard; key presses not checked"); return; }
        SelfTest.Check("settings: holding Alt+1 aims Blink", Spells.SpellControl.Input.IsAiming(0) && _casts == 0, $"aiming {Spells.SpellControl.Input.IsAiming(0)}, {_casts} casts");
        Send(Key.Digit1, false);
    }

    private static void AltOneCast()
    {
        if (_kb == null) return;
        SelfTest.Check("settings: Alt+1 cast the spell", _casts >= 1, $"{_casts} casts, bound {ModKeys.BoundPath(Slot1)}");
        SelfTest.Check("settings: the game's hotkey 1 and set hotkey 1 are held back under Alt",
            ModKeys.HeldBack("Hotkey1") && ModKeys.HeldBack("SetHotkey1") && !ModKeys.HeldBack("Hotkey7"),
            $"Hotkey1 {ModKeys.HeldBack("Hotkey1")}, SetHotkey1 {ModKeys.HeldBack("SetHotkey1")}, Hotkey7 {ModKeys.HeldBack("Hotkey7")}");
        Send(Key.Digit1, false);
        Send(Key.LeftAlt, false);
    }

    private static void PlainOne()
    {
        if (_kb == null) return;
        var hotkey = ModKeys.GameHotkey("Hotkey1");
        SelfTest.Check("settings: Alt released gives the game's hotkeys back", !ModKeys.HeldBack("Hotkey1") && (hotkey == null || hotkey.enabled),
            $"held {ModKeys.HeldBack("Hotkey1")}, enabled {hotkey?.enabled}");
        ReadySpells();
        _casts = 0;
        Send(Key.Digit1, true);
    }

    private static void PlainOneChecked()
    {
        SpellCaster.Casted -= OnCast;
        if (_kb == null) return;
        SelfTest.Check("settings: a plain 1 does not cast", _casts == 0 && !Spells.SpellControl.Input.IsAiming(0), $"{_casts} casts, aiming {Spells.SpellControl.Input.IsAiming(0)}");
        Send(Key.Digit1, false);
        if (_addedKeyboard)
        {
            try { InputSystem.RemoveDevice(_kb); } catch { /* gone */ }
        }
        _kb = null;
    }

    private static void OnCast(SpellCastInfo _) => _casts++;

    /// <summary>A key state event on the keyboard device, as if the key were pressed or released (other keys keep their state).</summary>
    private static void Send(Key key, bool down)
    {
        if (_kb == null) return;
        try
        {
            var buffer = StateEvent.From(_kb, out var ev, Unity.Collections.Allocator.Temp);
            _kb[key].WriteValueIntoEvent(down ? 1f : 0f, ev);
            InputSystem.QueueEvent(ev);
            buffer.Dispose();
        }
        catch (Exception e)
        {
            SelfTest.Log("settings: key event failed " + e.Message);
        }
    }

    private static void Reset()
    {
        Panel.Toggle();
        Panel.ResetKeys();
        bool all = ModKeys.All.All(k => ModKeys.Value(k.Id) == ModKeys.Default(k.Id));
        SelfTest.Check("settings: reset puts every key on its default", all,
            string.Join(", ", ModKeys.All.Where(k => ModKeys.Value(k.Id) != ModKeys.Default(k.Id)).Select(k => $"{k.Id}={ModKeys.Value(k.Id)}")));
        SelfTest.Check("settings: reset rebinds live", string.Equals(ModKeys.BoundPath(Slot1), "<Keyboard>/1", StringComparison.OrdinalIgnoreCase),
            ModKeys.BoundPath(Slot1) ?? "none");
        Panel.StepVolume(-0.1f);
        float v = Config.SoundVolume.Value;
        Panel.StepVolume(0.1f);
        SelfTest.Check("settings: volume steps", Math.Abs(Config.SoundVolume.Value - v - 0.1f) < 0.01f || Config.SoundVolume.Value >= 1f, $"{v} -> {Config.SoundVolume.Value}");
        Panel.Close();
    }

    private static void Restore()
    {
        foreach (var (id, key) in Saved)
            if (key.Length > 0 && ModKeys.Value(id) != key) ModKeys.Rebind(id, key, save: false);
        ModKeys.Save();
        SelfTest.Check("settings: player keys restored", Saved.All(kv => kv.Value.Length == 0 || ModKeys.Value(kv.Key) == kv.Value));
    }
}
