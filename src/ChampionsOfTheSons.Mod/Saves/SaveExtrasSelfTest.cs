using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.Saves;

/// <summary>COS-100 in-game checks: health share and carried items go into the save and come back.</summary>
internal static class SaveExtrasSelfTest
{
    public static void Register()
    {
        SelfTest.Add(2, "save extras: health share saved and restored", () =>
        {
            var h = LocalPlayer.Vitals.Health;
            h.SetCurrentValue(h.GetMax() * 0.5f);
            var save = PlayerState.ToSave();
            SelfTest.Check("save extras: health share in the save", Math.Abs(save.HealthFraction - 0.5) < 0.02, $"{save.HealthFraction:0.###}");
            SelfTest.Check("save extras: save time written", save.SavedAt > 1_700_000_000, save.SavedAt.ToString());
            h.SetCurrentValue(h.GetMax());
            SaveExtrasFeature.RestoreHealth(save.HealthFraction);
            SelfTest.Check("save extras: health share restored", Math.Abs(h._currentValue / h.GetMax() - 0.5f) < 0.02f, $"{h._currentValue:0.#}/{h.GetMax():0.#}");
            h.SetCurrentValue(h.GetMax());
        });
        SelfTest.Add(3, "save extras: trimmed carried items given back", () =>
        {
            var stick = Followups.FollowupsFeature.ItemsFor("Stick").FirstOrDefault();
            var inv = LocalPlayer.Inventory;
            if (stick == null || inv == null) { SelfTest.Check("save extras: stick item found", false); return; }
            int have = inv.AmountOf(stick.Id, true, false);
            int room = inv.GetAvailableSpaceForItem(stick.Id);
            int want = have + Math.Min(2, room);
            SaveExtrasFeature.RestoreItems(new Dictionary<int, int> { [stick.Id] = want });
            SelfTest.Check("save extras: missing sticks given back", inv.AmountOf(stick.Id, true, false) == want, $"had {have}, room {room}, now {inv.AmountOf(stick.Id, true, false)}, want {want}");
            SaveExtrasFeature.RestoreItems(new Dictionary<int, int> { [stick.Id] = want });
            SelfTest.Check("save extras: nothing added when none is missing", !SaveExtrasFeature.LastRestored.ContainsKey(stick.Id));
        });
    }
}
