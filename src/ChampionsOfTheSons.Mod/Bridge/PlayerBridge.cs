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

    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.Hit))]
    [HarmonyPrefix]
    private static void BeforePlayerHit(ref float damage, ImpactType impactType, bool isDemonic)
    {
        try
        {
            if (damage <= 0) return;
            var kind = isDemonic ? DamageKind.Magic : DamageKind.Physical;
            var character = PlayerState.Character;
            var result = PlayerDefense.Incoming(character.Stats, damage, kind, Rng);
            double final = result.Damage;
            if (result.Dodged) Followups.FollowupsFeature.OnDodge();
            else final = Followups.FollowupsFeature.OnIncoming(final, kind);
            if (!result.Dodged && final > 0)
            {
                OnEnemyHitPlayer(character, final);
                var health = LocalPlayer.Vitals?.Health;
                // Undying Promise: a lethal hit leaves 30% health instead, once per cooldown.
                if (health != null && PlayerEffects.Undying.TryTrigger(character.Inventory, health._currentValue, final))
                {
                    health.SetCurrentValue(health.GetMax() * (float)UniqueItems.UndyingHealth);
                    final = 0;
                }
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
    private static void OnEnemyHitPlayer(PlayerCharacter character, double damageTaken)
    {
        if (PlayerEffects.LastAttacker is not { } last || Time.time - last.Time > 0.75f) return;
        var enemy = last.Enemy;
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

    [HarmonyPatch(typeof(FirstPersonCharacter), nameof(FirstPersonCharacter.GetTargetVelocity))]
    [HarmonyPostfix]
    private static void AfterTargetVelocity(ref Vector3 __result)
    {
        if (PlayerEffects.Stunned)
        {
            __result = new Vector3(0, __result.y, 0);
            return;
        }
        float m = (float)PlayerState.Character.Stats.Get(StatIds.MovementSpeed);
        if (m <= 0 || Mathf.Approximately(m, 1)) return;
        __result = new Vector3(__result.x * m, __result.y, __result.z * m);
    }

    [HarmonyPatch(typeof(FirstPersonCharacter), nameof(FirstPersonCharacter.CalculateJumpVerticalSpeed))]
    [HarmonyPostfix]
    private static void AfterJumpSpeed(ref float __result)
    {
        float m = (float)PlayerState.Character.Stats.Get(StatIds.JumpPower);
        if (m > 0) __result *= m;
        if (PlayerEffects.Stunned) __result = 0;
    }
}
