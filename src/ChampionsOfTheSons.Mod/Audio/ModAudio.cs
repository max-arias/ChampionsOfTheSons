using FMODCustom;
using RedLoader;
using SonsSdk;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Audio;

/// <summary>
/// COS-101: the mod's sound layer. Cues are the embedded <c>sounds/&lt;cue&gt;.ogg</c> clips (tools/sounds/build_sounds.py), registered
/// with the game's FMOD system through SonsSdk <see cref="SoundTools"/>, once as 2D and once as 3D. Every sound follows the game's
/// SFX volume (which includes master) times the mod volume (<see cref="Config.SoundVolume"/>). Nothing is registered on a dedicated
/// server (the audio feature does not load there), so every call is a no-op until <see cref="Init"/> ran.
/// </summary>
public static class ModAudio
{
    public const float DefaultRange = 40f;

    /// <summary>Positioned sounds play at full volume within this many metres, then fade with distance.</summary>
    public const float NearDistance = 3f;

    private static readonly HashSet<string> Cues = new();
    private static readonly List<AudioLoop> Loops = new();
    private static readonly Dictionary<string, float> LastStart = new();
    private static readonly Dictionary<string, Sound> Sounds2D = new();
    private static readonly Dictionary<string, Sound> Sounds3D = new();

    /// <summary>The same cue does not start twice within this many seconds (five drops from one kill make one sound).</summary>
    private const float Repeat = 0.06f;

    public static bool Ready { get; private set; }

    /// <summary>The channel of the last <see cref="PlayAt"/> (self-test).</summary>
    internal static Channel LastChannel;

    /// <summary>The last positioned sound's 3D setup as FMOD reports it (self-test).</summary>
    public static string LastChannelInfo()
    {
        var ch = LastChannel;
        ch.getMode(out var mode);
        ch.get3DMinMaxDistance(out float min, out float max);
        ch.get3DLevel(out float level);
        ch.get3DSpread(out float spread);
        ch.get3DAttributes(out var pos, out _, out _);
        return $"mode {mode}, min {min:0.##} max {max:0.##}, 3D level {level:0.##}, spread {spread:0.#}, at ({pos.x:0.#}, {pos.y:0.#}, {pos.z:0.#})";
    }

    /// <summary>FMOD's core listener position now (self-test).</summary>
    public static Vector3? ListenerPosition() =>
        Ready && SoundTools.CoreSystem.Value.get3DListenerAttributes(0, out var p, out _, out _, out _) == RESULT.OK ? new Vector3(p.x, p.y, p.z) : null;



    /// <summary>Sounds started so far and the last cue (self-tests).</summary>
    public static int PlayedCount { get; private set; }
    public static string? LastCue { get; private set; }

    public static IReadOnlyCollection<string> KnownCues => Cues;
    public static int ActiveLoops => Loops.Count;

    /// <summary>Game SFX volume (already times master) times the mod volume.</summary>
    public static float Volume
    {
        get
        {
            float mod = 1f;
            try { mod = Mathf.Clamp01(Config.SoundVolume.Value); } catch { /* config not ready */ }
            float sfx = 1f;
            try { sfx = SoundTools.SfxVolume; } catch { /* settings not loaded */ }
            return Mathf.Clamp01(sfx) * mod;
        }
    }

    internal static void Init()
    {
        if (Ready || Net.ServerConfig.IsDedicatedServer) return;
        var asm = typeof(ModAudio).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith("sounds/", StringComparison.Ordinal) || !name.EndsWith(".ogg", StringComparison.Ordinal)) continue;
            string cue = name.Substring("sounds/".Length, name.Length - "sounds/".Length - ".ogg".Length);
            try
            {
                using var s = asm.GetManifestResourceStream(name)!;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                var bytes = ms.ToArray();
                Sounds2D[cue] = Create(bytes, MODE._2D);
                // Inverse rolloff tapered to silence at the range (Max 2026-11-21: drops sounded "in the ear"): full volume within
                // NearDistance, then quieter with distance as the game's own sounds do; linear rolloff over 70 m kept a drop 10 m away
                // at almost full volume, so nothing told the ear how far it was.
                Sounds3D[cue] = Create(bytes, MODE._3D | MODE._3D_INVERSETAPEREDROLLOFF);
                Cues.Add(cue);
            }
            catch (Exception e)
            {
                RLog.Error($"Champions of the Sons: sound {cue} failed to load: {e.Message}");
            }
        }
        Ready = Cues.Count > 0;
        RLog.Msg($"Champions of the Sons: {Cues.Count} sounds registered");
    }

    /// <summary>
    /// A sample sound from an in-memory Ogg file. Made here rather than with SoundTools.RegisterSound(id, bytes): in SonsSdk 0.8.6 that
    /// overload leaves out OPENMEMORY (FMOD then reads the bytes as a file name) and ignores use3d.
    /// </summary>
    private static Sound Create(byte[] bytes, MODE dims)
    {
        var info = new CREATESOUNDEXINFO { cbsize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(CREATESOUNDEXINFO)), length = (uint)bytes.Length };
        var result = SoundTools.CoreSystem.Value.createSound(bytes, MODE.OPENMEMORY | MODE.CREATESAMPLE | MODE.LOOP_OFF | dims, ref info, out var sound);
        if (result != RESULT.OK) throw new InvalidOperationException($"FMOD createSound: {result}");
        return sound;
    }

    private static bool CanPlay(string? cue, float volume, out float v)
    {
        v = Volume * volume;
        if (!Ready || cue == null || !Cues.Contains(cue) || v <= 0.001f) return false;
        float now = Time.unscaledTime;
        if (LastStart.TryGetValue(cue, out var last) && now - last < Repeat && now >= last) return false;
        LastStart[cue] = now;
        return true;
    }

    /// <summary>A one-shot that is not placed in the world (UI, the local player's own casts and rewards).</summary>
    public static void Play(string? cue, float volume = 1f, float pitch = 1f)
    {
        if (!CanPlay(cue, volume, out var v)) return;
        try
        {
            SoundTools.PlaySound(Sounds2D[cue!], v, pitch);
            Note(cue!);
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: sound {cue} failed: {e.Message}");
        }
    }

    /// <summary>A one-shot at a world position, audible up to <paramref name="range"/> metres.</summary>
    public static void PlayAt(string? cue, Vector3 position, float range = DefaultRange, float volume = 1f, float pitch = 1f)
    {
        if (!CanPlay(cue, volume, out var v)) return;
        try
        {
            LastChannel = SoundTools.PlaySound(Sounds3D[cue!], position, range, v, pitch);
            LastChannel.set3DMinMaxDistance(NearDistance, range);
            Note(cue!);
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: sound {cue} failed: {e.Message}");
        }
    }

    /// <summary>A looping sound at a world position; move it with <see cref="AudioLoop.Move"/>, end it with <see cref="AudioLoop.Stop"/>.
    /// Null when sound is off or unavailable.</summary>
    public static AudioLoop? StartLoop(string? cue, Vector3 position, float range = DefaultRange, float volume = 1f)
    {
        if (!CanPlay(cue, volume, out var v)) return null;
        try
        {
            var ch = SoundTools.PlaySound(Sounds3D[cue!], position, range, v, 1f);
            ch.set3DMinMaxDistance(NearDistance, range);
            ch.setMode(MODE.LOOP_NORMAL);
            ch.setLoopCount(-1);
            var loop = new AudioLoop(ch, volume);
            Loops.Add(loop);
            Note(cue!);
            return loop;
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: loop {cue} failed: {e.Message}");
            return null;
        }
    }

    private static void Note(string cue)
    {
        PlayedCount++;
        LastCue = cue;
    }

    /// <summary>Each frame: loops follow volume changes; finished ones are dropped.</summary>
    internal static void Tick()
    {
        if (Loops.Count == 0) return;
        float vol = Volume;
        for (int i = Loops.Count - 1; i >= 0; i--)
            if (!Loops[i].Update(vol)) Loops.RemoveAt(i);
    }

    internal static void StopAllLoops()
    {
        foreach (var l in Loops.ToArray()) l.Stop();
        Loops.Clear();
    }
}

/// <summary>A running looped sound.</summary>
public sealed class AudioLoop
{
    private Channel _channel;
    private readonly float _volume;
    private bool _stopped;

    internal AudioLoop(Channel channel, float volume)
    {
        _channel = channel;
        _volume = volume;
    }

    public bool Playing
    {
        get
        {
            if (_stopped) return false;
            try
            {
                _channel.isPlaying(out bool p);
                return p;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Move(Vector3 position)
    {
        if (_stopped) return;
        try { SoundTools.SetPosition(ref _channel, position.x, position.y, position.z); } catch { /* channel gone */ }
    }

    public void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        try { _channel.stop(); } catch { /* channel gone */ }
    }

    internal bool Update(float volume)
    {
        if (_stopped) return false;
        try { _channel.setVolume(volume * _volume); } catch { return false; }
        return true;
    }
}
