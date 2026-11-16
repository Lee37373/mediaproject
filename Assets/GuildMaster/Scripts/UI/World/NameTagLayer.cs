using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Player;
using GuildMaster.Units;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Name labels above adventurers, monsters and the guild master. Hidden in fog and when the
    /// camera is zoomed far out. F5 toggles them, alongside the other display toggles (F3, F4);
    /// N belongs to the notification window, with the rest of the window keys.
    /// Where tags would overlap only the most important shows (P4-123, user request 2026-11-04: a crowd's tags drowned
    /// the fight); a unit under the mouse pointer always shows its tag.
    /// A unit in a fight fades its tag out and back in a little after the fight (2026-11-14, user: "전투 중에 이름 가림,
    /// 마우스 올리면 바뀜"): the hovered or picked one still shows its tag.
    /// </summary>
    public class NameTagLayer : MonoBehaviour
    {
        /// <summary>Tags hide above this orthographic size to avoid clutter.</summary>
        const float MaxZoom = 14f;
        /// <summary>World-space gap above the unit's head (HP bar sits at 0.75 × scale).</summary>
        const float HeadOffset = 0.95f;
        /// <summary>Height of a tag on the layer, and the gap kept between two tags.</summary>
        const float TagHeight = 18f, TagGap = 2f;
        /// <summary>A hovered tag that lands on another steps up this many times at most.</summary>
        const int HoverSteps = 6;
        /// <summary>A unit counts as fighting while it has a live target, or this long after it was last hit.</summary>
        const float CombatHoldSeconds = 3f;
        /// <summary>Tag alpha per second while fading out (into a fight) or back in.</summary>
        const float FadeSpeed = 4f;

        static readonly Color AdventurerColor = new(0.7f, 1f, 0.7f);
        static readonly Color MonsterColor = new(1f, 0.72f, 0.66f);
        static readonly Color BossColor = new(1f, 0.55f, 0.2f);
        static readonly Color NemesisColor = new(1f, 0.32f, 0.42f);
        static readonly Color MasterColor = new(1f, 0.88f, 0.45f);
        static readonly Color VisitorColor = new(0.85f, 0.8f, 1f);

        /// <summary>Which tag wins a place (higher first). A hovered or picked unit's beats everything.</summary>
        public static class Priority
        {
            public const int Hovered = 100, Selected = 90, Nemesis = 60, Boss = 50, Master = 45, Member = 30, Visitor = 25, Monster = 10;
        }

        struct Candidate
        {
            public object Key;
            public Vector2 Local;
            public string Text;
            public Color Color;
            public int Priority;
            public bool Hovered;
            public float Alpha;
            /// <summary>Tie-break among equals: shown last frame first, then a fixed order (no flicker as units move).</summary>
            public bool ShownBefore;
            public int Order;
        }

        RectTransform _layer;
        readonly List<Text> _tags = new();
        int _used;
        bool _enabled = true;
        readonly List<Candidate> _candidates = new();
        readonly List<Rect> _placed = new();
        HashSet<object> _shown = new(), _shownNow = new();
        readonly Dictionary<string, float> _widths = new();
        /// <summary>Each unit's tag alpha (faded out while it fights).</summary>
        readonly Dictionary<object, float> _alpha = new();
        Text _measure;

        public bool Enabled => _enabled;
        /// <summary>Test hook: the real pointer picks out nothing (a watched test run must not depend on where the mouse sits).</summary>
        public static bool IgnorePointer { get; set; }
        /// <summary>Where this frame's tags went (layer space), for tests.</summary>
        public IReadOnlyList<Rect> Placed => _placed;
        /// <summary>How many units wanted a tag this frame.</summary>
        public int CandidateCount => _candidates.Count;
        /// <summary>Whether this unit's tag showed this frame.</summary>
        public bool IsShown(object unit) => _shown.Contains(unit);

        /// <summary>In a fight: chasing or striking a live target, or hit within the last few seconds.</summary>
        public static bool InCombat(Unit u)
            => u != null && ((u.CurrentTarget != null && u.CurrentTarget.IsAlive) || Time.time - u.LastAttackedTime < CombatHoldSeconds);

        public void Build(RectTransform layer) => _layer = layer;

        Text Acquire()
        {
            if (_used < _tags.Count)
            {
                var t = _tags[_used++];
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                return t;
            }
            var text = NewLabel();
            _tags.Add(text);
            _used++;
            return text;
        }

        Text NewLabel()
        {
            var text = UIBuilder.Label(_layer, "", 15, TextAnchor.LowerCenter, style: FontStyle.Bold);
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = text.rectTransform;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(160f, 20f);
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1f, -1f);
            return text;
        }

        /// <summary>How wide a label is on the layer (measured once per text).</summary>
        float WidthOf(string label)
        {
            if (_widths.TryGetValue(label, out var w)) return w;
            if (_measure == null)
            {
                _measure = NewLabel();
                _measure.enabled = false;
            }
            _measure.text = label;
            w = _measure.preferredWidth + 4f;
            if (_widths.Count > 2000) _widths.Clear();
            _widths[label] = w;
            return w;
        }

        void Add(object key, Vector3 world, string label, Color color, int priority, bool hovered, bool fighting = false)
        {
            if (string.IsNullOrEmpty(label) || !WorldOverlay.TryWorldToLocal(_layer, world, out var local)) return;
            float want = fighting && !hovered && priority != Priority.Selected ? 0f : 1f;
            float alpha = _alpha.TryGetValue(key, out var was) ? Mathf.MoveTowards(was, want, FadeSpeed * Time.unscaledDeltaTime) : want;
            if (hovered) alpha = 1f;
            if (_alpha.Count > 1024) _alpha.Clear(); // dead units' entries
            _alpha[key] = alpha;
            if (alpha < 0.02f) return;
            _candidates.Add(new Candidate
            {
                Key = key, Local = local, Text = label, Color = color, Hovered = hovered, Alpha = alpha,
                Priority = hovered ? Priority.Hovered : priority,
                ShownBefore = _shown.Contains(key),
                Order = key is Object o ? o.GetInstanceID() : 0,
            });
        }

        /// <summary>
        /// Labels are rebuilt only when what they show changes (P5-2: formatting every visible
        /// unit's label every frame was most of this layer's garbage).
        /// </summary>
        readonly Dictionary<Unit, (int key, object source, string text)> _labels = new();

        string LabelFor(Unit unit)
        {
            int key;
            object source;
            switch (unit)
            {
                case Adventurer a:
                    key = a.Data.level * 64 + (int)a.Tier * 2 + (a.IsVisitor ? 1 : 0);
                    source = a.Data.name;
                    break;
                case Monster m:
                    key = m.Level * 64 + (int)m.Tier * 2 + (m.IsBoss ? 1 : 0);
                    source = m.DisplayName;
                    break;
                default:
                    return "";
            }
            if (_labels.TryGetValue(unit, out var cached) && cached.key == key && ReferenceEquals(cached.source, source)) return cached.text;
            string text = unit switch
            {
                Adventurer { IsVisitor: true } a => $"{a.Data.name} Lv.{a.Data.level} (방문자)",
                Adventurer a => $"{a.Data.name} Lv.{a.Data.level} · {CombatTiers.Label(a.Tier)}",
                // A nemesis (P4-109) goes by the name its deeds gave it.
                Monster m => $"{(m.IsNemesis ? "[원수] " : m.IsBoss ? "[보스] " : "")}Lv.{m.Level} {m.DisplayName} · {CombatTiers.Label(m.Tier)}",
                _ => ""
            };
            _labels[unit] = (key, source, text);
            return text;
        }

        /// <summary>Whether the pointer is on this unit's figure (feet at its position, about a cell tall).</summary>
        public static bool Under(Vector2 pointer, Vector2 feet, float scale)
        {
            float dx = pointer.x - feet.x, dy = pointer.y - feet.y;
            return Mathf.Abs(dx) <= 0.45f * scale && dy >= -0.25f * scale && dy <= 1.05f * scale;
        }

        /// <summary>Two tags overlap (with the gap kept between them).</summary>
        public static bool Overlaps(Rect a, Rect b) => a.xMin < b.xMax + TagGap && b.xMin < a.xMax + TagGap && a.yMin < b.yMax + TagGap && b.yMin < a.yMax + TagGap;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.f5Key.wasPressedThisFrame) return;
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<InputField>() != null) return;
            _enabled = !_enabled;
        }

        void LateUpdate()
        {
            _used = 0;
            _candidates.Clear();
            _shownNow.Clear();
            var cam = Camera.main;
            if (_enabled && cam != null && cam.orthographicSize <= MaxZoom)
            {
                Gather(cam);
                Place();
            }
            (_shown, _shownNow) = (_shownNow, _shown);
            for (int i = _used; i < _tags.Count; i++)
                if (_tags[i].gameObject.activeSelf) _tags[i].gameObject.SetActive(false);
        }

        void Gather(Camera cam)
        {
            Services.TryGet<FogOfWar>(out var fog);
            Unit selected = Services.TryGet<InspectorView>(out var inspector) ? inspector.SelectedUnit : null;
            // The pointer over the map (not over a window) picks out the units under it.
            bool hovering = false;
            Vector2 pointer = default;
            var mouse = Mouse.current;
            if (mouse != null && !IgnorePointer && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                pointer = cam.ScreenToWorldPoint(mouse.position.ReadValue());
                hovering = true;
            }

            foreach (var u in UnitRegistry.All)
            {
                if (!u.IsAlive) continue;
                switch (u)
                {
                    case Adventurer a:
                        Add(a, a.transform.position + new Vector3(0f, HeadOffset), LabelFor(a), a.IsVisitor ? VisitorColor : AdventurerColor,
                            a == selected ? Priority.Selected : a.IsVisitor ? Priority.Visitor : Priority.Member,
                            hovering && Under(pointer, a.Position, 1f), InCombat(a));
                        break;
                    case Town.Merchant merchant:
                        Add(merchant, merchant.transform.position + new Vector3(0f, HeadOffset), "행상인", VisitorColor,
                            merchant == selected ? Priority.Selected : Priority.Visitor, hovering && Under(pointer, merchant.Position, 1f));
                        break;
                    case Monster m:
                        if (fog != null && !fog.IsVisible(m.Position) && !FogOfWar.ShowEverything) break;
                        Add(m, m.transform.position + new Vector3(0f, HeadOffset * m.Scale), LabelFor(m),
                            m.IsNemesis ? NemesisColor : m.IsBoss ? BossColor : MonsterColor,
                            m == selected ? Priority.Selected : m.IsNemesis ? Priority.Nemesis : m.IsBoss ? Priority.Boss : Priority.Monster,
                            hovering && Under(pointer, m.Position, m.Scale), InCombat(m));
                        break;
                }
            }
            if (Services.TryGet<PlayerAvatar>(out var avatar))
                Add(avatar, avatar.transform.position + new Vector3(0f, HeadOffset), "길드 마스터", MasterColor, avatar == selected ? Priority.Selected : Priority.Master,
                    hovering && Under(pointer, avatar.Position, 1f));
        }

        /// <summary>Most important first; a tag that would land on one already placed is left out, unless hovered.</summary>
        void Place()
        {
            _candidates.Sort((a, b) =>
                a.Priority != b.Priority ? b.Priority.CompareTo(a.Priority)
                : a.ShownBefore != b.ShownBefore ? (a.ShownBefore ? -1 : 1)
                : a.Order.CompareTo(b.Order));
            _placed.Clear();
            foreach (var c in _candidates)
            {
                float w = WidthOf(c.Text);
                var rect = new Rect(c.Local.x - w * 0.5f, c.Local.y, w, TagHeight);
                if (Blocked(rect))
                {
                    if (!c.Hovered) continue;
                    // A hovered tag steps up over the others rather than hide (several units under the pointer).
                    for (int s = 0; s < HoverSteps && Blocked(rect); s++) rect.y += TagHeight + TagGap;
                }
                _placed.Add(rect);
                _shownNow.Add(c.Key);
                var t = Acquire();
                t.text = c.Text;
                t.color = new Color(c.Color.r, c.Color.g, c.Color.b, c.Color.a * c.Alpha);
                t.rectTransform.anchoredPosition = new Vector2(c.Local.x, rect.y);
            }
        }

        bool Blocked(Rect rect)
        {
            foreach (var p in _placed) if (Overlaps(rect, p)) return true;
            return false;
        }
    }
}
