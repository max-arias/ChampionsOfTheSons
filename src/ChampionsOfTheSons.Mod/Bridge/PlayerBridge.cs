using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using HarmonyLib;
using RedLoader;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Bridge;

/// <summary>
/// Applies the local character's stat sheet to the game: max health and regen (Vitals), incoming damage
/// (PlayerStats.Hit: dodge, armor, block), movement speed and jump (FirstPersonCharacter).
/// </summary>
[HarmonyPatch]
public static class PlayerBridge
{
    private static readonly Rng Rng = Rng.FromTime();
    private static float _appliedMaxHealth = -1;

    /// <summary>Last incoming hit (vanilla, after our defenses, dodged), for self-tests and UI.</summary>
    public static (double Before, double After, bool Dodged)? LastIncoming { get; private set; }

    /// <summary>Raised for every incoming hit after our defenses: (damage taken, dodged, kind). Damage numbers over the player.</summary>
    public static event Action<double, bool, DamageKind>? PlayerHurt;

    /// <summary>Called every frame while the player is in the world.</summary>
    public static void Update(float dt)
    {
        if (!LocalPlayer.IsInWorld || LocalPlayer.Vitals == null) return;
        var sheet = PlayerState.Character.Stats;
        var health = LocalPlayer.Vitals.Health;
        if (health == null) return;

        float max = (float)Derived.MaxHealth(sheet);
        if (!Mathf.Approximately(max, _appliedMaxHealth) || !Mathf.Approximately(health.GetMax(), max))
        {
            float ratio = health.GetMax() > 0 ? health._currentValue / health.GetMax() : 1;
            health.SetMax(max);
            health.SetCurrentValue(Mathf.Clamp(ratio * max, 0, max));
            _appliedMaxHealth = max;
        }

        // Our regen on top of the game's own.
        if (health._currentValue > 0 && health._currentValue < max)
        {
            float extra = (float)Derived.HealthRegenPerSecond(sheet, 0) * dt;
            if (extra > 0) health.SetCurrentValue(Mathf.Min(max, health._currentValue + extra));
        }
    }

    public static void Reset() => _appliedMaxHealth = -1;

    private static readonly int PerfBeforePlayerHit = Perf.Perf.Register("Hook.PlayerHit");

    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.Hit))]
    [HarmonyPrefix]
    private static void BeforePlayerHit(ref float damage, bool isDemonic, ImpactType impactType)
    {
        using var perf = Perf.Perf.Scope(PerfBeforePlayerHit);
        try
        {
            if (damage <= 0) return;
            if (Admin.AdminState.God || Spells.FlickerRun.Untouchable) // Flicker Strike: untouchable during the run (COS-141)
            {
                LastIncoming = (damage, 0, false);
                damage = 0;
                return;
            }
            var kind = isDemonic ? DamageKind.Magic : DamageKind.Physical;
            // COS-92: only an enemy's attack can be dodged, armored or blocked; fire, poison, traps, falls, explosions and the
            // rest of the world take COTF's amount and only the damage-taken multipliers.
            bool recent = PlayerEffects.LastAttacker is { } la && Time.time - la.Time <= 0.75f;
            var source = GameRules.GameRulesFeature.Classify(impactType, isDemonic, recent, !recent && NearestEnemy(MeleeReach) != null);
            if (source != PlayerDamageSource.Enemy)
            {
                double game = damage;
                damage = (float)GameRules.GameRulesFeature.NonEnemyHit(source, damage);
                kind = DamageKind.Pure;
                GameRules.GameRulesFeature.LastHit = (source, game, damage);
            }
            if (Net.CoopTest.Enabled) RLog.Msg($"{Net.CoopTestJoin.Tag} client: took a {kind} hit of {damage:F1} before defense");
            if (source == PlayerDamageSource.Enemy) ScaleClientMelee(ref damage, kind);
            var character = PlayerState.Character;
            var result = PlayerDefense.Incoming(character.Stats, damage, kind, Rng, PlayerDebuffs.LostArmor); // armor corruption (COS-89)
            double final = result.Damage;
            if (result.Dodged) Followups.FollowupsFeature.OnDodge();
            else final = Followups.FollowupsFeature.OnIncoming(final, kind);
            if (!result.Dodged && final > 0)
            {
                if (source == PlayerDamageSource.Enemy) OnEnemyHitPlayer(character, final, kind);
                var health = LocalPlayer.Vitals?.Health;
                // Near Death Experience (COS-105): a lethal hit restores full health and starts its 10 s of protection.
                if (health != null && PlayerEffects.NearDeath.TryTrigger(character.Stats.IsOn(NearDeathState.PerkFlag), health._currentValue, final))
                {
                    health.SetCurrentValue(health.GetMax());
                    final = 0;
                    Spells.SpellWorld.Buffs.Apply("near_death", NearDeathState.Seconds, NearDeathState.Buff());
                    Loot.LootFeature.Message(string.Format(UI.UiText.T("ui.perks.near_death.triggered", "Last Breath: back from the brink. Ready again in {0} min."),
                        (int)(NearDeathState.Cooldown / 60)));
                }
                // Undying Promise: a lethal hit leaves 30% health instead, once per cooldown.
                else if (health != null && PlayerEffects.Undying.TryTrigger(character.Inventory, health._currentValue, final))
                {
                    health.SetCurrentValue(health.GetMax() * (float)UniqueItems.UndyingHealth);
                    final = 0;
                }
            }
            // Self-tests never kill the test character unless a step asks for it: a death leaves every later phase "not in world"
            // (COS-138; the fresh COS-129 save has a 100-health character).
            if (SelfTest.Enabled && !SelfTest.AllowDeath && LocalPlayer.Vitals?.Health is { } h && final >= h._currentValue)
            {
                SelfTest.Log($"self-test: lethal hit of {final:0} held to leave 1 health");
                final = Math.Max(0, h._currentValue - 1);
            }
            LastIncoming = (damage, final, result.Dodged);
            damage = (float)final;
            PlayerHurt?.Invoke(final, result.Dodged, kind);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: player defense hook failed: {e}");
        }
    }

    /// <summary>What the enemy that just hit us does besides damage: Basher stun, Poisonous, and our thorns back at it.</summary>
    private static void OnEnemyHitPlayer(PlayerCharacter character, double damageTaken, DamageKind kind)
    {
        var enemy = PlayerEffects.LastAttacker is { } last && Time.time - last.Time <= 0.75f ? last.Enemy : null;
        // Co-op client: the host ran the enemy's swing, so no hook here saw it; the nearest host enemy in reach is the attacker,
        // and its Basher stun and Poisonous apply to this player (COS-79).
        if (enemy == null && kind == DamageKind.Physical && Net.CoopSession.IsClient) enemy = NearestEnemy(MeleeReach);
        if (enemy == null) return;
        PlayerEffects.LastAttacker = null;
        PlayerEffects.Stun(EnemyHitEffects.StunSeconds(enemy.Profile));
        PlayerEffects.AddPoison(EnemyHitEffects.PoisonPerSecond(enemy.Profile, damageTaken), AbilityTuning.PoisonSeconds);
        double thorns = PlayerHitEffects.Thorns(character.Stats);
        if (thorns > 0 && enemy.IsActive)
        {
            double pierce = enemy.Profile.Armor * Math.Clamp(character.Stats.Get("thorns_armor_piercing"), 0, 1);
            EnemyPatches.DealForced(enemy, new OutgoingHit(thorns, DamageKind.Physical, false, pierce), creditPlayer: true);
        }
    }

    /// <summary>
    /// Co-op client (COS-79): the game hands a client's player the enemy's vanilla swing damage, and no impact hook sees it there,
    /// so the nearest host enemy in reach scales it by its synced profile (the same rule as on the host). It is then the attacker
    /// for Basher and Poisonous.
    /// </summary>
    private static void ScaleClientMelee(ref float damage, DamageKind kind)
    {
        if (kind != DamageKind.Physical || !Net.CoopSession.IsClient) return;
        if (PlayerEffects.LastAttacker is { } last && Time.time - last.Time <= 0.75f) return; // an impact hook already scaled it
        var enemy = NearestEnemy(MeleeReach);
        if (enemy == null) return;
        float scaled = (float)EnemyScaler.DamageToPlayer(enemy.Profile, damage, enemy.Combat.DamageDealtMultiplier);
        if (Net.CoopTest.Enabled) RLog.Msg($"{Net.CoopTestJoin.Tag} client: enemy swing scaled {damage:F1} -> {scaled:F1} by level {enemy.Profile.Level}");
        LastClientMeleeScaled = (damage, scaled);
        damage = scaled;
        PlayerEffects.LastAttacker = (enemy, Time.time);
    }

    /// <summary>Co-op test: the last enemy swing a client scaled (raw, scaled).</summary>
    public static (double Raw, double Scaled)? LastClientMeleeScaled { get; private set; }

    /// <summary>How far an enemy's swing reaches a player, for finding who hit a co-op client.</summary>
    private const float MeleeReach = 4.5f;

    private static EnemyRuntime? NearestEnemy(float range)
    {
        if (LocalPlayer.Transform == null) return null;
        var me = LocalPlayer.Transform.position;
        EnemyRuntime? best = null;
        float bestDist = range;
        foreach (var e in EnemyRegistry.Live)
        {
            if (!e.IsActive) continue;
            float d = Vector3.Distance(e.Actor!.transform.position, me);
            if (d <= bestDist) { bestDist = d; best = e; }
        }
        return best;
    }

    private static readonly int PerfAfterTargetVelocity = Perf.Perf.Register("Hook.PlayerTargetVelocity");

    [HarmonyPatch(typeof(FirstPersonCharacter), nameof(FirstPersonCharacter.GetTargetVelocity))]
    [HarmonyPostfix]
    private static void AfterTargetVelocity(ref Vector3 __result)
    {
        using var perf = Perf.Perf.Scope(PerfAfterTargetVelocity);
        if (Net.ServerConfig.OnDedicatedServer) return;
        if (PlayerEffects.Stunned || PlayerEffects.Rooted) // root and stun both stop movement (COS-89)
        {
            __result = new Vector3(0, __result.y, 0);
            return;
        }
        float m = (float)PlayerState.Character.Stats.Get(StatIds.MovementSpeed);
        if (m <= 0 || Mathf.Approximately(m, 1)) return;
        __result = new Vector3(__result.x * m, __result.y, __result.z * m);
    }

    private static readonly int PerfAfterJumpSpeed = Perf.Perf.Register("Hook.JumpSpeed");

    [HarmonyPatch(typeof(FirstPersonCharacter), nameof(FirstPersonCharacter.CalculateJumpVerticalSpeed))]
    [HarmonyPostfix]
    private static void AfterJumpSpeed(ref float __result)
    {
        using var perf = Perf.Perf.Scope(PerfAfterJumpSpeed);
        if (Net.ServerConfig.OnDedicatedServer) return;
        float m = (float)PlayerState.Character.Stats.Get(StatIds.JumpPower);
        if (m > 0) __result *= m;
        if (PlayerEffects.Stunned || PlayerEffects.Rooted) __result = 0;
    }
}
