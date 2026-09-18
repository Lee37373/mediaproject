using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Economy;
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

        static readonly string[] TypeLabels = { "토벌", "둥지 소탕", "탐사" };
        static readonly QuestType[] Types = { QuestType.Hunt, QuestType.ClearNest, QuestType.Explore };

        RectTransform _listContent;
        readonly List<QuestRow> _rows = new();
        Text _emptyLabel;

        Dropdown _type;
        RectTransform _speciesRow, _nestRow, _pointRow, _countRow;
        Dropdown _species;
        Dropdown _nest;
        Text _pointLabel;
        InputField _count, _gold, _exp;
        Dropdown _party;
        Text _danger;
        Text _message;

        readonly List<MonsterDefinition> _speciesOptions = new();
        readonly List<Nest> _nestOptions = new();
        Vector2? _pickedPoint;

        protected override void Build(RectTransform body)
        {
            UIBuilder.HLayout(body, 12).childForceExpandHeight = true;

            // ---- Left: active quests ----
            var left = UIBuilder.Rect(body, "List").Size(-1, -1, 1);
            UIBuilder.VLayout(left, 6, 0).childControlHeight = true;
            UIBuilder.Label(left, "진행 중인 의뢰", 17, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 26);
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

            _nestRow = FieldRow(form, "대상 둥지");
            _nest = UIBuilder.Dropdown(_nestRow, new List<string>(), _ => OnFormChanged(false));
            UIBuilder.Button(_nestRow, "지도", PickNest, 56, 30, null, 14);

            _pointRow = FieldRow(form, "탐사 지점");
            _pointLabel = UIBuilder.Label(_pointRow, "미지정", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, -1, 1);
            UIBuilder.Button(_pointRow, "지도에서 선택", PickPoint, 120, 30, null, 14);

            _countRow = FieldRow(form, "토벌 수");
            _count = UIBuilder.Input(_countRow, "5", InputField.ContentType.IntegerNumber);
            _count.text = "5";
            _count.onValueChanged.AddListener(_ => OnFormChanged(false));

            _gold = UIBuilder.Input(FieldRow(form, "보상 골드"), "0", InputField.ContentType.IntegerNumber);
            _gold.text = "50";
            _gold.onValueChanged.AddListener(_ => OnFormChanged(false));
            _exp = UIBuilder.Input(FieldRow(form, "보상 EXP"), "0", InputField.ContentType.IntegerNumber);
            _exp.text = "30";
            _exp.onValueChanged.AddListener(_ => OnFormChanged(false));
            _party = UIBuilder.Dropdown(FieldRow(form, "최대 인원"), new[] { "1명", "2명", "3명", "4명" }, _ => OnFormChanged(false));

            _danger = UIBuilder.Label(form, "", 15).Size(-1, 44);
            UIBuilder.Button(form, "의뢰 등록 (보상 예치)", Submit, -1, 38, new Color(0.25f, 0.45f, 0.3f), 17);
            _message = UIBuilder.Label(form, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 40);
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
                : _speciesOptions.Select(s => $"{s.displayName} (위험 {Quest.DangerLabel(s.danger)}, 관측 {intel.ObservedPopulation(s)}마리)").ToList());
            _species.SetValueWithoutNotify(Mathf.Clamp(prevSpecies, 0, Mathf.Max(0, _speciesOptions.Count - 1)));

            var prevNest = _nest.value < _nestOptions.Count ? _nestOptions[_nest.value] : null;
            _nestOptions.Clear();
            _nestOptions.AddRange(intel.KnownNests.Where(n => !intel.Get(n).Observed || intel.Get(n).LastCount > 0));
            _nest.ClearOptions();
            _nest.AddOptions(_nestOptions.Count == 0
                ? new List<string> { "(발견한 둥지 없음)" }
                : _nestOptions.Select(n => $"{n.DisplayName} ({intel.DescribeNest(n)})").ToList());
            int idx = prevNest != null ? _nestOptions.IndexOf(prevNest) : 0;
            _nest.SetValueWithoutNotify(Mathf.Max(0, idx));
        }

        QuestType CurrentType => Types[Mathf.Clamp(_type.value, 0, Types.Length - 1)];

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
                    break;
                case QuestType.ClearNest:
                    d.Nest = _nest.value < _nestOptions.Count ? _nestOptions[_nest.value] : null;
                    break;
                case QuestType.Explore:
                    d.Point = _pickedPoint ?? Vector2.zero;
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
            _nestRow.gameObject.SetActive(type == QuestType.ClearNest);
            _pointRow.gameObject.SetActive(type == QuestType.Explore);
            _pointLabel.text = _pickedPoint.HasValue ? $"({_pickedPoint.Value.x:0}, {_pickedPoint.Value.y:0}) 반경 8" : "미지정";

            var draft = BuildDraft();
            var danger = EstimateDanger(draft);
            var t = Services.Get<GuildTreasury>();
            string dangerText = danger.HasValue
                ? $"예상 위험도: {UIBuilder.Colored(Quest.DangerLabel(danger.Value), UITheme.Danger(danger.Value))}"
                : "예상 위험도: -";
            string afford = t.CanAfford(draft.RewardGold, draft.RewardExp)
                ? UIBuilder.Colored($"예치 후 잔액 {t.Gold - draft.RewardGold}G / {t.Exp - draft.RewardExp}EXP", UITheme.TextMuted)
                : UIBuilder.Colored("길드 자금이 부족합니다", UITheme.Bad);
            _danger.text = dangerText + "\n" + afford;
        }

        DangerLevel? EstimateDanger(QuestDraft d)
        {
            switch (d.Type)
            {
                case QuestType.Hunt when d.Species != null:
                    return d.Count >= 8 && d.Species.danger < DangerLevel.High ? d.Species.danger + 1 : d.Species.danger;
                case QuestType.ClearNest when d.Nest != null:
                    return QuestBoard.NestDanger(d.Nest);
                case QuestType.Explore when _pickedPoint.HasValue:
                    return QuestBoard.ExploreDanger(d.Point, d.Radius);
            }
            return null;
        }

        void PickPoint()
        {
            var picker = Services.Get<MapPicker>();
            Close();
            picker.Begin("탐사할 지점을 클릭하세요 (우클릭/ESC: 취소)", p =>
            {
                _pickedPoint = p;
                Open();
            }, Open);
        }

        void PickNest()
        {
            var picker = Services.Get<MapPicker>();
            Close();
            picker.Begin("소탕할 둥지를 클릭하세요 (우클릭/ESC: 취소)", p =>
            {
                Open();
                var nest = _nestOptions.OrderBy(n => Vector2.Distance(n.WorldPosition, p)).FirstOrDefault();
                if (nest != null && Vector2.Distance(nest.WorldPosition, p) < 3f)
                {
                    _nest.SetValueWithoutNotify(_nestOptions.IndexOf(nest));
                    OnFormChanged(false);
                }
                else _message.text = UIBuilder.Colored("그 위치에 발견된 둥지가 없습니다.", UITheme.Bad);
            }, Open);
        }

        void Submit()
        {
            var board = Services.Get<QuestBoard>();
            if (CurrentType == QuestType.Explore && !_pickedPoint.HasValue)
            {
                _message.text = UIBuilder.Colored("탐사 지점을 지도에서 선택하세요.", UITheme.Bad);
                return;
            }
            if (board.TryPost(BuildDraft(), out var quest, out var error))
            {
                _message.text = UIBuilder.Colored($"'{quest.Title}' 의뢰가 등록되었습니다.", UITheme.Good);
                _pickedPoint = null;
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
                row._cancel = UIBuilder.Button(bg.transform, "취소", row.OnCancel, 60, 30, UITheme.ButtonDanger, 14);
                return row;
            }

            public void Bind(Quest q, int index)
            {
                _quest = q;
                _bg.color = index % 2 == 0 ? UITheme.RowBg : UITheme.RowBgAlt;
                string status = q.Status switch
                {
                    QuestStatus.Open => UIBuilder.Colored("모집 중", UITheme.Accent),
                    QuestStatus.InProgress => UIBuilder.Colored("진행 중", UITheme.Exp),
                    QuestStatus.Completed => UIBuilder.Colored("보고 대기", UITheme.Good),
                    _ => q.Status.ToString(),
                };
                _title.text = $"[{q.Id}] {q.Title}  {status}  {UIBuilder.Colored("위험 " + Quest.DangerLabel(q.Danger), UITheme.Danger(q.Danger))}";
                string members = q.Assignees.Count == 0 ? "없음" : string.Join(", ", q.Assignees.Select(a => a.TakerName));
                _detail.text = $"진행 {q.ProgressText} · 보상 {q.RewardGold}G / {q.RewardExp}EXP · 인원 {q.Assignees.Count}/{q.MaxParty} ({members})";
                _cancel.interactable = q.IsActive;
            }

            void OnCancel()
            {
                if (_quest != null) Services.Get<QuestBoard>().Cancel(_quest);
            }
        }
    }
}
