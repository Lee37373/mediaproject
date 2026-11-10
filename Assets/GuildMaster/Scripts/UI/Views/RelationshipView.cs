using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Guide;
using GuildMaster.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The relationship graph (F-10, user request 2026-10-11; G): guild members and residents as nodes, a line for
    /// every pair that feels something — green for friendly, red for hostile, thicker the stronger — and lines that
    /// moved in the last few game hours blink. Each node shows what that person feels right now. Click a name to see
    /// only their lines and the details on the right. Reads the game; changes nothing.
    /// </summary>
    public class RelationshipView : UIPanel
    {
        const float GraphWidth = 660f, GraphHeight = 640f;
        const float RadiusX = 270f, RadiusY = 250f;
        const float NodeWidth = 112f, NodeHeight = 46f;

        static readonly Color MemberNode = new(0.22f, 0.32f, 0.26f, 0.97f);
        static readonly Color ResidentNode = new(0.36f, 0.28f, 0.2f, 0.97f);
        static readonly Color Friendly = new(0.4f, 0.9f, 0.45f);
        static readonly Color Hostile = new(0.95f, 0.35f, 0.3f);

        public override string Title => "관계도";
        protected override float RefreshInterval => 0.5f;

        class Node
        {
            public string Id, Name, Role;
            public bool IsResident;
            public Adventurer Member;
            public Resident Resident;
            public RectTransform Rect;
            public Image Back;
            public Text Label;
            public Image Badge;
            public Image BadgeIcon;
            public Vector2 Pos;
        }

        class Line
        {
            public Node A, B;
            public Image Image;
            public Color Color;
            public bool Blink;
        }

        RectTransform _graph, _lineLayer, _nodeLayer;
        Text _info, _legend;
        readonly List<Node> _nodes = new();
        readonly List<Line> _lines = new();
        string _signature;
        string _selected;

        protected override void Build(RectTransform body)
        {
            UIBuilder.HLayout(body, 10, 0, expandWidth: false);
            var graph = UIBuilder.Panel(body, "Graph", new Color(0.06f, 0.05f, 0.08f, 0.9f));
            graph.Size(GraphWidth, GraphHeight, 0, 1);
            _graph = graph.rectTransform;
            _lineLayer = UIBuilder.Rect(_graph, "Lines").Stretch();
            _nodeLayer = UIBuilder.Rect(_graph, "Nodes").Stretch();
            var hint = UIBuilder.Label(_graph, "이름을 누르면 그 사람의 관계만 보입니다.", 13, TextAnchor.LowerLeft, UITheme.TextMuted);
            hint.rectTransform.Stretch(10, 10, 0, 6);

            var side = UIBuilder.Panel(body, "Info", UITheme.PanelBgLight);
            side.Size(-1, -1, 1, 1);
            UIBuilder.VLayout(side, 6, 10);
            _legend = UIBuilder.Label(side.transform, LegendText(), 14, TextAnchor.UpperLeft).Size(-1, 44);
            _legend.verticalOverflow = VerticalWrapMode.Overflow;
            var content = UIBuilder.ScrollList(side.transform, out var scroll, 0);
            scroll.GetComponent<RectTransform>().Size(-1, -1, 1, 1);
            _info = UIBuilder.Label(content, "", 15, TextAnchor.UpperLeft);
            _info.horizontalOverflow = HorizontalWrapMode.Wrap;
            _info.verticalOverflow = VerticalWrapMode.Overflow;
        }

        protected override void OnOpened()
        {
            if (Services.TryGet<GuideTracker>(out var guide)) guide.Mark(GuideStep.Relationships);
        }

        // ---------------- Who is on the graph ----------------

        List<Node> People()
        {
            var list = new List<Node>();
            if (Services.TryGet<GuildRoster>(out var roster))
                foreach (var m in roster.Members)
                    if (m != null && m.IsAlive && m.Data != null)
                        list.Add(new Node
                        {
                            Id = m.Data.id, Name = m.Data.name, Member = m,
                            Role = $"{AdventurerData.ClassLabel(m.Data.cls)} Lv.{m.Data.level}",
                        });
            if (Services.TryGet<ResidentService>(out var residents))
                foreach (var r in residents.Residents)
                    if (r != null && r.Profile != null)
                        list.Add(new Node { Id = r.Profile.Id, Name = r.Profile.Name, Resident = r, IsResident = true, Role = r.Profile.JobLabel });
            return list;
        }

        void Rebuild(List<Node> people)
        {
            UIBuilder.Clear(_lineLayer);
            UIBuilder.Clear(_nodeLayer);
            _nodes.Clear();
            _lines.Clear();
            int n = people.Count;
            for (int i = 0; i < n; i++)
            {
                var node = people[i];
                // Members on the upper arc, residents below, all on one ellipse.
                float angle = Mathf.PI / 2f - 2f * Mathf.PI * i / Mathf.Max(1, n);
                node.Pos = new Vector2(Mathf.Cos(angle) * RadiusX, Mathf.Sin(angle) * RadiusY + 10f);
                var back = UIBuilder.Panel(_nodeLayer, "Node:" + node.Name, node.IsResident ? ResidentNode : MemberNode);
                node.Back = back;
                node.Rect = back.rectTransform;
                node.Rect.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), node.Pos, new Vector2(NodeWidth, NodeHeight));
                var button = back.gameObject.AddComponent<Button>();
                button.targetGraphic = back;
                string id = node.Id;
                button.onClick.AddListener(() => { _selected = _selected == id ? null : id; Refresh(); });
                node.Label = UIBuilder.Label(node.Rect, "", 14, TextAnchor.MiddleCenter);
                node.Label.rectTransform.Stretch(4, 4, 2, 2);
                var badge = UIBuilder.Panel(node.Rect, "Feeling", Color.white, raycast: false);
                badge.rectTransform.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-4, -4), new Vector2(24, 24));
                node.Badge = badge;
                node.BadgeIcon = UIBuilder.SpriteImage(badge.rectTransform, "Icon", null, Color.white);
                node.BadgeIcon.rectTransform.Stretch(4, 4, 4, 4);
                node.BadgeIcon.preserveAspect = true;
                var captured = node;
                back.Tooltip(() => NodeTooltip(captured));
                _nodes.Add(node);
            }
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                var rt = UIBuilder.Rect(_lineLayer, "Line");
                var img = rt.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                var a = _nodes[i].Pos;
                var b = _nodes[j].Pos;
                rt.anchoredPosition = (a + b) / 2f;
                rt.sizeDelta = new Vector2(Vector2.Distance(a, b), 2f);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
                _lines.Add(new Line { A = _nodes[i], B = _nodes[j], Image = img });
            }
        }

        // ---------------- Values ----------------

        static int? Feeling(Node from, Node to)
        {
            if (from.Member != null) return RelationGraph.Peek(from.Member.Data, to.Id);
            if (from.Resident != null) return RelationGraph.Peek(from.Resident.Profile, to.Id);
            return null;
        }

        static int Bond(Node a, Node b) => RelationGraph.Bond(Feeling(a, b), Feeling(b, a));

        Emotion FeelingNow(Node n, out string reason)
        {
            reason = null;
            Units.Unit unit = n.Member != null ? (Units.Unit)n.Member : n.Resident;
            return unit != null && Services.TryGet<EmotionService>(out var emotions) ? emotions.Of(unit, out reason) : Emotion.None;
        }

        float Now => Services.TryGet<GameClock>(out var c) ? c.TotalHours : 0f;

        bool Recent(Node a, Node b, out int sign)
        {
            sign = 0;
            return Services.TryGet<EmotionService>(out var emotions) && emotions.Book.ChangedRecently(a.Id, b.Id, Now, EmotionRules.RecentChangeHours, out sign);
        }

        // ---------------- Drawing ----------------

        /// <summary>The legend; recent lines blink, or glow steadily with the settings' calmer flashes (P6-3).</summary>
        static string LegendText()
            => UIBuilder.Colored("━ 호감", Friendly) + "   " + UIBuilder.Colored("━ 반감", Hostile) + "\n" +
               UIBuilder.Colored($"굵을수록 강함 · {(Session.GameSettings.CalmFlashes ? "밝게 빛나면" : "깜빡이면")} 최근 {EmotionRules.RecentChangeHours:0}시간 안에 바뀜", UITheme.TextMuted);

        protected override void Refresh()
        {
            _legend.text = LegendText();
            var people = People();
            string signature = string.Join(",", people.Select(p => p.Id));
            if (signature != _signature)
            {
                _signature = signature;
                Rebuild(people);
            }
            if (_selected != null && _nodes.All(n => n.Id != _selected)) _selected = null;

            foreach (var node in _nodes)
            {
                var e = FeelingNow(node, out _);
                node.Label.text = $"{node.Name}\n{UIBuilder.Colored(node.Role, UITheme.TextMuted)}";
                node.Badge.gameObject.SetActive(e != Emotion.None);
                if (e != Emotion.None)
                {
                    node.Badge.color = EmotionIcons.Background(e);
                    node.BadgeIcon.sprite = EmotionIcons.IconFor(e);
                }
                bool dim = _selected != null && node.Id != _selected && Bond(node, _nodes.First(x => x.Id == _selected)) == 0;
                var baseColor = node.IsResident ? ResidentNode : MemberNode;
                node.Back.color = node.Id == _selected ? Color.Lerp(baseColor, UITheme.Accent, 0.45f) : dim ? baseColor * new Color(1, 1, 1, 0.45f) : baseColor;
            }

            foreach (var line in _lines)
            {
                int bond = Bond(line.A, line.B);
                bool shown = RelationGraph.Shown(bond) && (_selected == null || line.A.Id == _selected || line.B.Id == _selected);
                line.Image.gameObject.SetActive(shown);
                if (!shown) continue;
                float strength = Mathf.Clamp01(Mathf.Abs(bond) / 100f);
                var c = bond > 0 ? Friendly : Hostile;
                c.a = 0.35f + 0.55f * strength;
                line.Color = c;
                line.Blink = Recent(line.A, line.B, out _);
                line.Image.color = c;
                var rt = line.Image.rectTransform;
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, RelationGraph.Thickness(bond));
            }

            _info.text = _selected != null ? Details(_nodes.First(n => n.Id == _selected)) : Overview();
        }

        protected override void Update()
        {
            base.Update();
            if (!Visible) return;
            // P6-3: with calmer flashes a recent line glows steadily (lighter, fully opaque) instead of blinking.
            bool calm = Session.GameSettings.CalmFlashes;
            float pulse = 0.25f + 0.75f * Mathf.PingPong(Time.unscaledTime * 2.2f, 1f);
            foreach (var line in _lines)
            {
                if (!line.Blink || !line.Image.gameObject.activeSelf) continue;
                var c = line.Color;
                if (calm) c = new Color(Mathf.Lerp(c.r, 1f, 0.45f), Mathf.Lerp(c.g, 1f, 0.45f), Mathf.Lerp(c.b, 1f, 0.45f), 1f);
                else c.a *= pulse;
                line.Image.color = c;
            }
        }

        static string Signed(int v) => (v > 0 ? "+" : "") + v;

        static string Colored(int bond, string text) => UIBuilder.Colored(text, bond > 0 ? Friendly : bond < 0 ? Hostile : UITheme.TextMuted);

        string NodeTooltip(Node n)
        {
            var e = FeelingNow(n, out string reason);
            string feel = e == Emotion.None ? "" : $"\n지금: {EmotionRules.Label(e)}" + (string.IsNullOrEmpty(reason) ? "" : $" ({reason})");
            if (n.IsResident)
                return $"{n.Name} · {n.Role}\n길드에 대한 생각 {Signed(n.Resident.Profile.GuildOpinion)}" + feel;
            return $"{n.Name} · {n.Role}\n스트레스 {Mathf.RoundToInt(n.Member.Data.stress)}" + feel;
        }

        string Details(Node n)
        {
            var sb = new StringBuilder();
            sb.Append(UIBuilder.Colored(n.Name, UITheme.Accent)).Append("  ").Append(n.Role).Append('\n');
            var e = FeelingNow(n, out string reason);
            sb.Append("지금 기분: ").Append(UIBuilder.Colored(EmotionRules.Label(e), EmotionIcons.Tint(e)));
            if (!string.IsNullOrEmpty(reason)) sb.Append(UIBuilder.Colored($" — {reason}", UITheme.TextMuted));
            sb.Append('\n');
            if (n.IsResident) sb.Append("길드에 대한 생각: ").Append(Signed(n.Resident.Profile.GuildOpinion)).Append('\n');
            sb.Append('\n');
            var rows = _nodes.Where(o => o != n)
                             .Select(o => (o, bond: Bond(n, o), mine: Feeling(n, o), theirs: Feeling(o, n)))
                             .Where(x => x.mine.HasValue || x.theirs.HasValue)
                             .OrderByDescending(x => Mathf.Abs(x.bond))
                             .ToList();
            if (rows.Count == 0) sb.Append(UIBuilder.Colored("아직 이렇다 할 관계가 없습니다.", UITheme.TextMuted));
            foreach (var (o, bond, mine, theirs) in rows)
            {
                string recent = Recent(n, o, out int sign) ? UIBuilder.Colored(sign >= 0 ? "  ▲최근" : "  ▼최근", sign >= 0 ? Friendly : Hostile) : "";
                sb.Append(Colored(bond, $"{o.Name}  {Relationships.Label(bond)}")).Append(recent).Append('\n');
                sb.Append(UIBuilder.Colored($"   내 마음 {RelationGraph.Describe(mine)} · 상대 마음 {RelationGraph.Describe(theirs)}", UITheme.TextMuted)).Append('\n');
            }
            return sb.ToString();
        }

        string Overview()
        {
            var sb = new StringBuilder();
            var pairs = _lines.Select(l => (l, bond: Bond(l.A, l.B))).Where(x => RelationGraph.Shown(x.bond)).ToList();
            var friends = pairs.Where(x => x.bond > 0).OrderByDescending(x => x.bond).Take(3).ToList();
            var foes = pairs.Where(x => x.bond < 0).OrderBy(x => x.bond).Take(3).ToList();
            sb.Append(UIBuilder.Colored("가까운 사이", UITheme.Accent)).Append('\n');
            if (friends.Count == 0) sb.Append(UIBuilder.Colored("  아직 없음", UITheme.TextMuted)).Append('\n');
            foreach (var (l, bond) in friends) sb.Append(Colored(bond, $"  {l.A.Name} — {l.B.Name}  {Relationships.Describe(bond)}")).Append('\n');
            sb.Append('\n').Append(UIBuilder.Colored("불편한 사이", UITheme.Accent)).Append('\n');
            if (foes.Count == 0) sb.Append(UIBuilder.Colored("  아직 없음", UITheme.TextMuted)).Append('\n');
            foreach (var (l, bond) in foes) sb.Append(Colored(bond, $"  {l.A.Name} — {l.B.Name}  {Relationships.Describe(bond)}")).Append('\n');

            var moved = _lines.Where(l => Recent(l.A, l.B, out _)).Take(6).ToList();
            sb.Append('\n').Append(UIBuilder.Colored("최근에 바뀐 관계", UITheme.Accent)).Append('\n');
            if (moved.Count == 0) sb.Append(UIBuilder.Colored("  없음", UITheme.TextMuted)).Append('\n');
            foreach (var l in moved)
            {
                Recent(l.A, l.B, out int sign);
                sb.Append(UIBuilder.Colored($"  {l.A.Name} — {l.B.Name}  {(sign >= 0 ? "좋아짐" : "나빠짐")}", sign >= 0 ? Friendly : Hostile)).Append('\n');
            }

            sb.Append('\n').Append(UIBuilder.Colored("지금 기분", UITheme.Accent)).Append('\n');
            bool any = false;
            foreach (var n in _nodes)
            {
                var e = FeelingNow(n, out string reason);
                if (e == Emotion.None || e == Emotion.Sleepy) continue;
                any = true;
                sb.Append("  ").Append(n.Name).Append(": ").Append(UIBuilder.Colored(EmotionRules.Label(e), EmotionIcons.Tint(e)));
                if (!string.IsNullOrEmpty(reason)) sb.Append(UIBuilder.Colored($" ({reason})", UITheme.TextMuted));
                sb.Append('\n');
            }
            if (!any) sb.Append(UIBuilder.Colored("  모두 평온합니다.", UITheme.TextMuted)).Append('\n');
            return sb.ToString();
        }
    }
}
