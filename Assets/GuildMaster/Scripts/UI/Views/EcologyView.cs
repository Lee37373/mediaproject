using System.Collections.Generic;
using System.Linq;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>Monster ecosystem report: per-species population/food and discovered nests.</summary>
    public class EcologyView : UIPanel
    {
        public override string Title => "생태계 보고서";

        RectTransform _species;
        RectTransform _nests;
        readonly List<Text> _speciesRows = new();
        readonly List<Button> _nestRows = new();

        protected override void Build(RectTransform body)
        {
            UIBuilder.HLayout(body, 12).childForceExpandHeight = true;
            var left = UIBuilder.Rect(body, "Species").Size(-1, -1, 1);
            UIBuilder.VLayout(left, 6, 0);
            UIBuilder.Label(left, "종별 개체 수", 16, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 24);
            _species = UIBuilder.ScrollList(left, out var s1);
            s1.GetComponent<RectTransform>().Size(-1, -1, -1, 1);

            var right = UIBuilder.Rect(body, "Nests").Size(-1, -1, 1);
            UIBuilder.VLayout(right, 6, 0);
            UIBuilder.Label(right, "발견한 둥지 (클릭: 이동)", 16, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 24);
            _nests = UIBuilder.ScrollList(right, out var s2);
            s2.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
        }

        protected override void Refresh()
        {
            var eco = Services.Get<EcosystemManager>();
            var species = eco.Species.ToList();
            EnsureRows(_species, _speciesRows, species.Count, p => UIBuilder.Label(p, "", 15).Size(-1, 44));
            for (int i = 0; i < species.Count; i++)
            {
                var s = species[i];
                int pop = eco.GetPopulation(s);
                int cap = eco.NestsOf(s).Sum(n => n.Capacity);
                string status = eco.IsExtinct(s) ? UIBuilder.Colored("멸종", UITheme.Bad)
                    : pop <= 2 ? UIBuilder.Colored("멸종 위기", UITheme.Warn)
                    : UIBuilder.Colored("안정", UITheme.Good);
                string food = s.prey.Count > 0 ? $" · 먹이 충족도 {eco.FoodFactor(s) * 100f:0}%" : "";
                string bar = new string('■', Mathf.Clamp(Mathf.RoundToInt(10f * pop / Mathf.Max(1, cap)), 0, 10)).PadRight(10, '□');
                _speciesRows[i].text = $"{s.displayName} {status}  {UIBuilder.Colored("위험 " + Quest.DangerLabel(s.danger), UITheme.Danger(s.danger))}\n" +
                                       $"<color=#{UITheme.Hex(UITheme.TextMuted)}>{bar} {pop}/{cap}{food}</color>";
            }

            var nests = eco.Nests.Where(n => n.Discovered).ToList();
            EnsureRows(_nests, _nestRows, nests.Count, p =>
            {
                var b = UIBuilder.Button(p, "", null, -1, 30, UITheme.RowBg, 14);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                return b;
            });
            for (int i = 0; i < nests.Count; i++)
            {
                var n = nests[i];
                var btn = _nestRows[i];
                btn.SetLabel($"{n.DisplayName} — {(n.IsEmpty ? UIBuilder.Colored("비어 있음", UITheme.TextMuted) : $"{n.Population}/{n.Capacity}마리")}");
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => Services.Get<CameraController>().JumpTo(n.WorldPosition));
            }
        }
    }
}
