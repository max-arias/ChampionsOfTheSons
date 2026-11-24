using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// The weapon bar (COS-192): weapons bound to number keys. A binding points at the inventory slot that holds the weapon and
/// follows it when slots swap (equipping one bound weapon over another swaps their slots, so both keys keep their weapon).
/// A binding goes when its slot empties.
/// </summary>
public sealed class WeaponBar
{
    /// <summary>Hotkey indexes 0-9 (index 9 is the 0 key), as the game's hotkeys.</summary>
    public const int Keys = 10;

    private readonly Dictionary<int, string> _slots = new();

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

    /// <summary>Whether the item in <paramref name="slot"/> can go on the bar: a weapon (it fits the weapon slot).</summary>
    public static bool CanBind(Inventory inventory, string slot, ContentDb content)
    {
        var item = inventory.Get(slot);
        return item != null && Inventory.Fits(item, WeaponTypes.WeaponSlot, content);
    }

    /// <summary>Binds the weapon in <paramref name="slot"/> to <paramref name="key"/>, replacing that key's weapon and moving it
    /// off any other key. False when the key is out of range or the slot holds no weapon.</summary>
    public bool Bind(int key, string slot, Inventory inventory, ContentDb content)
    {
        if (key < 0 || key >= Keys || !CanBind(inventory, slot, content)) return false;
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

    public void OnEmptied(string slot)
    {
        int k = KeyOf(slot);
        if (k >= 0) Unbind(k);
    }

    /// <summary>Loads saved bindings, keeping only keys in range that point at a weapon.</summary>
    public void Restore(IReadOnlyDictionary<int, string>? saved, Inventory inventory, ContentDb content)
    {
        _slots.Clear();
        if (saved != null)
            foreach (var (k, s) in saved)
                if (k >= 0 && k < Keys && CanBind(inventory, s, content) && KeyOf(s) < 0) _slots[k] = s;
        Changed?.Invoke();
    }
}
