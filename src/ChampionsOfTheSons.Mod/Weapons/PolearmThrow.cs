using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Core.Saving;
using ChampionsOfTheSons.Mod.Loot;
using RedLoader;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// Throwing a spear-type mod weapon (COS-206). The game does the throw: aim, attack, a pooled projectile flies and on
/// impact turns into the game's own stuck spear pickup, and the held item is used up. For ours the weapon slot's item goes
/// with it: it leaves the slot when the throw uses the game item up, flies as our model on the game's projectile, hits for
/// the weapon's damage times the type's <see cref="WeaponTypeDef.ThrowDamage"/>, and where it stops it becomes a mod ground
/// drop of the same rolled item, stuck there at the angle it landed (the game's spear pickup is removed, so no game spear
/// is gained). Walking onto it puts it back in the weapon slot on its old weapon bar key (the bag if the slot was filled
/// meanwhile). It is never lost: it does not expire, comes back to the bag when its owner dies or is far away, and a save
/// made while it is out holds it in the weapon slot.
/// </summary>
internal static class PolearmThrow
{
    /// <summary>A flight still going after this long ends at the player's feet (fell out of the world, sank).</summary>
    public const float MaxFlightSeconds = 8f;
    /// <summary>Further than this from its owner, the landed weapon comes back to the bag.</summary>
    public const float RecallDistance = 150f;
    /// <summary>A hit this long after the landing still counts as the throw's (the impact and the landing are one moment).</summary>
    private const float HitMemorySeconds = 1.5f;

    private enum Phase { None, Flying, Landed }

    private static Phase _phase;
    private static ItemInstance? _item;
    private static WeaponTypeDef? _type;
    private static int _key = -1;
    private static float _since, _landedAt = -100f;
    private static Transform? _projectile;
    private static GameObject? _model;
    private static Vector3 _lastPos;
    private static Quaternion _lastRot = Quaternion.identity;
    private static int _dropId = -1;
    /// <summary>When the projectile was seen gone (-1: still flying), and when the game's pickup is looked for next.</summary>
    private static float _impactAt = -1, _nextLook;
    private const float PickupWaitSeconds = 0.8f;

    // The weapon in hand, watched for the throw.
    private static RealWeapons.Weapon? _held;
    private static bool _armed;
    private static Transform? _seen;
    /// <summary>The thrown item shows the game's own spear, not the type's model (it then lies as a normal drop).</summary>
    private static bool _gameLook;
    private static Sons.Gameplay.PickUp? _stuckPickup;
    /// <summary>Whether the flight's place is known (the projectile was found); without it the game's pickup says where it landed.</summary>
    private static bool _tracked;

    /// <summary>The thrown item while it is out of the inventory (flying or lying in the world); null otherwise.</summary>
    public static ItemInstance? Out => _phase == Phase.None ? null : _item;

    public static bool Flying => _phase == Phase.Flying;
    public static bool Landed => _phase == Phase.Landed;
    public static int DropId => _dropId;

    /// <summary>What happened last (self-test, log).</summary>
    public static string Last { get; private set; } = "";

    /// <summary>The damage and pierce of a thrown hit arriving now, or null when no throw of ours is in the air.</summary>
    public static (double Damage, double Pierce)? ThrownHit
    {
        get
        {
            if (_item == null || _type == null) return null;
            if (_phase != Phase.Flying && Time.time - _landedAt > HitMemorySeconds) return null;
            return (WeaponTypes.WeaponDamage(_type, _item.Level, _item.Rarity) * _type.ThrowDamage, WeaponTypes.WeaponPierce(_item.Level, _item.Rarity));
        }
    }

    public static void Reset()
    {
        DropStuckPickup();
        if (_model != null) UnityEngine.Object.Destroy(_model);
        _phase = Phase.None;
        _item = null;
        _type = null;
        _projectile = null;
        _model = null;
        _dropId = -1;
        _held = null;
        _armed = false;
        _seen = null;
    }

    /// <summary>Every frame in the world, before the weapon slot sync.</summary>
    public static void Tick()
    {
        try
        {
            switch (_phase)
            {
                case Phase.None: Watch(); break;
                case Phase.Flying: Fly(); break;
                case Phase.Landed: Guard(); break;
            }
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: polearm throw failed: {e}");
            ReturnToBag("after an error");
        }
    }

    // ------------------------------------------------------------------ the throw

    /// <summary>Watches the spear in hand: once it has entered its throw and the game item is used up, it was thrown.</summary>
    private static void Watch()
    {
        var inv = LocalPlayer.Inventory;
        if (inv == null) return;
        var right = inv.RightHandItem;
        var now = right != null ? RealWeapons.Get(right._itemID) : null;
        if (now != null && now.Type?.Base == WeaponBase.Spear)
        {
            _held = now;
            var spear = HeldWeapons.Local()?.Holder.GetComponent<SpearWeaponController>();
            if (spear != null)
            {
                if (spear._isThrowing || spear._inMidThrow) _armed = true;
                else if (!spear._itemWasThrown && spear.IsItemIdle() && _seen == null) _armed = false; // aim let go without a throw
                // The projectile the game just let fly: known a few frames before the item leaves the hand, and it can
                // hit the ground before that (a throw at one's feet), so its place is followed from here on.
                if (_armed && _seen == null)
                {
                    var fired = spear.GetRangedWeapon()?._lastFiredProjectile;
                    if (fired != null && fired.gameObject.activeInHierarchy) _seen = fired.transform;
                }
            }
            Follow();
            return;
        }
        var was = _held;
        bool armed = _armed;
        var seen = _seen;
        Follow();
        _held = null;
        _armed = false;
        _seen = null;
        if (was == null || !armed || inv.AmountOf(was.Id, true, false) > 0) return;

        var pc = PlayerState.Character;
        var item = pc.Inventory.Get(WeaponTypes.WeaponSlot);
        if (item == null || WeaponTypes.Equipped(pc.Inventory, ModContent.Db)?.Id != was.TypeId) return;
        _key = pc.WeaponBar.KeyOf(WeaponTypes.WeaponSlot);
        _item = pc.Inventory.Take(WeaponTypes.WeaponSlot, int.MaxValue);
        if (_item == null) return;
        _type = was.Type;
        _phase = Phase.Flying;
        _since = Time.time;
        _impactAt = -1;
        _nextLook = 0;
        _projectile = seen != null ? seen : FindProjectile();
        _tracked = _projectile != null;
        if (seen == null && _projectile != null) { _lastPos = _projectile.position; _lastRot = _projectile.rotation; }
        else if (_projectile == null) { _lastPos = LocalPlayer.Transform.position; _lastRot = LocalPlayer.Transform.rotation; }
        PerkHooks.ShotTracker.RecordKind(RangedKind.ThrownSpear);
        Dress();
        Last = $"thrown {_item.BaseId}, projectile {(_projectile != null ? _projectile.name : "not found")}";
        RLog.Msg("Champions of the Sons: polearm throw: " + Last);
    }

    /// <summary>Keeps the last place of the projectile seen while the spear was still in hand.</summary>
    private static void Follow()
    {
        if (_seen == null || !_seen.gameObject.activeInHierarchy) return;
        _lastPos = _seen.position;
        _lastRot = _seen.rotation;
    }

    /// <summary>The game's spear projectile just thrown: the nearest live one to the player (looked up once per throw).</summary>
    private static Transform? FindProjectile()
    {
        Transform? best = null;
        float bestD = 12f;
        var from = LocalPlayer.Transform.position;
        foreach (var rb in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
        {
            if (rb == null || !rb.gameObject.activeInHierarchy || !rb.name.Contains("SpearAmmoProjectile", StringComparison.Ordinal)) continue;
            float d = Vector3.Distance(rb.transform.position, from);
            if (d < bestD) { bestD = d; best = rb.transform; }
        }
        return best;
    }

    /// <summary>Our model on the flying projectile: along its flight, middle on the projectile.</summary>
    private static void Dress()
    {
        if (_projectile == null || _type == null) return;
        // An item held as the game's own spear (COS-207) flies as the game's own spear too.
        _gameLook = WeaponSkin.GameTintFor(_item?.BaseId) != null;
        if (_gameLook) return;
        var holder = new GameObject("cots-thrown-tmp");
        var model = Bundles.CotsBundle.Spawn(_type.Model, holder.transform);
        if (model == null) { UnityEngine.Object.Destroy(holder); return; }
        model.transform.SetParent(null, false);
        UnityEngine.Object.Destroy(holder);
        model.name = WeaponSkin.ModelName;
        var b = ModelBounds(model);
        var t = model.transform;
        t.SetParent(_projectile, false);
        float scale = Mathf.Max(1e-4f, _projectile.lossyScale.z);
        float s = b.size.y > 1e-4f ? (float)_type.Length / (b.size.y * scale) : 1;
        // Model +Y (toward the head) along the projectile's forward; its middle on the projectile.
        t.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.forward);
        t.localScale = Vector3.one * s;
        t.localPosition = -(t.localRotation * (b.center * s));
        WeaponGlowFx.Apply(model, GlowLook.For(_item), light: false);
        _model = model;
    }

    private static Bounds ModelBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ------------------------------------------------------------------ flight and landing

    private static void Fly()
    {
        bool alive = _projectile != null && _projectile.gameObject.activeInHierarchy;
        if (alive)
        {
            _lastPos = _projectile!.position;
            _lastRot = _projectile.rotation;
            // The game's spear mesh on the projectile loads a moment later: ours shows, not it.
            if (!_gameLook)
                foreach (var r in _projectile.GetComponentsInChildren<Renderer>(true))
                if (r != null && r.enabled && r.TryCast<TrailRenderer>() == null && (_model == null || !r.transform.IsChildOf(_model.transform))) r.enabled = false;
            if (Time.time - _since < MaxFlightSeconds) return;
            Land(LocalPlayer.Transform.position, timedOut: true, null);
            return;
        }
        // It hit something. The game's stuck spear appears a moment after its projectile goes: wait for it briefly.
        if (_impactAt < 0) _impactAt = Time.time;
        if (Time.time < _nextLook) return;
        _nextLook = Time.time + 0.1f;
        var pickup = FindGamePickup(_lastPos, _tracked);
        if (pickup != null || Time.time - _impactAt > PickupWaitSeconds) Land(_lastPos, timedOut: false, pickup);
    }

    private static void Land(Vector3 at, bool timedOut, Sons.Gameplay.PickUp? gamePickup)
    {
        _impactAt = -1;
        _nextLook = 0;
        var pose = (Pos: at, Rot: _lastRot);
        bool hadModel = _model != null, havePose = hadModel && !timedOut;
        if (_model != null)
        {
            if (!timedOut) pose = (_model.transform.position, _model.transform.rotation);
            UnityEngine.Object.Destroy(_model); // the projectile goes back to the game's pool: nothing of ours stays on it
            _model = null;
        }
        _projectile = null;
        string pickup = "none (timed out)";
        if (!timedOut)
        {
            pickup = _gameLook ? KeepGamePickup(gamePickup, at, out var stuck) : RemoveGamePickup(gamePickup, at, out stuck);
            if (stuck is { } st)
            {
                // The game's stuck spear says exactly where and at what angle it landed.
                at = st.Pos;
                if (!hadModel) pose = (st.Pos, st.Rot * Quaternion.LookRotation(Vector3.right, Vector3.forward));
                havePose = true;
            }
            else if (!_tracked) at = LocalPlayer.Transform.position;
        }
        _landedAt = Time.time;
        var item = _item!;
        if (Net.CoopLoot.RequestDrop(item, at, DropSource.ThrownWeapon))
        {
            // Co-op client: the host makes the drop; it lies there like any drop and is picked up into the bag.
            Last = $"landed, drop asked of the host; game pickup {pickup}";
            _phase = Phase.None;
            _item = null;
        }
        else
        {
            // Our model at the landing pose; an item that looks like the game's spear keeps the game's stuck spear as its look.
            if (_gameLook && _stuckPickup != null) LootVisuals.NextPose = (pose.Pos, pose.Rot, "", 0);
            else if (havePose && _type != null && !_gameLook) LootVisuals.NextPose = (pose.Pos, pose.Rot, _type.Model, (float)_type.Length);
            var drop = LootFeature.SpawnAt(item, at, DropSource.ThrownWeapon);
            LootVisuals.NextPose = null;
            LootFeature.NoAutoPickup.Add(drop.Id);
            _dropId = drop.Id;
            _phase = Phase.Landed;
            Last = $"landed as drop {drop.Id} at {at.ToString("0.0")}; game pickup {pickup}";
        }
        RLog.Msg("Champions of the Sons: polearm throw: " + Last);
    }

    /// <summary>The game's stuck spear pickup of this throw: the one nearest the flight's end; with no flight seen, the one
    /// nearest the player's line of sight. Null when there is none (yet).</summary>
    private static Sons.Gameplay.PickUp? FindGamePickup(Vector3 near, bool tracked)
    {
        Sons.Gameplay.PickUp? best = null;
        float bestD = tracked ? 6f : 60f;
        var from = tracked ? near : LocalPlayer.Transform.position;
        foreach (var p in UnityEngine.Object.FindObjectsOfType<Sons.Gameplay.PickUp>())
        {
            if (p == null || !p.gameObject.activeInHierarchy || !p.name.Contains("SpearPickup", StringComparison.Ordinal)) continue;
            float d = Vector3.Distance(p.transform.position, from);
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    /// <summary>COS-207: for an item that looks like the game's own spear, the game's stuck spear stays as what the player sees,
    /// tinted like the item, but it can no longer be taken (it would give a game spear) and does not decay. It goes when the
    /// drop goes (<see cref="DropStuckPickup"/>).</summary>
    private static string KeepGamePickup(Sons.Gameplay.PickUp? pickup, Vector3 near, out (Vector3 Pos, Quaternion Rot)? stuck)
    {
        stuck = null;
        if (pickup == null) return "not found";
        stuck = (pickup.transform.position, pickup.transform.rotation);
        try
        {
            pickup._forceDisabled = true;
            pickup.SetPreventDecay(true);
            pickup.ToggleIcons(false);
            if (ColorUtility.TryParseHtmlString(WeaponSkin.GameTintFor(_item?.BaseId), out var tint))
                foreach (var r in pickup.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.materials)
                        if (m != null && m.HasProperty("_BaseColor"))
                        {
                            var c = m.GetColor("_BaseColor");
                            m.SetColor("_BaseColor", new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, c.a));
                        }
            _stuckPickup = pickup;
        }
        catch (Exception e)
        {
            return RemoveGamePickup(pickup, near, out _) + " (keeping it failed: " + e.Message + ")";
        }
        return $"{pickup.name} kept as the stuck look, not takeable";
    }

    /// <summary>Removes the game spear that stood in as the stuck look.</summary>
    private static void DropStuckPickup()
    {
        var p = _stuckPickup;
        _stuckPickup = null;
        if (p == null) return;
        RemoveGamePickup(p, p.transform.position, out _);
    }

    /// <summary>Takes the game's stuck spear pickup out of the world (it would give a game spear). Says what it did, and where
    /// the pickup was.</summary>
    private static string RemoveGamePickup(Sons.Gameplay.PickUp? pickup, Vector3 near, out (Vector3 Pos, Quaternion Rot)? stuck)
    {
        stuck = null;
        if (pickup == null) return "not found";
        stuck = (pickup.transform.position, pickup.transform.rotation);
        string what = $"{pickup.name} (item {pickup._itemId}) {Vector3.Distance(pickup.transform.position, near):0.0} m from the flight's end";
        try
        {
            if (!pickup.DestroyThroughBolt(false) && !pickup.TryDespawnToPool()) UnityEngine.Object.Destroy(pickup.gameObject);
        }
        catch (Exception e)
        {
            pickup.gameObject.SetActive(false);
            what += ", removal threw " + e.Message;
        }
        return what + ", removed";
    }

    // ------------------------------------------------------------------ lying in the world

    /// <summary>While it lies in the world: gone from the ground (picked up, see <see cref="OnPickedUp"/>) ends it; a dead or
    /// far-away owner gets it back.</summary>
    private static void Guard()
    {
        var drop = LootFeature.Ground.Get(_dropId);
        if (drop == null) { Clear("the drop is gone"); return; }
        var p = LocalPlayer.Transform;
        if (p == null) return;
        bool dead = false;
        try { dead = LocalPlayer.Stats != null && LocalPlayer.Stats.IsDead(); } catch { /* not ready */ }
        float dist = Vector3.Distance(p.position, new Vector3(drop.X, drop.Y, drop.Z));
        if (dead || dist > RecallDistance) ReturnToBag(dead ? "its owner died" : "its owner went far away");
    }

    /// <summary>A drop was picked up into the bag: our thrown weapon goes back into the weapon slot and onto its key.</summary>
    public static void OnPickedUp(int dropId, ItemInstance item)
    {
        if (_phase != Phase.Landed || dropId != _dropId) return;
        var pc = PlayerState.Character;
        string where = "the bag";
        if (pc.Inventory.Get(WeaponTypes.WeaponSlot) == null)
        {
            var bag = pc.Inventory.Slots.FirstOrDefault(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal) && ReferenceEquals(kv.Value, item)).Key
                ?? pc.Inventory.Slots.FirstOrDefault(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal) && kv.Value.BaseId == item.BaseId
                    && kv.Value.Level == item.Level && kv.Value.Rarity == item.Rarity).Key;
            if (bag != null && pc.Inventory.Move(bag, WeaponTypes.WeaponSlot, ModContent.Db))
            {
                where = "the weapon slot";
                if (_key >= 0) pc.WeaponBar.Bind(_key, WeaponTypes.WeaponSlot, pc.Inventory, ModContent.Db);
                else WeaponSlotSync.BindSlotToFirstKey();
            }
        }
        Clear("picked up into " + where);
    }

    /// <summary>Brings the weapon back without a pickup: removes the drop and puts the item in the bag (or at the feet).</summary>
    public static void ReturnToBag(string why)
    {
        var item = _item;
        DropStuckPickup();
        if (_phase == Phase.Landed && LootFeature.Ground.Get(_dropId) != null) LootFeature.Ground.Remove(_dropId);
        if (_model != null) UnityEngine.Object.Destroy(_model);
        _model = null;
        _projectile = null;
        bool had = _phase != Phase.None && item != null;
        _phase = Phase.None;
        _item = null;
        _dropId = -1;
        if (!had) return;
        var def = ModContent.Db.Items.TryGetValue(item!.BaseId, out var d) ? d : null;
        LootFeature.GiveOrDrop(item, def?.StackSize ?? 1);
        LootFeature.Message(string.Format(UI.UiText.T("ui.followups.picked_up", "Picked up {0}"), UI.UiText.ItemName(item)));
        Last = "returned to the bag: " + why;
        RLog.Msg("Champions of the Sons: polearm throw: " + Last);
    }

    private static void Clear(string why)
    {
        DropStuckPickup();
        _phase = Phase.None;
        _item = null;
        _dropId = -1;
        Last = why;
        RLog.Msg("Champions of the Sons: polearm throw: " + why);
    }

    /// <summary>A save made while the weapon is out holds it: in the weapon slot (on its key) when that is still empty, else in
    /// the first free bag slot. Ground drops are not saved, so a loaded game has it back.</summary>
    public static void AddToSave(PlayerSave save)
    {
        if (Out is not { } item) return;
        string? slot = !save.Slots.ContainsKey(WeaponTypes.WeaponSlot) ? WeaponTypes.WeaponSlot : null;
        for (int i = 0; slot == null && i < LootRoller.BagSlots; i++)
            if (!save.Slots.ContainsKey(Inventory.Bag(i))) slot = Inventory.Bag(i);
        if (slot == null) return; // nowhere to put it: the bag was filled to the last slot while it lay there
        save.Slots[slot] = SavedItem.From(item);
        if (slot == WeaponTypes.WeaponSlot && _key >= 0 && !save.WeaponBar.ContainsKey(_key)) save.WeaponBar[_key] = slot;
    }
}
