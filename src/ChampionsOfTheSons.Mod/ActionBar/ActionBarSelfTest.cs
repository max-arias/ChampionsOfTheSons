using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Pad;
using ChampionsOfTheSons.Mod.Spells;
using ChampionsOfTheSons.Mod.UI;
using ChampionsOfTheSons.Mod.Weapons;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.ActionBar;

/// <summary>
/// COS-151 checks: the weapon and consumable groups mirror the game's hotkeys (icon, count, held glow), the pad chords switch weapons
/// and use the picked consumable, and spell slots glow only when castable. Hotkeys are set through the game's save data (its own
/// setters need the inventory view open).
/// </summary>
internal static class ActionBarSelfTest
{
    private static ActionBar Bar => ActionBarFeature.Bar;
    private static PlayerCharacter Pc => PlayerState.Character;
    private static readonly string[] FoodNames = { "EnergyBar", "Energy Bar", "MRE", "CatFood", "Cat Food", "EnergyDrink" };
    private static int _machete, _spear, _food, _foodBefore;

    public static void Register()
    {
        float t = 0;
        SelfTest.Add(t += 1f, "actionbar: set up hotkeys", () =>
        {
            TestKit.Unpause();
            SelfTest.Check("actionbar: hotkey controller found", HotkeyBridge.Controller != null);
            _machete = GameItems.Find("Machete", 359);
            _spear = GameItems.Find("CraftedSpear", 474);
            _food = FoodNames.Select(n => GameItems.Find(n, -1)).FirstOrDefault(id => id > 0);
            SelfTest.Log($"actionbar: machete {_machete}, spear {_spear}, food {_food} ({HotkeyBridge.Name(_food)})");
            TestKit.EnsureGameItem(_machete);
            TestKit.EnsureGameItem(_spear);
            if (_food > 0)
                for (int i = 0; i < 3 && LocalPlayer.Inventory.AmountOf(_food, true, false) < 3; i++) LocalPlayer.Inventory.AddItem(_food, 1, true, false, null);
            TestKit.FreeHands();
            for (int i = 0; i < HotkeyBridge.Count; i++) HotkeyBridge.SetForTest(i, -1);
            HotkeyBridge.SetForTest(0, _machete);
            HotkeyBridge.SetForTest(1, _spear);
            if (_food > 0) HotkeyBridge.SetForTest(ActionBarConfig.ConsumableHotkeys[0], _food);
            foreach (int k in PlayerState.Character.WeaponBar.Bindings.Keys.ToList()) PlayerState.Character.WeaponBar.Unbind(k);
            PlayerState.Character.Inventory.Remove(Core.Rules.WeaponTypes.WeaponSlot); // the weapon bar shows only game hotkeys here
            HotkeyBridge.Trigger(0);
            Bar.Dirty();
        });
        SelfTest.Add(t += 1.5f, "actionbar: groups show the hotkeys", () =>
        {
            SelfTest.Log("actionbar: " + HotkeyBridge.Describe());
            SelfTest.Check("actionbar: bar visible in world", Bar.Visible);
            var w = Bar.Weapons;
            SelfTest.Check("actionbar: weapon slot 1 shows the machete", w.Shown(0).ItemId == _machete && (w.Slot(0).Icon.Root.activeSelf || w.Slot(0).Name.TextObject.text.Length > 0),
                $"shown {w.Shown(0).ItemId}");
            SelfTest.Check("actionbar: weapon keys 1 and 2", w.Slot(0).Key.TextObject.text == "1" && w.Slot(1).Key.TextObject.text == "2");
            SelfTest.Check("actionbar: held machete glows ember", w.Slot(0).GlowHex == UiTheme.EmberGlow && w.Slot(1).GlowHex == "", $"'{w.Slot(0).GlowHex}' / '{w.Slot(1).GlowHex}'");
            SelfTest.Check("actionbar: empty weapon slot shows nothing", w.Shown(2).Empty && !w.Slot(2).Icon.Root.activeSelf);
            if (_food > 0)
            {
                var c = Bar.Consumables;
                SelfTest.Check("actionbar: consumable slot 5 shows the food with its count", c.Shown(0).ItemId == _food && c.Slot(0).Count.TextObject.text == c.Shown(0).Amount.ToString(),
                    $"shown {c.Shown(0).ItemId} x{c.Shown(0).Amount}, count '{c.Slot(0).Count.TextObject.text}'");
                SelfTest.Check("actionbar: first consumable is picked", c.Picked == 0 && c.Slot(0).GlowHex == UiTheme.PickGlow);
            }
            TestKit.Screenshot("actionbar");
        });
        // The game blocks hotkeys while an equip (first-look) animation runs; wait for the machete's to end.
        SelfTest.Add(t += 8f, "actionbar: pad chord for next weapon", () =>
        {
            SelfTest.Log($"actionbar: midAction {LocalPlayer.IsInMidAction}");
            PadInput.Inject(PadButton.RightShoulder, PadButton.DpadUp);
        });
        SelfTest.Add(t += 3f, "actionbar: pad next weapon", () =>
        {
            SelfTest.Check("actionbar: pad chord ran next weapon", GamepadFeature.LastBarAction == "next_weapon", GamepadFeature.LastBarAction);
            SelfTest.Check("actionbar: spear in hand after next weapon", HotkeyBridge.At(1).Held, HotkeyBridge.Describe());
            SelfTest.Check("actionbar: glow moved to the spear", Bar.Weapons.Slot(1).GlowHex == UiTheme.EmberGlow);
            if (_food <= 0) { SelfTest.Log("actionbar: no food item found, consumable use skipped"); return; }
            SelfTest.Log($"actionbar: midAction {LocalPlayer.IsInMidAction}");
            _foodBefore = LocalPlayer.Inventory.AmountOf(_food, true, false);
            PadInput.Inject(PadButton.RightShoulder, PadButton.DpadDown);
        });
        SelfTest.Add(t += 6f, "actionbar: pad used the consumable", () =>
        {
            if (_food <= 0) return;
            int after = LocalPlayer.Inventory.AmountOf(_food, true, false);
            SelfTest.Check("actionbar: pad chord ran use consumable", GamepadFeature.LastBarAction == "use_consumable", GamepadFeature.LastBarAction);
            SelfTest.Check("actionbar: one consumable used", after == _foodBefore - 1, $"{_foodBefore} -> {after}");
            SelfTest.Check("actionbar: count follows", Bar.Consumables.Slot(0).Count.TextObject.text == (after > 1 ? after.ToString() : ""),
                $"'{Bar.Consumables.Slot(0).Count.TextObject.text}'");
        });
        SelfTest.Add(t += 0.5f, "actionbar: spell glow follows energy", () =>
        {
            var id = ModContent.Db.Spells.Keys.Where(k => SpellCaster.IsImplemented(k) && Pc.Spells.CostOf(k, Pc.Stats).Total > 1).OrderBy(k => Pc.Spells.CostOf(k, Pc.Stats).Total).First();
            Pc.Spells.Equip(0, id);
            Pc.Spells.Tick(1000, Pc.Stats);
            SpellCaster.Gain(SpellCaster.MaxEnergy);
            SpellBar.Instance.Refresh();
            SelfTest.Check("actionbar: ready spell glows", SpellBar.Instance.SlotGlow(0) == UiTheme.RuneGlow,
                $"'{SpellBar.Instance.SlotGlow(0)}', {id} progress {Pc.Spells.CooldownProgress(0):0.##}, energy {SpellCaster.Energy:0.#}/{SpellCaster.MaxEnergy:0.#}, cost {Pc.Spells.CostOf(id, Pc.Stats).Total:0.#}, active {Pc.Spells.IsActive(0)}");
            SpellCaster.Spend(SpellCaster.Energy);
            SpellBar.Instance.Refresh();
            SelfTest.Check("actionbar: no glow without energy", SpellBar.Instance.SlotGlow(0) == "", $"'{SpellBar.Instance.SlotGlow(0)}', energy {SpellCaster.Energy:0.#}");
            SpellCaster.Gain(SpellCaster.MaxEnergy);
        });
        SelfTest.Add(t += 0.2f, "actionbar: group keys parse", () =>
        {
            SelfTest.Check("actionbar: '1,2,0' is indices 0,1,9", ActionBarConfig.Parse("1, 2,0", 4).SequenceEqual(new[] { 0, 1, 9 }));
            SelfTest.Check("actionbar: at most 4 keys, junk skipped", ActionBarConfig.Parse("5,x,6,7,8,9", 4).SequenceEqual(new[] { 4, 5, 6, 7 }));
        });
    }
}
