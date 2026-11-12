using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Recruitment;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Guild members overview, as a party board (2026-11-05, UI round — the team: "see the parties at a glance, less empty
    /// space"). Each party is a card — its name, what it is doing and where, and a tile per member with class colour, level,
    /// health and stress bars, what they are doing and what needs looking at (중상, 물약 없음, 휴가…). Members without a
    /// party share the last card. Clicking a tile shows that member on the right (double click, or 추적, follows them with
    /// the camera); 상세 and 관리 open their windows as before.
    /// </summary>
    public class RosterView : UIPanel
    {
        public override string Title => "길드원";

        internal static readonly Color CardBg = new(0.13f, 0.12f, 0.17f, 1f);
        static readonly Color TileBg = new(0.17f, 0.15f, 0.22f, 1f);
        static readonly Color TileSelected = new(0.23f, 0.19f, 0.31f, 1f);
        static readonly Color TileDanger = new(0.55f, 0.23f, 0.27f, 1f);
        static readonly Color Track = new(0.23f, 0.20f, 0.28f, 1f);
        /// <summary>Bar track inside a tile: the tile face is drawn brighter by its button tint, so the usual track vanished.</summary>
        static readonly Color TileTrack = new(0.09f, 0.08f, 0.12f, 1f);
        static readonly Color ChipPlain = new(0.20f, 0.18f, 0.26f, 1f);
        static readonly Color PillOut = new(0.14f, 0.20f, 0.29f, 1f), PillOutText = new(0.66f, 0.80f, 1f, 1f);
        static readonly Color PillBack = new(0.29f, 0.23f, 0.12f, 1f), PillBackText = new(0.98f, 0.82f, 0.54f, 1f);
        static readonly Color PillHome = new(0.14f, 0.25f, 0.16f, 1f), PillHomeText = new(0.61f, 0.90f, 0.61f, 1f);
        static readonly Color ChipBad = new(0.29f, 0.14f, 0.19f, 1f), ChipBadText = new(1f, 0.70f, 0.67f, 1f);
        static readonly Color ChipWarn = new(0.29f, 0.23f, 0.12f, 1f), ChipWarnText = new(0.98f, 0.82f, 0.54f, 1f);

        /// <summary>The colour each class is drawn in (tiles, cards, the event card's people).</summary>
        internal static Color ClassColor(AdventurerClass c) => c switch
        {
            AdventurerClass.Warrior => new Color(0.91f, 0.47f, 0.37f, 1f),
            AdventurerClass.Ranger => new Color(0.50f, 0.80f, 0.42f, 1f),
            AdventurerClass.Mage => new Color(0.66f, 0.61f, 1f, 1f),
            _ => new Color(0.95f, 0.85f, 0.54f, 1f),
        };

        static string ClassInitial(AdventurerClass c) => c switch
        {
            AdventurerClass.Warrior => "전",
            AdventurerClass.Ranger => "궁",
            AdventurerClass.Mage => "마",
            _ => "사",
        };

        /// <summary>
        /// The item each class is known by, drawn in the avatar circle (user, 2026-11-10: "전" → a sword): warrior a sword,
        /// ranger a bow, mage a crystal staff, priest a holy wand. Icons from the CharacterMaker pack's thumbnails, copied to
        /// Resources/UI/Class; null when missing (the initial letter shows instead).
        /// </summary>
        internal static Sprite ClassIcon(AdventurerClass c)
        {
            if (s_classIcons.TryGetValue(c, out var sprite)) return sprite;
            string key = c switch
            {
                AdventurerClass.Warrior => "warrior",
                AdventurerClass.Ranger => "ranger",
                AdventurerClass.Mage => "mage",
                _ => "priest",
            };
            sprite = Resources.Load<Sprite>("UI/Class/class_" + key);
            s_classIcons[c] = sprite;
            return sprite;
        }

        static readonly Dictionary<AdventurerClass, Sprite> s_classIcons = new();

        static Sprite s_disc;
        static Sprite Disc => s_disc != null ? s_disc : s_disc = SpriteFactory.Circle(64, Color.white, Color.white);

        const float TileHeight = 84f, Gap = 8f, CardPad = 10f;

        readonly List<(Image bg, Text text)> _summary = new();
        Button _notice;
        Text _noticeState;
        RectTransform _content;
        readonly List<PairRow> _pairs = new();
        Detail _detail;
        Adventurer _selected;
        Adventurer _lastClicked;
        float _lastClickTime;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 0);

            var bar = UIBuilder.Row(body, 40, 6);
            for (int i = 0; i < 5; i++) _summary.Add(EventCardView.Chip(bar, 13));
            UIBuilder.Rect(bar, "Spacer").Size(-1, -1, 1);
            // Recruitment notice (P4-38): pay to bring the next applicant within a few hours.
            _noticeState = UIBuilder.Label(bar, "", 13, TextAnchor.MiddleRight, UITheme.TextMuted).Size(300, 40);
            _notice = UIBuilder.Button(bar, $"모집 공고 ({VisitorService.NoticeCost}G)", PostNotice, 170, 38, UITheme.Header, 15);

            var main = UIBuilder.Rect(body, "Main");
            main.Size(-1, -1, -1, 1);
            var h = UIBuilder.HLayout(main, 12, 0);
            h.childForceExpandHeight = true;

            _content = UIBuilder.ScrollList(main, out var scroll, 12);
            scroll.GetComponent<RectTransform>().Size(-1, -1, 1, -1);
            _detail = Detail.Create(main, this);
        }

        // ---------------- Grouping by party (P4-49) ----------------

        enum Kind { Out, Back, Home, None }

        class Group
        {
            public string Title, Status, Detail, Right;
            public Kind Kind;
            public bool IsParty;
            public readonly List<Adventurer> Members = new();
        }

        readonly List<Group> _groups = new();
        readonly List<Group> _freeGroups = new();
        readonly List<Adventurer> _party = new();
        readonly HashSet<Adventurer> _placed = new();

        Group NewGroup(string title, bool isParty)
        {
            Group g;
            if (_freeGroups.Count > 0) { g = _freeGroups[^1]; _freeGroups.RemoveAt(_freeGroups.Count - 1); }
            else g = new Group();
            g.Title = title;
            g.IsParty = isParty;
            g.Status = g.Detail = g.Right = "";
            g.Kind = Kind.None;
            g.Members.Clear();
            _groups.Add(g);
            return g;
        }

        static string Where(Quests.Quest q) => q == null ? "" : $"{q.Title} · {Quests.QuestBoard.DescribeArea(q.Point)}";

        /// <summary>
        /// Parties: standing parties first (P4-50), then members sharing a quest, or a hunting band (P4-37) — from
        /// gathering at the gate to coming home — leader first; everyone else in one last group.
        /// </summary>
        void BuildGroups(IReadOnlyList<Adventurer> members)
        {
            _freeGroups.AddRange(_groups);
            _groups.Clear();
            _placed.Clear();
            if (Services.TryGet<GuildParties>(out var parties))
            {
                foreach (var p in parties.Parties)
                {
                    var leader = p.Leader;
                    if (leader == null) continue;
                    var g = NewGroup(p.Name, true);
                    var q = leader.CurrentQuest;
                    if (p.Settling) { g.Status = "정산 중"; g.Kind = Kind.Back; g.Detail = "돌아와서 몫을 나누는 중"; }
                    else if (p.OnOuting && p.Arrived.Count > 0)
                    {
                        g.Status = $"귀환 {p.Arrived.Count}/{p.Members.Count}";
                        g.Kind = Kind.Back;
                        g.Detail = (q != null ? Where(q) : "자율 사냥 마침") + " · 정산 대기";
                    }
                    else if (p.OnOuting) { g.Status = q != null ? "의뢰 중" : "자율 사냥"; g.Kind = Kind.Out; g.Detail = q != null ? Where(q) : "마을 밖에서 사냥 중"; }
                    else { g.Status = "마을"; g.Kind = Kind.Home; g.Detail = "마을에서 다음 일을 기다리는 중"; }
                    g.Right = p.Pool > 0 ? $"모은 골드 {UIBuilder.Colored($"{p.Pool}G", UITheme.Accent)}" : $"{p.Members.Count}명";
                    foreach (var m in p.Members)
                        if (Contains(members, m) && _placed.Add(m)) g.Members.Add(m);
                }
            }
            Services.TryGet<HuntBandService>(out var bands);
            foreach (var a in members)
            {
                if (_placed.Contains(a) || !a.IsAlive) continue;
                _party.Clear();
                var quest = a.CurrentQuest;
                string status = null, detail = null;
                var kind = Kind.Out;
                if (quest != null && quest.IsActive)
                {
                    // Assignees are in the order they took it: the one who took it first leads.
                    foreach (var t in quest.Assignees)
                        if (t is Adventurer m && m.IsAlive && m.CurrentQuest == quest && Contains(members, m)) _party.Add(m);
                    status = "의뢰 중";
                    detail = Where(quest);
                }
                else if (bands != null && bands.BandOf(a) is { } band)
                {
                    foreach (var m in band.Members) if (m.IsAlive && Contains(members, m)) _party.Add(m);
                    status = band.Mustering ? "모이는 중" : "자율 사냥";
                    kind = band.Mustering ? Kind.Home : Kind.Out;
                    detail = band.Mustering ? "자율 사냥 · 길드 앞에서 모이는 중" : "자율 사냥 중";
                }
                _party.RemoveAll(m => _placed.Contains(m)); // already shown with their standing party
                if (_party.Count < 2) continue;
                var g = NewGroup($"{_party[0].Data.name}의 파티", true);
                g.Status = status;
                g.Kind = kind;
                g.Detail = detail;
                g.Right = $"{_party.Count}명";
                foreach (var m in _party) { g.Members.Add(m); _placed.Add(m); }
            }
            Group solo = null;
            foreach (var a in members)
            {
                if (_placed.Contains(a)) continue;
                solo ??= NewGroup("파티 없음", false);
                solo.Members.Add(a);
            }
            if (solo != null)
            {
                solo.Status = "혼자";
                solo.Detail = "파티에 들지 않은 길드원";
                solo.Right = $"{solo.Members.Count}명";
            }
        }

        static bool Contains(IReadOnlyList<Adventurer> list, Adventurer a)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == a) return true;
            return false;
        }

        // ---------------- Refresh ----------------

        protected override void Refresh()
        {
            var roster = Services.Get<GuildRoster>();
            var members = roster.Members;
            BuildGroups(members);
            ShowSummary(members);

            if (_selected != null && (!_selected.IsAlive || !Contains(members, _selected))) _selected = null;
            if (_selected == null && _groups.Count > 0 && _groups[0].Members.Count > 0) _selected = _groups[0].Members[0];

            int pairCount = (_groups.Count + 1) / 2;
            while (_pairs.Count < pairCount) _pairs.Add(PairRow.Create(_content, this));
            for (int i = 0; i < _pairs.Count; i++)
            {
                bool on = i < pairCount;
                _pairs[i].gameObject.SetActive(on);
                if (!on) continue;
                var left = _groups[i * 2];
                var right = i * 2 + 1 < _groups.Count ? _groups[i * 2 + 1] : null;
                _pairs[i].Bind(left, right);
            }
            _detail.Bind(_selected);

            if (Services.TryGet<VisitorService>(out var visitors))
            {
                var reason = visitors.CannotPostNoticeReason();
                _notice.interactable = reason == null;
                _noticeState.text = reason ?? $"{VisitorService.NoticeMinHours:0}~{VisitorService.NoticeMaxHours:0}시간 안에 지원자가 광장에 옵니다";
            }
        }

        void ShowSummary(IReadOnlyList<Adventurer> members)
        {
            int alive = 0, outside = 0, danger = 0, wounded = 0, leave = 0, parties = 0;
            Services.TryGet<World.WorldService>(out var world);
            Services.TryGet<Legacy.GuildLegacy>(out var legacy);
            Services.TryGet<Welfare.GuildWelfare>(out var welfare);
            foreach (var a in members)
            {
                if (a == null || !a.IsAlive) continue;
                alive++;
                if (world != null && !world.Grid.IsInTown(a.Position)) outside++;
                if (a.HpRatio < 0.3f) danger++;
                if (legacy != null && legacy.IsWounded(a)) wounded++;
                if (welfare != null && welfare.IsOnLeave(a)) leave++;
            }
            foreach (var g in _groups) if (g.IsParty) parties++;
            EventCardView.SetChip(_summary[0], $"길드원 <b>{alive}</b>", ChipPlain, UITheme.Text);
            EventCardView.SetChip(_summary[1], $"파티 <b>{parties}</b>", ChipPlain, UITheme.Text);
            EventCardView.SetChip(_summary[2], $"밖 <b>{outside}</b> · 마을 <b>{alive - outside}</b>", ChipPlain, UITheme.Text);
            EventCardView.SetChip(_summary[3], $"위험 <b>{danger}</b>", danger > 0 ? ChipBad : ChipPlain, danger > 0 ? ChipBadText : UITheme.TextMuted);
            EventCardView.SetChip(_summary[4], $"부상 <b>{wounded}</b> · 휴가 <b>{leave}</b>", wounded + leave > 0 ? ChipWarn : ChipPlain,
                                  wounded + leave > 0 ? ChipWarnText : UITheme.TextMuted);
        }

        void PostNotice()
        {
            if (Services.TryGet<VisitorService>(out var visitors)) visitors.PostNotice();
        }

        void Select(Adventurer a)
        {
            if (a == null) return;
            // A second click on the same tile follows them (as a row click did before 2026-11-05).
            if (a == _lastClicked && Time.unscaledTime - _lastClickTime < 0.35f) Follow(a);
            _lastClicked = a;
            _lastClickTime = Time.unscaledTime;
            _selected = a;
            Refresh();
        }

        static void Follow(Adventurer a)
        {
            if (a == null) return;
            if (Services.TryGet<CameraController>(out var cam)) cam.JumpTo(a.Position);
            if (!Services.TryGet<InspectorView>(out var inspector)) return;
            inspector.Show(a);
            inspector.FocusSelected();
        }

        // ---------------- Little pieces ----------------

        static Image Bar(Transform parent, out Image fill, Color? trackColor = null)
        {
            var track = UIBuilder.Panel(parent, "Track", trackColor ?? Track, raycast: false);
            fill = UIBuilder.Panel(track.transform, "Fill", UITheme.Good, raycast: false);
            var rt = fill.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return track;
        }

        static void SetBar(Image fill, float ratio, Color color)
        {
            var rt = fill.rectTransform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            rt.offsetMax = Vector2.zero;
            fill.color = color;
        }

        static Color HpColor(float r) => r > 0.6f ? UITheme.Good : r > 0.3f ? UITheme.Warn : UITheme.Bad;

        static Color StressColor(Welfare.StressLevel level)
            => level == Welfare.StressLevel.Calm ? UITheme.Good : MemberCareView.LevelColor(level);

        static Image Avatar(Transform parent, float size, out Image ring, out Text letter)
        {
            ring = UIBuilder.SpriteImage(parent, "Avatar", Disc, Color.white);
            var rt = ring.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            ring.Size(size, size);
            var inner = UIBuilder.SpriteImage(ring.transform, "Inner", Disc, CardBg);
            inner.rectTransform.Stretch(3, 3, 3, 3);
            letter = UIBuilder.Label(ring.transform, "", Mathf.RoundToInt(size * 0.38f), TextAnchor.MiddleCenter, UITheme.Text, FontStyle.Bold);
            letter.rectTransform.Stretch();
            // The class item, a little inside the ring.
            var icon = UIBuilder.SpriteImage(ring.transform, "Icon", null, Color.white);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            float pad = Mathf.Round(size * 0.08f); // the icons have their own margin
            icon.rectTransform.Stretch(pad, pad, pad, pad);
            icon.enabled = false;
            return ring;
        }

        /// <summary>Colours the avatar for a class and puts its item in it (the initial when the icon is missing).</summary>
        static void SetAvatar(Image ring, Text letter, AdventurerClass c)
        {
            var color = ClassColor(c);
            ring.color = color;
            // The inside takes a little of the class colour so a dark item (the bow) still reads.
            if (ring.transform.Find("Inner")?.GetComponent<Image>() is { } inner) inner.color = Color.Lerp(CardBg, color, 0.28f);
            var sprite = ClassIcon(c);
            var icon = ring.transform.Find("Icon")?.GetComponent<Image>();
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.enabled = sprite != null;
            }
            letter.text = sprite != null ? "" : ClassInitial(c);
            letter.color = color;
        }

        /// <summary>What needs looking at, as coloured words after the state (at most two).</summary>
        static string Flags(Adventurer a)
        {
            var d = a.Data;
            var parts = new List<string>();
            if (Services.TryGet<Legacy.GuildLegacy>(out var legacy) && legacy.IsWounded(a)) parts.Add(UIBuilder.Colored("중상", ChipBadText));
            bool outside = Services.TryGet<World.WorldService>(out var world) && !world.Grid.IsInTown(a.Position);
            if (outside && d.potions <= 0) parts.Add(UIBuilder.Colored("물약 없음", ChipBadText));
            if (Services.TryGet<Welfare.GuildWelfare>(out var welfare))
            {
                var care = welfare.Care(a);
                if (care.Demand != Welfare.CareDemand.None) parts.Add(UIBuilder.Colored($"{Welfare.CareRules.Label(care.Demand)} 요구", ChipWarnText));
                else if (welfare.IsOnLeave(a)) parts.Add(UIBuilder.Colored("휴가", UITheme.Exp));
            }
            var level = Welfare.StressRules.LevelOf(d.stress);
            if (level != Welfare.StressLevel.Calm) parts.Add(UIBuilder.Colored(Welfare.StressRules.Label(level), MemberCareView.LevelColor(level)));
            if (d.fatigue >= 70f) parts.Add(UIBuilder.Colored("피곤", ChipWarnText));
            if (a.IsNewcomer) parts.Add(UIBuilder.Colored("신입", UITheme.Exp));
            if (parts.Count > 2) parts.RemoveRange(2, parts.Count - 2);
            return string.Join(" ", parts);
        }

        static bool InDanger(Adventurer a)
            => a.HpRatio < 0.3f || Services.TryGet<Legacy.GuildLegacy>(out var legacy) && legacy.IsWounded(a);

        // ---------------- Two party cards side by side ----------------

        class PairRow : MonoBehaviour
        {
            PartyCard _left, _right;

            public static PairRow Create(Transform parent, RosterView owner)
            {
                var rt = UIBuilder.Rect(parent, "PairRow");
                var h = UIBuilder.HLayout(rt, 12, 0, expandWidth: true);
                h.childForceExpandHeight = true;
                var row = rt.gameObject.AddComponent<PairRow>();
                row._left = PartyCard.Create(rt, owner);
                row._right = PartyCard.Create(rt, owner);
                return row;
            }

            public void Bind(Group left, Group right)
            {
                // A group with no neighbour takes the whole row, four tiles across (no empty half, 2026-11-05).
                _right.gameObject.SetActive(right != null);
                float hl = _left.Bind(left, right != null ? 2 : 4);
                float hr = right != null ? _right.Bind(right, 2) : 0f;
                ((RectTransform)transform).Size(-1, Mathf.Max(hl, hr), -1, 0);
            }
        }

        class PartyCard : MonoBehaviour
        {
            const int MaxCols = 4;
            Image _bg;
            RectTransform _inner;
            Text _title, _detail, _right;
            (Image bg, Text text) _status;
            readonly List<RectTransform> _rows = new();
            readonly List<MemberTile> _tiles = new();
            RosterView _owner;

            public static PartyCard Create(Transform parent, RosterView owner)
            {
                var bg = UIBuilder.Panel(parent, "PartyCard", CardBg, raycast: false);
                bg.Size(-1, -1, 1);
                var card = bg.gameObject.AddComponent<PartyCard>();
                card._bg = bg;
                card._owner = owner;
                card._inner = UIBuilder.Rect(bg.transform, "Inner");
                card._inner.Stretch();
                var v = UIBuilder.VLayout(card._inner, Gap, 0);
                v.padding = new RectOffset((int)CardPad, (int)CardPad, (int)CardPad, (int)CardPad);
                v.childAlignment = TextAnchor.UpperLeft;

                var head = UIBuilder.Row(card._inner, 22, 8);
                card._title = UIBuilder.Label(head, "", 15, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold);
                card._title.horizontalOverflow = HorizontalWrapMode.Overflow;
                card._status = EventCardView.Chip(head, 12);
                UIBuilder.Rect(head, "Spacer").Size(-1, -1, 1);
                card._right = UIBuilder.Label(head, "", 12, TextAnchor.MiddleRight, UITheme.TextMuted);
                card._right.horizontalOverflow = HorizontalWrapMode.Overflow;
                card._detail = UIBuilder.Label(card._inner, "", 12, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 18);
                return card;
            }

            /// <summary>Shows a group; returns the height it needs.</summary>
            public float Bind(Group g, int cols)
            {
                _bg.color = CardBg;
                _inner.gameObject.SetActive(true);
                _title.text = g.Title;
                _title.color = g.IsParty ? UITheme.Accent : UITheme.TextMuted;
                var (pill, pillText) = g.Kind switch
                {
                    Kind.Out => (PillOut, PillOutText),
                    Kind.Back => (PillBack, PillBackText),
                    Kind.Home => (PillHome, PillHomeText),
                    _ => (ChipPlain, UITheme.TextMuted),
                };
                EventCardView.SetChip(_status, g.Status, pill, pillText);
                _detail.text = g.Detail;
                _right.text = g.Right;

                int rows = (g.Members.Count + cols - 1) / cols;
                while (_rows.Count < rows)
                {
                    var row = UIBuilder.Row(_inner, TileHeight, Gap);
                    _rows.Add(row);
                    for (int c = 0; c < MaxCols; c++) _tiles.Add(MemberTile.Create(row, _owner));
                }
                for (int r = 0; r < _rows.Count; r++) _rows[r].gameObject.SetActive(r < rows);
                for (int r = 0; r < _rows.Count; r++)
                    for (int c = 0; c < MaxCols; c++)
                    {
                        var tile = _tiles[r * MaxCols + c];
                        tile.gameObject.SetActive(c < cols);
                        if (c >= cols) continue;
                        int i = r * cols + c;
                        if (i < g.Members.Count) tile.Bind(g.Members[i], g.IsParty && i == 0, g.Members[i] == _owner._selected);
                        else tile.Empty();
                    }
                return CardPad * 2 + 22 + Gap + 18 + Gap + rows * TileHeight + Mathf.Max(0, rows - 1) * Gap;
            }
        }

        class MemberTile : MonoBehaviour
        {
            Image _edge, _face, _ring, _hpFill, _stressFill;
            Text _letter, _line, _state;
            RectTransform _content;
            Button _button;
            Adventurer _adv;

            public static MemberTile Create(Transform parent, RosterView owner)
            {
                var edge = UIBuilder.Panel(parent, "MemberTile", TileBg);
                edge.Size(-1, -1, 1);
                var tile = edge.gameObject.AddComponent<MemberTile>();
                tile._edge = edge;
                tile._face = UIBuilder.Panel(edge.transform, "Face", TileBg);
                tile._face.rectTransform.Stretch(2, 2, 2, 2);
                tile._button = edge.gameObject.AddComponent<Button>();
                tile._button.targetGraphic = tile._face;
                var colors = tile._button.colors;
                colors.highlightedColor = new Color(1.2f, 1.2f, 1.3f, 1f);
                colors.colorMultiplier = 1.4f;
                tile._button.colors = colors;
                tile._button.onClick.AddListener(() => owner.Select(tile._adv));
                tile._button.onClick.AddListener(GuildMaster.Audio.Sound.Click);

                tile._content = UIBuilder.Rect(tile._face.transform, "Content");
                tile._content.Stretch();
                var h = UIBuilder.HLayout(tile._content, 8, 0);
                h.padding = new RectOffset(8, 8, 6, 6);
                h.childForceExpandHeight = false;
                h.childAlignment = TextAnchor.MiddleLeft;
                Avatar(tile._content, 40, out tile._ring, out tile._letter);

                var info = UIBuilder.Rect(tile._content, "Info");
                info.Size(-1, -1, 1);
                UIBuilder.VLayout(info, 3, 0);
                tile._line = UIBuilder.Label(info, "", 14).Size(-1, 18);
                tile._line.horizontalOverflow = HorizontalWrapMode.Overflow;
                tile._hpFill = BarRow(info, "HP");
                tile._stressFill = BarRow(info, "스트레스");
                tile._state = UIBuilder.Label(info, "", 12).Size(-1, 16);
                tile._state.horizontalOverflow = HorizontalWrapMode.Overflow;
                return tile;
            }

            static Image BarRow(Transform parent, string label)
            {
                var row = UIBuilder.Row(parent, 12, 4);
                var name = UIBuilder.Label(row, label, 11, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(42, 12);
                name.verticalOverflow = VerticalWrapMode.Overflow;
                name.horizontalOverflow = HorizontalWrapMode.Overflow;
                var holder = UIBuilder.Rect(row, "BarHolder");
                holder.Size(-1, 12, 1);
                var track = Bar(holder, out var fill, TileTrack);
                track.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0, 8));
                return fill;
            }

            public void Bind(Adventurer a, bool leader, bool selected)
            {
                _adv = a;
                var d = a.Data;
                bool danger = InDanger(a);
                _content.gameObject.SetActive(true);
                _button.interactable = true;
                _edge.color = selected ? UITheme.Accent : danger ? TileDanger : TileBg;
                _face.color = selected ? TileSelected : TileBg;
                var cls = ClassColor(d.cls);
                SetAvatar(_ring, _letter, d.cls);
                string star = leader ? UIBuilder.Colored(" ★", UITheme.Accent) : "";
                _line.text = $"<b>{d.name}</b>{star} {UIBuilder.Colored($"{AdventurerData.ClassLabel(d.cls)} Lv.{d.level}", cls)}";
                SetBar(_hpFill, a.HpRatio, HpColor(a.HpRatio));
                var level = Welfare.StressRules.LevelOf(d.stress);
                SetBar(_stressFill, d.stress / 100f, StressColor(level));
                string flags = Flags(a);
                _state.text = UIBuilder.Colored(a.StateLabel, danger ? ChipBadText : UITheme.Text) + (flags.Length > 0 ? "  " + flags : "");
            }

            /// <summary>The empty half of a row (an odd number of members).</summary>
            public void Empty()
            {
                _adv = null;
                _content.gameObject.SetActive(false);
                _button.interactable = false;
                _edge.color = new Color(0f, 0f, 0f, 0f);
                _face.color = new Color(0f, 0f, 0f, 0f);
            }
        }

        // ---------------- The chosen member ----------------

        class Detail : MonoBehaviour
        {
            Image _ring;
            Text _letter, _name, _cls, _who, _traits, _now, _empty;
            Image _hp, _exp, _fatigue, _stress;
            Text _hpText, _expText, _fatigueText, _stressText;
            readonly List<Text> _stats = new();
            RectTransform _body;
            Adventurer _adv;

            public static Detail Create(Transform parent, RosterView owner)
            {
                var bg = UIBuilder.Panel(parent, "Detail", CardBg, raycast: false);
                bg.Size(320, -1);
                var me = bg.gameObject.AddComponent<Detail>();
                me._empty = UIBuilder.Label(bg.transform, "길드원이 없습니다.", 14, TextAnchor.MiddleCenter, UITheme.TextMuted);
                me._empty.rectTransform.Stretch();
                me._body = UIBuilder.Rect(bg.transform, "Body");
                me._body.Stretch();
                var v = UIBuilder.VLayout(me._body, 10, 12);

                var head = UIBuilder.Row(me._body, 60, 12);
                head.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false; // the avatar stays round
                Avatar(head, 56, out me._ring, out me._letter);
                me._ring.Size(56, 56);
                var names = UIBuilder.Rect(head, "Names");
                names.Size(-1, -1, 1);
                var nv = UIBuilder.VLayout(names, 1, 0);
                nv.childAlignment = TextAnchor.MiddleLeft;
                me._name = UIBuilder.Label(names, "", 18, TextAnchor.MiddleLeft, UITheme.Text, FontStyle.Bold).Size(-1, 24);
                me._name.horizontalOverflow = HorizontalWrapMode.Overflow;
                me._cls = UIBuilder.Label(names, "", 13).Size(-1, 18);
                me._who = UIBuilder.Label(names, "", 12, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 16);

                me._hp = me.BarLine("HP", out me._hpText);
                me._exp = me.BarLine("EXP", out me._expText);
                me._fatigue = me.BarLine("피로", out me._fatigueText);
                me._stress = me.BarLine("스트레스", out me._stressText);

                for (int r = 0; r < 2; r++)
                {
                    var row = UIBuilder.Row(me._body, 44, 6);
                    for (int c = 0; c < 3; c++)
                    {
                        var cell = UIBuilder.Panel(row, "Stat", TileBg, raycast: false);
                        cell.Size(-1, 44, 1);
                        var cv = UIBuilder.VLayout(cell, 0, 0);
                        cv.padding = new RectOffset(8, 8, 4, 4);
                        UIBuilder.Label(cell.transform, StatNames[r * 3 + c], 11, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 14);
                        me._stats.Add(UIBuilder.Label(cell.transform, "", 15, TextAnchor.MiddleLeft, UITheme.Text, FontStyle.Bold).Size(-1, 20));
                    }
                }

                me._traits = UIBuilder.Label(me._body, "", 13, TextAnchor.UpperLeft);
                me._traits.verticalOverflow = VerticalWrapMode.Overflow;
                me._now = UIBuilder.Label(me._body, "", 12, TextAnchor.UpperLeft, UITheme.TextMuted);
                me._now.verticalOverflow = VerticalWrapMode.Overflow;
                UIBuilder.Rect(me._body, "Push").Size(-1, 0, -1, 1);

                var buttons = UIBuilder.Row(me._body, 44, 6);
                UIBuilder.Button(buttons, "추적 (F)", () => Follow(me._adv), -1, 44, UITheme.Header, 14).Size(-1, 44, 1);
                UIBuilder.Button(buttons, "상세", me.OpenDetail, -1, 44, UITheme.Header, 14).Size(-1, 44, 1);
                UIBuilder.Button(buttons, "관리", me.OpenCare, -1, 44, UITheme.Header, 14).Size(-1, 44, 1);
                return me;
            }

            static readonly string[] StatNames = { "골드", "물약", "조명탄", "처치", "완료 의뢰", "전리품" };

            Image BarLine(string label, out Text value)
            {
                var row = UIBuilder.Row(_body, 16, 8);
                UIBuilder.Label(row, label, 12, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(56, 16);
                var holder = UIBuilder.Rect(row, "Holder");
                holder.Size(-1, 16, 1);
                var track = Bar(holder, out var fill);
                track.rectTransform.Anchor(new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0, 8));
                value = UIBuilder.Label(row, "", 12, TextAnchor.MiddleRight).Size(76, 16);
                return fill;
            }

            public void Bind(Adventurer a)
            {
                _adv = a;
                _body.gameObject.SetActive(a != null);
                _empty.gameObject.SetActive(a == null);
                if (a == null) return;
                var d = a.Data;
                var cls = ClassColor(d.cls);
                SetAvatar(_ring, _letter, d.cls);
                bool leader = Services.TryGet<GuildParties>(out var parties) && parties.PartyOf(a) is { } p && p.Leader == a;
                string titles = d.titles.Count > 0 ? " " + UIBuilder.Colored($"[칭호 {d.titles.Count}]", UITheme.Accent) : "";
                _name.text = $"{d.name} {(leader ? UIBuilder.Colored("★", UITheme.Accent) + " " : "")}{InspectorView.GradeTag(d.grade)}{titles}";
                _cls.text = UIBuilder.Colored($"{AdventurerData.ClassLabel(d.cls)} Lv.{d.level}", cls) + (a.IsNewcomer ? " " + UIBuilder.Colored("신입", UITheme.Exp) : "");
                _who.text = $"{d.mbti} · 가치관 {d.values}";

                SetBar(_hp, a.HpRatio, HpColor(a.HpRatio));
                _hpText.text = $"{a.Hp:0}/{a.Stats.MaxHp:0}";
                SetBar(_exp, d.ExpToNextLevel > 0 ? d.exp / (float)d.ExpToNextLevel : 0f, UITheme.Exp);
                _expText.text = $"{d.exp}/{d.ExpToNextLevel}";
                SetBar(_fatigue, d.fatigue / 100f, d.fatigue >= 70f ? UITheme.Warn : UITheme.TextMuted);
                _fatigueText.text = $"{d.fatigue:0}";
                var level = Welfare.StressRules.LevelOf(d.stress);
                SetBar(_stress, d.stress / 100f, StressColor(level));
                _stressText.text = $"{d.stress:0} {Welfare.StressRules.Label(level)}";

                _stats[0].text = LivingCostRules.Purse(d.gold, d.debt);
                _stats[1].text = $"{d.potions}";
                _stats[2].text = $"{d.flares}";
                _stats[3].text = $"{d.kills}";
                _stats[4].text = $"{d.questsCompleted}";
                _stats[5].text = $"{d.LootCount}";

                _traits.text = Traits.TraitSet.Format(a.Traits.Traits, UITheme.Good, UITheme.Bad);
                string quest = a.CurrentQuest != null ? $" · 의뢰 {a.CurrentQuest.Title}" : "";
                string flags = Flags(a);
                _now.text = $"지금: {UIBuilder.Colored(a.StateLabel, UITheme.Accent)}{quest} · 기분 {MoodFormat.Mood(a)}" + (flags.Length > 0 ? "\n" + flags : "");
            }

            void OpenDetail()
            {
                if (_adv != null && Services.TryGet<AdventurerDetailView>(out var view)) view.Show(_adv);
            }

            void OpenCare()
            {
                if (_adv != null && Services.TryGet<MemberCareView>(out var view)) view.Show(_adv);
            }
        }
    }
}
