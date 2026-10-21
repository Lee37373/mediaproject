using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Persuasion;
using GuildMaster.Player;
using GuildMaster.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Quest board. P4-82 (user decision 2026-10-17): the player no longer posts hunts — the town posts one on every
    /// den the guild has seen, other clients' requests come in as proposal cards, and the guild keeps a fee on both.
    /// Left: the quests on the board, with who posted them; the guild master takes down what the guild should not
    /// bother with. Right: hiring an escort, the one quest the guild pays for itself (gold escrowed).
    /// </summary>
    public class QuestBoardView : UIPanel
    {
        public override string Title => "의뢰 게시판";

        RectTransform _listContent;
        readonly List<QuestRow> _rows = new();
        Text _emptyLabel;

        InputField _gold, _hours;
        Dropdown _party;
        Text _summary;
        Text _message;

        protected override void Build(RectTransform body)
        {
            UIBuilder.HLayout(body, 12).childForceExpandHeight = true;

            // ---- Left: quests on the board ----
            var left = UIBuilder.Rect(body, "List").Size(-1, -1, 1);
            UIBuilder.VLayout(left, 6, 0).childControlHeight = true;
            var listHeader = UIBuilder.Row(left, 30, 8);
            UIBuilder.Label(listHeader, "게시된 의뢰", 17, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 26, 1);
            UIBuilder.Button(listHeader, "의뢰 요청 보기", OpenProposals, 150, 28, new Color(0.25f, 0.45f, 0.3f), 14)
                .Tooltip(() => "다른 의뢰인이 맡기려는 요청(게시판에 없는 둥지)을 보고, 마음에 드는 것을 받아 게시판에 올립니다.");
            _listContent = UIBuilder.ScrollList(left, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
            _emptyLabel = UIBuilder.Label(_listContent, "게시된 의뢰가 없습니다. 길드가 마을 밖에서 둥지를 발견하면 마을이 토벌 의뢰를 올립니다.",
                15, TextAnchor.MiddleCenter, UITheme.TextMuted).Size(-1, 40);
            _emptyLabel.horizontalOverflow = HorizontalWrapMode.Wrap;

            // ---- Right: hiring an escort ----
            var form = UIBuilder.Panel(body, "Form", UITheme.PanelBgLight).rectTransform.Size(400, -1, 0);
            UIBuilder.VLayout(form, 8, 12);
            UIBuilder.Label(form, "호위 고용", 17, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 26);
            var about = UIBuilder.Label(form, "길드 마스터가 마을 밖에 나갈 때 곁을 지킬 길드원을 고용합니다. 보상은 길드 금고에서 예치하고, 쓰지 않은 시간만큼 돌려받습니다.",
                14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 60);
            about.horizontalOverflow = HorizontalWrapMode.Wrap;

            _hours = UIBuilder.Input(FieldRow(form, "최대 시간"), "6", InputField.ContentType.IntegerNumber);
            _hours.text = "6";
            _hours.onValueChanged.AddListener(_ => UpdateSummary());
            _gold = UIBuilder.Input(FieldRow(form, "시간당 골드"), "0", InputField.ContentType.IntegerNumber);
            _gold.text = "10";
            _gold.onValueChanged.AddListener(_ => UpdateSummary());
            _party = UIBuilder.Dropdown(FieldRow(form, "최대 인원"), new[] { "1명", "2명", "3명", "4명" }, _ => UpdateSummary());
            _party.SetValueWithoutNotify(1);

            _summary = UIBuilder.Label(form, "", 15).Size(-1, 48);
            _summary.verticalOverflow = VerticalWrapMode.Overflow;
            UIBuilder.Button(form, "호위 고용 (보상 예치)", Submit, -1, 38, new Color(0.25f, 0.45f, 0.3f), 17);
            _message = UIBuilder.Label(form, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 40);
            _message.horizontalOverflow = HorizontalWrapMode.Wrap;
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

        protected override void OnOpened() => UpdateSummary();

        /// <summary>The escort as typed in: reward fields are hourly rates, the escrow covers the maximum hours (gold only, P4-82).</summary>
        QuestDraft BuildDraft()
        {
            int hours = Mathf.Clamp(ParseInt(_hours.text), 0, 24);
            var d = new QuestDraft
            {
                Type = QuestType.Escort,
                Hours = hours,
                RewardGold = ParseInt(_gold.text) * hours,
                MaxParty = _party.value + 1,
            };
            if (Services.TryGet<PlayerAvatar>(out var master)) d.Point = master.Position;
            return d;
        }

        static int ParseInt(string s) => int.TryParse(s, out var v) ? v : 0;

        void UpdateSummary()
        {
            var draft = BuildDraft();
            var t = Services.Get<GuildTreasury>();
            string afford = t.CanAfford(draft.RewardGold)
                ? UIBuilder.Colored($"예치 {draft.RewardGold}G · 예치 후 잔액 {t.Gold - draft.RewardGold}G", UITheme.TextMuted)
                : UIBuilder.Colored($"예치 {draft.RewardGold}G · 길드 자금이 부족합니다", UITheme.Bad);
            _summary.text = $"예상 위험도: {UIBuilder.Colored(Quest.DangerLabel(DangerLevel.Low), UITheme.Danger(DangerLevel.Low))} (마스터가 가는 곳에 따라 다름)\n{afford}";
        }

        void Submit()
        {
            var board = Services.Get<QuestBoard>();
            if (board.TryPost(BuildDraft(), out var quest, out var error))
            {
                _message.text = UIBuilder.Colored($"'{quest.Title}' 의뢰가 등록되었습니다. 호위대가 모이면 마을 밖으로 이동하세요.", UITheme.Good);
                UpdateSummary();
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
            UpdateSummary();
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
                row._persuade.Tooltip(() => $"의뢰가 {PersuasionRules.OpenAfterHours:0}시간 동안 아무도 맡지 않으면, 길드원을 직접 찾아가 설득하거나 길드 돈으로 보상을 더 얹는 협상을 할 수 있습니다.");
                row._cancel = UIBuilder.Button(bg.transform, "내리기", row.OnCancel, 64, 30, UITheme.ButtonDanger, 14);
                row._cancel.Tooltip(() => row._quest == null ? "" : row._quest.FundedByClient
                    ? "아무도 맡지 않은 의뢰를 게시판에서 내립니다. 마을은 이 둥지를 다시 올리지 않습니다(의뢰 요청으로 다시 받을 수 있습니다)."
                    : "호위를 끝냅니다. 아무도 맡지 않았으면 예치금을 돌려받고, 맡았으면 호위한 시간만큼 지급합니다.");
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
                // Party work its first taker is still gathering for in town (P4-97).
                Services.TryGet<Adventurers.PartyService>(out var invites);
                bool gathering = q.Status == QuestStatus.InProgress && invites != null && invites.IsRecruiting(q);
                string status = q.Status switch
                {
                    QuestStatus.Open => UIBuilder.Colored("모집 중", UITheme.Accent) + (canPersuade ? " " + UIBuilder.Colored("협상 가능", UITheme.Warn) : ""),
                    QuestStatus.InProgress when gathering => UIBuilder.Colored($"동료 모으는 중 · {Mathf.CeilToInt(invites.RecruitHoursLeft(q))}시간 남음", UITheme.Warn),
                    QuestStatus.InProgress => UIBuilder.Colored("진행 중", UITheme.Exp),
                    QuestStatus.Completed => UIBuilder.Colored("보고 대기", UITheme.Good),
                    _ => q.Status.ToString(),
                };
                // The level it asks of a member (P4-88): the ones far under it hold back from it.
                string level = q.Type == QuestType.Escort ? "" : UIBuilder.Colored($" · 권장 Lv.{Adventurers.QuestRisk.RecommendedLevel(q)}", UITheme.TextMuted);
                _title.text = $"[{q.Id}] {q.Title}  {status}  {UIBuilder.Colored("위험 " + Quest.DangerLabel(q.Danger), UITheme.Danger(q.Danger))}{level}";
                string members = q.Assignees.Count == 0 ? "없음" : string.Join(", ", q.Assignees.Select(a => a.TakerName));
                // P4-82: who pays, and the guild's fee (the reward shown is what the members share). The progress goes
                // last: with the den it names it is the longest part, and the line is cut where it runs out.
                string client = UIBuilder.Colored(q.ClientLabel, q.FundedByClient ? UITheme.Text : UITheme.Warn);
                string fee = q.FundedByClient ? $" · {UIBuilder.Colored($"수수료 {q.FeeGold}G", UITheme.Accent)}" : " (예치)";
                _detail.text = $"{client} · 보상 {q.RewardGold}G / {q.RewardExp}EXP{fee} · 인원 {q.Assignees.Count}/{q.MaxParty} ({members}) · 진행 {q.ProgressText}";
                // A client's quest comes down only while untaken, a disaster or the dragon never (QuestBoard.CanTakeDown); the guild's escort ends any time.
                bool canTakeDown = QuestBoard.CanTakeDown(q);
                _cancel.gameObject.SetActive(canTakeDown);
                _cancel.SetLabel(q.FundedByClient ? "내리기" : "취소");
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
