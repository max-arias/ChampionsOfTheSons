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
                    new StatModifier(StatIds.MovementSpeed, EffectOp.Mult, Math.Max(1, p.Amount2)),
                });
                return true;
            case "spell.purge":
                PlayerEffects.ClearDebuffs();
                buffs.Apply("team.purge_immunity", SpellTuning.PurgeImmunitySeconds, SpellTuning.PurgeImmunity());
                if (p.Amount > 0 && LocalPlayer.Vitals?.Health is { } health)
                {
                    var (hp, energy) = SpellTuning.PurgeHeal(health._currentValue, health.GetMax(), SpellCaster.Energy, SpellCaster.MaxEnergy);
                    PlayerEffects.Heal(hp);
                    PlayerEffects.AddEnergy(energy);
                }
                return true;
            case "spell.healing_dome":
                // Amount: share of the caster's max health per second; Amount2: share of max energy per second; Seconds: the pulse interval.
                PlayerEffects.Heal(maxHealth * Math.Clamp(p.Amount, 0, 1) * p.Seconds);
                if (p.Amount2 > 0) PlayerEffects.AddEnergy(Derived.MaxEnergy(PlayerState.Character.Stats) * Math.Clamp(p.Amount2, 0, 1) * p.Seconds);
                return true;
            case "spell.sun_flare":
                PlayerEffects.Heal(Math.Max(0, p.Amount));
                buffs.Apply("team.flare_boost", 1, new[]
                {
                    new StatModifier(StatIds.MovementSpeed, EffectOp.Mult, Math.Max(1, p.Amount2)),
                    new StatModifier(StatIds.AllDamageTaken, EffectOp.Mult, SpellTuning.SunFlareResistance),
                });
                return true;
            default:
                return false;
        }
    }
}
