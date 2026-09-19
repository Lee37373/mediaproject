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
        Text _exp;
        Text _materials;
        Text _members;
        Text _monsters;
        Text _extra;
        Button[] _speedButtons;
        float _timer;

        /// <summary>Extra status text slot (e.g. LLM status in Phase 3).</summary>
        public static System.Func<string> ExtraStatus;

        public void Build(RectTransform canvas)
        {
            var bar = UIBuilder.Panel(canvas, "HUD", UITheme.PanelBg);
            bar.rectTransform.Anchor(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(-12, 46));
            var h = UIBuilder.HLayout(bar, 14, 6);
            h.padding = new RectOffset(14, 14, 6, 6);

            _time = UIBuilder.Label(bar.transform, "", 18, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(250);
            string[] labels = { "II", "x1", "x2", "x4" };
            _speedButtons = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                _speedButtons[i] = UIBuilder.Button(bar.transform, labels[i], () => Services.Get<GameClock>().SetSpeed(idx), 46, 32, null, 14);
            }
            UIBuilder.Rect(bar.transform, "Spacer").Size(-1, -1, 1);
            _gold = UIBuilder.Label(bar.transform, "", 17).Size(150);
            _exp = UIBuilder.Label(bar.transform, "", 17).Size(190);
            _materials = UIBuilder.Label(bar.transform, "", 17).Size(120);
            _members = UIBuilder.Label(bar.transform, "", 17).Size(130);
            _monsters = UIBuilder.Label(bar.transform, "", 17).Size(130);
            _extra = UIBuilder.Label(bar.transform, "", 15, TextAnchor.MiddleRight, UITheme.TextMuted).Size(200);
        }

        void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = 0.25f;
            if (!Services.TryGet<GameClock>(out var clock)) return;

            _time.text = clock.FormatTime() + (clock.IsNight ? " ☾" : " ☀") + (clock.IsHeld ? UIBuilder.Colored(" 정지 중", UITheme.TextMuted) : "");
            for (int i = 0; i < _speedButtons.Length; i++)
                _speedButtons[i].GetComponentInChildren<Text>().color = i == clock.SpeedIndex ? UITheme.Accent : UITheme.TextMuted;

            var t = Services.Get<GuildTreasury>();
            _gold.text = $"{UIBuilder.Colored("골드", UITheme.Accent)} {t.Gold:N0}";
            _exp.text = $"{UIBuilder.Colored("EXP", UITheme.Exp)} {t.Exp:N0} {UIBuilder.Colored($"(+{t.ExpPerHour:0.#}/h)", UITheme.TextMuted)}";
            var db = Services.Get<GameDatabase>();
            int mats = t.Stock.Where(kv => db.Item(kv.Key)?.category == ItemCategory.Material).Sum(kv => kv.Value);
            _materials.text = $"재료 {mats}";
            var roster = Services.Get<GuildRoster>();
            _members.text = $"길드원 {roster.Members.Count}/{roster.Capacity}";
            _monsters.text = $"시야 내 몬스터 {Services.Get<GuildIntel>().VisibleMonsters}";
            _extra.text = ExtraStatus?.Invoke() ?? "";
        }
    }
}
