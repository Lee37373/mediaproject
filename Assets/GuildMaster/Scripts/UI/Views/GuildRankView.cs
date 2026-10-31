using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Core;
using GuildMaster.Guild;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The guild's rank window (P4-106, H1, G): the rank and fame now, every rank from F to S with its three conditions (the
    /// next one with where each stands) and what it opens, and the latest changes to the fame.
    /// </summary>
    public class GuildRankView : UIPanel
    {
        public override string Title => "길드 등급";

        Text _header;
        Text _table;
        RectTransform _content;
        ScrollRect _scroll;
        readonly List<Text> _rows = new();

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 4);
            _header = UIBuilder.Label(body, "", 19, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 30);
            _table = UIBuilder.Label(body, "", 15).Size(-1, 330);
            _table.horizontalOverflow = HorizontalWrapMode.Wrap;
            _table.verticalOverflow = VerticalWrapMode.Overflow;
            UIBuilder.Label(body, "명성 내역 (최근)", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 24);
            _content = UIBuilder.ScrollList(body, out _scroll, 2);
            _scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
        }

        protected override void Refresh()
        {
            if (!Services.TryGet<GuildRankService>(out var rank)) { _header.text = "길드 등급이 없습니다."; return; }
            var standing = rank.Standing;
            _header.text = $"{RankFormat.Rank(rank.Rank)} · 명성 {rank.Fame}"
                           + (rank.Victory ? " · 승리" : UIBuilder.Colored($"  다음 {RankFormat.Rank(rank.Next)}", UITheme.TextMuted));

            var sb = new StringBuilder();
            foreach (GuildRank r in Enum.GetValues(typeof(GuildRank)))
            {
                bool reached = r <= rank.Rank;
                bool next = !rank.Victory && r == rank.Next;
                var color = reached ? UITheme.Good : next ? UITheme.Accent : UITheme.TextMuted;
                string name = UIBuilder.Colored($"<b>{RankFormat.Rank(r)}</b>{(reached && r > GuildRank.F ? " 달성" : "")}", color);
                if (r == GuildRank.F)
                {
                    sb.AppendLine($"{name} — 시작");
                    continue;
                }
                var goals = GuildRankRules.GoalsFor(r).Select(g => next ? RankFormat.Line(g, standing) : g.Label);
                sb.AppendLine($"{name} — {string.Join(" · ", goals)}");
                sb.AppendLine(UIBuilder.Colored($"      {GuildRankRules.Rewards(r)}", UITheme.TextMuted));
            }
            _table.text = sb.ToString();

            var history = rank.History;
            EnsureRows(_content, _rows, Mathf.Max(1, history.Count), parent =>
            {
                var t = UIBuilder.Label(parent, "", 14);
                t.verticalOverflow = VerticalWrapMode.Overflow;
                return t;
            });
            if (history.Count == 0)
            {
                _rows[0].text = UIBuilder.Colored("아직 명성을 얻은 일이 없습니다.", UITheme.TextMuted);
                return;
            }
            // Newest first.
            for (int i = 0; i < history.Count; i++)
            {
                var h = history[history.Count - 1 - i];
                int day = Mathf.FloorToInt(h.hour / 24f) + 1;
                string amount = UIBuilder.Colored(h.amount > 0 ? $"+{h.amount}" : h.amount.ToString(), h.amount > 0 ? UITheme.Good : UITheme.Bad);
                _rows[i].text = $"{UIBuilder.Colored($"{day}일 {Mathf.FloorToInt(h.hour % 24f):00}시", UITheme.TextMuted)} {amount} {h.reason}";
            }
        }
    }
}
