using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Data;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>Top resource/time bar with speed controls.</summary>
    public class HudView : MonoBehaviour
    {
        Text _time;
        Text _gold;
        Text _quests;
        Text _materials;
        Text _members;
        Text _monsters;
        Text _extra;
        Button[] _speedButtons;
        float _timer;
        /// <summary>The gold as shown: it rolls towards the treasury's (P4-106 H8), less what is still flying in.</summary>
        float _shownGold = -1f;
        float _pulse;

        /// <summary>Extra status text slot (e.g. LLM status in Phase 3).</summary>
        public static System.Func<string> ExtraStatus;

        /// <summary>Where coins fly to (P4-106 H8).</summary>
        public RectTransform GoldAnchor => _gold != null ? _gold.rectTransform : null;

        /// <summary>Gold already in the treasury whose coins are still on their way: not counted on the bar until they land.</summary>
        public int Withheld { get; set; }

        /// <summary>A coin landed: the gold swells a moment.</summary>
        public void Pulse() => _pulse = 1f;

        public void Build(RectTransform canvas)
        {
            var bar = UIBuilder.Panel(canvas, "HUD", UITheme.PanelBg);
            bar.rectTransform.Anchor(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(-12, 46));
            var h = UIBuilder.HLayout(bar, 14, 6);
            h.padding = new RectOffset(14, 14, 6, 6);

            _time = UIBuilder.Label(bar.transform, "", 18, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(330);
            // One button per GameClock.SpeedScales entry, so a new speed needs no change here.
            _speedButtons = new Button[GameClock.SpeedScales.Length];
            for (int i = 0; i < _speedButtons.Length; i++)
            {
                int idx = i;
                string label = idx == 0 ? "II" : $"x{GameClock.SpeedScales[idx]:0}";
                _speedButtons[i] = UIBuilder.Button(bar.transform, label, () => Services.Get<GameClock>().SetSpeed(idx), 46, 32, null, 14);
            }
            UIBuilder.Rect(bar.transform, "Spacer").Size(-1, -1, 1);
            _gold = UIBuilder.Label(bar.transform, "", 17).Size(150);
            _quests = UIBuilder.Label(bar.transform, "", 17).Size(190);
            _materials = UIBuilder.Label(bar.transform, "", 17).Size(120);
            _members = UIBuilder.Label(bar.transform, "", 17).Size(130);
            _monsters = UIBuilder.Label(bar.transform, "", 17).Size(130);
            _extra = UIBuilder.Label(bar.transform, "", 15, TextAnchor.MiddleRight, UITheme.TextMuted).Size(200);
        }

        void Update()
        {
            RollGold();
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.25f;
            if (!Services.TryGet<GameClock>(out var clock)) return;

            // The season and its day before the clock (P4-111b): "가을 3일 · 13일차 08:00".
            string season = Seasons.SeasonService.Label;
            if (season.Length > 0) season = UIBuilder.Colored(season, SeasonColor(Seasons.SeasonService.Now)) + " · ";
            _time.text = season + clock.FormatTime() + (clock.IsNight ? " ☾" : " ☀") + (clock.IsHeld ? UIBuilder.Colored(" 정지 중", UITheme.TextMuted) : "");
            for (int i = 0; i < _speedButtons.Length; i++)
                _speedButtons[i].GetComponentInChildren<Text>().color = i == clock.SpeedIndex ? UITheme.Accent : UITheme.TextMuted;

            var t = Services.Get<GuildTreasury>();
            // P4-82: no guild EXP any more; the slot shows the board instead, which the town now fills by itself.
            if (Services.TryGet<Quests.QuestBoard>(out var board))
            {
                int open = 0, waiting = 0;
                foreach (var q in board.Quests)
                {
                    if (!q.IsActive) continue;
                    open++;
                    if (q.Assignees.Count == 0) waiting++;
                }
                _quests.text = $"{UIBuilder.Colored("의뢰", UITheme.Exp)} {open}건 {UIBuilder.Colored($"(대기 {waiting})", UITheme.TextMuted)}";
            }
            var db = Services.Get<GameDatabase>();
            int mats = t.Stock.Where(kv => db.Item(kv.Key)?.category == ItemCategory.Material).Sum(kv => kv.Value);
            _materials.text = $"재료 {mats}";
            var roster = Services.Get<GuildRoster>();
            _members.text = $"길드원 {roster.CountText}";
            _monsters.text = $"시야 내 몬스터 {Services.Get<GuildIntel>().VisibleMonsters}";
            _extra.text = ExtraStatus?.Invoke() ?? "";
        }

        static Color SeasonColor(Seasons.Season s) => s switch
        {
            Seasons.Season.Spring => new Color(0.55f, 0.85f, 0.45f),
            Seasons.Season.Summer => new Color(0.95f, 0.7f, 0.3f),
            Seasons.Season.Autumn => new Color(0.9f, 0.55f, 0.25f),
            _ => new Color(0.6f, 0.8f, 1f),
        };

        /// <summary>
        /// Every frame: the shown gold rolls towards the treasury's less what is still flying in — quickly, so a big sum
        /// counts up for about half a second — and a landed coin makes it swell (P4-106 H8).
        /// </summary>
        void RollGold()
        {
            if (_gold == null || !Services.TryGet<GuildTreasury>(out var t)) return;
            float target = Mathf.Max(0, t.Gold - Withheld);
            if (_shownGold < 0f || !GuildMaster.Session.GameSettings.RewardEffects) _shownGold = target;
            float step = Mathf.Max(1f, Mathf.Abs(target - _shownGold) * 8f * Time.unscaledDeltaTime);
            _shownGold = Mathf.MoveTowards(_shownGold, target, step);
            _gold.text = $"{UIBuilder.Colored("골드", UITheme.Accent)} {Mathf.RoundToInt(_shownGold):N0}";
            _pulse = Mathf.Max(0f, _pulse - Time.unscaledDeltaTime * 4f);
            float s = 1f + 0.18f * _pulse;
            _gold.rectTransform.localScale = new Vector3(s, s, 1f);
        }
    }
}
