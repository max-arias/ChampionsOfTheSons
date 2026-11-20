using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Loot;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Audio;

/// <summary>COS-101 in game: every cue loads into FMOD, events start their cue, loops start and stop, the mod volume mutes.
/// Whether a sound is audible cannot be checked headless; the log line with the FMOD channel state is for a windowed run.</summary>
internal static class AudioSelfTest
{
    private static AudioLoop? _loop;
    private static float _volumeBefore;

    public static void Register()
    {
        SelfTest.Add(2f, "audio: cues registered", Registered);
        SelfTest.Add(2.5f, "audio: every cue plays 2D and 3D", PlayAll);
        SelfTest.Add(3f, "audio: level up plays its cue", LevelUp);
        SelfTest.Add(3.5f, "audio: a drop plays its rarity cue", Drop);
        SelfTest.Add(4f, "audio: loop starts", LoopStart);
        SelfTest.Add(5f, "audio: loop stops", LoopStop);
        SelfTest.Add(5.5f, "audio: mod volume 0 mutes", Mute);
    }

    private static void Registered()
    {
        SelfTest.Check("audio: sounds registered", ModAudio.Ready && ModAudio.KnownCues.Count >= 20, $"{ModAudio.KnownCues.Count} cues");
        foreach (var cue in new[] { SoundCues.LevelUp, SoundCues.Pickup, SoundCues.PerkBought, SoundCues.Craft, SoundCues.UiClick, SoundCues.UiOpen,
                     SoundCues.Drop(Rarity.Common), SoundCues.Drop(Rarity.Legendary), SoundCues.Spell("spell.firebolt"), SoundCues.Telegraph(EnemyAbility.Meteor),
                     SoundCues.Impact(EnemyAbility.Meteor), SoundCues.LoopWind, SoundCues.LoopFire })
            SelfTest.Check($"audio: cue {cue} exists", ModAudio.KnownCues.Contains(cue));
        SelfTest.Log($"audio: game sfx volume {SonsSdk.SoundTools.SfxVolume:0.##}, mod volume {Config.SoundVolume.Value:0.##}, effective {ModAudio.Volume:0.##}");
    }

    private static void PlayAll()
    {
        int before = ModAudio.PlayedCount;
        var at = LocalPlayer.Transform.position + LocalPlayer.Transform.forward * 3;
        var cues = ModAudio.KnownCues.Where(c => !c.StartsWith("loop_", StringComparison.Ordinal)).ToList();
        foreach (var cue in cues) ModAudio.PlayAt(cue, at, 20f, 0.2f);
        SelfTest.Check("audio: every one-shot started in 3D", ModAudio.PlayedCount - before == cues.Count, $"{ModAudio.PlayedCount - before} of {cues.Count}");
        // 2D right after is throttled per cue (same frame); the 2D path is covered by the level-up step.
    }

    private static void LevelUp()
    {
        PlayerState.FromSave(new Core.Saving.PlayerSave { Level = 3, Xp = 0 });
        var pc = PlayerState.Character;
        int before = ModAudio.PlayedCount;
        pc.AddXp(XpCurve.Goal(pc.Progress.Level) / Math.Max(0.01, pc.Stats.Get(StatIds.ExpGain)) + 1);
        SelfTest.Check("audio: level up sound", ModAudio.PlayedCount > before && pc.Progress.Level >= 4, $"last {ModAudio.LastCue}, level {pc.Progress.Level}");
    }

    private static void Drop()
    {
        LootFeature.ClearAll();
        var item = TestKit.Give(rarity: (int)Rarity.Legendary);
        PlayerState.Character.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        var p = LocalPlayer.Transform.position + LocalPlayer.Transform.forward * 4;
        LootFeature.Ground.Add(item, p.x, p.y, p.z);
        SelfTest.Check("audio: a drop plays its rarity cue", ModAudio.LastCue == SoundCues.Drop(item.Rarity), $"{item.Rarity}: {ModAudio.LastCue ?? "none"}");
        LootFeature.ClearAll();
    }

    private static void LoopStart()
    {
        _loop = ModAudio.StartLoop(SoundCues.LoopWind, LocalPlayer.Transform.position, 30f, 0.3f);
        SelfTest.Check("audio: loop started", _loop != null && ModAudio.ActiveLoops >= 1);
        SelfTest.Check("audio: FMOD reports the loop channel playing", _loop?.Playing == true, $"playing={_loop?.Playing}");
    }

    private static void LoopStop()
    {
        _loop?.Move(LocalPlayer.Transform.position + Vector3.up);
        _loop?.Stop();
        ModAudio.Tick();
        SelfTest.Check("audio: loop stopped and dropped", _loop != null && !_loop.Playing && ModAudio.ActiveLoops == 0, $"{ModAudio.ActiveLoops} loops");
        _loop = null;
    }

    private static void Mute()
    {
        _volumeBefore = Config.SoundVolume.Value;
        Config.SoundVolume.Value = 0;
        int before = ModAudio.PlayedCount;
        ModAudio.Play(SoundCues.Craft);
        SelfTest.Check("audio: nothing plays at mod volume 0", ModAudio.PlayedCount == before);
        Config.SoundVolume.Value = _volumeBefore;
    }
}
