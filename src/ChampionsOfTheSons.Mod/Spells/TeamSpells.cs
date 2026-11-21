using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>
/// COS-23: a teammate's support spell, received as an <see cref="AuraPulse"/> with the caster's numbers, applied to the local
/// player. The caster's machine decides the numbers (its stats and perks); range is checked here against our own position.
/// </summary>
internal static class TeamSpells
{
    /// <summary>How far War Cry and Purge reach teammates (the dome and the flare use their own zone radius).</summary>
    public const float WarCryRadius = 20f, PurgeRadius = 20f;

    public static bool Apply(AuraPulse p)
    {
        var buffs = SpellWorld.Buffs;
        double maxHealth = LocalPlayer.Vitals?.Health?.GetMax() ?? 0;
        switch (p.BuffId)
        {
            case "spell.war_cry":
                buffs.Apply("team.war_cry", p.Seconds, new[]
                {
                    new StatModifier(StatIds.AllDamage, EffectOp.Mult, Math.Max(1, p.Amount)),
                    new StatModifier(StatIds.AttackSpeed, EffectOp.Mult, Math.Max(1, p.Amount2)),
                });
                return true;
            case "spell.purge":
                PlayerEffects.ClearDebuffs();
                if (p.Amount > 0) PlayerEffects.Heal(maxHealth * Math.Clamp(p.Amount, 0, 1));
                return true;
            case "spell.healing_dome":
                // Amount: share of max health per second; Amount2: share of max energy per second; Seconds: the pulse interval.
                PlayerEffects.Heal(maxHealth * Math.Clamp(p.Amount, 0, 1) * p.Seconds);
                if (p.Amount2 > 0) PlayerEffects.AddEnergy(Derived.MaxEnergy(PlayerState.Character.Stats) * Math.Clamp(p.Amount2, 0, 1) * p.Seconds);
                return true;
            case "spell.sun_flare":
                PlayerEffects.Heal(Math.Max(0, p.Amount));
                buffs.Apply("team.flare_boost", 2, SpellTuning.Speed(Math.Max(1, p.Amount2)));
                return true;
            default:
                return false;
        }
    }
}
