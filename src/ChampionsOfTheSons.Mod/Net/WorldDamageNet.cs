using ChampionsOfTheSons.Core.Networking;
using RedLoader;

using ChampionsOfTheSons.Mod.Spells;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// COS-213 (Max, 2026-11-24: "in coop spells need to work the same"): the host applies a verified client's <see cref="WorldHit"/>s
/// to its world, and runs their Black Hole pulls. In the Net namespace so a dedicated server (no local player) loads it.
/// </summary>
public sealed class WorldDamageNet : IModFeature
{
    public void Init() => CotsNetEvent.Received += OnNet;

    public void Update(float dt, bool inWorld)
    {
        if (CoopSession.IsHost) WorldDamage.StepRemote(dt);
    }

    private static void OnNet(IMessage message, BoltConnection from)
    {
        if (message is PlayerSpellHit ph && CoopSession.IsClient)
        {
            // A teammate's spell, sent by the host (who checked the game's PvP setting and the distance).
            if (float.IsFinite(ph.Amount) && ph.Amount > 0) WorldDamage.TakePlayerHit(ph.Amount);
            if (CoopTest.Enabled) RLog.Msg($"{CoopTestJoin.Tag} client: player {ph.CasterId}'s spell hit me for {ph.Amount:0.#}");
            return;
        }
        if (message is not WorldHit hit || !CoopSession.IsHost || !CoopFeature.Verified.Contains(from)) return;
        try
        {
            WorldDamage.ApplyRemote(hit, from);
            if (CoopTest.Enabled || CoopSelfTest.ServerDriver)
            {
                var last = WorldDamage.Last;
                RLog.Msg($"{CoopTestJoin.Tag} host: world hit {hit.Kind} r {hit.Radius:0.#} amount {hit.Amount:0}: {last.Actors} actors, {last.Breakables} breakables, {last.Trees} trees ({last.Felled} felled), {last.Structures} structures; PvP {WorldState.Pvp}, player hits sent {WorldDamage.PlayerHitsSent}");
            }
        }
        catch (Exception e)
        {
            RLog.Warning($"{CoopTestJoin.Tag} world hit failed: {e.Message}");
        }
    }
}
