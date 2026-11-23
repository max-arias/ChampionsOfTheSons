using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Mod.Weapons;
using RedLoader;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// Weapon types in co-op (COS-93): every player tells the others which type is in their hand; receivers find that
/// player's body (the held weapon nearest to the reported position) and put the same model on its held game weapon.
/// In the Net namespace so a dedicated server, which runs no Weapons feature, still relays.
/// </summary>
public sealed class CoopWeapons : IModFeature
{
    /// <summary>Resend interval while a type is held: keeps late joiners and the position current.</summary>
    private const float Resend = 2f;
    /// <summary>A remote held weapon farther than this from the reported position is someone else's.</summary>
    private const float MatchRadius = 4f;

    private sealed class Remote
    {
        public string TypeId = "", ItemId = "";
        public int Rarity;
        public Vector3 Position;
        public float Seen;
        public WeaponSkin? Skin;
    }

    private static readonly Dictionary<uint, Remote> Players = new();
    private static float _sendTimer, _matchTimer;
    private static string _sentType = "", _sentItem = "";
    private static int _sentRarity;

    public void Init() => CotsNetEvent.Received += OnNet;

    public void OnGameStart()
    {
        foreach (var p in Players.Values) p.Skin?.Remove();
        Players.Clear();
        _sentType = "";
    }

    public void Update(float dt, bool inWorld)
    {
        if (!CoopSession.Active || !inWorld || LocalPlayer.Transform == null) return;
        string type = WeaponModels.Active?.Id ?? "";
        var item = type != "" ? PlayerState.Character.Inventory.Get(Core.Rules.WeaponTypes.WeaponSlot) : null;
        string itemId = item?.BaseId ?? "";
        int rarity = (int)(item?.Rarity ?? 0);
        _sendTimer -= dt;
        if (type != _sentType || itemId != _sentItem || rarity != _sentRarity || (type != "" && _sendTimer <= 0))
        {
            _sendTimer = Resend;
            (_sentType, _sentItem, _sentRarity) = (type, itemId, rarity);
            CoopSession.Broadcast(new HeldWeapon(0, type, CoopSession.ToVec(LocalPlayer.Transform.position), rarity, itemId));
        }
        _matchTimer -= dt;
        if (_matchTimer <= 0)
        {
            _matchTimer = 0.5f;
            MatchRemotes();
            foreach (var p in Players.Values) p.Skin?.Rescan();
        }
        foreach (var p in Players.Values) p.Skin?.Keep();
    }

    private static void OnNet(IMessage message, BoltConnection from)
    {
        if (message is not HeldWeapon held) return;
        try
        {
            if (CoopSession.IsHost)
            {
                if (!CoopFeature.Verified.Contains(from)) return;
                held = held with { PlayerId = CoopSession.IdOf(from) };
                CoopSession.SendToPeers(held, except: from);
            }
            if (!Players.TryGetValue(held.PlayerId, out var p)) Players[held.PlayerId] = p = new Remote();
            p.TypeId = held.TypeId;
            p.ItemId = held.ItemId;
            p.Rarity = held.Rarity;
            p.Position = CoopSession.ToUnity(held.Position);
            p.Seen = Time.realtimeSinceStartup;
        }
        catch (Exception e)
        {
            RLog.Error($"{CoopTestJoin.Tag} held weapon handler failed: {e}");
        }
    }

    /// <summary>Puts on, swaps or removes each remote player's model. No-op on a dedicated server (no bodies to dress).</summary>
    private static float _nextScan;

    /// <summary>The weapon type a remote player shows now, or null (none, or stale: they left or crashed).</summary>
    private static Core.Content.WeaponTypeDef? WantType(Remote p)
    {
        ModContent.Db.WeaponTypes.TryGetValue(p.TypeId, out var type);
        return type != null && Time.realtimeSinceStartup - p.Seen > Resend * 3 ? null : type;
    }

    private static void MatchRemotes()
    {
        if (Players.Count == 0) return;
        // Finding the remote players' held weapons is a scan of every object (~55 ms, COS-140): only when a model has to be put
        // on or changed, at most once a second (every 2 s after a scan that found nothing).
        bool needScan = Players.Values.Any(p =>
        {
            var want = WantType(p);
            if (want == null) return false;
            var look = GlowLook.For(p.ItemId == "" ? null : p.ItemId, (Core.Rarity)p.Rarity);
            return p.Skin == null || !p.Skin.Alive || !p.Skin.Holder!.gameObject.activeInHierarchy || p.Skin.Type != want || p.Skin.Look.Key != look.Key;
        });
        List<HeldWeapons.Held> candidates;
        if (needScan && Time.realtimeSinceStartup >= _nextScan)
        {
            candidates = HeldWeapons.Remote().ToList();
            _nextScan = Time.realtimeSinceStartup + (candidates.Count == 0 ? 2f : 1f);
        }
        else if (needScan) return;
        else candidates = new();
        foreach (var (id, p) in Players)
        {
            var type = WantType(p);
            HeldWeapons.Held? best = null;
            float bestDist = MatchRadius;
            // Nothing scanned (nothing to change): a live skin of the right type keeps its holder.
            if (!needScan && p.Skin != null && p.Skin.Alive && p.Skin.Holder!.gameObject.activeInHierarchy) best = new HeldWeapons.Held(p.Skin.Holder, 0, Core.Rules.HeldKind.None);
            else if (type != null)
                foreach (var c in candidates)
                {
                    if (RealWeapons.Get(c.ItemId)?.TypeId != type.Id) continue; // only our item of that type (COS-187)
                    float d = Vector3.Distance(c.Holder.position, p.Position);
                    if (d < bestDist) { bestDist = d; best = c; }
                }

            var look = type != null ? GlowLook.For(p.ItemId == "" ? null : p.ItemId, (Core.Rarity)p.Rarity) : GlowLook.None;
            var skin = p.Skin;
            if (skin != null && (!skin.Alive || best == null || skin.Holder != best.Value.Holder || skin.Type != type || skin.Look.Key != look.Key))
            {
                if (skin.Holder != null) skin.Remove();
                p.Skin = null;
            }
            if (p.Skin == null && best != null && type != null)
            {
                p.Skin = new WeaponSkin(best.Value.Holder, type, look);
                // Reach stays the wielder's business: their own client computes their hits.
                p.Skin.Apply(stretchReach: false);
                RealWeapons.TrackRemote(best.Value.Holder); // their animator needs the base weapon's id too (COS-186)
                RLog.Msg($"{CoopTestJoin.Tag} player {id} shows {type.Id} on {best.Value.Holder.name} ({p.Skin.Source})");
            }
        }
    }
}
