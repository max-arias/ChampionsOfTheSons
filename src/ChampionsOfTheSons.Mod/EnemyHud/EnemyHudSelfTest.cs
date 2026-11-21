using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Hud;
using Sons.Ai.Vail;

namespace ChampionsOfTheSons.Mod.EnemyHud;

/// <summary>COS-68 scanner, danger marks and named elites; COS-67 boss bar. Screenshots need the windowed run.</summary>
internal static class EnemyHudSelfTest
{
    private static Nameplates Plates => HudFeature.Instance!.Plates;
    private static EnemyHudFeature Bars => EnemyHudFeature.Instance!;

    private static VailActor? _elite, _plain, _boss, _boss2;
    private const float T0 = 20f;

    /// <summary>Affixes that never act on the player, so a spawned elite or boss stays still in the test.</summary>
    private static readonly EnemyAbility[] Passive =
    {
        EnemyAbility.Steadfast, EnemyAbility.EliteSteadfast, EnemyAbility.BossSteadfast, EnemyAbility.ExtraHealth,
        EnemyAbility.ExtraDamage, EnemyAbility.Juggernaut, EnemyAbility.Undead, EnemyAbility.RainEmpowerment,
    };

    public static void Register()
    {
        SelfTest.Add(T0, "enemyhud: spawn", Spawn);
        SelfTest.Add(T0 + 2.5f, "enemyhud: plates, names, danger", PlatesAndNames);
        SelfTest.Add(T0 + 2.6f, "enemyhud: aim at the elite", () => Aim(_elite));
        SelfTest.Add(T0 + 3.5f, "enemyhud: scanner opens on the enemy in front", ScannerOpens);
        SelfTest.Add(T0 + 3.6f, "enemyhud: shot scanner", () => TestKit.Screenshot("enemyhud-01-scanner"));
        SelfTest.Add(T0 + 4f, "enemyhud: look at another enemy", () => Aim(_plain));
        SelfTest.Add(T0 + 4.8f, "enemyhud: scanner follows the look", ScannerFollows);
        SelfTest.Add(T0 + 5f, "enemyhud: hit the boss", () =>
        {
            Plates.TestCrosshair = null;
            SelfTest.Check("boss bar: hidden before the boss is engaged", Bars.VisibleBars == 0, Bars.VisibleBars.ToString());
            TestKit.Hit(_boss!, 400);
        });
        SelfTest.Add(T0 + 5.8f, "enemyhud: boss bar shows", BossBarShows);
        SelfTest.Add(T0 + 5.9f, "enemyhud: shot boss bar", () => TestKit.Screenshot("enemyhud-02-bossbar"));
        SelfTest.Add(T0 + 6.2f, "enemyhud: open a panel", () => UI.CotsUi.Inventory.Open());
        SelfTest.Add(T0 + 6.8f, "enemyhud: hidden under panels", () =>
        {
            SelfTest.Check("boss bar: hidden while a panel is open", !Bars.LayerShown);
            UI.CotsUi.Inventory.Close();
        });
        SelfTest.Add(T0 + 7.5f, "enemyhud: kill the boss", () => TestKit.Kill(_boss!));
        SelfTest.Add(T0 + 8.3f, "enemyhud: gone on death", () =>
            SelfTest.Check("boss bar: hides when the boss dies", Bars.VisibleBars == 0, Bars.VisibleBars.ToString()));
        SelfTest.Add(T0 + 8.5f, "enemyhud: aware boss", () =>
        {
            _boss2 = SpawnBoss(16, 5);
            var r = TestKit.Profile(_boss2);
            if (r != null) r.TestAware = true;
        });
        SelfTest.Add(T0 + 9.5f, "enemyhud: aware boss shows, then loses track", () =>
        {
            SelfTest.Check("boss bar: an aware boss nearby gets a bar without a hit", Bars.VisibleBars == 1, Bars.VisibleBars.ToString());
            var r = TestKit.Profile(_boss2);
            if (r != null) r.TestAware = false;
        });
        SelfTest.Add(T0 + 10.5f, "enemyhud: grace", () =>
            SelfTest.Check("boss bar: stays a moment after the boss loses track", Bars.VisibleBars == 1, Bars.VisibleBars.ToString()));
        SelfTest.Add(T0 + 14f, "enemyhud: lost track", () =>
            SelfTest.Check("boss bar: hides once the boss has lost track", Bars.VisibleBars == 0, Bars.VisibleBars.ToString()));
        SelfTest.Add(T0 + 14.5f, "enemyhud: cleanup", Cleanup);
    }

    /// <summary>Puts the test crosshair on an enemy's chest: the headless camera looks level, and the spawn may stand downhill.</summary>
    private static void Aim(VailActor? target)
    {
        var cam = HudKit.PlayerCamera();
        var sim = target?.GetWorldSimActor();
        var r = TestKit.Profile(target);
        if (cam == null || sim == null || r == null) return;
        var chest = cam.WorldToScreenPoint(sim._position + UnityEngine.Vector3.up * (1.2f * (float)r.Profile.Scale));
        Plates.TestCrosshair = new UnityEngine.Vector2(chest.x, chest.y);
    }

    private static VailActor? SpawnBoss(float distance, float sideways)
    {
        var rules = WorldState.Rules with { DisabledAbilities = Enum.GetValues<EnemyAbility>().Where(a => !Passive.Contains(a)).ToArray() };
        EnemyRegistry.TestProfileOverride = p => p with
        {
            Tier = EnemyTier.Boss,
            Abilities = new[] { EnemyAbility.BossSteadfast, EnemyAbility.ExtraHealth, EnemyAbility.Juggernaut },
            MaxHealth = p.MaxHealth * 20,
            Scale = 1.2,
            Name = null, // picked by the registry like any other boss
        };
        try
        {
            var v = TestKit.Spawn(VailActorTypeId.Carl, distance, sideways, rules);
            var r = TestKit.Profile(v);
            if (r != null)
            {
                r.TestActive = true; // the headless game keeps spawns dormant
                r.TestAware = false; // COS-134: it would notice the player on its own; the steps set awareness when they want it
                r.Actor = v; // the registry may still point at the actor SetMaxHealthStat saw; the game's bridge fixes that later
            }
            return v;
        }
        finally
        {
            EnemyRegistry.TestProfileOverride = null;
        }
    }

    private static void Spawn()
    {
        // COS-134: a boss left by an earlier phase (the admin test spawns one) can still be aware of the player and hold a bar.
        int cleared = 0;
        foreach (var r in EnemyRegistry.Snapshot())
            if (r.Profile.Tier >= EnemyTier.Miniboss && r.Actor != null) { TestKit.Kill(r.Actor); cleared++; }
        if (cleared > 0) SelfTest.Log($"enemyhud: killed {cleared} leftover bosses from earlier phases");
        var elites = WorldState.Rules with
        {
            PartyLevelOverride = Difficulties.Hell.BaseLevelMin,
            DisabledAbilities = Enum.GetValues<EnemyAbility>().Where(a => !Passive.Contains(a)).ToArray(),
        };
        _elite = TestKit.Spawn(VailActorTypeId.Carl, 7, 0, elites);
        _plain = TestKit.Spawn(VailActorTypeId.Carl, 9, 4, WorldState.Rules with { AllowElites = false });
        _boss = SpawnBoss(14, -5);
        SelfTest.Check("enemyhud: spawned elite, plain and boss", _elite != null && _plain != null && _boss != null);
        SelfTest.Check("enemyhud: spawned boss is Boss tier", TestKit.Profile(_boss)?.Profile.Tier == EnemyTier.Boss);
    }

    private static void PlatesAndNames()
    {
        int level = PlayerState.Character.Progress.Level;
        var elite = TestKit.Profile(_elite)!;
        var plain = TestKit.Profile(_plain)!;
        var e = Plates.Info(_elite!);
        var p = Plates.Info(_plain!);
        SelfTest.Log($"enemyhud: elite {e}; plain {p}; player level {level}");
        string? name = HudText.PersonalName(elite.Profile);
        SelfTest.Check("names: an elite gets a personal name", elite.Profile.Name != null && !string.IsNullOrEmpty(name), name ?? "none");
        SelfTest.Check("names: a normal enemy has none", plain.Profile.Name == null);
        SelfTest.Check("names: the elite's plate shows its name", e is { Visible: true } && name != null && e.Value.Name.Contains(name), e?.Name ?? "");
        SelfTest.Check("names: the name parts resolve to text, not ids", name != null && !name.Contains("elite."), name ?? "");
        SelfTest.Check("danger: plate marks follow the rule (elite)", e is { } ev && ev.Danger == EnemyInfo.Danger(elite.Profile.Tier, elite.Profile.Level, level), $"{e?.Danger}");
        SelfTest.Check("danger: plate marks follow the rule (plain)", p is { } pv && pv.Danger == EnemyInfo.Danger(EnemyTier.Normal, plain.Profile.Level, level), $"{p?.Danger}");
        var missing = Enum.GetValues<EnemyAbility>().Where(a => HudText.AffixExplained(a).Length == 0).ToList();
        SelfTest.Check("scanner: every affix has an explanation line", missing.Count == 0, string.Join(",", missing));
    }

    private static void ScannerOpens()
    {
        var elite = TestKit.Profile(_elite);
        var looked = Plates.LookedAt;
        SelfTest.Log($"enemyhud: looked at {looked?.Profile.KindId} lv {looked?.Profile.Level} tier {looked?.Profile.Tier}; scanned {Plates.Scanned?.Profile.Tier}");
        bool real = ReferenceEquals(looked, elite);
        SelfTest.Check("scanner: the crosshair on its body finds the enemy", real);
        if (!real)
        {
            var cam = HudKit.PlayerCamera();
            var sim = _elite!.GetWorldSimActor();
            if (cam != null && sim != null)
                SelfTest.Log($"enemyhud: look miss: screen {UnityEngine.Screen.width}x{UnityEngine.Screen.height}, cam {cam.name} at {cam.transform.position} fwd {cam.transform.forward}; " +
                    $"elite feet {cam.WorldToScreenPoint(sim._position)} head {cam.WorldToScreenPoint(sim._position + UnityEngine.Vector3.up * 2.3f)}; player fwd {TheForest.Utils.LocalPlayer.Transform.forward}");
            // Keep the rest of the checks meaningful even if the headless camera looks elsewhere.
            Plates.ForceFocus(_elite);
            return;
        }
        CheckCard(elite!);
    }

    private static void CheckCard(EnemyRuntime target)
    {
        var card = Plates.Card;
        SelfTest.Log($"enemyhud: card '{card.HeadText}' / '{card.StatsText}' / '{card.AffixText}' at {card.Bounds}");
        SelfTest.Check("scanner: card shown for the looked-at enemy", card.Visible && ReferenceEquals(card.Target, target));
        var name = HudText.PersonalName(target.Profile);
        SelfTest.Check("scanner: card names the enemy and its danger", (name == null || card.HeadText.Contains(name)) &&
            card.HeadText.Contains(HudText.Danger(EnemyInfo.Danger(target.Profile.Tier, target.Profile.Level, PlayerState.Character.Progress.Level))), card.HeadText);
        SelfTest.Check("scanner: card shows health, armor, damage and bounty", card.StatsText.Contains("/") && card.StatsText.Split('\n').Length == 4, card.StatsText);
        int lines = card.AffixText.Length == 0 ? 0 : card.AffixText.Split('\n').Length;
        SelfTest.Check("scanner: one line per affix", lines == target.Profile.Abilities.Count, $"{lines} vs {target.Profile.Abilities.Count}");
        var b = card.Bounds;
        float k = HudKit.CanvasPerPixel;
        SelfTest.Check("scanner: card inside the screen", b.xMin >= 0 && b.yMin >= 0 && b.xMax <= UnityEngine.Screen.width * k + 0.5f && b.yMax <= UnityEngine.Screen.height * k + 0.5f, b.ToString());
    }

    private static void ScannerFollows()
    {
        var plain = TestKit.Profile(_plain)!;
        SelfTest.Check("scanner: switches to the newly looked-at enemy", ReferenceEquals(Plates.Card.Target, plain), Plates.Card.Target?.Profile.Tier.ToString() ?? "none");
        CheckCard(plain);
        // After the forced look in ScannerOpens (if the real one failed), the elite card checks run here too.
    }

    private static void BossBarShows()
    {
        var boss = TestKit.Profile(_boss)!;
        var bar = Bars.Bars[0];
        var health = boss.Stats == null ? null : EnemyRegistry.Health(boss.Stats);
        float expected = health == null ? 1 : (float)(health._currentValue / boss.Profile.MaxHealth);
        SelfTest.Log($"enemyhud: bar '{bar.NameText}' frac {bar.Fraction:0.000} trail {bar.TrailFraction:0.000} vs {expected:0.000}; bounds {bar.Bounds}");
        SelfTest.Check("boss bar: shows after the player hits the boss", Bars.VisibleBars == 1 && ReferenceEquals(bar.Target, boss), Bars.VisibleBars.ToString());
        string want = HudText.PersonalName(boss.Profile) ?? HudText.KindTitle(boss.Profile);
        SelfTest.Check("boss bar: shows the boss's name", bar.NameText == want, $"'{bar.NameText}' vs '{want}'");
        SelfTest.Check("boss bar: follows the boss's health", bar.Fraction < 0.9999f && Math.Abs(bar.Fraction - expected) < 0.01f, $"{bar.Fraction} vs {expected}");
        SelfTest.Check("boss bar: the damage chunk trails behind the fill", bar.TrailFraction >= bar.Fraction, $"{bar.TrailFraction} vs {bar.Fraction}");
        var b = bar.Bounds;
        float k = HudKit.CanvasPerPixel, ch = UnityEngine.Screen.height * k, cw = UnityEngine.Screen.width * k;
        // Top band only: clear of the level-up banner (150 canvas units down) and the bottom XP bar.
        SelfTest.Check("boss bar: sits in the top band, inside the screen", b.xMin >= 0 && b.xMax <= cw + 0.5f && b.yMax <= ch && ch - b.yMin <= 150, b.ToString());
        SelfTest.Check("boss bar: spans 80% of the screen width", Math.Abs(b.width - cw * BossBar.ScreenShare) < 2, $"{b.width:0} of {cw:0}");
        int barIndex = Bars.PanelIndex, hudIndex = HudFeature.Instance?.PanelIndex ?? -1;
        SelfTest.Check("boss bar: draws over the nameplates", barIndex > hudIndex, $"bar panel {barIndex}, HUD panel {hudIndex}");
        // A boss in view with the scanner open: both at once, for the screenshot.
        Plates.ForceFocus(_boss);
    }

    private static void Cleanup()
    {
        Plates.ForceFocus(null);
        Plates.TestCrosshair = null;
        foreach (var v in new[] { _elite, _plain, _boss, _boss2 })
            if (v != null && !v.IsDead()) TestKit.Kill(v);
        PlayerState.Character.Massacre.Reset();
    }
}
