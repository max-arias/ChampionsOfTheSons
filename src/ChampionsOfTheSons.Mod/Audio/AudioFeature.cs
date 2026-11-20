using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Mod.Loot;
using ChampionsOfTheSons.Mod.Net;
using ChampionsOfTheSons.Mod.Spells;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Audio;

/// <summary>
/// COS-101: registers the sounds and plays them on the mod's events. Panels, perks, crafting and enemy abilities call
/// <see cref="ModAudio"/> where they act; the events that already exist are hooked here. Not loaded on a dedicated server.
/// </summary>
internal sealed class AudioFeature : IModFeature
{
    /// <summary>How far a drop is heard: COTF's drop sounds carried far so a kill out of sight still told you something dropped.</summary>
    public const float DropRange = 70f;

    private bool _panelWasOpen;

    public void Init()
    {
        ModAudio.Init();
        if (!ModAudio.Ready) return;

        PlayerState.Character.LeveledUp += _ => ModAudio.Play(SoundCues.LevelUp);
        LootFeature.Ground.Added += d => ModAudio.PlayAt(SoundCues.Drop(d.Item.Rarity), new Vector3(d.X, d.Y, d.Z), DropRange);
        // Picked up here, or claimed by a teammate on the host: heard where the drop lay.
        LootFeature.Ground.Removed += (d, picked) =>
        {
            if (picked) ModAudio.PlayAt(SoundCues.Pickup, new Vector3(d.X, d.Y, d.Z), 15f);
        };
        SpellCaster.Casted += c => ModAudio.Play(SoundCues.Spell(c.SpellId));
        CotsNetEvent.Received += (m, _) =>
        {
            // A client's own pickup: the host took the drop off the ground and sent the item.
            if (m is PickupGranted && CoopSession.IsClient) ModAudio.Play(SoundCues.Pickup);
        };
        SelfTest.PhaseReset += ModAudio.StopAllLoops;
    }

    public void OnGameStart() => ModAudio.StopAllLoops();

    public void Update(float dt, bool inWorld)
    {
        ModAudio.Tick();
        bool open = UI.CotsUi.AnyPanelOpen || Book.BookHost.ModPageActive;
        if (open && !_panelWasOpen) ModAudio.Play(SoundCues.UiOpen);
        _panelWasOpen = open;
    }

    public void RegisterSelfTests() => AudioSelfTest.Register();
}
