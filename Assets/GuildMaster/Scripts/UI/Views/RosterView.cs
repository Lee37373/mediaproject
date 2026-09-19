using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>Guild members overview. Clicking a row focuses the camera and opens the inspector.</summary>
    public class RosterView : UIPanel
    {
        public override string Title => "길드원";

        RectTransform _content;
        readonly List<MemberRow> _rows = new();
        Text _summary;

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 6, 0);
            _summary = UIBuilder.Label(body, "", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 24);
            _content = UIBuilder.ScrollList(body, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
        }

        protected override void Refresh()
        {
            var roster = Services.Get<GuildRoster>();
            _summary.text = $"길드원 {roster.Members.Count}/{roster.Capacity}명 · 행을 클릭하면 해당 모험가를 추적합니다.";
            EnsureRows(_content, _rows, roster.Members.Count, MemberRow.Create);
            for (int i = 0; i < roster.Members.Count; i++) _rows[i].Bind(roster.Members[i], i);
        }

        class MemberRow : MonoBehaviour
        {
            Image _bg;
            Text _main;
            Text _sub;
            Text _traits;
            Adventurer _adv;

            public static MemberRow Create(Transform parent)
            {
                var bg = UIBuilder.Panel(parent, "MemberRow", UITheme.RowBg);
                bg.Size(-1, 86);
                var row = bg.gameObject.AddComponent<MemberRow>();
                row._bg = bg;
                UIBuilder.VLayout(bg, 2, 6).padding = new RectOffset(12, 12, 6, 6);
                row._main = UIBuilder.Label(bg.transform, "", 16).Size(-1, 24);
                row._sub = UIBuilder.Label(bg.transform, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);
                row._traits = UIBuilder.Label(bg.transform, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);
                var btn = bg.gameObject.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.onClick.AddListener(row.Focus);
                return row;
            }

            public void Bind(Adventurer a, int index)
            {
                _adv = a;
                _bg.color = index % 2 == 0 ? UITheme.RowBg : UITheme.RowBgAlt;
                var d = a.Data;
                string hpColor = UITheme.Hex(a.HpRatio > 0.6f ? UITheme.Good : a.HpRatio > 0.3f ? UITheme.Warn : UITheme.Bad);
                _main.text = $"{d.name} {InspectorView.GradeTag(d.grade)}  <color=#{UITheme.Hex(UITheme.TextMuted)}>{AdventurerData.ClassLabel(d.cls)} Lv.{d.level} · {d.mbti} · 가치관 {d.values}</color>  " +
                             $"<color=#{hpColor}>HP {a.Hp:0}/{a.Stats.MaxHp:0}</color>  {UIBuilder.Colored(a.StateLabel, UITheme.Accent)}";
                string quest = a.CurrentQuest != null ? $" · 의뢰 {a.CurrentQuest.Title}" : "";
                _traits.text = "특성: " + Traits.TraitSet.Format(a.Traits.Traits, UITheme.Good, UITheme.Bad);
                _sub.text = $"골드 {d.gold} · EXP {d.exp}/{d.ExpToNextLevel} · 피로 {d.fatigue:0} · 스트레스 {d.stress:0} · 물약 {d.potions} · 전리품 {d.LootCount} · 처치 {d.kills} · 완료 {d.questsCompleted}{quest}";
            }

            void Focus()
            {
                if (_adv == null) return;
                if (Services.TryGet<CameraController>(out var cam)) cam.JumpTo(_adv.Position);
                if (!Services.TryGet<InspectorView>(out var inspector)) return;
                inspector.Show(_adv);
                inspector.FocusSelected();
            }
        }
    }
}
