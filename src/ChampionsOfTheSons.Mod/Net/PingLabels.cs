using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Hud;
using ChampionsOfTheSons.Mod.Loot;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TheForest.Utils;
using TMPro;
using UnityEngine;
using static SUI.SUI;
using Ping = ChampionsOfTheSons.Core.Networking.Ping;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// Screen labels for ping markers (COS-98): an enemy ping shows the enemy's name in its tier colour and a health bar and
/// follows it (it ends when the enemy dies), an item ping the item's name in its rarity colour, a location ping the distance.
/// Labels shrink with distance. The in-world pillar stays as the mark; these sit above it.
/// </summary>
internal static class PingLabels
{
    private const string PanelId = "cots-pings";
    private const int Max = 8;

    private sealed class Label
    {
        public SContainerOptions Root = null!;
        public SLabelOptions Text = null!;
        public SContainerOptions Bar = null!, Fill = null!;
        public uint Player;
        public bool Used;
    }

    private static SPanelOptions? _panel;
    private static readonly Label[] Labels = new Label[Max];

    /// <summary>The local player's last enemy ping target in single player (no net id there).</summary>
    public static Sons.Ai.Vail.VailActor? LocalTarget { get; set; }

    /// <summary>Label text by player (self-test).</summary>
    public static readonly Dictionary<uint, string> Shown = new();

    public static void Build()
    {
        if (_panel != null) return;
        _panel = RegisterNewPanel(PanelId, enableInput: false);
        for (int i = 0; i < Max; i++)
        {
            var l = new Label();
            l.Root = SContainer.TopLeft(0, 0).Size(260, 46);
            l.Text = HudKit.Label("", 20, "#FFFFFF", TextAlignmentOptions.Bottom).TopLeft(0, 0).Size(260, 30);
            l.Text.TextObject.fontStyle = FontStyles.Bold;
            ActionBar.BarSlot.Outline(l.Text, 0.3f);
            l.Bar = SContainer.TopLeft(50, 34).Size(160, 6).Background(HudKit.C(UiTheme.BarBack), EBackground.None);
            l.Fill = SContainer.Background(HudKit.C("#E04A3A"), EBackground.None);
            l.Bar.Add(l.Fill);
            l.Root.Add(l.Text);
            l.Root.Add(l.Bar);
            _panel.Add(l.Root);
            HudKit.Place(l.Root.RectTransform, Vector2.zero, new Vector2(0.5f, 0), 0, 0);
            l.Root.Active(false);
            Labels[i] = l;
        }
        HudKit.NoRaycast(_panel.Root);
    }

    public static void Update(PingBoard board, bool show)
    {
        if (_panel == null) return;
        if (_panel.Root.activeSelf != show) _panel.Active(show);
        foreach (var l in Labels) l.Used = false;
        Shown.Clear();
        if (!show) { HideUnused(); return; }
        var cam = HudKit.PlayerCamera();
        if (cam == null || LocalPlayer.Transform == null) { HideUnused(); return; }
        float k = HudKit.CanvasPerPixel;
        List<uint>? ended = null;
        foreach (var ping in board.Active)
        {
            var at = CoopSession.ToUnity(ping.Position);
            string text;
            float health = -1;
            switch (ping.Kind)
            {
                case PingKind.Enemy:
                    var actor = ResolveEnemy(ping);
                    var runtime = actor != null ? EnemyRegistry.Get(actor) : null;
                    if (actor == null || actor.IsDead() || runtime == null || !runtime.IsActive)
                    {
                        (ended ??= new()).Add(ping.PlayerId);
                        continue;
                    }
                    at = actor.transform.position + Vector3.up * 2.4f;
                    var p = runtime.Profile;
                    string name = HudText.PersonalName(p) ?? HudText.KindTitle(p);
                    text = $"<color={HudKit.TierHex(p.Tier)}>{(p.Tier >= EnemyTier.Elite ? $"<b>{name}</b>" : name)}</color>  <size=16>{UiText.T("ui.hud.level", "Lv")} {p.Level}</size>";
                    var h = runtime.Stats != null ? EnemyRegistry.Health(runtime.Stats) : null;
                    health = h != null && p.MaxHealth > 0 ? (float)Math.Clamp(h._currentValue / p.MaxHealth, 0, 1) : -1;
                    break;
                case PingKind.Item:
                    var drop = LootFeature.Ground.Get((int)ping.TargetId);
                    if (drop == null) { (ended ??= new()).Add(ping.PlayerId); continue; }
                    at = new Vector3(drop.X, drop.Y, drop.Z) + Vector3.up * 1.2f;
                    text = $"<color={UiTheme.RarityHex(drop.Item.Rarity)}>{UiText.ItemName(drop.Item)}</color>";
                    break;
                default:
                    at += Vector3.up * 1.5f;
                    float m = Vector3.Distance(LocalPlayer.Transform.position, at);
                    text = $"<color=#8FD4FF>{m:0} m</color>";
                    break;
            }
            Shown[ping.PlayerId] = text;
            var l = Free(ping.PlayerId);
            if (l == null) continue;
            Vector3 sp = cam.WorldToScreenPoint(at);
            if (sp.z <= 0.1f) { l.Root.Active(false); continue; }
            if (!l.Root.Root.activeSelf) l.Root.Active(true);
            if (l.Text.TextObject.text != text) l.Text.Text(text);
            l.Bar.Active(health >= 0);
            if (health >= 0) HudKit.SetFill(l.Fill, health);
            float dist = Vector3.Distance(cam.transform.position, at);
            float scale = Mathf.Clamp(1.4f - dist / 120f, 0.6f, 1.2f);
            l.Root.RectTransform.localScale = new Vector3(scale, scale, 1);
            l.Root.RectTransform.anchoredPosition = new Vector2(sp.x * k, sp.y * k + 14);
        }
        HideUnused();
        if (ended != null) foreach (var id in ended) board.Remove(id);
    }

    private static Sons.Ai.Vail.VailActor? ResolveEnemy(Ping ping)
    {
        if (ping.TargetId != 0)
        {
            var a = EnemySyncFeature.ActorOf(ping.TargetId);
            if (a != null) return a;
        }
        return ping.PlayerId == CoopExtras.LocalPingId ? LocalTarget : null;
    }

    private static Label? Free(uint player)
    {
        foreach (var l in Labels)
            if (!l.Used && l.Player == player) { l.Used = true; return l; }
        foreach (var l in Labels)
            if (!l.Used && !l.Root.Root.activeSelf) { l.Used = true; l.Player = player; return l; }
        return null;
    }

    private static void HideUnused()
    {
        foreach (var l in Labels)
            if (!l.Used && l.Root.Root.activeSelf) l.Root.Active(false);
    }
}
