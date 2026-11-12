using System.Collections.Generic;
using System.Text;
using GuildMaster.Core;
using GuildMaster.Reports;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The daily accounts (F-3, B key): income and spending of a game day, what happened that day, and a chart of
    /// the last days' closing gold. Browse days with the arrows or by clicking a bar; "오늘" returns to the running day.
    /// </summary>
    public class DailyReportView : UIPanel
    {
        public override string Title => "일일 정산";
        protected override float RefreshInterval => 0.5f;

        /// <summary>Bars in the chart, newest last.</summary>
        const int MaxBars = 14;
        const float PlotHeight = 150f;
        const float SlotWidth = 60f;
        const float LabelHeight = 18f;
        /// <summary>Room for the day label below and the value label above the tallest bar.</summary>
        const float MaxBarHeight = PlotHeight - 8f - LabelHeight * 2f;

        /// <summary>Day shown; 0 = follow the running day.</summary>
        int _day;
        Text _pageTitle;
        Text _headline;
        Text _subline;
        Text _activity;
        Text _incomeTotal;
        Text _expenseTotal;
        Button _prev;
        Button _next;
        RectTransform _plot;
        readonly Dictionary<GoldCategory, Text> _amounts = new();
        readonly Dictionary<GoldCategory, GameObject> _rows = new();
        readonly List<Bar> _bars = new();
        readonly List<DailyReport> _pages = new();

        /// <summary>Opens on the report of this day (a finished day from its notification, for instance).</summary>
        public void ShowDay(int day)
        {
            _day = day;
            if (Visible) Refresh();
        }

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 0);

            var nav = UIBuilder.Row(body, 38);
            _prev = UIBuilder.Button(nav, "< 이전", () => Step(-1), 100, 34, UITheme.Header, 15);
            _pageTitle = UIBuilder.Label(nav, "", 20, TextAnchor.MiddleCenter, UITheme.Accent, FontStyle.Bold).Size(-1, -1, 1);
            _next = UIBuilder.Button(nav, "다음 >", () => Step(1), 100, 34, UITheme.Header, 15);
            UIBuilder.Button(nav, "오늘", () => ShowDay(0), 80, 34, UITheme.Button, 15);
            UIBuilder.Button(nav, "소식지 (O)", OpenNews, 110, 34, UITheme.Button, 15); // P4-130

            var summary = UIBuilder.Panel(body, "Summary", UITheme.RowBg);
            summary.Size(-1, 70);
            UIBuilder.VLayout(summary, 2, 10);
            _headline = UIBuilder.Label(summary.transform, "", 24, TextAnchor.MiddleLeft, UITheme.Text, FontStyle.Bold).Size(-1, 34);
            _subline = UIBuilder.Label(summary.transform, "", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);

            var cols = UIBuilder.Rect(body, "Columns");
            UIBuilder.HLayout(cols, 10, 0, expandWidth: true);
            cols.Size(-1, -1, -1, 1);

            var money = UIBuilder.Panel(cols, "Money", UITheme.RowBg);
            money.Size(-1, -1, 1, 1);
            UIBuilder.VLayout(money, 2, 12);
            _incomeTotal = AddLine(money.transform, "수입", true, UITheme.Good);
            foreach (var c in GoldCategories.Income) AddCategory(money.transform, c);
            _expenseTotal = AddLine(money.transform, "지출", true, UITheme.Bad);
            foreach (var c in GoldCategories.Expense) AddCategory(money.transform, c);

            var events = UIBuilder.Panel(cols, "Activity", UITheme.RowBg);
            events.Size(-1, -1, 1, 1);
            UIBuilder.VLayout(events, 6, 12);
            UIBuilder.Label(events.transform, "이 날의 활동", 17, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 24);
            _activity = UIBuilder.Label(events.transform, "", 15, TextAnchor.UpperLeft).Size(-1, -1, 1, 1);
            _activity.lineSpacing = 1.2f;

            UIBuilder.Label(body, "골드 추이  (막대 = 그날 마감 골드 · 초록 = 흑자, 빨강 = 적자 · 막대를 누르면 그날로 이동)", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 24);
            var plot = UIBuilder.Panel(body, "Plot", new Color(0, 0, 0, 0.25f));
            plot.Size(-1, PlotHeight);
            _plot = plot.rectTransform;
            UIBuilder.HLayout(_plot, 4, 4);
        }

        Text AddLine(Transform parent, string label, bool header, Color color, GoldCategory? category = null)
        {
            var row = UIBuilder.Row(parent, 24);
            UIBuilder.Label(row, header ? label : "    " + label, header ? 17 : 15, TextAnchor.MiddleLeft, color).Size(-1, -1, 1);
            var value = UIBuilder.Label(row, "", header ? 17 : 15, TextAnchor.MiddleRight, color).Size(150, -1);
            if (header) value.fontStyle = FontStyle.Bold;
            if (category.HasValue) _rows[category.Value] = row.gameObject;
            return value;
        }

        void AddCategory(Transform parent, GoldCategory c)
            => _amounts[c] = AddLine(parent, GoldCategories.Label(c), false, UITheme.Text, c);

        /// <summary>The town newspaper of the day shown (P4-130).</summary>
        void OpenNews()
        {
            if (!Services.TryGet<NewspaperView>(out var news)) return;
            int i = CurrentIndex();
            news.ShowDay(i >= 0 && i < _pages.Count && _pages[i].Closed ? _pages[i].Day : 0);
            if (!news.Visible) news.Open();
            else news.BringToFront();
        }

        void Step(int direction)
        {
            if (_pages.Count == 0) return;
            int idx = Mathf.Clamp(CurrentIndex() + direction, 0, _pages.Count - 1);
            ShowDay(idx == _pages.Count - 1 ? 0 : _pages[idx].Day);
        }

        int CurrentIndex()
        {
            if (_day == 0) return _pages.Count - 1;
            int i = _pages.FindIndex(r => r.Day == _day);
            return i >= 0 ? i : _pages.Count - 1;
        }

        protected override void Refresh()
        {
            if (!Services.TryGet<DailyLedger>(out var ledger)) return;
            _pages.Clear();
            _pages.AddRange(ledger.Reports);
            _pages.Add(ledger.Current);
            int idx = CurrentIndex();
            var r = _pages[idx];
            if (_day != 0 && r == ledger.Current) _day = 0;

            _prev.interactable = idx > 0;
            _next.interactable = idx < _pages.Count - 1;

            string state = r.Closed ? UIBuilder.Colored("마감", UITheme.TextMuted) : UIBuilder.Colored("집계 중", UITheme.Warn);
            string from = r.StartHour >= 1f ? UIBuilder.Colored($"  ·  {Mathf.FloorToInt(r.StartHour):00}:00부터", UITheme.TextMuted) : "";
            _pageTitle.text = $"{r.Day}일차  {state}{from}";

            var netColor = r.Net > 0 ? UITheme.Good : r.Net < 0 ? UITheme.Bad : UITheme.Text;
            _headline.text = $"순이익 {UIBuilder.Colored(DailyReportTexts.SignedMoney(r.Net), netColor)}";
            _subline.text = $"골드 {DailyReportTexts.Money(r.GoldStart)} → {DailyReportTexts.Money(r.GoldEnd)}";

            _incomeTotal.text = r.Income == 0 ? "0G" : "+" + DailyReportTexts.Money(r.Income);
            _expenseTotal.text = r.Expense == 0 ? "0G" : "-" + DailyReportTexts.Money(r.Expense);
            foreach (var kv in _amounts)
            {
                int amount = r.Amount(kv.Key);
                bool income = GoldCategories.IsIncome(kv.Key);
                kv.Value.text = amount == 0
                    ? UIBuilder.Colored("-", UITheme.TextMuted)
                    : UIBuilder.Colored((income ? "+" : "-") + DailyReportTexts.Money(amount), income ? UITheme.Good : UITheme.Bad);
                // The catch-all lines only appear when something ended up in them.
                if (kv.Key == GoldCategory.OtherIncome || kv.Key == GoldCategory.OtherExpense)
                    _rows[kv.Key].SetActive(amount != 0);
            }

            var sb = new StringBuilder();
            foreach (var line in DailyReportTexts.ActivityLines(r))
            {
                var color = line.Tone switch
                {
                    LineTone.Good => UITheme.Good,
                    LineTone.Bad => UITheme.Bad,
                    LineTone.Muted => UITheme.TextMuted,
                    _ => UITheme.Text,
                };
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(UIBuilder.Colored(line.Text, color));
            }
            _activity.text = sb.ToString();

            RefreshChart(r);
        }

        void RefreshChart(DailyReport selected)
        {
            int start = Mathf.Max(0, _pages.Count - MaxBars);
            int count = _pages.Count - start;
            int maxGold = 1;
            for (int i = start; i < _pages.Count; i++) maxGold = Mathf.Max(maxGold, _pages[i].GoldEnd);
            EnsureRows(_plot, _bars, count, parent => Bar.Create(parent, ShowDay));
            for (int i = 0; i < count; i++) _bars[i].Bind(_pages[start + i], maxGold, _pages[start + i] == selected);
        }

        /// <summary>One day in the chart: a bar for the closing gold, its value above and the day below.</summary>
        class Bar : MonoBehaviour
        {
            Image _bg;
            Image _bar;
            Text _value;
            Text _dayLabel;
            int _dayNumber;
            System.Action<int> _onClick;

            public static Bar Create(Transform parent, System.Action<int> onClick)
            {
                var bg = UIBuilder.Panel(parent, "Bar", Color.clear);
                bg.Size(SlotWidth, -1, 0);
                var bar = bg.gameObject.AddComponent<Bar>();
                bar._bg = bg;
                bar._onClick = onClick;

                bar._bar = UIBuilder.Panel(bg.transform, "Fill", UITheme.Good, raycast: false);
                bar._bar.rectTransform.Anchor(new Vector2(0.18f, 0), new Vector2(0.82f, 0), new Vector2(0.5f, 0), new Vector2(0, LabelHeight), new Vector2(0, 2));

                bar._value = UIBuilder.Label(bg.transform, "", 13, TextAnchor.LowerCenter, UITheme.Text);
                bar._value.rectTransform.Anchor(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, LabelHeight), new Vector2(0, LabelHeight));
                bar._value.horizontalOverflow = HorizontalWrapMode.Overflow;

                bar._dayLabel = UIBuilder.Label(bg.transform, "", 13, TextAnchor.MiddleCenter, UITheme.TextMuted);
                bar._dayLabel.rectTransform.Anchor(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, LabelHeight));

                var button = bg.gameObject.AddComponent<Button>();
                button.targetGraphic = bg;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(bar.Click);
                return bar;
            }

            public void Bind(DailyReport r, int maxGold, bool selected)
            {
                _dayNumber = r.Closed ? r.Day : 0;
                float frac = Mathf.Clamp01(r.GoldEnd / (float)maxGold);
                float height = Mathf.Max(2f, frac * MaxBarHeight);
                _bar.rectTransform.sizeDelta = new Vector2(0, height);
                var tint = r.Net >= 0 ? UITheme.Good : UITheme.Bad;
                _bar.color = new Color(tint.r, tint.g, tint.b, r.Closed ? 0.9f : 0.55f);
                _value.rectTransform.anchoredPosition = new Vector2(0, LabelHeight + height);
                _value.text = r.GoldEnd.ToString("N0");
                _dayLabel.text = r.Closed ? $"{r.Day}일" : "오늘";
                _dayLabel.color = selected ? UITheme.Accent : UITheme.TextMuted;
                _bg.color = selected ? UITheme.RowBg : Color.clear;
            }

            void Click() => _onClick?.Invoke(_dayNumber);
        }
    }
}
