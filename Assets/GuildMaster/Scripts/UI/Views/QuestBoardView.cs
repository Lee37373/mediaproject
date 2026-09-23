using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
using GuildMaster.Persuasion;
using GuildMaster.Player;
using GuildMaster.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>Quest board: list of active quests + form to post new ones (rewards are escrowed).</summary>
    public class QuestBoardView : UIPanel
    {
        public override string Title => "의뢰 게시판";

        static readonly string[] TypeLabels = { "토벌", "둥지 소탕", "호위" };
        static readonly QuestType[] Types = { QuestType.Hunt, QuestType.Escort };

        RectTransform _listContent;
        readonly List<QuestRow> _rows = new();
        Text _emptyLabel;

        Dropdown _type;
        RectTransform _speciesRow, _hoursRow, _countRow, _pointRow;
        Text _pointLabel;
        /// <summary>Centre of the hunting area picked on the map (hunt quests).</summary>
        Vector2? _huntPoint;
        Dropdown _species;
        Text _goldLabel, _expLabel;
        InputField _count, _gold, _exp, _hours;
        Dropdown _party;
        Text _danger;
        Text _message;

        readonly List<MonsterDefinition> _speciesOptions = new();


        protected override void Build(RectTransform body)
        {
            UIBuilder.HLayout(body, 12).childForceExpandHeight = true;

            // ---- Left: active quests ----
            var left = UIBuilder.Rect(body, "List").Size(-1, -1, 1);
            UIBuilder.VLayout(left, 6, 0).childControlHeight = true;
            var listHeader = UIBuilder.Row(left, 30, 8);
            UIBuilder.Label(listHeader, "진행 중인 의뢰", 17, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 26, 1);
            UIBuilder.Button(listHeader, "의뢰 제안 받기", OpenProposals, 150, 28, new Color(0.25f, 0.45f, 0.3f), 14)
                .Tooltip(() => "길드가 아는 정보로 의뢰 후보를 몇 개 제안받고, 마음에 드는 것을 골라 등록합니다.");
            _listContent = UIBuilder.ScrollList(left, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
            _emptyLabel = UIBuilder.Label(_listContent, "등록된 의뢰가 없습니다. 오른쪽에서 새 의뢰를 등록하세요.", 15, TextAnchor.MiddleCenter, UITheme.TextMuted).Size(-1, 40);

            // ---- Right: new quest form ----
            var form = UIBuilder.Panel(body, "Form", UITheme.PanelBgLight).rectTransform.Size(400, -1, 0);
            UIBuilder.VLayout(form, 8, 12);
            UIBuilder.Label(form, "새 의뢰 등록", 17, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 26);

            _type = UIBuilder.Dropdown(FieldRow(form, "유형"), TypeLabels, _ => OnFormChanged(true));

            _speciesRow = FieldRow(form, "대상 몬스터");
            _species = UIBuilder.Dropdown(_speciesRow, new List<string>(), _ => OnFormChanged(false));

            _hoursRow = FieldRow(form, "최대 시간");
            _hours = UIBuilder.Input(_hoursRow, "6", InputField.ContentType.IntegerNumber);
            _hours.text = "6";
            _hours.onValueChanged.AddListener(_ => OnFormChanged(false));

            _countRow = FieldRow(form, "토벌 수");
            _count = UIBuilder.Input(_countRow, "5", InputField.ContentType.IntegerNumber);
            _count.text = "5";
            _count.onValueChanged.AddListener(_ => OnFormChanged(false));

            // Hunts: the player marks where to hunt; adventurers go there and fight what they see.
            _pointRow = FieldRow(form, "사냥 위치");
            _pointLabel = UIBuilder.Label(_pointRow, "", 14, TextAnchor.MiddleLeft).Size(-1, 30, 1);
            UIBuilder.Button(_pointRow, "지도", PickHuntPoint, 56, 30, null, 14);

            var goldRow = FieldRow(form, "보상 골드");
            _goldLabel = goldRow.GetComponentInChildren<Text>();
            _gold = UIBuilder.Input(goldRow, "0", InputField.ContentType.IntegerNumber);
            _gold.text = "50";
            _gold.onValueChanged.AddListener(_ => OnFormChanged(false));
            var expRow = FieldRow(form, "보상 EXP");
            _expLabel = expRow.GetComponentInChildren<Text>();
            _exp = UIBuilder.Input(expRow, "0", InputField.ContentType.IntegerNumber);
            _exp.text = "30";
            _exp.onValueChanged.AddListener(_ => OnFormChanged(false));
            _party = UIBuilder.Dropdown(FieldRow(form, "최대 인원"), new[] { "1명", "2명", "3명", "4명" }, _ => OnFormChanged(false));

            _danger = UIBuilder.Label(form, "", 15).Size(-1, 88);
            _danger.verticalOverflow = VerticalWrapMode.Overflow;
            UIBuilder.Button(form, "의뢰 등록 (보상 예치)", Submit, -1, 38, new Color(0.25f, 0.45f, 0.3f), 17);
            _message = UIBuilder.Label(form, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 40);
        }

        void OpenProposals()
        {
            if (!Services.TryGet<QuestProposalView>(out var view)) return;
            if (!view.Visible) view.Open();
            else view.BringToFront();
        }

        static RectTransform FieldRow(Transform parent, string label)
        {
            var row = UIBuilder.Row(parent, 30);
            UIBuilder.Label(row, label, 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(92);
            return row;
        }

        protected override void OnOpened()
        {
            RebuildOptions();
            OnFormChanged(true);
        }

        void RebuildOptions()
        {
            // Only species/nests the guild has seen, with observed counts (GuildIntel).
            var intel = Services.Get<GuildIntel>();
            _speciesOptions.Clear();
            _speciesOptions.AddRange(intel.KnownSpecies);
            int prevSpecies = _species.value;
            _species.ClearOptions();
            _species.AddOptions(_speciesOptions.Count == 0
                ? new List<string> { "(확인한 몬스터 없음)" }
                : _speciesOptions.Select(s => $"{s.displayName} (관측 {intel.ObservedPopulation(s)}마리)").ToList());
            _species.SetValueWithoutNotify(Mathf.Clamp(prevSpecies, 0, Mathf.Max(0, _speciesOptions.Count - 1)));

        }

        QuestType CurrentType => Types[Mathf.Clamp(_type.value, 0, Types.Length - 1)];

        /// <summary>
        /// Names the known nest the hunting area covers (P4-32): wiping it out finishes the quest
        /// whatever the kill count says, so it is worth pointing at when picking the spot.
        /// </summary>
        string NestHint()
        {
            var species = _species.value < _speciesOptions.Count ? _speciesOptions[_species.value] : null;
            if (species == null || !_huntPoint.HasValue || !Services.TryGet<GuildIntel>(out var intel)) return "";
            foreach (var n in intel.KnownNests)
            {
                if (intel.SpeciesOf(n) != species) continue;
                if (Vector2.Distance(n.WorldPosition, _huntPoint.Value) > QuestBoard.HuntRadius) continue;
                return UIBuilder.Colored($"  {intel.NameOf(n)} 포함 — 비우면 바로 완료", UITheme.Good);
            }
            return "";
        }

        QuestDraft BuildDraft()
        {
            var d = new QuestDraft
            {
                Type = CurrentType,
                RewardGold = ParseInt(_gold.text),
                RewardExp = ParseInt(_exp.text),
                MaxParty = _party.value + 1,
                Count = ParseInt(_count.text),
                Radius = 8f,
            };
            switch (d.Type)
            {
                case QuestType.Hunt:
                    d.Species = _species.value < _speciesOptions.Count ? _speciesOptions[_species.value] : null;
                    d.HasPoint = _huntPoint.HasValue;
                    d.Point = _huntPoint ?? default;
                    d.Radius = QuestBoard.HuntRadius;
                    break;
                case QuestType.Escort:
                    // Reward fields are hourly rates; the escrow covers the maximum hours.
                    d.Hours = Mathf.Clamp(ParseInt(_hours.text), 0, 24);
                    d.RewardGold *= d.Hours;
                    d.RewardExp *= d.Hours;
                    if (Services.TryGet<PlayerAvatar>(out var master)) d.Point = master.Position;
                    break;
            }
            return d;
        }

        static int ParseInt(string s) => int.TryParse(s, out var v) ? v : 0;

        void OnFormChanged(bool typeChanged)
        {
            var type = CurrentType;
            _speciesRow.gameObject.SetActive(type == QuestType.Hunt);
            _countRow.gameObject.SetActive(type == QuestType.Hunt);
            _pointRow.gameObject.SetActive(type == QuestType.Hunt);
            _pointLabel.text = _huntPoint.HasValue ? QuestBoard.DescribeArea(_huntPoint.Value) + NestHint() : UIBuilder.Colored("지정 안 됨", UITheme.Bad);
            _hoursRow.gameObject.SetActive(type == QuestType.Escort);
            _goldLabel.text = type == QuestType.Escort ? "시간당 골드" : "보상 골드";
            _expLabel.text = type == QuestType.Escort ? "시간당 EXP" : "보상 EXP";

            var draft = BuildDraft();
            var danger = EstimateDanger(draft);
            var t = Services.Get<GuildTreasury>();
            string dangerText = danger.HasValue
                ? $"예상 위험도: {UIBuilder.Colored(Quest.DangerLabel(danger.Value), UITheme.Danger(danger.Value))}"
                : "예상 위험도: -";
            string afford = t.CanAfford(draft.RewardGold, draft.RewardExp)
                ? UIBuilder.Colored($"예치 후 잔액 {t.Gold - draft.RewardGold}G / {t.Exp - draft.RewardExp}EXP", UITheme.TextMuted)
                : UIBuilder.Colored("길드 자금이 부족합니다", UITheme.Bad);
            string risk = RiskText(draft, danger);
            _danger.text = dangerText + (risk.Length > 0 ? "\n" + risk : "") + "\n" + afford;
        }

        /// <summary>
        /// P4-11: how the current guild would judge this quest — the same margin the adventurers use
        /// (QuestRisk), for the strongest member leading a party of the allowed size. A "무모" quest is
        /// shown in red with the level or party size it would need; posting it is still allowed.
        /// </summary>
        static string RiskText(QuestDraft draft, DangerLevel? danger)
        {
            if (!danger.HasValue || !Services.TryGet<GuildRoster>(out var roster)) return "";
            var best = roster.Members.Where(a => a != null && a.IsAlive)
                .OrderByDescending(a => AdventurerStats.Power(a.Data)).FirstOrDefault();
            if (best == null) return UIBuilder.Colored("현재 길드원이 없습니다.", UITheme.Bad);

            var d = danger.Value;
            float power = AdventurerStats.Power(best.Data);
            int party = Mathf.Clamp(draft.MaxParty, 1, 4);
            string label = QuestRisk.Label(QuestRisk.Margin(power, party, d));
            var color = label == QuestRisk.Reckless ? UITheme.Bad : label == QuestRisk.Risky ? UITheme.Warn : UITheme.Good;
            string who = party == 1 ? $"{best.Data.name}(Lv{best.Data.level}) 혼자" : $"{best.Data.name}(Lv{best.Data.level}) 포함 {party}인";
            string text = $"현재 길드원 기준: {UIBuilder.Colored(label, color)} ({who})";
            if (label != QuestRisk.Reckless && label != QuestRisk.Risky) return text;

            int level = QuestRisk.SoloLevel(d, best.Data.gearTier);
            int need = QuestRisk.PartyNeeded(power, d);
            string advice = need > 1 ? $"Lv{level} 이상 혼자 또는 {need}인 파티 권장" : $"Lv{level} 이상 권장";
            return text + "\n" + UIBuilder.Colored(label == QuestRisk.Reckless ? $"{advice} · 그래도 등록할 수 있습니다" : advice, color);
        }

        DangerLevel? EstimateDanger(QuestDraft d)
        {
            switch (d.Type)
            {
                case QuestType.Hunt when d.Species != null:
                    return d.HasPoint
                        ? QuestBoard.HuntDanger(d.Species, d.Count, d.Point, d.Radius <= 0f ? 8f : d.Radius)
                        : QuestBoard.HuntDanger(d.Species, d.Count);
                case QuestType.Escort:
                    return DangerLevel.Low;
            }
            return null;
        }

        void PickHuntPoint()
        {
            var picker = Services.Get<MapPicker>();
            Close();
            picker.Begin("사냥할 위치를 클릭하세요 (우클릭/ESC: 취소)", p =>
            {
                Open();
                var error = QuestBoard.HuntPointError(p);
                if (error != null) { _message.text = UIBuilder.Colored(error, UITheme.Bad); return; }
                _huntPoint = p;
                _message.text = "";
                OnFormChanged(false);
            }, Open);
        }

        void Submit()
        {
            var board = Services.Get<QuestBoard>();
            if (board.TryPost(BuildDraft(), out var quest, out var error))
            {
                _message.text = UIBuilder.Colored(quest.Type == QuestType.Escort
                    ? $"'{quest.Title}' 의뢰가 등록되었습니다. 호위대가 모이면 마을 밖으로 이동하세요."
                    : $"'{quest.Title}' 의뢰가 등록되었습니다.", UITheme.Good);
                RebuildOptions();
                OnFormChanged(false);
                Refresh();
            }
            else _message.text = UIBuilder.Colored(error, UITheme.Bad);
        }

        protected override void Refresh()
        {
            var quests = Services.Get<QuestBoard>().Quests;
            EnsureRows(_listContent, _rows, quests.Count, parent => QuestRow.Create(parent));
            for (int i = 0; i < quests.Count; i++) _rows[i].Bind(quests[i], i);
            _emptyLabel.gameObject.SetActive(quests.Count == 0);
            _emptyLabel.transform.SetAsLastSibling();
            OnFormChanged(false);
        }

        /// <summary>One quest line in the list.</summary>
        class QuestRow : MonoBehaviour
        {
            Image _bg;
            Text _title;
            Text _detail;
            Button _cancel;
            Button _persuade;
            Quest _quest;

            public static QuestRow Create(Transform parent)
            {
                var bg = UIBuilder.Panel(parent, "QuestRow", UITheme.RowBg);
                bg.Size(-1, 64);
                var row = bg.gameObject.AddComponent<QuestRow>();
                row._bg = bg;
                var h = UIBuilder.HLayout(bg, 8, 6);
                h.padding = new RectOffset(10, 8, 6, 6);
                var texts = UIBuilder.Rect(bg.transform, "Texts").Size(-1, -1, 1);
                UIBuilder.VLayout(texts, 2, 0);
                row._title = UIBuilder.Label(texts, "", 16).Size(-1, 24);
                row._detail = UIBuilder.Label(texts, "", 14, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);
                row._persuade = UIBuilder.Button(bg.transform, "협상", row.OnPersuade, 120, 30, UITheme.Header, 14);
                row._persuade.Tooltip(() => $"의뢰가 {PersuasionRules.OpenAfterHours:0}시간 동안 아무도 맡지 않으면, 길드원을 직접 찾아가 설득하거나 보상을 더 얹는 협상을 할 수 있습니다.");
                row._cancel = UIBuilder.Button(bg.transform, "취소", row.OnCancel, 60, 30, UITheme.ButtonDanger, 14);
                return row;
            }

            public void Bind(Quest q, int index)
            {
                _quest = q;
                _bg.color = index % 2 == 0 ? UITheme.RowBg : UITheme.RowBgAlt;
                // P4-24: an untaken quest can be persuaded onto someone once it has waited long enough.
                Services.TryGet<PersuasionService>(out var persuasion);
                bool canPersuade = persuasion != null && persuasion.IsOpen(q);
                bool waiting = q.Status == QuestStatus.Open && q.Assignees.Count == 0;
                _persuade.gameObject.SetActive(waiting);
                _persuade.interactable = canPersuade;
                if (waiting)
                    _persuade.SetLabel(canPersuade ? "협상" : $"협상 {Mathf.CeilToInt(persuasion != null ? persuasion.HoursUntilOpen(q) : PersuasionRules.OpenAfterHours)}시간 후");
                string status = q.Status switch
                {
                    QuestStatus.Open => UIBuilder.Colored("모집 중", UITheme.Accent) + (canPersuade ? " " + UIBuilder.Colored("협상 가능", UITheme.Warn) : ""),
                    QuestStatus.InProgress => UIBuilder.Colored("진행 중", UITheme.Exp),
                    QuestStatus.Completed => UIBuilder.Colored("보고 대기", UITheme.Good),
                    _ => q.Status.ToString(),
                };
                _title.text = $"[{q.Id}] {q.Title}  {status}  {UIBuilder.Colored("위험 " + Quest.DangerLabel(q.Danger), UITheme.Danger(q.Danger))}";
                string members = q.Assignees.Count == 0 ? "없음" : string.Join(", ", q.Assignees.Select(a => a.TakerName));
                _detail.text = $"진행 {q.ProgressText} · 보상 {q.RewardGold}G / {q.RewardExp}EXP · 인원 {q.Assignees.Count}/{q.MaxParty} ({members})";
                _cancel.interactable = q.IsActive;
            }

            void OnPersuade()
            {
                if (_quest != null && Services.TryGet<PersuadeDialogView>(out var dialog)) dialog.Begin(_quest);
            }

            void OnCancel()
            {
                if (_quest != null) Services.Get<QuestBoard>().Cancel(_quest);
            }
        }
    }
}
