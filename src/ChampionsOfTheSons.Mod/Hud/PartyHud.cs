using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Mod.Net;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.Hud;

/// <summary>
/// COS-81 on the HUD: the party's system lines (joined, left, died, reached a level) stacked at the left, each fading after a
/// few seconds, and a "name [Lv N]" tag over every other player's body.
/// </summary>
internal sealed class PartyHud
{
    private const int MaxLines = 5;
    private const float LineSeconds = 8f;
    private const int MaxTags = 8;
    private const float TagRange = 120f;

    private sealed class LineView
    {
        public SLabelOptions Label = null!;
        public CanvasGroup Group = null!;
        public float Age = -1;
    }

    private readonly List<LineView> _lines = new();
    private readonly List<(SLabelOptions Label, RectTransform Rt)> _tags = new();
    private readonly Queue<string> _incoming = new();

    /// <summary>Text of the newest line shown (self-tests).</summary>
    public string LastLine { get; private set; } = "";

    public void Build(SContainerOptions layer)
    {
        for (int i = 0; i < MaxLines; i++)
        {
            var l = HudKit.Label("", 18, UiTheme.Text, TextAlignmentOptions.Left).TopLeft(0, 0).Size(520, 26);
            layer.Add(l);
            HudKit.Place(l.RectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), 24, 40 - i * 28);
            var view = new LineView { Label = l, Group = HudKit.Group(l.Root) };
            l.Active(false);
            _lines.Add(view);
        }
        for (int i = 0; i < MaxTags; i++)
        {
            var t = HudKit.Label("", 16, UiTheme.Text, TextAlignmentOptions.Center).TopLeft(0, 0).Size(300, 22);
            layer.Add(t);
            t.Active(false);
            _tags.Add((t, t.RectTransform));
        }
        CoopParty.Line += line => _incoming.Enqueue(Format(line)); // shown on the next Update (Unity objects only from there)
    }

    public static string Format(Announce line)
    {
        string name = $"<color={UiTheme.Stat}>{(line.Name.Length > 0 ? line.Name : UiText.T("ui.coop.player", "Player"))}</color>";
        string text = line.Kind switch
        {
            AnnounceKind.Joined => UiText.T("ui.coop.joined", "{0} joined (level {1})"),
            AnnounceKind.Left => UiText.T("ui.coop.left", "{0} left"),
            AnnounceKind.Died => UiText.T("ui.coop.died", "{0} died"),
            _ => UiText.T("ui.coop.level_up", "{0} reached level {1}"),
        };
        try
        {
            return string.Format(text, name, $"<color={UiTheme.Gold}>{line.Level}</color>");
        }
        catch (FormatException)
        {
            return name;
        }
    }

    public void Update(float dt, bool shown)
    {
        while (_incoming.Count > 0) Push(_incoming.Dequeue());
        foreach (var v in _lines)
        {
            if (v.Age < 0) continue;
            v.Age += dt;
            if (v.Age >= LineSeconds) { v.Age = -1; v.Label.Active(false); continue; }
            v.Group.alpha = v.Age < 0.25f ? v.Age / 0.25f : v.Age > LineSeconds - 1 ? LineSeconds - v.Age : 1;
        }
        UpdateTags(shown);
    }

    /// <summary>Newest line at the top; the others move down one row.</summary>
    private void Push(string text)
    {
        LastLine = text;
        for (int i = _lines.Count - 1; i > 0; i--)
        {
            var from = _lines[i - 1];
            var to = _lines[i];
            to.Age = from.Age;
            to.Label.Text(from.Label.TextObject.text);
            to.Label.Active(from.Age >= 0);
        }
        _lines[0].Age = 0;
        _lines[0].Label.Text(text);
        _lines[0].Label.Active(true);
    }

    private void UpdateTags(bool shown)
    {
        int used = 0;
        var cam = shown ? HudKit.PlayerCamera() : null;
        if (cam != null && CoopSession.Active)
        {
            float k = HudKit.CanvasPerPixel;
            var bodies = CoopBodies.All;
            foreach (var m in CoopParty.Others)
            {
                if (used >= _tags.Count) break;
                // The tag rides on the body nearest the position the player last reported.
                Vector3 at = m.Position;
                float best = 6f;
                foreach (var b in bodies)
                {
                    if (b.Local || b.Root == null) continue;
                    float d = Vector3.Distance(b.Root.position, m.Position);
                    if (d < best) { best = d; at = b.Root.position; }
                }
                var head = at + Vector3.up * 2.25f;
                if ((head - cam.transform.position).sqrMagnitude > TagRange * TagRange) continue;
                var sp = cam.WorldToScreenPoint(head);
                if (sp.z <= 0.1f) continue;
                var (label, rt) = _tags[used++];
                label.Text($"{m.Name} <color={UiTheme.Gold}>[{UiText.T("ui.hud.level", "Lv")}{m.Level}]</color>");
                HudKit.Place(rt, Vector2.zero, new Vector2(0.5f, 0), sp.x * k, sp.y * k);
                label.Active(true);
            }
        }
        for (int i = used; i < _tags.Count; i++) _tags[i].Label.Active(false);
    }
}
