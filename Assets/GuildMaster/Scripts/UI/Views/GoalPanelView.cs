using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Guild;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The HUD's next goal (P4-106, H1): the guild's rank and fame, and the conditions for the next rank with where each
    /// stands — the first ones are the tutorial (a first quest, a first recruit, a first upgrade). Under the side menu; a click
    /// opens the rank window.
    /// </summary>
    public class GoalPanelView : MonoBehaviour
    {
        const float RefreshSeconds = 0.5f;
        const int Lines = 3;

        Text _title;
        readonly List<Text> _lines = new();
        float _timer;

        public void Build(RectTransform canvas, Vector2 topLeft)
        {
            var panel = UIBuilder.Panel(canvas, "NextGoal", UITheme.PanelBg);
            panel.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), topLeft, new Vector2(330, 34 + Lines * 24));
            var layout = UIBuilder.VLayout(panel, 2, 8);
            layout.padding.top = 6;
            _title = UIBuilder.Label(panel.transform, "", 16, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 24);
            for (int i = 0; i < Lines; i++) _lines.Add(UIBuilder.Label(panel.transform, "", 14).Size(-1, 22));
            panel.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                if (!Services.TryGet<GuildRankView>(out var view)) return;
                if (!view.Visible) view.Open();
                else view.BringToFront();
            });
            panel.Tooltip(() => "다음 길드 등급의 조건입니다. 누르면 등급표와 명성 내역을 엽니다(G).");
        }

        void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = RefreshSeconds;
            if (_title == null || !Services.TryGet<GuildRankService>(out var rank)) return;
            if (rank.Victory)
            {
                _title.text = $"{RankFormat.Rank(rank.Rank)} 길드 · 명성 {rank.Fame}";
                _lines[0].text = UIBuilder.Colored("최고 등급 — 승리!", UITheme.Good);
                for (int i = 1; i < _lines.Count; i++) _lines[i].text = "";
                return;
            }
            _title.text = $"다음 목표: {RankFormat.Rank(rank.Next)}  {UIBuilder.Colored($"(지금 {RankFormat.Rank(rank.Rank)} · 명성 {rank.Fame})", UITheme.TextMuted)}";
            var goals = rank.NextGoals;
            var standing = rank.Standing;
            for (int i = 0; i < _lines.Count; i++)
                _lines[i].text = i < goals.Count ? "· " + RankFormat.Line(goals[i], standing) : "";
        }
    }
}
