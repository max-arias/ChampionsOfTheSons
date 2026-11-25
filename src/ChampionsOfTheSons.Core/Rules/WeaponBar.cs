using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// The weapon bar, or quickbar (COS-192, COS-212): bag weapons bound to number keys. A binding points at the bag slot that holds
/// the weapon and follows it when slots swap. A binding goes when its slot empties. Only <see cref="WeaponKeys"/> take weapons
/// (keys 1-6 by default); the other number keys stay the game's item hotkeys.
/// </summary>
public sealed class WeaponBar
{
    /// <summary>Hotkey indexes 0-9 (index 9 is the 0 key), as the game's hotkeys.</summary>
    public const int Keys = 10;

    private readonly Dictionary<int, string> _slots = new();

    /// <summary>The keys that take weapons, in order (the mod sets them from its config). Default: keys 1-6.</summary>
    public IReadOnlyList<int> WeaponKeys { get; set; } = new[] { 0, 1, 2, 3, 4, 5 };

    public event Action? Changed;

    /// <summary>Key index → inventory slot.</summary>
    public IReadOnlyDictionary<int, string> Bindings => _slots;

    public string? SlotOf(int key) => _slots.TryGetValue(key, out var s) ? s : null;

    public int KeyOf(string slot)
    {
        foreach (var (k, s) in _slots)
            if (s == slot) return k;
        return -1;
    }

    /// <summary>Whether the item in <paramref name="slot"/> can go on the bar: a weapon in the bag.</summary>
    public static bool CanBind(Inventory inventory, string slot, ContentDb content) =>
        slot.StartsWith("bag.", StringComparison.Ordinal) && WeaponTypes.IsWeapon(inventory.Get(slot), content);

    /// <summary>The first weapon key with nothing on it, or -1 when the bar is full.</summary>
    public int FirstFreeKey()
    {
        foreach (int k in WeaponKeys)
            if (!_slots.ContainsKey(k)) return k;
        return -1;
    }

    /// <summary>Binds the weapon in <paramref name="slot"/> to the first free key (keeps its key if it has one). The key, or -1.</summary>
    public int BindFirstFree(string slot, Inventory inventory, ContentDb content)
    {
        int had = KeyOf(slot);
        if (had >= 0) return had;
        int key = FirstFreeKey();
        return key >= 0 && Bind(key, slot, inventory, content) ? key : -1;
    }

    /// <summary>Right-click on a bag weapon: unbinds it when bound, else binds it to the first free key. The key now (-1: unbound or full).</summary>
    public int Toggle(string slot, Inventory inventory, ContentDb content)
    {
        int had = KeyOf(slot);
        if (had >= 0) { Unbind(had); return -1; }
        return BindFirstFree(slot, inventory, content);
    }

    /// <summary>Swaps what two keys hold (either may be empty). False when a key is not a weapon key.</summary>
    public bool SwapKeys(int a, int b)
    {
        if (a == b || !WeaponKeys.Contains(a) || !WeaponKeys.Contains(b)) return false;
        var sa = SlotOf(a);
        var sb = SlotOf(b);
        _slots.Remove(a);
        _slots.Remove(b);
        if (sa != null) _slots[b] = sa;
        if (sb != null) _slots[a] = sb;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Binds the weapon in <paramref name="slot"/> to <paramref name="key"/>, replacing that key's weapon and moving it
    /// off any other key. False when the key is out of range or the slot holds no weapon.</summary>
    public bool Bind(int key, string slot, Inventory inventory, ContentDb content)
    {
        if (!WeaponKeys.Contains(key) || !CanBind(inventory, slot, content)) return false;
        int old = KeyOf(slot);
        if (old == key) return true;
        if (old >= 0) _slots.Remove(old);
        _slots[key] = slot;
        Changed?.Invoke();
        return true;
    }

    public bool Unbind(int key)
    {
        if (!_slots.Remove(key)) return false;
        Changed?.Invoke();
        return true;
    }

    public void OnSwapped(string a, string b)
    {
        bool changed = false;
        foreach (var k in _slots.Keys.ToList())
        {
            if (_slots[k] == a) { _slots[k] = b; changed = true; }
            else if (_slots[k] == b) { _slots[k] = a; changed = true; }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>The bag was reordered (sort): every binding follows its item to its new slot (old slot -> new slot).</summary>
    public void Remap(IReadOnlyDictionary<string, string> moved)
    {
        bool changed = false;
        foreach (var k in _slots.Keys.ToList())
            if (moved.TryGetValue(_slots[k], out var to) && to != _slots[k]) { _slots[k] = to; changed = true; }
        if (changed) Changed?.Invoke();
    }

    public void OnEmptied(string slot)
    {
        int k = KeyOf(slot);
        if (k >= 0) Unbind(k);
    }

    /// <summary>Loads saved bindings, keeping only weapon keys that point at a bag weapon.</summary>
    public void Restore(IReadOnlyDictionary<int, string>? saved, Inventory inventory, ContentDb content)
    {
        _slots.Clear();
        if (saved != null)
            foreach (var (k, s) in saved)
                if (WeaponKeys.Contains(k) && CanBind(inventory, s, content) && KeyOf(s) < 0) _slots[k] = s;
        Changed?.Invoke();
    }
}
