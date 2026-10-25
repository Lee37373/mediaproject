using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Traits;
using GuildMaster.Units;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Adventurer detail window (P4-6): stats, traits (hover for description and effects), skills
    /// (hover for description, live cooldown), gear, record, latest thought, memories, relations and
    /// recent dialogue. Opened from the inspector's [상세] and the roster's [상세] buttons.
    /// </summary>
    public class AdventurerDetailView : UIPanel
    {
        const int DialoguePreview = 4;
        const int MemoryPreview = 5;

        public override string Title => "모험가 상세";
        protected override float RefreshInterval => 0.25f;

        Adventurer _who;
        Text _header, _stats, _gear, _record, _thought, _mood, _memories, _relations, _dialogue;
        RectTransform _traitRow, _skillRow;
        readonly List<Text> _skillLabels = new();

        protected override void Build(RectTransform body)
        {
            UIBuilder.HLayout(body, 12, 0).childForceExpandHeight = true;

            // ---- Left: who, numbers, gear, record ----
            var left = UIBuilder.Panel(body, "Left", UITheme.PanelBgLight).rectTransform.Size(400, -1, 0);
            UIBuilder.VLayout(left, 6, 12);
            _header = Block(left, 72, 18);
            Section(left, "능력치");
            _stats = Block(left, 118);
            Section(left, "장비와 소지품");
            _gear = Block(left, 72);
            Section(left, "전적");
            _record = Block(left, 72);
            var buttons = UIBuilder.Row(left, 34);
            UIBuilder.Button(buttons, "추적", FocusCamera, 90, 32, null, 15);
            UIBuilder.Button(buttons, "대화 기록 전체", OpenDialogueLog, 140, 32, null, 15);
            UIBuilder.Button(buttons, "관리", OpenCare, 90, 32, null, 15);

            // ---- Right: traits, skills, mind, social ----
            var right = UIBuilder.Panel(body, "Right", UITheme.PanelBgLight).rectTransform.Size(-1, -1, 1);
            UIBuilder.VLayout(right, 6, 12);
            Section(right, "특성  " + UIBuilder.Colored("(마우스를 올리면 설명)", UITheme.TextMuted));
            _traitRow = UIBuilder.Row(right, 30, 6);
            Section(right, "스킬  " + UIBuilder.Colored("(마우스를 올리면 설명)", UITheme.TextMuted));
            _skillRow = UIBuilder.Row(right, 30, 6);
            Section(right, "최근 생각");
            // Two lines more for what the decision weighed (P4-104a).
            _thought = Block(right, 62);
            // Its mood and what is on its mind (P4-104c): room taken from the memories and the talk (both have their full windows).
            Section(right, "기분");
            _mood = Block(right, 74);
            Section(right, "기억");
            _memories = Block(right, 84);
            Section(right, "관계");
            _relations = Block(right, 44);
            Section(right, "최근 대화");
            _dialogue = Block(right, 72);
        }

        static void Section(Transform parent, string title)
            => UIBuilder.Label(parent, title, 15, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 22);

        static Text Block(Transform parent, float height, int size = 14)
        {
            var t = UIBuilder.Label(parent, "", size, TextAnchor.UpperLeft).Size(-1, height);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        public void Show(Adventurer who)
        {
            _who = who;
            RebuildChips();
            Open();
        }

        // ---------------- Chips (built once per adventurer) ----------------

        void RebuildChips()
        {
            UIBuilder.Clear(_traitRow);
            UIBuilder.Clear(_skillRow);
            _skillLabels.Clear();
            if (_who == null) return;

            var traits = _who.Traits?.Traits ?? (IReadOnlyList<TraitDefinition>)new List<TraitDefinition>();
            if (traits.Count == 0) Chip(_traitRow, UIBuilder.Colored("없음", UITheme.TextMuted), UITheme.RowBg, null);
            foreach (var t in traits)
            {
                var trait = t;
                Chip(_traitRow, trait.displayName, trait.isPositive ? new Color(0.2f, 0.36f, 0.22f) : new Color(0.42f, 0.18f, 0.17f),
                    () => TraitTooltip(trait));
            }

            if (_who.Skills.Count == 0) Chip(_skillRow, UIBuilder.Colored("없음", UITheme.TextMuted), UITheme.RowBg, null);
            foreach (var s in _who.Skills)
            {
                var skill = s;
                _skillLabels.Add(Chip(_skillRow, skill.Name, new Color(0.2f, 0.26f, 0.42f), () => SkillTooltip(skill)));
            }
        }

        static Text Chip(Transform row, string text, Color color, System.Func<string> tooltip)
        {
            var bg = UIBuilder.Panel(row, "Chip", color);
            var h = UIBuilder.HLayout(bg, 0, 0);
            h.padding = new RectOffset(10, 10, 4, 4);
            // The row sizes the chip from this layout's preferred width (label + padding).
            var label = UIBuilder.Label(bg.transform, text, 14, TextAnchor.MiddleCenter);
            label.raycastTarget = false;
            bg.Size(-1, 28, 0);
            if (tooltip != null) bg.Tooltip(tooltip);
            return label;
        }

        static string TraitTooltip(TraitDefinition t)
        {
            var sb = new StringBuilder();
            sb.Append(UIBuilder.Colored(t.displayName, t.isPositive ? UITheme.Good : UITheme.Bad))
              .Append(UIBuilder.Colored(t.isPositive ? "  긍정 특성" : "  부정 특성", UITheme.TextMuted));
            // Flavour (the line the LLM also sees), then the mechanical effect.
            if (!string.IsNullOrEmpty(t.promptHint)) sb.Append('\n').Append(UIBuilder.Colored(t.promptHint, UITheme.TextMuted));
            if (!string.IsNullOrEmpty(t.description)) sb.Append('\n').Append(UIBuilder.Colored("효과: ", UITheme.Accent)).Append(t.description);
            return sb.ToString();
        }

        static string SkillTooltip(Skill s)
        {
            var sb = new StringBuilder();
            sb.Append(UIBuilder.Colored(s.Name, UITheme.Accent)).Append(UIBuilder.Colored($"  재사용 {s.Cooldown:0}초", UITheme.TextMuted));
            if (!string.IsNullOrEmpty(s.Description)) sb.Append('\n').Append(s.Description);
            return sb.ToString();
        }

        // ---------------- Live text ----------------

        /// <summary>What the guild's facilities make of its stats (P4-87; 100% = the three stat buildings at level 3).</summary>
        static string FacilityScaleText(Adventurer a)
        {
            if (!Services.TryGet<Town.FacilityService>(out var f)) return "";
            var k = f.ScaleFor(a);
            string who = a.IsGuildMember && !a.IsVisitor ? "내실" : "내실(길드원만)";
            return $"{who} 공격 {k.Attack * 100f:0}%·방어 {k.Defense * 100f:0}%·체력 {k.MaxHp * 100f:0}%";
        }

        protected override void Refresh()
        {
            if (_who == null || _who.Data == null) { _header.text = "대상이 없습니다."; return; }
            var a = _who;
            var d = a.Data;
            if (!a.IsAlive) _header.text = $"{d.name}  {UIBuilder.Colored("(사망)", UITheme.Bad)}";
            else
                _header.text =
                    $"{UIBuilder.Colored(d.name, UITheme.Accent)} {InspectorView.GradeTag(d.grade)}" + (a.IsVisitor ? " " + UIBuilder.Colored("(방문자)", UITheme.Warn) : "") + "\n" +
                    UIBuilder.Colored($"{AdventurerData.ClassLabel(d.cls)} Lv.{d.level} · 전투 {CombatTiers.Label(a.Tier)} · {d.mbti} · 가치관 {d.values}", UITheme.TextMuted) + "\n" +
                    $"상태: {UIBuilder.Colored(a.StateLabel, UITheme.Accent)}" + (a.CurrentQuest != null ? $" · {a.CurrentQuest.Title} ({a.CurrentQuest.ProgressText})" : "");

            var s = a.Stats;
            _stats.text =
                $"HP {a.Hp:0}/{s.MaxHp:0}   공격 {s.Attack:0.#}   방어 {s.Defense:0.#}\n" +
                $"사거리 {s.AttackRange:0.#}   공격 간격 {s.AttackCooldown:0.##}초   이동 {s.MoveSpeed:0.#}   시야 {s.SightRadius:0.#}\n" +
                $"피로      {Bar(d.fatigue)}\n" +
                $"스트레스 {Bar(d.stress)}\n" +
                UIBuilder.Colored($"후퇴 기준 체력 {a.RetreatThreshold * 100f:0}% · {FacilityScaleText(a)}", UITheme.TextMuted);

            var db = Services.Get<GameDatabase>();
            var next = GearShopping.NextGear(d, db);
            string nextText = next == null ? "최고 등급" :
                Services.TryGet<Economy.TradeService>(out var trade) ? $"다음 {next.displayName} {trade.GoodsPrice(next)}G" : next.displayName;
            string loot = d.loot.Count == 0 ? "없음" : string.Join(", ", d.loot.Select(l => $"{db.Item(l.itemId)?.displayName ?? l.itemId} x{l.count}"));
            _gear.text =
                $"장비 {(d.gearTier == 0 ? "없음" : $"{d.gearTier}등급")} {UIBuilder.Colored($"(공격 +{3 * d.gearTier}, 방어 +{2 * d.gearTier}) · {nextText}", UITheme.TextMuted)}\n" +
                $"{LivingCostRules.Purse(d.gold, d.debt)} · 물약 {d.potions} · 조명탄 {d.flares}\n" +
                UIBuilder.Colored("전리품: " + loot, UITheme.TextMuted);

            _record.text =
                $"EXP {d.exp}/{d.ExpToNextLevel}  {Bar(100f * d.exp / Mathf.Max(1, d.ExpToNextLevel))}\n" +
                $"처치 {d.kills} · 완료한 의뢰 {d.questsCompleted}\n" +
                UIBuilder.Colored($"기억 {d.memories.Count}개 · 대화 {d.dialogue.Count}줄", UITheme.TextMuted);

            for (int i = 0; i < _skillLabels.Count && i < a.Skills.Count; i++)
            {
                var sk = a.Skills[i];
                _skillLabels[i].text = sk.IsReady ? sk.Name : $"{sk.Name} {UIBuilder.Colored($"{sk.CooldownLeft:0}초", UITheme.TextMuted)}";
            }

            _thought.text = string.IsNullOrEmpty(a.LastThought) ? UIBuilder.Colored("아직 판단한 적이 없습니다.", UITheme.TextMuted)
                : a.LastThinking?.Choice != null
                    ? $"{a.LastThought}\n{UIBuilder.Colored("고려한 것: " + a.LastThinking.Describe(), UITheme.TextMuted)}"
                    : a.LastThought;

            float now = Services.TryGet<GameClock>(out var clock) ? clock.TotalHours : 0f;
            _mood.text = $"기분 {MoodFormat.Mood(a)}{MoodFormat.Inspiration(a, now)} {UIBuilder.Colored($"· 스트레스 {d.stress:0}", UITheme.TextMuted)}\n{MoodFormat.Lines(a, now, 3)}";

            _memories.text = d.memories.Count == 0 ? UIBuilder.Colored("아직 기억이 없습니다.", UITheme.TextMuted)
                : string.Join("\n", d.memories.Skip(Mathf.Max(0, d.memories.Count - MemoryPreview)).Select(m => m.Format()));

            _relations.text = RelationsText(a);

            var lines = d.dialogue;
            _dialogue.text = lines.Count == 0 ? UIBuilder.Colored("아직 나눈 대화가 없습니다.", UITheme.TextMuted)
                : string.Join("\n", lines.Skip(Mathf.Max(0, lines.Count - DialoguePreview)).Select(l =>
                    $"{UIBuilder.Colored(l.kind == DialogueKind.Recruit ? "[영입]" : $"[잡담·{l.with}]", UITheme.TextMuted)} {UIBuilder.Colored(l.speaker, l.fromPlayer ? UITheme.Accent : UITheme.Good)}: {l.text}"));
        }

        /// <summary>Feelings towards every current member, strongest first.</summary>
        static string RelationsText(Adventurer a)
        {
            if (a.IsVisitor || !Services.TryGet<GuildRoster>(out var roster)) return UIBuilder.Colored("길드원이 아닙니다.", UITheme.TextMuted);
            var parts = Relationships.Strongest(a, roster.Members, 8)
                .Select(x => $"{x.other.Data.name} {UIBuilder.Colored(Relationships.Describe(x.value), x.value >= 15 ? UITheme.Good : x.value <= -15 ? UITheme.Bad : UITheme.TextMuted)}")
                .ToList();
            return parts.Count == 0 ? UIBuilder.Colored("다른 길드원이 없습니다.", UITheme.TextMuted) : string.Join(", ", parts);
        }

        /// <summary>Ten-block text gauge for 0–100 values.</summary>
        static string Bar(float value)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(value / 10f), 0, 10);
            return UIBuilder.Colored(new string('■', filled), UITheme.Accent) + UIBuilder.Colored(new string('□', 10 - filled), UITheme.TextMuted) + $" {value:0}";
        }

        void FocusCamera()
        {
            if (_who == null || !_who.IsAlive || !Services.TryGet<CameraController>(out var cam)) return;
            cam.Focus(_who.transform);
        }

        void OpenCare()
        {
            if (_who != null && !_who.IsVisitor && Services.TryGet<MemberCareView>(out var view)) view.Show(_who);
        }

        void OpenDialogueLog()
        {
            if (_who != null && Services.TryGet<DialogueLogView>(out var view)) view.Show(_who);
        }
    }
}
