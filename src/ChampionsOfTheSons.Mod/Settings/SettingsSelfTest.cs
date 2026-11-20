using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Spells;
using TheForest.Utils;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ChampionsOfTheSons.Mod.Settings;

/// <summary>COS-104 in game: the overlay lists every key, rebinds live (a pressed key casts the spell on its new key), Esc cancels,
/// clashes are flagged, reset restores the defaults. The player's own key choices are put back at the end (the cfg file is shared).</summary>
internal static class SettingsSelfTest
{
    private const string Slot1 = "spell_slot_1", Slot2 = "spell_slot_2", TestKey = "backslash";

    private static readonly Dictionary<string, string> Saved = new();
    private static int _casts;
    private static bool _pressed;

    private static SettingsPanel Panel => SettingsFeature.Panel;
    private static PlayerCharacter Pc => PlayerState.Character;

    public static void Register()
    {
        SelfTest.Add(2f, "settings: open the overlay", OpenOverlay);
        SelfTest.Add(2.5f, "settings: defaults clash with no game key", Defaults);
        SelfTest.Add(3f, "settings: rebind, cancel, clashes", Rebind);
        SelfTest.Add(3.5f, "settings: shot the overlay", () => TestKit.Screenshot("60-settings-overlay"));
        SelfTest.Add(4f, "settings: press the new key", PressKey);
        SelfTest.Add(4.2f, "settings: release the key", ReleaseKey);
        SelfTest.Add(4.8f, "settings: spell cast on the new key", CastOnNewKey);
        SelfTest.Add(5.2f, "settings: reset to defaults", Reset);
        SelfTest.Add(5.6f, "settings: player keys restored", Restore);
    }

    private static void OpenOverlay()
    {
        Saved.Clear();
        foreach (var k in ModKeys.All) Saved[k.Id] = ModKeys.Value(k.Id);
        Panel.Toggle();
        SelfTest.Check("settings: overlay opens", Panel.IsOpen);
        SelfTest.Check("settings: every mod key listed", Panel.RowIds.Count == 6 + SpellBook.Slots && ModKeys.All.All(k => ModKeys.Entry(k.Id) != null),
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
        SelfTest.Check("settings: V is flagged as a game key", ModKeys.GameClashes("v").Count > 0);
    }

    private static void Rebind()
    {
        Panel.StartListening(Slot1);
        SelfTest.Check("settings: row waits for a key", Panel.Listening == Slot1);
        Panel.Accept(TestKey);
        SelfTest.Check("settings: config holds the new key", ModKeys.Value(Slot1) == TestKey, ModKeys.Value(Slot1));
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
    }

    private static void PressKey()
    {
        Panel.Close();
        PlayerState.FromSave(new Core.Saving.PlayerSave { Level = 12, Xp = 0 });
        Pc.Perks.UnlockSpell("spell.blink", Pc.Progress);
        Pc.Spells.Equip(0, "spell.blink");
        Pc.Spells.Tick(1000, Pc.Stats);
        var stamina = LocalPlayer.Vitals.Stamina;
        stamina.SetCurrentValue(stamina.GetMax());
        _casts = 0;
        SpellCaster.Casted += OnCast;
        _pressed = Send(Key.Backslash, true);
    }

    private static void ReleaseKey()
    {
        if (_pressed) Send(Key.Backslash, false);
    }

    private static void CastOnNewKey()
    {
        SpellCaster.Casted -= OnCast;
        if (!_pressed)
        {
            SelfTest.Log("settings: no keyboard to send a key press to; binding checked above only");
            return;
        }
        SelfTest.Check("settings: pressing the new key cast the spell", _casts >= 1, $"{_casts} casts, bound {ModKeys.BoundPath(Slot1)}");
    }

    private static void OnCast(SpellCastInfo _) => _casts++;

    /// <summary>A key state event on the real keyboard device, as if the key were pressed or released.</summary>
    private static bool Send(Key key, bool down)
    {
        try
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            var buffer = StateEvent.From(kb, out var ev, Unity.Collections.Allocator.Temp);
            kb[key].WriteValueIntoEvent(down ? 1f : 0f, ev);
            InputSystem.QueueEvent(ev);
            buffer.Dispose();
            return true;
        }
        catch (Exception e)
        {
            SelfTest.Log("settings: key event failed " + e.Message);
            return false;
        }
    }

    private static void Reset()
    {
        Panel.Toggle();
        Panel.ResetKeys();
        bool all = ModKeys.All.All(k => ModKeys.Value(k.Id) == ModKeys.Default(k.Id));
        SelfTest.Check("settings: reset puts every key on its default", all,
            string.Join(", ", ModKeys.All.Where(k => ModKeys.Value(k.Id) != ModKeys.Default(k.Id)).Select(k => $"{k.Id}={ModKeys.Value(k.Id)}")));
        SelfTest.Check("settings: reset rebinds live", string.Equals(ModKeys.BoundPath(Slot1), "<Keyboard>/" + Config.SpellDefaults[0], StringComparison.OrdinalIgnoreCase),
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
