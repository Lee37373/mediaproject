using System;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Welfare;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The guild master's care window for one member (F-2): how stressed they are and what that does, what they
    /// have asked for, and the tools — interview, leave, bonus, dismissal. Opened from the roster, the detail
    /// window and the "요구" notifications.
    /// </summary>
    public class MemberCareView : UIPanel
    {
        /// <summary>Real seconds a dismissal stays armed after the first click.</summary>
        const float FireConfirmSeconds = 3f;

        public override string Title => "길드원 관리";
        protected override float RefreshInterval => 0.25f;

        Adventurer _who;
        Text _header, _state, _result;
        ActionRow _interview, _leave, _bonus, _treat, _tribute, _fire;
        string _resultText = "";
        float _fireArmedUntil;

        class ActionRow
        {
            public Button Button;
            public Text Desc;
        }

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 0);
            _header = Block(body, 52, 17);
            Section(body, "마음 상태");
            _state = Block(body, 138);
            Section(body, "관리  " + UIBuilder.Colored("(길드 마스터가 할 수 있는 일)", UITheme.TextMuted));
            _interview = AddAction(body, "면담", Interview, UITheme.Button);
            _leave = AddAction(body, "휴가", Leave, UITheme.Button);
            _bonus = AddAction(body, "보너스", Bonus, UITheme.Button);
            _treat = AddAction(body, "치료", Treat, UITheme.Button);
            _tribute = AddAction(body, "상납 요구", Tribute, UITheme.Button); // 2026-10-29: 상납금 talk
            _fire = AddAction(body, "해고", Fire, UITheme.ButtonDanger);
            _result = Block(body, 64);
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

        static ActionRow AddAction(Transform parent, string label, UnityAction onClick, Color color)
        {
            var row = UIBuilder.Row(parent, 50, 10);
            var button = UIBuilder.Button(row, label, onClick, 170, 46, color, 16);
            var desc = UIBuilder.Label(row, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, -1, 1);
            return new ActionRow { Button = button, Desc = desc };
        }

        public void Show(Adventurer who)
        {
            _who = who;
            _resultText = "";
            _fireArmedUntil = 0f;
            Open();
        }

        public static Color LevelColor(StressLevel level) => level switch
        {
            StressLevel.Tense => UITheme.Accent,
            StressLevel.Strained => UITheme.Warn,
            StressLevel.Breaking => UITheme.Bad,
            _ => UITheme.Good,
        };

        // ---------------- Live text ----------------

        protected override void Refresh()
        {
            _result.text = _resultText;
            bool ready = _who != null && _who.Data != null && _who.IsAlive
                         && Services.TryGet<GuildWelfare>(out _) && Services.TryGet<GameClock>(out _);
            if (!ready)
            {
                _header.text = UIBuilder.Colored("관리할 길드원이 없습니다.", UITheme.TextMuted);
                _state.text = "";
                foreach (var row in new[] { _interview, _leave, _bonus, _treat, _tribute, _fire })
                {
                    row.Button.interactable = false;
                    row.Desc.text = "";
                }
                return;
            }

            var welfare = Services.Get<GuildWelfare>();
            Services.TryGet<Legacy.GuildLegacy>(out var legacy);
            float now = Services.Get<GameClock>().TotalHours;
            var a = _who;
            var d = a.Data;
            var care = welfare.Care(a);
            var level = StressRules.LevelOf(d.stress);
            var levelColor = LevelColor(level);

            _header.text =
                $"{UIBuilder.Colored(d.name, UITheme.Accent)} {InspectorView.GradeTag(d.grade)}\n" +
                UIBuilder.Colored($"{AdventurerData.ClassLabel(d.cls)} Lv.{d.level} · {d.mbti} · 가치관 {d.values} · 골드 {d.gold}", UITheme.TextMuted) + "\n" +
                $"상태: {UIBuilder.Colored(a.StateLabel, UITheme.Accent)}" + (a.CurrentQuest != null ? $" · {a.CurrentQuest.Title}" : "");

            var sb = new System.Text.StringBuilder();
            sb.Append($"스트레스 {Bar(d.stress)}  {UIBuilder.Colored(StressRules.Label(level), levelColor)}\n");
            sb.Append($"피로      {Bar(d.fatigue)}\n");
            sb.Append(UIBuilder.Colored(StressRules.Effect(level), levelColor)).Append('\n');
            if (care.OnLeave(now))
                sb.Append(UIBuilder.Colored($"휴가 중: 약 {Mathf.CeilToInt(care.LeaveHoursLeft(now))}시간 남았습니다. 그동안 의뢰를 받지 않습니다.", UITheme.Exp)).Append('\n');
            if (legacy != null && legacy.IsWounded(a))
                sb.Append(UIBuilder.Colored($"중상: 약 {Mathf.CeilToInt(legacy.WoundedHoursLeft(a))}시간 동안 의뢰를 받지 않습니다. 치료하면 바로 회복합니다.", UITheme.Bad)).Append('\n');
            if (care.Demand != CareDemand.None)
                sb.Append(UIBuilder.Colored($"요구: {CareRules.Label(care.Demand)}  (약 {Mathf.CeilToInt(care.DemandHoursLeft(now))}시간 안에 들어주세요. 요구한 일은 효과가 {CareRules.DemandMetFactor:0.#}배입니다.)", UITheme.Warn)).Append('\n');
            else
                sb.Append(UIBuilder.Colored("요구: 없음", UITheme.TextMuted)).Append('\n');
            if (care.Grudges > 0)
                sb.Append(UIBuilder.Colored($"쌓인 불만 {care.Grudges}회 (요구를 무시하면 늘고, 들어주면 줄어듭니다. {CareRules.QuitGrudges}회가 되면 떠날 생각을 합니다.)", UITheme.Bad)).Append('\n');
            if (care.Resigning)
                sb.Append(UIBuilder.Colored($"이탈 예고: 약 {Mathf.CeilToInt(care.ResignHoursLeft(now))}시간 뒤 길드를 떠납니다. 스트레스를 {StressRules.StrainedAt:0} 아래로 낮추면 붙잡을 수 있습니다.", UITheme.Bad));
            _state.text = sb.ToString();

            // ---- Tools ----
            var talk = welfare.CanInterview(a);
            _interview.Button.interactable = talk.Ok;
            _interview.Button.SetLabel("면담" + Requested(care, CareDemand.Talk));
            // A real conversation now (user request 2026-10-01, CounselDialogView): how much it helps depends on what is said.
            _interview.Desc.text = talk.Ok
                ? "무료 · 대화로 고민을 들어 줍니다. 공감·질문·구체적인 도움이 통하고, 다그치면 오히려 스트레스가 오릅니다."
                    + (care.Demand == CareDemand.Talk ? $" (요구한 면담: 효과 {CareRules.DemandMetFactor:0.#}배)" : "")
                : UIBuilder.Colored(talk.Reason, UITheme.Warn);

            var leave = welfare.CanGrantLeave(a);
            float leaveRelief = CareRules.LeaveRelief * (care.Demand == CareDemand.Leave ? CareRules.DemandMetFactor : 1f);
            _leave.Button.interactable = leave.Ok;
            _leave.Button.SetLabel("휴가" + Requested(care, CareDemand.Leave));
            _leave.Desc.text = leave.Ok
                ? $"무료 · 스트레스 -{leaveRelief:0} · {CareRules.LeaveHours:0}시간 동안 쉬며 의뢰를 받지 않습니다."
                : UIBuilder.Colored(leave.Reason, UITheme.Warn);

            var bonus = welfare.CanGiveBonus(a);
            int cost = welfare.BonusCost(a);
            float bonusRelief = CareRules.BonusRelief(d.values) * (care.Demand == CareDemand.Bonus ? CareRules.DemandMetFactor : 1f);
            _bonus.Button.interactable = bonus.Ok;
            _bonus.Button.SetLabel($"보너스 {cost}G" + Requested(care, CareDemand.Bonus));
            string treasuryGold = Services.TryGet<GuildTreasury>(out var treasury) ? $" (창고 {treasury.Gold}G)" : "";
            _bonus.Desc.text = bonus.Ok
                ? $"스트레스 약 -{bonusRelief:0} · 창고 골드를 길드원에게 줍니다.{treasuryGold}"
                : UIBuilder.Colored(bonus.Reason, UITheme.Warn);

            var treat = legacy != null ? legacy.CanTreat(a) : CareCheck.No("치료할 수 없습니다.");
            _treat.Button.interactable = treat.Ok;
            _treat.Button.SetLabel(legacy != null ? $"치료 {legacy.TreatCost(a)}G" : "치료");
            _treat.Desc.text = treat.Ok
                ? $"중상을 바로 치료하고 체력을 {Legacy.LegacyRules.TreatHpRatio * 100f:0}%까지 회복합니다. 치료하지 않으면 시간이 지나야 낫습니다."
                : UIBuilder.Colored(treat.Reason, UITheme.Warn);

            // Tribute (상납금, user decision 2026-10-29): talk the member out of some of their gold.
            var tribute = Services.TryGet<TributeService>(out var tributes) ? tributes.CanAsk(a, now) : CareCheck.No("상납금을 받을 수 없습니다.");
            _tribute.Button.interactable = tribute.Ok;
            _tribute.Desc.text = tribute.Ok
                ? $"대화로 길드원의 돈을 받아냅니다 (소지금 {d.gold}G). 사정·명분·보답 약속이 통하고, 협박하면 빨리 뜯어내지만 스트레스와 원망이 쌓입니다."
                : UIBuilder.Colored(tribute.Reason, UITheme.Warn);

            bool armed = Time.unscaledTime < _fireArmedUntil;
            _fire.Button.interactable = true;
            _fire.Button.SetLabel(armed ? "정말 해고" : "해고");
            _fire.Desc.text = armed
                ? UIBuilder.Colored("한 번 더 누르면 해고합니다.", UITheme.Bad)
                : "길드에서 내보냅니다. 소지품은 가져가고, 친한 길드원은 마음이 무겁습니다.";
        }

        static string Requested(MemberCare care, CareDemand kind) => care.Demand == kind ? " (요구)" : "";

        /// <summary>Ten-block text gauge for 0-100 values.</summary>
        static string Bar(float value)
        {
            int filled = Mathf.Clamp(Mathf.RoundToInt(value / 10f), 0, 10);
            return UIBuilder.Colored(new string('■', filled), UITheme.Accent) + UIBuilder.Colored(new string('□', 10 - filled), UITheme.TextMuted) + $" {value:0}";
        }

        // ---------------- Actions ----------------

        /// <summary>Opens the counselling talk (user request 2026-10-01); the one-click interview only without that window.</summary>
        void Interview()
        {
            if (_who == null || !Services.TryGet<GuildWelfare>(out var welfare)) return;
            _fireArmedUntil = 0f;
            var check = welfare.CanInterview(_who);
            if (!check.Ok) { Report(CareResult.Fail(check.Reason)); Refresh(); return; }
            if (Services.TryGet<CounselDialogView>(out var talk)) talk.Show(_who);
            else Do(w => w.Interview(_who));
        }
        void Leave() => Do(w => w.GrantLeave(_who));

        /// <summary>Opens the tribute talk (상납금, 2026-10-29).</summary>
        void Tribute()
        {
            if (_who == null || !Services.TryGet<TributeService>(out var tributes) || !Services.TryGet<GameClock>(out var clock)) return;
            _fireArmedUntil = 0f;
            var check = tributes.CanAsk(_who, clock.TotalHours);
            if (!check.Ok) { Report(CareResult.Fail(check.Reason)); Refresh(); return; }
            if (Services.TryGet<TributeDialogView>(out var talk)) talk.Show(_who);
        }
        void Bonus() => Do(w => w.GiveBonus(_who));

        /// <summary>Pays a healer for a badly wounded member (F-6).</summary>
        void Treat()
        {
            if (_who == null || !Services.TryGet<Legacy.GuildLegacy>(out var legacy)) return;
            _fireArmedUntil = 0f;
            Report(legacy.Treat(_who));
            Refresh();
        }

        void Do(Func<GuildWelfare, CareResult> action)
        {
            if (_who == null || !Services.TryGet<GuildWelfare>(out var welfare)) return;
            _fireArmedUntil = 0f;
            Report(action(welfare));
            Refresh();
        }

        /// <summary>Dismissal needs two clicks within a few seconds.</summary>
        void Fire()
        {
            if (_who == null || !Services.TryGet<GuildWelfare>(out var welfare)) return;
            if (Time.unscaledTime >= _fireArmedUntil)
            {
                _fireArmedUntil = Time.unscaledTime + FireConfirmSeconds;
                Refresh();
                return;
            }
            _fireArmedUntil = 0f;
            var result = welfare.Fire(_who);
            Report(result);
            if (result.Ok) _who = null;
            Refresh();
        }

        void Report(CareResult r)
        {
            if (!r.Ok)
            {
                _resultText = UIBuilder.Colored(r.Message, UITheme.Bad);
                return;
            }
            _resultText = UIBuilder.Colored(r.Message, UITheme.Good);
            if (!string.IsNullOrEmpty(r.Reply) && _who != null && _who.Data != null)
                _resultText += $"\n{UIBuilder.Colored(_who.Data.name, UITheme.Accent)}: \"{r.Reply}\"";
        }
    }
}
