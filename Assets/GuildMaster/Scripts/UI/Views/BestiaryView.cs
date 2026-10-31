using System.Collections.Generic;
using System.Linq;
using GuildMaster.Bestiary;
using GuildMaster.Core;
using GuildMaster.Data;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The bestiary window (P4-107, H4, K): every kind the guild has seen with its research level, and the chosen kind's page —
    /// what is known of it at that level (<see cref="BestiaryText.Page"/>) and how far the next level is. Only what the guild
    /// has seen: a kind never in sight is not listed.
    /// </summary>
    public class BestiaryView : UIPanel
    {
        public override string Title => "몬스터 도감";

        Text _header;
        RectTransform _list;
        readonly List<Button> _rows = new();
        Text _pageTitle, _page;
        MonsterDefinition _chosen;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 4);
            _header = UIBuilder.Label(body, "", 16, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 26);
            var split = UIBuilder.Rect(body, "Split").Size(-1, -1, -1, 1);
            UIBuilder.HLayout(split, 12).childForceExpandHeight = true;
            var left = UIBuilder.Rect(split, "Kinds").Size(300, -1, 0, 1);
            UIBuilder.VLayout(left, 4, 0);
            _list = UIBuilder.ScrollList(left, out var scroll, 4);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
            var right = UIBuilder.Rect(split, "Page").Size(-1, -1, 1, 1);
            UIBuilder.VLayout(right, 8, 0);
            _pageTitle = UIBuilder.Label(right, "", 20, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 32);
            _page = UIBuilder.Label(right, "", 15, TextAnchor.UpperLeft).Size(-1, -1, -1, 1);
            _page.horizontalOverflow = HorizontalWrapMode.Wrap;
            _page.verticalOverflow = VerticalWrapMode.Overflow;
        }

        /// <summary>Opens on one kind's page (from the inspector or a notification).</summary>
        public void Show(MonsterDefinition species)
        {
            _chosen = species;
            if (!Visible) Open();
            else BringToFront();
        }

        protected override void Refresh()
        {
            if (!Services.TryGet<BestiaryService>(out var bestiary)) return;
            var kinds = bestiary.Seen.ToList();
            int studied = kinds.Count(k => bestiary.Level(k) >= 2), mastered = kinds.Count(k => bestiary.Level(k) >= BestiaryRules.MaxLevel);
            _header.text = kinds.Count == 0
                ? "아직 본 몬스터가 없습니다. 길드원이 본 종만 기록됩니다."
                : $"본 종 {kinds.Count} · 연구 2단계 이상 {studied} · 숙련 {mastered}  (관찰·처치·스킬 목격·전리품 매입으로 연구가 쌓입니다)";

            EnsureRows(_list, _rows, Mathf.Max(1, kinds.Count), parent =>
            {
                var b = UIBuilder.Button(parent, "", null, -1, 36, UITheme.RowBg, 15);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                return b;
            });
            if (kinds.Count == 0)
            {
                _rows[0].SetLabel(UIBuilder.Colored("  (없음)", UITheme.TextMuted));
                _rows[0].onClick.RemoveAllListeners();
                _pageTitle.text = "";
                _page.text = "";
                return;
            }
            if (_chosen == null || !kinds.Contains(_chosen)) _chosen = kinds[0];
            Services.TryGet<Nemeses.NemesisService>(out var nemeses);
            for (int i = 0; i < kinds.Count; i++)
            {
                var k = kinds[i];
                int level = bestiary.Level(k);
                var stars = new string('★', level) + new string('☆', BestiaryRules.MaxLevel - level);
                string name = k == _chosen ? UIBuilder.Colored(k.displayName, UITheme.Accent) : k.displayName;
                // Its living nemeses (P4-109).
                int living = nemeses != null ? nemeses.Living.Count(n => n.Species == k) : 0;
                string marked = living > 0 ? "  " + UIBuilder.Colored($"원수 {living}", UITheme.Bad) : "";
                _rows[i].SetLabel($"  {name}  {UIBuilder.Colored(stars, level >= BestiaryRules.MaxLevel ? UITheme.Good : UITheme.Warn)}{marked}");
                _rows[i].onClick.RemoveAllListeners();
                _rows[i].onClick.AddListener(() => { _chosen = k; Refresh(); });
            }

            int lv = bestiary.Level(_chosen), points = bestiary.Points(_chosen), next = BestiaryRules.NextAt(lv);
            _pageTitle.text = $"{_chosen.displayName}  {UIBuilder.Colored($"연구 {lv}단계", UITheme.TextMuted)}";
            var lines = BestiaryText.Page(_chosen, lv);
            lines.Add("");
            lines.Add(next > 0
                ? UIBuilder.Colored($"다음 단계까지 연구 {points}/{next} — 2단계: 스킬·생태·약점과 강함, 3단계: 숙련(피해 +10%·받는 피해 −5%)", UITheme.TextMuted)
                : UIBuilder.Colored("연구 완료", UITheme.Good));
            if (nemeses != null) AddNemeses(lines, nemeses);
            _page.text = string.Join("\n", lines);
        }

        /// <summary>The kind's nemeses (P4-109): the living with where they were last seen, then the fallen and who brought them down.</summary>
        void AddNemeses(List<string> lines, Nemeses.NemesisService nemeses)
        {
            var list = nemeses.OfKind(_chosen).ToList();
            if (list.Count == 0) return;
            Services.TryGet<Ecosystem.GuildIntel>(out var intel);
            lines.Add("");
            lines.Add(UIBuilder.Colored("원수", UITheme.Bad));
            foreach (var n in list)
            {
                string story = nemeses.Story(n);
                if (n.Fallen)
                {
                    string by = n.SlainBy != null ? $"{n.SlainBy}{Adventurers.MemoryRecorder.Josa(n.SlainBy, "이", "가")} 쓰러뜨림" : "쓰러짐";
                    lines.Add(UIBuilder.Colored($"  {n.Name} Lv.{n.Level} — {story} · {by}", UITheme.TextMuted));
                }
                else
                {
                    string seen = intel != null ? $"마지막 목격 {intel.AgoText(n.LastSeenHour)}" : "";
                    lines.Add($"  {UIBuilder.Colored(n.Name, UITheme.Bad)} Lv.{n.Level} — {story} · {seen}");
                }
            }
        }
    }
}
