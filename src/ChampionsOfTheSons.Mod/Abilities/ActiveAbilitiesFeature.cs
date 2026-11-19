using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using RedLoader;

namespace ChampionsOfTheSons.Mod.Abilities;

/// <summary>
/// Hooks one active ability into <see cref="EnemyAbilities"/>: one <see cref="AbilityInstance"/> per enemy, created on its first
/// tick while alive and active, disposed (objects and effects removed) as soon as the enemy dies, goes dormant or despawns.
/// </summary>
internal sealed class ActiveHandler : IAbilityHandler
{
    private readonly Func<EnemyRuntime, AbilityInstance> _create;
    private readonly Dictionary<EnemyRuntime, AbilityInstance> _live = new();

    public ActiveHandler(EnemyAbility ability, Func<EnemyRuntime, AbilityInstance> create)
    {
        Ability = ability;
        _create = create;
    }

    public EnemyAbility Ability { get; }

    public AbilityInstance? InstanceOf(EnemyRuntime enemy) => _live.GetValueOrDefault(enemy);

    public void OnTick(EnemyRuntime enemy, float dt)
    {
        if (!_live.TryGetValue(enemy, out var instance))
        {
            instance = _create(enemy);
            _live[enemy] = instance;
        }
        try
        {
            instance.Tick(dt);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: {Ability} failed, switching it off for this enemy: {e}");
            Drop(enemy);
            _live[enemy] = new DeadInstance(enemy);
        }
    }

    public void OnDeath(EnemyRuntime enemy) => Drop(enemy);

    /// <summary>Removes the effects of every enemy that is no longer alive and active (dormant, dead, despawned).</summary>
    public void Sweep()
    {
        if (_live.Count == 0) return;
        foreach (var enemy in _live.Keys.ToList())
            if (!enemy.IsActive) Drop(enemy);
    }

    public void Clear()
    {
        foreach (var enemy in _live.Keys.ToList()) Drop(enemy);
    }

    private void Drop(EnemyRuntime enemy)
    {
        if (!_live.Remove(enemy, out var instance)) return;
        try
        {
            instance.Dispose();
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: {Ability} cleanup failed: {e}");
        }
    }

    /// <summary>Placeholder for an instance that threw: stays quiet until the enemy goes inactive and is swept.</summary>
    private sealed class DeadInstance : AbilityInstance
    {
        public DeadInstance(EnemyRuntime enemy) : base(enemy, ActiveAbilityRules.Spec(EnemyAbility.Warp)) { }

        protected override void Step(float dt) { }
    }
}

internal static class ActiveAbilitySystem
{
    private static readonly List<ActiveHandler> Handlers = new();

    public static IReadOnlyList<ActiveHandler> All => Handlers;

    public static void Register(ActiveHandler handler)
    {
        Handlers.Add(handler);
        EnemyAbilities.Register(handler);
    }

    /// <summary>Once per host frame, after the enemies ticked: removes what belongs to inactive enemies and ages the player effects.</summary>
    public static void Sweep(float dt)
    {
        AbilityForces.Age(dt);
        foreach (var h in Handlers) h.Sweep();
    }

    public static ActiveHandler For(EnemyAbility ability) => Handlers.First(h => h.Ability == ability);

    /// <summary>Unity objects the enemy's abilities currently own (0 once it died or went dormant).</summary>
    public static int ObjectCount(EnemyRuntime enemy) => Handlers.Sum(h => h.InstanceOf(enemy)?.ObjectCount ?? 0);

    public static void Reset()
    {
        foreach (var h in Handlers) h.Clear();
        AbilityForces.Reset();
        AbilityDamage.Reset();
    }
}

/// <summary>
/// COS-20: the active enemy affixes — Meteor, Laser, Black Hole, Blizzard, Radiance, Flare, Cataclysms, Warp, Chains, Trapper.
/// Rules and numbers live in Core (<see cref="ActiveAbilityRules"/>); this is the placeholder-visual game side, host only.
/// </summary>
public sealed class ActiveAbilitiesFeature : IModFeature
{
    public void Init()
    {
        ActiveAbilitySystem.Register(new ActiveHandler(EnemyAbility.Meteor, e => new MeteorInstance(e)));
        ActiveAbilitySystem.Register(new ActiveHandler(EnemyAbility.Laser, e => new LaserInstance(e)));
        ActiveAbilitySystem.Register(new ActiveHandler(EnemyAbility.BlackHole, e => new BlackHoleInstance(e)));
        ActiveAbilitySystem.Register(new ActiveHandler(EnemyAbility.Blizzard, e => new BlizzardInstance(e)));
        ActiveAbilitySystem.Register(new ActiveHandler(EnemyAbility.Radiance, e => new RadianceInstance(e)));
        ActiveAbilitySystem.Register(new ActiveHandler(EnemyAbility.Flare, e => new FlareInstance(e)));
        ActiveAbilitySystem.Register(new ActiveHandler(EnemyAbility.FireCataclysm, e => new CataclysmInstance(e, arcane: false)));
        ActiveAbilitySystem.Register(new ActiveHandler(EnemyAbility.ArcaneCataclysm, e => new CataclysmInstance(e, arcane: true)));
        ActiveAbilitySystem.Register(new ActiveHandler(EnemyAbility.Warp, e => new WarpInstance(e)));
        ActiveAbilitySystem.Register(new ActiveHandler(EnemyAbility.Chains, e => new ChainsInstance(e)));
        ActiveAbilitySystem.Register(new ActiveHandler(EnemyAbility.Trapper, e => new TrapperInstance(e)));
    }

    public void RegisterSelfTests() => ActiveAbilitiesSelfTest.Register();
}
