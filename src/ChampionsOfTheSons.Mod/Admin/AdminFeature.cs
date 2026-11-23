using System.Reflection;
using System.Text;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Net;
using RedLoader;
using Sons.Ai.Vail;
using Sons.Multiplayer;
using SonsSdk;
using SonsSdk.Networking;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Admin;

/// <summary>
/// COS-99: admin and cheat commands, for the host or a dedicated-server admin only (Max, 2026-11-20). Clients cannot use them.
/// <list type="bullet">
/// <item>Local console (the game's debug console, through SonsSdk): <c>cots &lt;command&gt; ...</c>. Runs in single player and
/// on the co-op host; a client's console refuses.</item>
/// <item>Dedicated server: the same commands as server chat <c>/cots ...</c> (SonsSdk <see cref="ServerCommands"/>), which the
/// game only lets through for players with the Admin role; on a listen server chat commands are refused (the host has its console).</item>
/// </list>
/// Commands that change a player run on the host; for a client the host sends an <see cref="AdminGrant"/>, which the client
/// accepts only from the host's connection.
/// </summary>
public sealed class AdminFeature : IModFeature
{
    public const string Tag = "[COTS-ADMIN]";

    private static ConfigEntry<bool>? _enabled;

    /// <summary>Last reply of <see cref="Run"/> (self-tests).</summary>
    public static string LastReply { get; private set; } = "";

    public static bool CommandsEnabled => _enabled?.Value ?? true;

    private sealed record Target(BoltConnection? Connection, string Name)
    {
        public bool Local => Connection == null;
    }

    public void Init()
    {
        var category = ConfigSystem.CreateFileCategory("ChampionsOfTheSonsAdmin", "Champions of the Sons (admin commands)", "ChampionsOfTheSonsAdmin.cfg");
        _enabled = category.CreateEntry("commands_enabled", true, "Admin commands",
            "Allow the 'cots' console and server-chat commands (host and dedicated-server admins only; clients never).");

        CotsNetEvent.Received += OnNet;
        if (!ServerConfig.IsDedicatedServer && !ServerConfig.OnDedicatedServer) RegisterLocalConsole();
        try
        {
            ServerCommands.RegisterCommand("cots", PlayerRoles.Admin, OnServerChat);
        }
        catch (Exception e)
        {
            RLog.Warning($"{Tag} server chat command not registered: {e.Message}");
        }
    }

    private static bool _consoleRegistered;

    public void OnGameStart()
    {
        AdminState.Reset();
        // The game's debug console may not exist yet when the SDK initializes: try again once the world is up.
        if (!_consoleRegistered && !ServerConfig.IsDedicatedServer && !ServerConfig.OnDedicatedServer) RegisterLocalConsole();
    }

    public void Update(float dt, bool inWorld)
    {
        if (inWorld) AdminState.Update();
    }

    /// <summary>SonsMod.RegisterCommand is protected on the mod class; call it for the mod instance so the wiring stays in this folder.</summary>
    private static void RegisterLocalConsole()
    {
        try
        {
            var method = typeof(SonsMod).GetMethod("RegisterCommand", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(string), typeof(Func<string, bool>) }, null);
            if (method == null || ChampionsOfTheSonsMod.Instance == null)
            {
                RLog.Warning($"{Tag} SonsMod.RegisterCommand not found: no console commands");
                return;
            }
            method.Invoke(ChampionsOfTheSonsMod.Instance, new object[] { "cots", (Func<string, bool>)OnLocalConsole });
            _consoleRegistered = true;
            RLog.Msg($"{Tag} console command 'cots' registered (type 'cots help')");
        }
        catch (Exception e)
        {
            var inner = e is TargetInvocationException { InnerException: { } ie } ? ie : e;
            RLog.Warning($"{Tag} console command not registered yet: {inner.GetType().Name}: {inner.Message}");
        }
    }

    private static bool OnLocalConsole(string args)
    {
        var source = CoopSession.IsClient ? CommandSource.Client : CoopSession.IsHost ? CommandSource.Host : CommandSource.SinglePlayer;
        return Run(args, source, null);
    }

    private static ServerCommands.EExecutionResult OnServerChat(ServerCommands.CommandArgs args)
    {
        // The game checked the Admin role before calling this. A listen server's own chat is refused: its host has the console,
        // and its role list does not tell the host from a client.
        var source = ServerConfig.IsDedicatedServer ? CommandSource.ServerAdmin : CommandSource.ServerPlayer;
        var sender = ServerConfig.IsDedicatedServer ? ConnectionOf(args.SteamId) : null;
        bool ok = Run(string.Join(' ', args.Args ?? Array.Empty<string>()), source, sender);
        return ok ? ServerCommands.EExecutionResult.Success : ServerCommands.EExecutionResult.CommandFailed;
    }

    private static BoltConnection? ConnectionOf(ulong steamId)
    {
        foreach (var c in CoopSession.Peers)
        {
            try
            {
                if (c.RemoteEndPoint.SteamId.Id == steamId) return c;
            }
            catch
            {
                // endpoint without a Steam id
            }
        }
        return null;
    }

    /// <summary>
    /// Runs one command line. <paramref name="sender"/> is the admin's own connection on a dedicated server (the default target
    /// of player commands there); null for the local console. Returns false when the command was refused or failed.
    /// </summary>
    public static bool Run(string line, CommandSource source, BoltConnection? sender)
    {
        if (!AdminAccess.MayRun(source, CommandsEnabled))
        {
            return Reply(false, CommandsEnabled
                ? "admin commands are for the host or a server admin only"
                : "admin commands are switched off (ChampionsOfTheSonsAdmin.cfg)");
        }
        var (name, args) = AdminAccess.Parse(line);
        try
        {
            return name switch
            {
                "help" => Reply(true, Help()),
                "xp" => Grant(AdminAction.AddXp, args, 0, sender, minArgs: 1),
                "level" => Grant(AdminAction.SetLevel, args, 0, sender, minArgs: 1),
                "addlevels" => Grant(AdminAction.AddLevels, args, 0, sender, minArgs: 1),
                "points" => Grant(AdminAction.AddPerkPoints, args, 0, sender, minArgs: 1),
                "respec" => Grant(AdminAction.Respec, args, -1, sender),
                "give" => Give(args, sender),
                "giveall" => GiveAll(args, sender),
                "learnall" => Grant(AdminAction.LearnAllSpells, args, -1, sender),
                "playtest" => Playtest(args, sender),
                "mastery" => Mastery(args, sender),
                "heal" => Grant(AdminAction.Heal, args, -1, sender),
                "god" => Toggle(AdminAction.God, args, sender, AdminState.God),
                "nocooldowns" => Toggle(AdminAction.NoCooldowns, args, sender, AdminState.NoCooldowns),
                "energy" => Toggle(AdminAction.InfiniteEnergy, args, sender, AdminState.InfiniteEnergy),
                "spawn" => Spawn(args, sender),
                "extralevels" => SetExtraLevels(args),
                "list" => List(args),
                "players" => Reply(true, Players()),
                "testsave" => TestSave(source),
                _ => Reply(false, $"unknown command '{name}'; 'cots help' lists them"),
            };
        }
        catch (Exception e)
        {
            RLog.Error($"{Tag} '{line}' failed: {e}");
            return Reply(false, $"'{name}' failed: {e.Message}");
        }
    }

    private static readonly (string Usage, string What)[] Commands =
    {
        ("help", "this list"),
        ("players", "player ids for the [player] argument (0 = host, 'all' = everyone)"),
        ("xp <amount> [player]", "add XP (scaled by XP gain like any XP)"),
        ("level <n> [player]", "set the level (refunds perks the new level cannot pay for)"),
        ("addlevels <n> [player]", "add levels"),
        ("points <n> [player]", "add permanent bonus perk points (negative removes)"),
        ("respec [player]", "refund every perk and spell"),
        ("give <item id | random[:rarity]> [level] [player]", "roll an item into the bag (rarity 0-3; level defaults to the target's)"),
        ("giveall <item type | all> [rarity 0-3] [player]", "one of every item of that type into the bag (cots list items shows types)"),
        ("learnall [player]", "every spell unlocked for free, empty spell slots filled"),
        ("playtest [player]", "playtest setup: level 30, god mode, no cooldowns, infinite energy, every spell fully mastered, one of every weapon"),
        ("mastery <spell id | all> <0-5> [player]", "set spell mastery: its cost and cooldown cuts and upgrades"),
        ("heal [player]", "full health and energy, debuffs cleared"),
        ("god [on|off] [player]", "incoming hits do no damage"),
        ("nocooldowns [on|off] [player]", "spells are always ready"),
        ("energy [on|off] [player]", "infinite energy: casts cost nothing, even spells above your max energy"),
        ("spawn <normal|elite|miniboss|boss> [actor type] [count]", "spawn enemies of that tier in front of you (actor type: Carl, Fingers, ...)"),
        ("extralevels <0-100>", "enemy levels added on top of the party level and the game's difficulty, for everyone"),
        ("list <items|perks|spells|stats|enemies> [filter]", "list content ids"),
        ("testsave", "dev: save this single-player world into the self-test slot 1999999999 (COS-129)"),
    };

    /// <summary>Dev (COS-129): writes the current single-player world to the self-test slot, so a fresh world on the
    /// current game build can become the test save without touching the player's own saves.</summary>
    private static bool TestSave(CommandSource source)
    {
        if (source != CommandSource.SinglePlayer)
            return Reply(false, "testsave works only from the console in a single-player world");
        Sons.Save.SaveGameManager.Save(Sons.Save.SaveGameType.SinglePlayer, "", 1999999999);
        return Reply(true, "saved this world to single-player slot 1999999999 (the self-test save)");
    }

    private static string Help()
    {
        var sb = new StringBuilder("Champions of the Sons admin commands (host / server admin only):");
        foreach (var (usage, what) in Commands) sb.Append($"\n  cots {usage} - {what}");
        return sb.ToString();
    }

    private static bool Reply(bool ok, string text)
    {
        LastReply = text;
        foreach (var l in text.Split('\n')) RLog.Msg($"{Tag} {l}");
        return ok;
    }

    // ------------------------------------------------------------------ player commands

    /// <summary>[player] argument: missing = the sender (local player, or the admin's own connection on a server), a player id, or "all".</summary>
    private static List<Target>? Targets(string? arg, BoltConnection? sender, out string? error)
    {
        error = null;
        bool hasLocal = !ServerConfig.IsDedicatedServer && LocalPlayer.IsInWorld;
        if (string.IsNullOrEmpty(arg))
        {
            if (sender != null) return new List<Target> { new(sender, $"player {CoopSession.IdOf(sender)}") };
            if (hasLocal) return new List<Target> { new(null, "you") };
            error = "no player to target: give a player id ('cots players')";
            return null;
        }
        if (arg.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var all = CoopSession.Peers.Select(c => new Target(c, $"player {CoopSession.IdOf(c)}")).ToList();
            if (hasLocal) all.Insert(0, new Target(null, "you"));
            return all;
        }
        if (!uint.TryParse(arg, out var id))
        {
            error = $"'{arg}' is not a player id ('cots players')";
            return null;
        }
        if (id == CoopSession.HostId)
        {
            if (hasLocal) return new List<Target> { new(null, "you") };
            error = "player 0 is the host, and this server has no player of its own";
            return null;
        }
        var conn = CoopSession.ByPlayerId(id);
        if (conn == null)
        {
            error = $"no player {id} ('cots players')";
            return null;
        }
        return new List<Target> { new(conn, $"player {id}") };
    }

    /// <summary><paramref name="amountIndex"/>: which argument is the amount (-1: none); the argument after it is the player.</summary>
    private static bool Grant(AdminAction action, string[] args, int amountIndex, BoltConnection? sender, int minArgs = 0, string itemArg = "")
    {
        if (args.Length < minArgs) return Reply(false, $"missing argument; 'cots help'");
        double amount = 0;
        if (amountIndex >= 0 && !double.TryParse(args[amountIndex], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out amount))
            return Reply(false, $"'{args[amountIndex]}' is not a number");
        int playerIndex = amountIndex + 1;
        return Send(new AdminGrant(action, amount, itemArg), args.Length > playerIndex ? args[playerIndex] : null, sender);
    }

    private static bool Send(AdminGrant grant, string? playerArg, BoltConnection? sender)
    {
        var targets = Targets(playerArg, sender, out var error);
        if (targets == null) return Reply(false, error!);
        var lines = new List<string>();
        foreach (var t in targets)
        {
            if (t.Local)
            {
                lines.Add($"{t.Name}: {AdminState.Apply(grant)}");
            }
            else
            {
                CotsNetEvent.Instance.SendTo(grant, t.Connection!);
                lines.Add($"{t.Name}: sent {grant.Action} {grant.Amount:0.##} {grant.Arg}".TrimEnd());
            }
        }
        return Reply(true, string.Join("\n", lines));
    }

    private static bool Toggle(AdminAction action, string[] args, BoltConnection? sender, bool current)
    {
        bool? on = args.Length > 0 ? args[0].ToLowerInvariant() switch { "on" or "1" or "true" => true, "off" or "0" or "false" => false, _ => null } : null;
        string? player = on == null ? (args.Length > 0 ? args[0] : null) : (args.Length > 1 ? args[1] : null);
        // Without on/off it flips the local state; for another player "on" is the default.
        bool value = on ?? (player == null ? !current : true);
        return Send(new AdminGrant(action, value ? 1 : 0, ""), player, sender);
    }

    private static bool Give(string[] args, BoltConnection? sender)
    {
        if (args.Length < 1) return Reply(false, "give <item id | random[:rarity]> [level] [player]");
        string id = args[0];
        if (!id.StartsWith("random", StringComparison.OrdinalIgnoreCase) && !ModContent.Db.Items.ContainsKey(id))
            return Reply(false, $"unknown item id '{id}' (cots list items <filter>)");
        int level = 0;
        if (args.Length > 1 && !int.TryParse(args[1], out level)) return Reply(false, $"'{args[1]}' is not a level");
        string? player = args.Length > 2 ? args[2] : null;
        if (level <= 0)
        {
            var lines = GiveAtOwnLevel(id, player, sender, out var error);
            return lines == null ? Reply(false, error!) : Reply(true, string.Join("\n", lines));
        }
        return Send(new AdminGrant(AdminAction.GiveItem, level, id), player, sender);
    }

    /// <summary>Gives an item at each target's own level: the host knows its clients' levels from their position reports.</summary>
    private static List<string>? GiveAtOwnLevel(string id, string? player, BoltConnection? sender, out string? error)
    {
        var targets = Targets(player, sender, out error);
        if (targets == null) return null;
        var lines = new List<string>();
        foreach (var t in targets)
        {
            int lv = t.Local ? PlayerState.Character.Progress.Level : CoopSession.LevelOf(t.Connection!);
            var grant = new AdminGrant(AdminAction.GiveItem, lv, id);
            if (t.Local) lines.Add($"{t.Name}: {AdminState.Apply(grant)}");
            else
            {
                CotsNetEvent.Instance.SendTo(grant, t.Connection!);
                lines.Add($"{t.Name}: sent {id} at level {lv}");
            }
        }
        return lines;
    }

    /// <summary>COS-166: one of every item of a type (optionally one rarity), each at the target's level.</summary>
    private static bool GiveAll(string[] args, BoltConnection? sender)
    {
        if (args.Length < 1) return Reply(false, "giveall <item type | all> [rarity 0-3] [player]");
        var db = ModContent.Db;
        string type = args[0];
        int? rarity = null;
        int next = 1;
        if (args.Length > 1 && args[1].Length == 1 && int.TryParse(args[1], out var r)) { rarity = r; next = 2; }
        string? player = args.Length > next ? args[next] : null;
        bool all = type.Equals("all", StringComparison.OrdinalIgnoreCase);
        var defs = db.Items.Values
            .Where(d => (all || d.Type.Equals(type, StringComparison.OrdinalIgnoreCase)) && (rarity == null || d.Rarity == rarity))
            .OrderBy(d => d.Type, StringComparer.Ordinal).ThenBy(d => d.Rarity).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
        if (defs.Count == 0)
        {
            var types = db.Items.Values.Select(d => d.Type).Distinct().OrderBy(t => t, StringComparer.Ordinal);
            return Reply(false, $"no items of type '{type}'{(rarity == null ? "" : $" and rarity {rarity}")}; types: {string.Join(", ", types)}, all");
        }
        int given = 0, full = 0;
        foreach (var def in defs)
        {
            var lines = GiveAtOwnLevel(def.Id, player, sender, out var error);
            if (lines == null) return Reply(false, error!);
            foreach (var l in lines)
                if (l.EndsWith("bag is full", StringComparison.Ordinal)) full++;
                else given++;
        }
        string what = rarity == null ? type : $"{type} rarity {rarity}";
        return Reply(full == 0, $"giveall {what}: {given} given" + (full > 0 ? $", {full} did not fit (bag is full)" : ""));
    }

    /// <summary>COS-176: sets spell mastery for a spell or all of them.</summary>
    private static bool Mastery(string[] args, BoltConnection? sender)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out int level) || level < 0 || level > SpellMastery.MaxLevel)
            return Reply(false, $"mastery <spell id | all> <0-{SpellMastery.MaxLevel}> [player]");
        return Send(new AdminGrant(AdminAction.SetMastery, level, args[0]), args.Length > 2 ? args[2] : null, sender);
    }

    /// <summary>COS-166: everything a hands-on playtest needs in one command.</summary>
    private static bool Playtest(string[] args, BoltConnection? sender)
    {
        const int Level = 30;
        string? player = args.Length > 0 ? args[0] : null;
        var lines = new List<string>();
        bool ok = true;
        void Step(bool result) { ok &= result; lines.Add(LastReply); }
        bool localHigher = player == null && sender == null && PlayerState.Character.Progress.Level >= Level;
        if (!localHigher) Step(Send(new AdminGrant(AdminAction.SetLevel, Level, ""), player, sender));
        Step(Send(new AdminGrant(AdminAction.God, 1, ""), player, sender));
        Step(Send(new AdminGrant(AdminAction.NoCooldowns, 1, ""), player, sender));
        Step(Send(new AdminGrant(AdminAction.InfiniteEnergy, 1, ""), player, sender));
        Step(Send(new AdminGrant(AdminAction.LearnAllSpells, 0, ""), player, sender));
        Step(Send(new AdminGrant(AdminAction.SetMastery, SpellMastery.MaxLevel, "all"), player, sender));
        Step(Send(new AdminGrant(AdminAction.Heal, 0, ""), player, sender));
        Step(GiveAll(player == null ? new[] { "weapon" } : new[] { "weapon", player }, sender));
        lines.Add("ready: spawn targets with 'cots spawn <normal|elite|miniboss|boss>'; 'god off', 'nocooldowns off' and 'energy off' undo the switches");
        return Reply(ok, string.Join("\n", lines));
    }

    // ------------------------------------------------------------------ world commands

    private static bool Spawn(string[] args, BoltConnection? sender)
    {
        if (args.Length < 1 || !Enum.TryParse<EnemyTier>(args[0], true, out var tier))
            return Reply(false, "spawn <normal|elite|miniboss|boss> [actor type] [count]");
        var type = VailActorTypeId.Carl;
        if (args.Length > 1 && !Enum.TryParse(args[1], true, out type)) return Reply(false, $"unknown actor type '{args[1]}'");
        int count = 1;
        if (args.Length > 2 && (!int.TryParse(args[2], out count) || count < 1)) return Reply(false, $"'{args[2]}' is not a count");
        count = System.Math.Min(count, 20);

        Vector3 at, forward, right;
        if (sender != null && CoopSession.TryPosition(sender, out var pos))
        {
            at = CoopSession.ToUnity(pos);
            forward = Vector3.forward;
            right = Vector3.right;
        }
        else if (!ServerConfig.IsDedicatedServer && LocalPlayer.IsInWorld && LocalPlayer.Transform != null)
        {
            var t = LocalPlayer.Transform;
            at = t.position;
            forward = t.forward;
            right = t.right;
        }
        else return Reply(false, "no position to spawn at");

        var saved = WorldState.Rules;
        WorldState.SetRulesForTest(saved with { ForcedTier = tier });
        int spawned = 0;
        try
        {
            for (int i = 0; i < count; i++)
                if (ActorTools.Spawn(type, at + forward * 8 + right * ((i - (count - 1) / 2f) * 2.5f)) != null) spawned++;
        }
        finally
        {
            WorldState.SetRulesForTest(saved);
        }
        return Reply(spawned > 0, $"spawned {spawned} {tier} {type}");
    }

    /// <summary>COS-130: there is no difficulty to set; the host's extra enemy levels (as in host settings) go on top of the party level and the game's difficulty.</summary>
    private static bool SetExtraLevels(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var n) || n < 0 || n > 100) return Reply(false, "extralevels <0-100>");
        WorldState.Update(r => r with { ExtraEnemyLevels = n });
        WorldState.NotifyChanged();
        HostRules.WorldRulesFeature.Broadcast();
        var game = WorldState.Rules.GameDifficulty;
        return Reply(true, $"extra enemy levels set to {n} (game difficulty {game.Mode} adds {game.LevelOffset}); enemies rolled from now on use it");
    }

    private static bool List(string[] args)
    {
        if (args.Length < 1) return Reply(false, "list <items|perks|spells|stats|enemies> [filter]");
        var db = ModContent.Db;
        IEnumerable<string> ids = args[0].ToLowerInvariant() switch
        {
            "items" => db.Items.Values.Select(d => $"{d.Id}  (rarity {d.Rarity}, {d.Type})"),
            "perks" => db.Perks.Keys,
            "spells" => db.Spells.Keys,
            "stats" => db.ItemStats.Keys,
            "enemies" => Enum.GetNames<VailActorTypeId>(),
            _ => Array.Empty<string>(),
        };
        string? filter = args.Length > 1 ? args[1] : null;
        var hits = ids.Where(s => filter == null || s.Contains(filter, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s, StringComparer.Ordinal).ToList();
        if (hits.Count == 0) return Reply(false, $"nothing matches '{string.Join(' ', args)}'");
        const int Max = 60;
        var text = string.Join("\n", hits.Take(Max)) + (hits.Count > Max ? $"\n... {hits.Count - Max} more, narrow it with a filter" : "");
        return Reply(true, text);
    }

    private static string Players()
    {
        var sb = new StringBuilder();
        if (!ServerConfig.IsDedicatedServer && LocalPlayer.IsInWorld)
            sb.Append($"0: you (host), level {PlayerState.Character.Progress.Level}");
        foreach (var c in CoopSession.Peers)
            sb.Append($"\n{CoopSession.IdOf(c)}: {c.RemoteEndPoint}, level {CoopSession.LevelOf(c)}");
        return sb.Length == 0 ? "no players" : sb.ToString().TrimStart('\n');
    }

    // ------------------------------------------------------------------ client side

    private static void OnNet(IMessage message, BoltConnection from)
    {
        if (message is not AdminGrant grant) return;
        // Only a client takes grants, and only from its host: clients cannot send each other admin commands.
        if (!CoopSession.IsClient || BoltNetwork.server == null || !ConnectionComparer.Instance.Equals(from, BoltNetwork.server))
        {
            RLog.Warning($"{Tag} ignored an admin grant ({grant.Action}) that did not come from the host");
            return;
        }
        RLog.Msg($"{Tag} host: {AdminState.Apply(grant)}");
    }

    public void RegisterSelfTests() => AdminSelfTest.Register();
}
