using Bolt;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Net;
using RedLoader;
using SonsSdk;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.HostRules;

/// <summary>
/// COS-11: the difficulty picker (once per world without a chosen difficulty, only while the player is controllable), the host
/// settings panel, their keybinds (in RedLoader's mod settings) and the host → client sync of the choice.
/// </summary>
public sealed class WorldRulesFeature : IModFeature
{
    internal static DifficultyPicker Picker { get; private set; } = null!;
    internal static HostSettingsPanel Settings { get; private set; } = null!;

    public static KeybindConfigEntry HostSettingsKey { get; private set; } = null!;

    private static bool _pickerHandled;
    private static float _syncTimer;

    /// <summary>The picker opens by itself only for the host, in a world with no chosen difficulty, once, while the player can move.
    /// (The original opened it during cutscenes and the ending, original #13.)</summary>
    public static bool ShouldAutoOpen(bool inWorld, bool isHost, bool difficultyChosen, bool alreadyHandled, bool playerControllable) =>
        inWorld && isHost && !difficultyChosen && !alreadyHandled && playerControllable;

    public static bool PlayerControllable
    {
        get
        {
            try
            {
                return GameState.IsPlayerControllable;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Init()
    {
        Picker = new DifficultyPicker();
        Picker.Build();
        Picker.Chosen += OnChosen;
        Settings = new HostSettingsPanel();
        Settings.Build();

        HostSettingsKey = Config.Category.CreateKeybindEntry("host_settings_key", "o", "Host settings", "Open the host settings (difficulty, multipliers, enemy abilities).");
        HostSettingsKey.SetScope(needsPlayerControllable: true);
        HostSettingsKey.Notify(OpenSettings);

        CotsNetEvent.Received += OnNet;
    }

    public void OnGameStart() => _pickerHandled = false;

    public void Update(float dt, bool inWorld)
    {
        if (Picker == null) return;

        // Close with Escape; the panels take the cursor while open.
        if (Settings.IsOpen && Input.GetKeyDown(KeyCode.Escape)) Settings.Close();
        else if (Settings.IsOpen && Mod.Settings.ModKeys.PressedThisFrame(HostSettingsKey)) Settings.Close();
        if (Picker.IsOpen && Input.GetKeyDown(KeyCode.Escape)) Picker.Close();

        if (!SelfTest.Enabled &&
            ShouldAutoOpen(inWorld, EnemyRegistry.IsAuthority, WorldState.DifficultyChosen, _pickerHandled, PlayerControllable))
        {
            _pickerHandled = true;
            Picker.Open();
        }

        // The host repeats its settings now and then so players who join later get them too.
        if (inWorld && BoltNetwork.isRunning && BoltNetwork.isServer && WorldState.DifficultyChosen)
        {
            _syncTimer -= dt;
            if (_syncTimer <= 0)
            {
                _syncTimer = 10;
                Broadcast();
            }
        }
    }

    private static void OpenSettings()
    {
        if (!EnemyRegistry.IsAuthority || Settings.IsOpen) return;
        Settings.Open();
    }

    private static void OnChosen(DifficultyTier tier)
    {
        _pickerHandled = true;
        WorldState.SetDifficulty(tier);
        Broadcast();
        RLog.Msg($"Champions of the Sons: difficulty {tier.Name} chosen");
    }

    private static void Broadcast()
    {
        try
        {
            if (!BoltNetwork.isRunning || !BoltNetwork.isServer) return;
            CotsNetEvent.Instance.Send(new WorldSettings(WorldState.Rules.Difficulty.Id, WorldState.FriendlyFire), GlobalTargets.Others);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: could not send world settings: {e.Message}");
        }
    }

    private static void OnNet(IMessage message, BoltConnection from)
    {
        if (message is not WorldSettings ws || EnemyRegistry.IsAuthority) return;
        var tier = Difficulties.ById(ws.DifficultyId);
        if (tier != null && (!WorldState.DifficultyChosen || WorldState.Rules.Difficulty != tier)) WorldState.SetDifficulty(tier);
    }

    public void RegisterSelfTests() => WorldRulesSelfTest.Register();
}
