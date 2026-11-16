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
    /// opens the rank window. 2026-11-13: an outlined HUD card with a flag and a tick box for each condition.
    /// </summary>
    public class GoalPanelView : MonoBehaviour
    {
        const float RefreshSeconds = 0.5f;
        const int Lines = 3;
        const float Width = 330f, Top = 42f, LineH = 26f;

        Text _title;
        readonly List<Text> _lines = new();
        readonly List<Image> _checks = new();
        readonly List<bool> _met = new();
        float _timer;

        public void Build(RectTransform canvas, Vector2 topLeft)
        {
            var panel = UIBuilder.Panel(canvas, "NextGoal", UITheme.PanelBg);
            HudStyle.Card(panel);
            panel.rectTransform.Anchor(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), topLeft, new Vector2(Width, Top + Lines * LineH + 10));
            HudStyle.Place(HudStyle.NewImage(panel.transform, "hud_icon_goal", "Flag").rectTransform, 12, 10, 24, 24);
            _title = UIBuilder.Label(panel.transform, "", 16, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold);
            HudStyle.Place(_title.rectTransform, 40, 9, Width - 52, 26);
            for (int i = 0; i < Lines; i++)
            {
                var check = HudStyle.NewImage(panel.transform, "hud_icon_check_off", "Check");
                HudStyle.Place(check.rectTransform, 13, Top + i * LineH, 24, 24);
                _checks.Add(check);
                var line = UIBuilder.Label(panel.transform, "", 14);
                HudStyle.Place(line.rectTransform, 42, Top + i * LineH, Width - 54, 24);
                _lines.Add(line);
            }
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
                for (int i = 0; i < _checks.Count; i++) _checks[i].gameObject.SetActive(false);
                return;
            }
            _title.text = $"다음 목표 {RankFormat.Rank(rank.Next)}  <size=13>{UIBuilder.Colored($"지금 {RankFormat.Rank(rank.Rank)} · 명성 {rank.Fame}", UITheme.TextMuted)}</size>";
            var goals = rank.NextGoals;
            var standing = rank.Standing;
            for (int i = 0; i < _lines.Count; i++)
            {
                bool shown = i < goals.Count;
                _lines[i].text = shown ? RankFormat.Line(goals[i], standing) : "";
                _checks[i].gameObject.SetActive(shown);
                bool met = shown && GuildRankRules.Progress(goals[i], standing) >= goals[i].Count;
                if (shown) HudStyle.Slice(_checks[i], met ? "hud_icon_check_on" : "hud_icon_check_off");
                // A box just ticked pops (2026-11-13, DOTween).
                while (_met.Count <= i) _met.Add(met);
                if (met && !_met[i]) UIMotion.Punch(_checks[i].transform, 0.5f, 0.5f);
                _met[i] = met;
            }
        }
    }
}
