using ChampionsOfTheSons.Core.Rules;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>Spell checks: unlock + slot Blink, cast it, position/cooldown/energy, refusals, spell bar HUD.</summary>
internal static class SpellsSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;

    public static void Register()
    {
        // Blink lifts and drops the player; on a fresh level-1 test save (COS-129) the fall killed them and every later phase
        // ran "not in world". God mode covers the Blink steps only (Spells2 checks healing, which god mode would mask).
        SelfTest.Add(49.9f, "spells: god mode for Blink", () => ChampionsOfTheSons.Mod.Admin.AdminState.Apply(new ChampionsOfTheSons.Core.Networking.AdminGrant(ChampionsOfTheSons.Core.Networking.AdminAction.God, 1, "")));
        SelfTest.Add(50f, "spells: unlock and slot Blink", Setup);
        SelfTest.Add(50.5f, "spells: bar shows the slot", BarShows);
        SelfTest.Add(51f, "spells: blink range in open air", OpenAirRange);
        SelfTest.Add(51.5f, "spells: cast Blink", CastBlink);
        SelfTest.Add(53f, "spells: shot bar on cooldown", () => TestKit.Screenshot("30-spellbar-cooldown"));
        SelfTest.Add(53.5f, "spells: refusals", Refusals);
        SelfTest.Add(54.5f, "spells: persists in the save", SaveRoundTrip);
        SelfTest.Add(57f, "spells: god mode off", () => ChampionsOfTheSons.Mod.Admin.AdminState.Apply(new ChampionsOfTheSons.Core.Networking.AdminGrant(ChampionsOfTheSons.Core.Networking.AdminAction.God, 0, "")));
    }

    private static void Setup()
    {
        var save = new Core.Saving.PlayerSave { Level = 12, Xp = 0 };
        PlayerState.FromSave(save);
        SelfTest.Check("spells: Blink unlocks with 2 points", Pc.Perks.UnlockSpell("spell.blink", Pc.Progress) == BuyResult.Ok);
        SelfTest.Check("spells: Blink slots", Pc.Spells.Equip(0, "spell.blink"));
        SelfTest.Check("spells: equipping starts the cooldown", !Pc.Spells.IsReady(0));
        Pc.Spells.Tick(1000, Pc.Stats);
        SelfTest.Check("spells: cooldown runs out", Pc.Spells.IsReady(0));

        var stamina = LocalPlayer.Vitals.Stamina;
        stamina.SetCurrentValue(stamina.GetMax());
        SelfTest.Log($"spells: energy {SpellCaster.Energy:0.#} / {SpellCaster.MaxEnergy:0.#} (stamina {stamina._currentValue:0.#} / {stamina.GetMax():0.#})");
        SelfTest.Check("spells: energy is full", SpellCaster.Energy > SpellCaster.MaxEnergy * 0.99);
    }

    private static void BarShows()
    {
        var bar = SpellBar.Instance;
        bar.Refresh();
        SelfTest.Check("spells: bar visible in world", bar.Visible);
        SelfTest.Check("spells: bar slot 1 shows Blink with its key", bar.SlotLabel(0) == "Blink" && bar.SlotKeyLabel(0).Length > 0, $"{bar.SlotLabel(0)} [{bar.SlotKeyLabel(0)}]");
    }

    private static void OpenAirRange()
    {
        // 300 m up there is nothing to hit: the landing point is exactly the range away.
        var origin = LocalPlayer.Transform.position + Vector3.up * 300;
        var feet = SpellCaster.BlinkTarget(origin, Vector3.forward, SpellCaster.BlinkRange, 1.6f);
        float horizontal = Vector2.Distance(new Vector2(origin.x, origin.z), new Vector2(feet.x, feet.z));
        SelfTest.Check("spells: Blink goes the full range in the open", Mathf.Abs(horizontal - SpellCaster.BlinkRange) < 0.01f, $"{horizontal:0.00} m");

        // Facing the ground the player stops above it, not inside it.
        var down = SpellCaster.BlinkTarget(LocalPlayer.Transform.position + Vector3.up * 1.6f, Vector3.down, SpellCaster.BlinkRange, 1.6f);
        var ground = SpellCaster.GroundY(down + Vector3.up * 3);
        SelfTest.Check("spells: Blink looking down stays above the ground", ground == null || down.y >= ground.Value, $"feet {down.y:0.00}, ground {ground?.ToString("0.00") ?? "none"}");
    }

    private static void CastBlink()
    {
        var player = LocalPlayer.Transform;
        var cam = Camera.main != null ? Camera.main.transform : player;
        var before = player.position;
        // Aim level so the test does not depend on where the camera happens to point vertically.
        double energyBefore = SpellCaster.Energy;
        var expected = SpellCaster.BlinkTarget(cam.position, cam.forward, SpellCaster.BlinkRange, cam.position.y - player.position.y);

        var outcome = SpellCaster.Cast(0);
        var after = player.position;
        float moved = Vector3.Distance(before, after);
        float expectedMoved = Vector3.Distance(before, expected);
        SelfTest.Check("spells: cast outcome", outcome == SpellCastOutcome.Cast, outcome.ToString());
        SelfTest.Check("spells: player moved by about the planned distance", moved > 0.5f && moved <= SpellCaster.BlinkRange + 1.5f && Mathf.Abs(moved - expectedMoved) < 2f,
            $"moved {moved:0.00} m, planned {expectedMoved:0.00} m, from {before} to {after}");
        SelfTest.Check("spells: cooldown started", !Pc.Spells.IsReady(0) && Pc.Spells.CooldownProgress(0) < 0.05);
        double spent = energyBefore - SpellCaster.Energy;
        SelfTest.Check("spells: energy spent", Math.Abs(spent - 25) < 3, $"{spent:0.##} (cost 25)");
        SelfTest.Check("spells: second cast is on cooldown", SpellCaster.Cast(0) == SpellCastOutcome.OnCooldown);
        SpellBar.Instance.Refresh();
        SelfTest.Check("spells: bar shows cooldown", SpellBar.Instance.CooldownOverlayFraction(0) > 0.9f && SpellBar.Instance.SlotRemaining(0).Length > 0,
            $"overlay {SpellBar.Instance.CooldownOverlayFraction(0):0.00}, text '{SpellBar.Instance.SlotRemaining(0)}'");
    }

    private static void Refusals()
    {
        Pc.Spells.Tick(1000, Pc.Stats);
        var stamina = LocalPlayer.Vitals.Stamina;
        stamina.SetCurrentValue(1);
        SelfTest.Check("spells: no energy refuses the cast", SpellCaster.Cast(0) == SpellCastOutcome.NotEnoughEnergy && Pc.Spells.IsReady(0));
        SelfTest.Check("spells: empty slot", SpellCaster.Cast(4) == SpellCastOutcome.EmptySlot);
        SelfTest.Check("spells: every spell has an effect", ModContent.Db.Spells.Keys.All(SpellCaster.IsImplemented));
        stamina.SetCurrentValue(stamina.GetMax());
    }

    private static void SaveRoundTrip()
    {
        var save = PlayerState.ToSave();
        PlayerState.FromSave(save);
        SelfTest.Check("spells: loadout survives a save round trip", Pc.Spells.Loadout[0] == "spell.blink" && Pc.Perks.UnlockedSpells.Contains("spell.blink"));
    }
}
