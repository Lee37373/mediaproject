using System.Collections.Generic;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Reports;
using GuildMaster.Session;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The town newspaper (P4-130, F-9 — user decisions 2026-11-05: it opens by itself each morning, three to five stories,
    /// written lines only). The day's big moment on top, the other stories below; a click on a story goes to its person (or,
    /// when they are gone, its place) and closes the paper. The game waits while it is open, like the decision cards; a paper
    /// that comes while a menu or a talk holds the game opens once they are done. Browse the past days with the arrows (O).
    /// </summary>
    public class NewspaperView : UIPanel
    {
        /// <summary>Runs every frame even while the window is closed (the panel's own object is off then).</summary>
        class Driver : MonoBehaviour
        {
            public NewspaperView Owner;
            void Update() { if (Owner != null) Owner.DriverTick(); }
        }

        public override string Title => "마을 소식";
        protected override float RefreshInterval => 0.5f;

        /// <summary>Day shown; 0 = the latest paper.</summary>
        int _day;
        bool _holdingClock, _wantsOpen;
        Text _pageTitle, _empty, _othersHead;
        Button _prev, _next, _auto;
        Story _lead;
        RectTransform _list;
        readonly List<Story> _rows = new();

        /// <summary>Shows the paper of this day (a notification's, for instance).</summary>
        public void ShowDay(int day)
        {
            _day = day;
            if (Visible) Refresh();
        }

        protected override void Build(RectTransform body)
        {
            var root = body.GetComponentInParent<Canvas>().rootCanvas.gameObject;
            root.AddComponent<Driver>().Owner = this;
            EventBus.Subscribe<NewsPaperDeliveredEvent>(OnDelivered);

            UIBuilder.VLayout(body, 8, 0);
            var nav = UIBuilder.Row(body, 38);
            _prev = UIBuilder.Button(nav, "< 이전", () => Step(-1), 100, 34, UITheme.Header, 15);
            _pageTitle = UIBuilder.Label(nav, "", 22, TextAnchor.MiddleCenter, UITheme.Accent, FontStyle.Bold).Size(-1, -1, 1);
            _next = UIBuilder.Button(nav, "다음 >", () => Step(1), 100, 34, UITheme.Header, 15);
            UIBuilder.Button(nav, "그날 정산", OpenAccounts, 110, 34, UITheme.Button, 15);

            _lead = Story.Create(body, this, true);
            _othersHead = UIBuilder.Label(body, "그 밖의 소식", 16, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 24);
            _list = UIBuilder.Rect(body, "Stories");
            UIBuilder.VLayout(_list, 6, 0);
            _list.Size(-1, -1, -1, 1);
            _empty = UIBuilder.Label(body, "아직 소식지가 없습니다. 하루가 지나면 다음 날 아침에 나옵니다.", 16, TextAnchor.MiddleCenter, UITheme.TextMuted).Size(-1, 60);

            var actions = UIBuilder.Row(body, 36, 10);
            _auto = UIBuilder.Button(actions, "", ToggleAuto, 230, 34, UITheme.Button, 14);
            UIBuilder.Label(actions, "창이 열려 있는 동안 게임이 멈춥니다. 기사를 누르면 그 인물·장소로 갑니다.", 13, TextAnchor.MiddleLeft, UITheme.TextMuted)
                .Size(-1, -1, 1);
            UIBuilder.Button(actions, "닫기", Close, 100, 34, UITheme.ButtonDanger);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<NewsPaperDeliveredEvent>(OnDelivered);
            if (_holdingClock && Services.TryGet<GameClock>(out var clock)) clock.Release();
        }

        static bool OpensByItself => NewsDesk.AutoOpen && GameSettings.NewsEachMorning;

        void OnDelivered(NewsPaperDeliveredEvent e)
        {
            _day = 0;
            if (!OpensByItself) return;
            if (Blocked()) _wantsOpen = true;
            else Open();
        }

        /// <summary>A menu is open or something else holds the game (a talk, a decision card): the paper waits for them.</summary>
        bool Blocked() => MenuState.IsOpen || (Services.TryGet<GameClock>(out var clock) && clock.IsHeld && !_holdingClock);

        void DriverTick()
        {
            if (_wantsOpen && !Visible && OpensByItself && !Blocked())
            {
                _wantsOpen = false;
                Open();
            }
            if (Visible == _holdingClock || !Services.TryGet<GameClock>(out var clock)) return;
            if (Visible) clock.Hold();
            else clock.Release();
            _holdingClock = Visible;
        }

        void ToggleAuto()
        {
            GameSettings.NewsEachMorning = !GameSettings.NewsEachMorning;
            Refresh();
        }

        void OpenAccounts()
        {
            var paper = Current();
            if (paper == null || !Services.TryGet<DailyReportView>(out var report)) return;
            report.ShowDay(paper.day);
            if (!report.Visible) report.Open();
            else report.BringToFront();
        }

        IReadOnlyList<NewsPaper> Papers => Services.TryGet<NewsDesk>(out var desk) ? desk.Papers : System.Array.Empty<NewsPaper>();

        int CurrentIndex()
        {
            var papers = Papers;
            if (papers.Count == 0) return -1;
            if (_day == 0) return papers.Count - 1;
            for (int i = 0; i < papers.Count; i++)
                if (papers[i].day == _day) return i;
            return papers.Count - 1;
        }

        /// <summary>The paper shown, or null when there is none yet.</summary>
        public NewsPaper Current()
        {
            int i = CurrentIndex();
            return i >= 0 ? Papers[i] : null;
        }

        void Step(int direction)
        {
            var papers = Papers;
            int i = CurrentIndex();
            if (i < 0) return;
            int to = Mathf.Clamp(i + direction, 0, papers.Count - 1);
            ShowDay(to == papers.Count - 1 ? 0 : papers[to].day);
        }

        protected override void Refresh()
        {
            _auto.SetLabel(GameSettings.NewsEachMorning ? "아침마다 저절로 열기: 켬" : "아침마다 저절로 열기: 끔");
            var papers = Papers;
            int idx = CurrentIndex();
            var paper = idx >= 0 ? papers[idx] : null;
            _prev.interactable = idx > 0;
            _next.interactable = idx >= 0 && idx < papers.Count - 1;
            _empty.gameObject.SetActive(paper == null);
            _lead.gameObject.SetActive(paper != null);
            _othersHead.gameObject.SetActive(paper != null && paper.articles.Count > 1);
            if (paper == null)
            {
                _pageTitle.text = "마을 소식";
                EnsureRows(_list, _rows, 0, parent => Story.Create(parent, this, false));
                return;
            }
            _pageTitle.text = $"{paper.day}일차 마을 소식";
            _lead.Bind(paper.Lead, 0);
            int others = paper.articles.Count - 1;
            EnsureRows(_list, _rows, others, parent => Story.Create(parent, this, false));
            for (int i = 0; i < others; i++) _rows[i].Bind(paper.articles[i + 1], i + 1);
        }

        /// <summary>To the story's person while they are around, else to its place; the paper closes so the world shows.</summary>
        public void Go(NewsArticle a)
        {
            if (a == null) return;
            bool went = a.Subject != null && NotificationActions.GoToPerson(a.Subject);
            if (!went && a.has_place && Services.TryGet<CameraController>(out var cam))
            {
                cam.JumpTo(a.Place);
                went = true;
            }
            if (went) Close();
        }

        static bool CanGo(NewsArticle a)
            => a != null && (a.has_place || (a.Subject != null && Services.TryGet<Adventurers.GuildRoster>(out var roster) && roster.Find(a.Subject) != null));

        static Color ToneColor(NewsKind k) => NewsRules.Tone(k) switch
        {
            LineTone.Good => UITheme.Good,
            LineTone.Bad => UITheme.Bad,
            LineTone.Muted => UITheme.TextMuted,
            _ => UITheme.Exp,
        };

        /// <summary>One story: its tag, headline and lines; the lead is bigger.</summary>
        class Story : MonoBehaviour
        {
            Image _bg;
            Text _tag, _headline, _body, _go;
            Button _button;
            NewsArticle _article;
            NewspaperView _owner;
            bool _lead;

            public static Story Create(Transform parent, NewspaperView owner, bool lead)
            {
                var bg = UIBuilder.Panel(parent, lead ? "Lead" : "Story", UITheme.RowBg);
                bg.Size(-1, lead ? 150 : 92, -1, 0); // explicit 0 flex: its flexible body line would stretch it
                var s = bg.gameObject.AddComponent<Story>();
                s._bg = bg;
                s._owner = owner;
                s._lead = lead;
                var v = UIBuilder.VLayout(bg, lead ? 6 : 2, 0);
                v.padding = lead ? new RectOffset(16, 16, 12, 10) : new RectOffset(12, 12, 8, 6);
                var top = UIBuilder.Row(bg.transform, lead ? 22 : 20);
                s._tag = UIBuilder.Label(top, "", lead ? 15 : 13, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, -1, 1);
                s._go = UIBuilder.Label(top, "▶ 보러 가기", 13, TextAnchor.MiddleRight, UITheme.TextMuted).Size(120, -1, 0);
                s._headline = UIBuilder.Label(bg.transform, "", lead ? 28 : 19, TextAnchor.MiddleLeft, UITheme.Text, FontStyle.Bold).Size(-1, lead ? 44 : 28);
                s._headline.horizontalOverflow = HorizontalWrapMode.Wrap;
                s._headline.verticalOverflow = VerticalWrapMode.Truncate;
                s._body = UIBuilder.Label(bg.transform, "", lead ? 17 : 14, TextAnchor.UpperLeft, lead ? UITheme.Text : UITheme.TextMuted).Size(-1, -1, -1, 1);
                s._body.horizontalOverflow = HorizontalWrapMode.Wrap;
                s._body.verticalOverflow = VerticalWrapMode.Truncate;
                s._button = bg.gameObject.AddComponent<Button>();
                s._button.targetGraphic = bg;
                var colors = s._button.colors;
                colors.disabledColor = Color.white; // a story with nowhere to go keeps its colour
                s._button.colors = colors;
                s._button.onClick.AddListener(() => s._owner.Go(s._article));
                return s;
            }

            public void Bind(NewsArticle a, int index)
            {
                _article = a;
                if (a == null) return;
                var tone = ToneColor(a.kind);
                string tag = NewsRules.Tag(a.kind);
                _tag.text = _lead ? $"어제의 한 장면  ·  {UIBuilder.Colored(tag, tone)}" : UIBuilder.Colored(tag, tone);
                _headline.text = a.headline;
                _body.text = a.body;
                bool can = CanGo(a);
                _go.gameObject.SetActive(can);
                _button.interactable = can;
                _bg.color = _lead ? new Color(tone.r * 0.25f + 0.12f, tone.g * 0.25f + 0.1f, tone.b * 0.25f + 0.16f, 0.95f)
                    : index % 2 == 0 ? UITheme.RowBg : UITheme.RowBgAlt;
            }
        }
    }
}
