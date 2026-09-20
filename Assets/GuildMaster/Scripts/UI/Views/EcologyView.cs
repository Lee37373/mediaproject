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
    /// <summary>Ecosystem report from the guild's own observations: known species and discovered nests.</summary>
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
            UIBuilder.Label(left, "확인한 종 (관측 기준)", 16, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 24);
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
            // Only what the guild has seen (GuildIntel), never the real simulation numbers.
            var intel = Services.Get<GuildIntel>();
            var species = intel.KnownSpecies.ToList();
            EnsureRows(_species, _speciesRows, Mathf.Max(1, species.Count), p => UIBuilder.Label(p, "", 15).Size(-1, 44));
            if (species.Count == 0)
                _speciesRows[0].text = UIBuilder.Colored("아직 확인한 몬스터가 없습니다. 모험가를 보내 주변을 탐사하세요.", UITheme.TextMuted);
            for (int i = 0; i < species.Count; i++)
            {
                var s = species[i];
                int nests = intel.KnownNestCount(s);
                int seen = intel.ObservedPopulation(s);
                string nestText = nests > 0 ? $"발견한 둥지 {nests}곳 · 관측 {seen}마리" : "둥지 위치 모름";
                string status = nests > 0 && seen == 0 ? UIBuilder.Colored(" · 관측된 개체 없음", UITheme.Warn) : "";
                // Compact here (the row is one line); the nest inspector shows the full item list.
                var gear = intel.GearOf(s);
                if (gear.Any) status += UIBuilder.Colored(GearTag(gear), gear.HasCrown ? UITheme.Accent : UITheme.Warn);
                // P5-1: flag species the guild has hunted down to a handful.
                var ecology = EcologyWarning.Status(s, seen, nests);
                if (ecology != EcologyStatus.Healthy)
                    status += UIBuilder.Colored(" · " + EcologyWarning.Label(ecology),
                                                ecology == EcologyStatus.Endangered ? UITheme.Bad : UITheme.Warn);
                _speciesRows[i].text = $"{s.displayName}  {UIBuilder.Colored("위험 " + Quest.DangerLabel(s.danger), UITheme.Danger(s.danger))}\n" +
                                       $"<color=#{UITheme.Hex(UITheme.TextMuted)}>{nestText}</color>{status}";
            }

            var nestsList = intel.KnownNests.ToList();
            EnsureRows(_nests, _nestRows, nestsList.Count, p =>
            {
                var b = UIBuilder.Button(p, "", null, -1, 30, UITheme.RowBg, 14);
                b.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
                return b;
            });
            for (int i = 0; i < nestsList.Count; i++)
            {
                var n = nestsList[i];
                var btn = _nestRows[i];
                var nestGear = intel.GearAt(n);
                string gearTag = nestGear.Any
                    ? UIBuilder.Colored(GearTag(nestGear), nestGear.HasCrown ? UITheme.Accent : UITheme.Warn)
                    : "";
                btn.SetLabel($"{intel.NameOf(n)} — {intel.DescribeNest(n)}{gearTag}");
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => Services.Get<CameraController>().JumpTo(n.WorldPosition));
            }
        }

        /// <summary>One-line tag for observed gear (P4-22); the full list lives in the inspector.</summary>
        static string GearTag(GuildIntel.GearIntel gear)
        {
            if (gear.HasCrown) return " · 우두머리 관측";
            if (gear.GearedCount > 0) return $" · 장비 착용 {gear.GearedCount}마리 관측";
            return $" · 삼킨 아이템 관측 (HP +{gear.AbsorbedHp:0})";
        }
    }
}
