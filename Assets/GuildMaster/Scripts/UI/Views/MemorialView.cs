using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Legacy;
using GuildMaster.Welfare;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The wall of the fallen (F-6): everyone who died, how they fell, what became of what they left behind, and the
    /// guild master's answers — a memorial that eases the survivors' minds and a member sent to fetch the relics.
    /// Opened with H and from the "유품 회수 가능" notifications.
    /// 2026-11-15 (user picked layout A: "기능적으로나 UI적으로나 너무 슴슴"): a garden of stones on the left — the class cut
    /// in each, flowers once a memorial was held, a candle once a relic came home, a gold stone for a hero, a badge while
    /// their killer lives on as a nemesis — and on the right a parchment page for the chosen one: the line on the stone,
    /// the day, the last words, the relics, who carries one on (and handing it on), the friends' words, the anniversary.
    /// </summary>
    public class MemorialView : UIPanel
    {
        public override string Title => "추모의 벽";
        protected override float RefreshInterval => 0.5f;

        const float CardW = 150f, CardH = 214f, GapX = 12f, GapY = 12f, GardenW = 680f;

        static readonly Color Ink = new(0.24f, 0.16f, 0.09f, 1f);
        static readonly Color InkMuted = new(0.46f, 0.34f, 0.22f, 1f);
        static readonly Color InkGold = new(0.68f, 0.45f, 0.08f, 1f);
        static readonly Color InkRed = new(0.62f, 0.18f, 0.12f, 1f);
        static readonly Color StoneText = new(0.22f, 0.2f, 0.18f, 1f);
        static readonly Color NemesisAlive = new(0.70f, 0.20f, 0.16f, 1f);
        static readonly Color NemesisDown = new(0.34f, 0.52f, 0.28f, 1f);

        Text _summary, _empty;
        RectTransform _garden;
        readonly List<Stone> _stones = new();
        Page _page;
        string _resultText = "";
        int _focusId;

        protected override void Build(RectTransform body)
        {
            _summary = UIBuilder.Label(body, "", 15, TextAnchor.MiddleLeft, UITheme.TextMuted);
            _summary.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(4, 0), new Vector2(GardenW, 26));
            UISkin.Outline(_summary);

            // The garden: dark earth, stones in rows, scrolling when there are many.
            var earth = UIBuilder.Panel(body, "Garden", new Color(0.13f, 0.11f, 0.09f, 0.92f));
            var ert = earth.rectTransform;
            ert.anchorMin = new Vector2(0, 0);
            ert.anchorMax = new Vector2(0, 1);
            ert.pivot = new Vector2(0, 1);
            ert.offsetMin = new Vector2(0, 0);
            ert.offsetMax = new Vector2(GardenW, -32);
            HudStyle.Card(earth);
            earth.color = new Color(0.42f, 0.38f, 0.34f, 1f);
            var scroll = earth.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = UIBuilder.Rect(earth.transform, "Viewport").Stretch(14, 14, 14, 14);
            viewport.gameObject.AddComponent<RectMask2D>();
            _garden = UIBuilder.Rect(viewport, "Stones");
            _garden.anchorMin = new Vector2(0, 1);
            _garden.anchorMax = new Vector2(1, 1);
            _garden.pivot = new Vector2(0.5f, 1);
            _garden.sizeDelta = Vector2.zero;
            var grid = _garden.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CardW, CardH);
            grid.spacing = new Vector2(GapX, GapY);
            grid.padding = new RectOffset(6, 6, 14, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.UpperCenter;
            UIBuilder.FitContent(_garden);
            scroll.viewport = viewport;
            scroll.content = _garden;
            _empty = UIBuilder.Label(viewport, "아직 쓰러진 길드원이 없습니다.\n\n죽은 길드원은 되살릴 수 없지만, 이곳에서 그들을 기억하고\n유품을 되찾아 남은 이들에게 물려줄 수 있습니다.",
                16, TextAnchor.MiddleCenter, UITheme.TextMuted);
            _empty.rectTransform.Stretch(20, 20, 20, 20);

            _page = Page.Create(body, this);
        }

        /// <summary>Opens the wall with this record chosen (from a notification).</summary>
        public void ShowRecord(int recordId)
        {
            _focusId = recordId;
            _resultText = "";
            if (!Visible) Open();
            else
            {
                BringToFront();
                Refresh();
            }
        }

        protected override void OnOpened()
        {
            if (!Services.TryGet<GuildLegacy>(out _)) _focusId = 0;
        }

        void Choose(int id)
        {
            if (_focusId == id) return;
            _focusId = id;
            _resultText = "";
            Refresh();
        }

        protected override void Refresh()
        {
            if (!Services.TryGet<GuildLegacy>(out var legacy) || !Services.TryGet<GameClock>(out var clock))
            {
                _summary.text = "추모의 벽을 열 수 없습니다.";
                EnsureRows(_garden, _stones, 0, Stone.Create);
                _page.Show(null, null, 0f, 0, "");
                return;
            }
            var fallen = legacy.Fallen;
            int memorials = 0, nemeses = 0, heroes = 0;
            foreach (var r in fallen)
            {
                if (r.MemorialHeld) memorials++;
                if (r.IsHero) heroes++;
                if (NemesisOf(r, out bool alive) != null && alive) nemeses++;
            }
            _summary.text = fallen.Count == 0 ? "잠든 동료가 없습니다."
                : $"잠든 동료 {fallen.Count}명 · 영웅 {heroes} · 추모식 {memorials}" + (nemeses > 0 ? UIBuilder.Colored($" · 원수 {nemeses}명 생존", new Color(1f, 0.6f, 0.5f)) : "");
            _empty.gameObject.SetActive(fallen.Count == 0);

            // Newest first: the one who just fell is at the top. The chosen one stays chosen.
            if (fallen.Count > 0 && legacy.Find(_focusId) == null) _focusId = fallen[fallen.Count - 1].Id;
            EnsureRows(_garden, _stones, fallen.Count, Stone.Create);
            FallenRecord chosen = null;
            for (int i = 0; i < fallen.Count; i++)
            {
                var r = fallen[fallen.Count - 1 - i];
                NemesisOf(r, out bool alive);
                _stones[i].Bind(r, r.Id == _focusId, NemesisState(r), this);
                if (r.Id == _focusId) chosen = r;
            }
            _page.Show(chosen, legacy, clock.TotalHours, clock.Day, _resultText);
        }

        /// <summary>The nemesis that killed them (P4-109), and whether it still lives.</summary>
        static Nemeses.Nemesis NemesisOf(FallenRecord r, out bool alive)
        {
            alive = false;
            if (r == null || !Services.TryGet<Nemeses.NemesisService>(out var svc)) return null;
            foreach (var n in svc.Living)
                foreach (var d in n.Deeds)
                    if (d.kind == Nemeses.DeedKind.Killed && d.victim_id == r.AdventurerId) { alive = true; return n; }
            foreach (var n in svc.FallenOnes)
                foreach (var d in n.Deeds)
                    if (d.kind == Nemeses.DeedKind.Killed && d.victim_id == r.AdventurerId) return n;
            return null;
        }

        /// <summary>0 = no nemesis, 1 = it lives on, 2 = it has fallen.</summary>
        static int NemesisState(FallenRecord r) => NemesisOf(r, out bool alive) == null ? 0 : alive ? 1 : 2;

        // ---------------- Actions ----------------

        void HoldMemorial(FallenRecord rec)
        {
            if (rec == null || !Services.TryGet<GuildLegacy>(out var legacy)) return;
            Report(legacy.HoldMemorial(rec));
        }

        void SendRetriever(FallenRecord rec)
        {
            if (rec == null || !Services.TryGet<GuildLegacy>(out var legacy)) return;
            Report(legacy.SendRetriever(rec));
        }

        void Bequeath(FallenRecord rec, Adventurer heir)
        {
            if (rec == null || !Services.TryGet<GuildLegacy>(out var legacy)) return;
            Report(legacy.Bequeath(rec, heir));
        }

        void Report(CareResult r)
        {
            _resultText = UIBuilder.Colored(r.Message, r.Ok ? new Color(0.22f, 0.45f, 0.18f) : InkRed);
            Refresh();
        }

        static Text InkLabel(Transform parent, string text, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var t = UIBuilder.Label(parent, text, size, anchor, color, style);
            t.raycastTarget = false;
            return t;
        }

        // ---------------- A stone in the garden ----------------

        class Stone : MonoBehaviour
        {
            Image _glow, _body, _mark, _flowers, _candle, _badge;
            Text _name, _sub, _badgeText;
            FallenRecord _rec;
            MemorialView _owner;

            public static Stone Create(Transform parent)
            {
                var root = UIBuilder.Rect(parent, "Stone");
                var s = root.gameObject.AddComponent<Stone>();
                s._glow = HudStyle.NewImage(root, "mem_glow", "Glow");
                s._glow.rectTransform.Stretch(-18, -18, -16, -10);
                s._body = HudStyle.NewImage(root, "mem_stone", "Body");
                s._body.rectTransform.Stretch();
                s._body.raycastTarget = true;
                if (s._body.sprite == null) s._body.color = new Color(0.6f, 0.58f, 0.55f, 1f);
                s._mark = HudStyle.NewImage(root, "mem_cls_warrior", "Mark");
                Place(s._mark.rectTransform, 0, 30, 60, 60);
                s._name = InkLabel(root, "", 22, StoneText, FontStyle.Bold, TextAnchor.MiddleCenter);
                Place(s._name.rectTransform, 0, 98, 140, 30);
                s._sub = InkLabel(root, "", 13, new Color(0.3f, 0.28f, 0.26f, 1f), FontStyle.Bold, TextAnchor.MiddleCenter);
                Place(s._sub.rectTransform, 0, 126, 140, 20);
                s._flowers = HudStyle.NewImage(root, "mem_flowers", "Flowers");
                s._flowers.rectTransform.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(-34, -164), new Vector2(62, 30));
                s._candle = HudStyle.NewImage(root, "mem_candle", "Candle");
                s._candle.rectTransform.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(46, -160), new Vector2(22, 40));
                s._badge = HudStyle.NewImage(root, "mem_badge", "Badge");
                s._badge.rectTransform.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 8), new Vector2(84, 24));
                s._badgeText = InkLabel(s._badge.transform, "", 12, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
                s._badgeText.rectTransform.Stretch(0, 0, 0, 1);
                UISkin.Outline(s._badgeText);
                var button = root.gameObject.AddComponent<Button>();
                button.targetGraphic = s._body;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => { if (s._owner != null && s._rec != null) s._owner.Choose(s._rec.Id); });
                button.onClick.AddListener(Audio.Sound.Click);
                root.gameObject.AddComponent<ButtonJuice>();
                s._body.Tooltip(() => s._rec == null ? "" : $"{s._rec.Name} — “{s._rec.Epitaph}”");
                return s;
            }

            static void Place(RectTransform rt, float x, float top, float w, float h)
                => rt.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(x, -top), new Vector2(w, h));

            public void Bind(FallenRecord rec, bool chosen, int nemesis, MemorialView owner)
            {
                _rec = rec;
                _owner = owner;
                _glow.gameObject.SetActive(chosen);
                HudStyle.Slice(_body, rec.IsHero ? "mem_stone_hero" : "mem_stone");
                HudStyle.Slice(_mark, ClassMark(rec.Cls));
                _name.text = rec.Name;
                _sub.text = $"Lv.{rec.Level} · {rec.Day}일차";
                _flowers.gameObject.SetActive(rec.MemorialHeld);
                _candle.gameObject.SetActive(rec.Count(RelicState.Recovered) > 0);
                _badge.gameObject.SetActive(nemesis != 0);
                if (nemesis != 0)
                {
                    _badge.color = nemesis == 1 ? NemesisAlive : NemesisDown;
                    _badgeText.text = nemesis == 1 ? "원수 생존" : "원한 풀림";
                }
            }
        }

        static string ClassMark(AdventurerClass c) => c switch
        {
            AdventurerClass.Ranger => "mem_cls_ranger",
            AdventurerClass.Mage => "mem_cls_mage",
            AdventurerClass.Priest => "mem_cls_priest",
            _ => "mem_cls_warrior",
        };

        // ---------------- The page for the chosen one ----------------

        class Page
        {
            const int MaxRelics = 6;

            MemorialView _owner;
            RectTransform _root;
            Image _portrait, _portraitMark, _badge;
            Text _name, _cls, _hero, _epitaph, _badgeText;
            Text _fell, _tally, _last;
            readonly List<(Image slot, Image mark)> _relics = new();
            Text _relicNone, _heirLine;
            RectTransform _heirRow;
            readonly List<Button> _heirButtons = new();
            readonly List<Adventurer> _heirs = new();
            readonly List<(Image box, Text who, Text line)> _quotes = new();
            Text _quotesTitle, _anniversary, _result, _blank;
            Button _memorial, _fetch, _place;
            string _memorialTip = "", _fetchTip = "", _heirTip = "";
            FallenRecord _rec;

            public static Page Create(RectTransform body, MemorialView owner)
            {
                var p = new Page { _owner = owner };
                var paper = HudStyle.NewImage(body, "mem_paper", "Page");
                if (paper.sprite == null) paper.color = new Color(0.93f, 0.87f, 0.73f, 1f);
                var rt = paper.rectTransform;
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(1, 1);
                rt.offsetMin = new Vector2(GardenW + 16, 0);
                rt.offsetMax = new Vector2(0, 0);
                p._root = rt;

                // Who: a small stone, the name, class, hero line and the line on the stone.
                p._portrait = HudStyle.NewImage(rt, "mem_stone", "Portrait");
                At(p._portrait.rectTransform, 26, 22, 76, 106);
                p._portraitMark = HudStyle.NewImage(p._portrait.transform, "mem_cls_warrior", "Mark");
                p._portraitMark.rectTransform.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(32, 32));
                p._name = InkLabel(rt, "", 28, Ink, FontStyle.Bold);
                At(p._name.rectTransform, 118, 20, 230, 38);
                p._cls = InkLabel(rt, "", 15, InkMuted, FontStyle.Bold);
                At(p._cls.rectTransform, 118, 58, 360, 22);
                p._hero = InkLabel(rt, "", 15, InkGold, FontStyle.Bold);
                At(p._hero.rectTransform, 118, 80, 380, 22);
                p._epitaph = InkLabel(rt, "", 16, new Color(0.38f, 0.25f, 0.13f, 1f), FontStyle.Italic);
                At(p._epitaph.rectTransform, 118, 102, 400, 26);
                p._badge = HudStyle.NewImage(rt, "mem_badge", "Nemesis");
                p._badge.rectTransform.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-26, -26), new Vector2(96, 26));
                p._badge.raycastTarget = true;
                p._badgeText = InkLabel(p._badge.transform, "", 13, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
                p._badgeText.rectTransform.Stretch(0, 0, 0, 1);
                UISkin.Outline(p._badgeText);
                p._badge.Tooltip(() => p.NemesisTip());

                var line = HudStyle.NewImage(rt, "mem_divider", "Line");
                Stretched(line.rectTransform, 140, 4);

                // That day.
                p._fell = InkLabel(rt, "", 15, Ink);
                Stretched(p._fell.rectTransform, 152, 22);
                p._tally = InkLabel(rt, "", 15, Ink);
                Stretched(p._tally.rectTransform, 176, 22);
                p._last = InkLabel(rt, "", 15, Ink);
                Stretched(p._last.rectTransform, 200, 22);

                // The relics.
                var relicTitle = InkLabel(rt, "유품", 16, InkMuted, FontStyle.Bold);
                At(relicTitle.rectTransform, 28, 236, 60, 44);
                for (int i = 0; i < MaxRelics; i++)
                {
                    var slot = HudStyle.NewImage(rt, "mem_slot", "Relic");
                    At(slot.rectTransform, 84 + i * 54, 236, 44, 44);
                    slot.raycastTarget = true;
                    var mark = HudStyle.NewImage(slot.transform, "mem_mark_home", "Mark");
                    mark.rectTransform.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-2, -2), new Vector2(20, 20));
                    int k = i;
                    slot.Tooltip(() => p.RelicTip(k));
                    p._relics.Add((slot, mark));
                }
                p._relicNone = InkLabel(rt, "남긴 물건이 없습니다.", 15, InkMuted);
                At(p._relicNone.rectTransform, 84, 236, 360, 44);

                // Who carries one on — or the friends it could go to.
                var heirTitle = InkLabel(rt, "물려받은 이", 16, InkMuted, FontStyle.Bold);
                At(heirTitle.rectTransform, 28, 292, 110, 34);
                p._heirLine = InkLabel(rt, "", 15, Ink);
                At(p._heirLine.rectTransform, 132, 292, 380, 34);
                p._heirLine.Tooltip(() => p._heirTip);
                p._heirRow = UIBuilder.Rect(rt, "Heirs");
                At(p._heirRow, 132, 292, 352, 34);
                UIBuilder.HLayout(p._heirRow, 6);
                for (int i = 0; i < MemorialRules.HeirChoices; i++)
                {
                    int k = i;
                    var b = UIBuilder.Button(p._heirRow, "", () => p.OnHeir(k), 82, 34, new Color(0.62f, 0.5f, 0.3f), 14);
                    b.Tooltip(() => p.HeirChoiceTip(k));
                    p._heirButtons.Add(b);
                }

                // The friends' words.
                p._quotesTitle = InkLabel(rt, "남은 이들의 말", 16, InkMuted, FontStyle.Bold);
                Stretched(p._quotesTitle.rectTransform, 338, 24);
                for (int i = 0; i < MemorialRules.Remembrances; i++)
                {
                    var box = HudStyle.NewImage(rt, "mem_quote", "Quote");
                    if (box.sprite == null) box.color = new Color(0.9f, 0.82f, 0.66f, 1f);
                    Stretched(box.rectTransform, 366 + i * 46, 38);
                    var who = InkLabel(box.transform, "", 15, InkRed, FontStyle.Bold);
                    who.rectTransform.Anchor(new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(60, 0));
                    var said = InkLabel(box.transform, "", 14, Ink);
                    said.rectTransform.Stretch(76, 10, 0, 0);
                    p._quotes.Add((box, who, said));
                }
                p._anniversary = InkLabel(rt, "", 14, InkMuted);
                Stretched(p._anniversary.rectTransform, 462, 22);

                p._blank = InkLabel(rt, "왼쪽에서 비석을 고르세요.", 17, InkMuted, FontStyle.Normal, TextAnchor.MiddleCenter);
                p._blank.rectTransform.Stretch(30, 30, 30, 30);

                // What the guild master can do.
                p._result = InkLabel(rt, "", 14, Ink);
                p._result.rectTransform.Anchor(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 76), new Vector2(-56, 40));
                p._result.horizontalOverflow = HorizontalWrapMode.Wrap;
                var actions = UIBuilder.Rect(rt, "Actions");
                actions.Anchor(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(-56, 44));
                UIBuilder.HLayout(actions, 8, 0, true);
                p._memorial = UIBuilder.Button(actions, "추모식", () => owner.HoldMemorial(p._rec), -1, 44, null, 15);
                p._memorial.Tooltip(() => p._memorialTip);
                p._fetch = UIBuilder.Button(actions, "유품 회수", () => owner.SendRetriever(p._rec), -1, 44, null, 15);
                p._fetch.Tooltip(() => p._fetchTip);
                p._place = UIBuilder.Button(actions, "현장 보기", p.OnPlace, -1, 44, null, 15);
                p._place.Tooltip(() => "쓰러진 자리로 카메라를 옮깁니다.");
                return p;
            }

            static void At(RectTransform rt, float x, float top, float w, float h)
                => rt.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -top), new Vector2(w, h));

            /// <summary>The page's width less its margins, at that height.</summary>
            static void Stretched(RectTransform rt, float top, float h)
            {
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(0.5f, 1);
                rt.offsetMin = new Vector2(28, -top - h);
                rt.offsetMax = new Vector2(-28, -top);
            }

            public void Show(FallenRecord rec, GuildLegacy legacy, float now, int today, string result)
            {
                _rec = rec;
                bool any = rec != null && legacy != null;
                for (int i = 0; i < _root.childCount; i++)
                {
                    var child = _root.GetChild(i).gameObject;
                    if (child != _blank.gameObject) child.SetActive(any);
                }
                _blank.gameObject.SetActive(!any);
                if (!any) return;

                HudStyle.Slice(_portrait, rec.IsHero ? "mem_stone_hero" : "mem_stone");
                HudStyle.Slice(_portraitMark, ClassMark(rec.Cls));
                _name.text = rec.Name;
                _cls.text = $"{rec.ClassLabel} Lv.{rec.Level} · 가치관 {rec.Values}";
                _hero.text = rec.IsHero ? $"★ 길드의 영웅 — {rec.HeroReason}" : "";
                _epitaph.text = string.IsNullOrEmpty(rec.Epitaph) ? "" : $"“{rec.Epitaph}”";

                int nemesis = NemesisState(rec);
                _badge.gameObject.SetActive(nemesis != 0);
                _badge.color = nemesis == 1 ? NemesisAlive : NemesisDown;
                _badgeText.text = nemesis == 1 ? "원수 생존" : "원한 풀림";

                _fell.text = $"{rec.Day}일차 · {LegacyTexts.Killer(rec.KillerName)}";
                _tally.text = $"처치 {rec.Kills} · 완료한 의뢰 {rec.QuestsDone}" + (rec.FriendIds.Count > 0 ? $" · 친했던 길드원 {rec.FriendIds.Count}명" : "");
                _last.text = string.IsNullOrEmpty(rec.LastWords) ? "" : $"마지막 말: “{rec.LastWords}”";

                // Relics: a check when home, an hourglass while it lies out there, a cross when lost.
                _relicNone.gameObject.SetActive(!rec.HasRelics);
                for (int i = 0; i < _relics.Count; i++)
                {
                    bool on = i < rec.Relics.Count;
                    _relics[i].slot.gameObject.SetActive(on);
                    if (!on) continue;
                    var relic = rec.Relics[i];
                    HudStyle.Slice(_relics[i].mark, relic.State switch
                    {
                        RelicState.Recovered => "mem_mark_home",
                        RelicState.Lost => "mem_mark_lost",
                        _ => "mem_mark_lying",
                    });
                    _relics[i].slot.color = relic.State == RelicState.Lost ? new Color(0.7f, 0.66f, 0.6f, 1f) : Color.white;
                }

                // The heirloom.
                _heirs.Clear();
                var check = legacy.CanBequeath(rec);
                if (rec.HasHeir)
                {
                    _heirLine.text = $"{rec.HeirName} · {MemorialRules.HeirloomName(rec.Name, rec.HeirItem)}";
                    _heirTip = $"{rec.HeirName}이(가) 이 유품을 지니고 싸웁니다 (공격·체력 +{MemorialRules.HeirloomAttack * 100f:0}%).";
                }
                else if (check.Ok)
                {
                    _heirs.AddRange(legacy.HeirCandidates(rec));
                    _heirLine.text = "";
                }
                else
                {
                    _heirLine.text = UIBuilder.Colored(check.Reason, InkMuted);
                    _heirTip = check.Reason;
                }
                _heirLine.gameObject.SetActive(_heirs.Count == 0);
                _heirRow.gameObject.SetActive(_heirs.Count > 0);
                for (int i = 0; i < _heirButtons.Count; i++)
                {
                    bool on = i < _heirs.Count;
                    _heirButtons[i].gameObject.SetActive(on);
                    if (on) _heirButtons[i].SetLabel(_heirs[i].Data.name);
                }

                // The friends' words, and when they will next remember.
                _quotesTitle.gameObject.SetActive(rec.Remembrances.Count > 0);
                for (int i = 0; i < _quotes.Count; i++)
                {
                    bool on = i < rec.Remembrances.Count;
                    _quotes[i].box.gameObject.SetActive(on);
                    if (!on) continue;
                    _quotes[i].who.text = rec.Remembrances[i].Name;
                    _quotes[i].line.text = rec.Remembrances[i].Line;
                }
                int days = MemorialRules.DaysToAnniversary(rec.Day, today);
                _anniversary.text = (days == 0 ? "오늘이 기일입니다" : $"기일까지 {days}일") + " · 그날 저녁 친구들이 그를 기립니다"
                                    + (rec.Anniversaries > 0 ? $" (지금까지 {rec.Anniversaries}번)" : "");
                // A short page when there are no friends' words: the anniversary line moves up.
                ((RectTransform)_anniversary.transform).offsetMax = new Vector2(-28, rec.Remembrances.Count > 0 ? -462 + (MemorialRules.Remembrances - rec.Remembrances.Count) * 46 : -338);
                ((RectTransform)_anniversary.transform).offsetMin = new Vector2(28, ((RectTransform)_anniversary.transform).offsetMax.y - 22);

                _result.text = result;

                var memorial = legacy.CanHoldMemorial(rec);
                int cost = legacy.MemorialCost(rec);
                _memorial.interactable = memorial.Ok;
                _memorial.SetLabel(rec.MemorialHeld ? "추모 완료" : $"추모식 {cost}G");
                _memorialTip = memorial.Ok
                    ? $"{cost}G · 고인에게 정이 있던(호감도 0 이상) 길드원의 스트레스가 -{LegacyRules.MemorialReliefMin:0}~{LegacyRules.MemorialReliefMax:0}만큼 내리고, 가까웠던 친구들이 고인을 떠올리며 한마디씩 합니다."
                    : memorial.Reason;
                var fetch = legacy.CanSendRetriever(rec);
                _fetch.interactable = fetch.Ok;
                _fetch.SetLabel(legacy.IsRetrieving(rec) ? "회수 중..." : "유품 회수");
                _fetchTip = fetch.Ok
                    ? "쉬고 있는 길드원 한 명이 홀로 현장으로 가 유품을 찾아옵니다. 가까웠던 길드원이 우선입니다. 되찾은 유품은 남은 이에게 물려줄 수 있습니다."
                    : fetch.Reason;
            }

            string RelicTip(int i)
            {
                if (_rec == null || i >= _rec.Relics.Count) return "";
                var r = _rec.Relics[i];
                string state = r.State switch
                {
                    RelicState.Recovered => "되찾음" + (string.IsNullOrEmpty(r.Note) ? "" : $" ({r.Note})"),
                    RelicState.Lost => "잃음" + (string.IsNullOrEmpty(r.Note) ? "" : $" ({r.Note})"),
                    _ => Services.TryGet<GameClock>(out var c) ? $"현장에 있음 (약 {Mathf.CeilToInt(_rec.HoursLeft(c.TotalHours))}시간 남음)" : "현장에 있음",
                };
                return $"{LegacyTexts.ItemLabel(r.Name, r.Count)} — {state}";
            }

            string HeirChoiceTip(int i)
            {
                if (_rec == null || i >= _heirs.Count) return "";
                var a = _heirs[i];
                bool friend = _rec.Relations.TryGetValue(a.Data.id, out int v) && v >= StressRules.FriendRelation;
                string item = MemorialRules.Keepsake(_rec.Cls);
                return $"{a.Data.name}({AdventurerData.ClassLabel(a.Data.cls)} Lv.{a.Data.level})에게 {MemorialRules.HeirloomName(_rec.Name, item)}{MemoryRecorder.Josa(item, "을", "를")} 물려줍니다.\n"
                       + (friend ? "고인과 가까웠던 사이입니다. " : a.Data.cls == _rec.Cls ? "고인과 같은 직업입니다. " : "")
                       + $"공격·체력 +{MemorialRules.HeirloomAttack * 100f:0}% (최대 {MemorialRules.MaxHeirlooms}개), 오래 남는 좋은 생각, 칭호 '{MemorialRules.HeirTitle(_rec.Name)}'.";
            }

            string NemesisTip()
            {
                var n = NemesisOf(_rec, out bool alive);
                if (n == null) return "";
                return alive ? $"{_rec?.Name}{(string.IsNullOrEmpty(_rec?.Name) ? "" : "을(를)")} 죽인 원수 '{n.Name}'이(가) 아직 살아 있습니다. 친구들은 그 현상금 의뢰를 더 원합니다."
                             : $"원수 '{n.Name}'은(는) 이미 쓰러졌습니다" + (string.IsNullOrEmpty(n.SlainBy) ? "." : $" ({n.SlainBy}).");
            }

            void OnHeir(int i)
            {
                if (_rec == null || i >= _heirs.Count) return;
                _owner.Bequeath(_rec, _heirs[i]);
            }

            void OnPlace()
            {
                if (_rec != null && Services.TryGet<CameraController>(out var cam)) cam.JumpTo(_rec.Position);
            }
        }
    }
}
