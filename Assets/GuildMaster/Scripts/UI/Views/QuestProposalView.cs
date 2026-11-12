using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Proposals;
using GuildMaster.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The quest proposal list (F-8): a few ready-made quests to choose from, with the LLM's names and pitches when
    /// it is on. P4-82: each is another client's request for a den not on the board; taking it posts it to the board
    /// as that client's quest (the client pays, the guild keeps its fee), where it is negotiated and taken like any
    /// other. Opened from the quest board and from the "새 의뢰 요청" notification.
    /// </summary>
    public class QuestProposalView : UIPanel
    {
        public override string Title => "의뢰 제안";
        protected override float RefreshInterval => 0.25f;

        Text _status, _empty, _message;
        Button _reroll;
        string _rerollReason;
        RectTransform _listContent;
        readonly List<ProposalCard> _cards = new();

        protected override void Build(RectTransform body)
        {
            UIBuilder.VLayout(body, 8, 0);
            var head = UIBuilder.Row(body, 36, 8);
            _status = UIBuilder.Label(head, "", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 30, 1);
            _reroll = UIBuilder.Button(head, "다시 뽑기", Reroll, 130, 32, UITheme.Header, 15);
            _reroll.Tooltip(() => _rerollReason);
            UIBuilder.Button(head, "게시판", OpenBoard, 110, 32, null, 15)
                .Tooltip(() => "의뢰 게시판을 엽니다. 마을이 올린 의뢰를 보고, 원하지 않는 의뢰를 내리거나 호위를 고용합니다.");

            _listContent = UIBuilder.ScrollList(body, out var scroll);
            scroll.GetComponent<RectTransform>().Size(-1, -1, -1, 1);
            _empty = UIBuilder.Label(_listContent, "", 15, TextAnchor.MiddleCenter, UITheme.TextMuted).Size(-1, 70);
            _empty.horizontalOverflow = HorizontalWrapMode.Wrap;

            _message = UIBuilder.Label(body, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 44);
            _message.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        protected override void OnOpened()
        {
            _message.text = "";
            if (Services.TryGet<QuestProposalService>(out var service)) service.Ensure();
        }

        protected override void Refresh()
        {
            if (!Services.TryGet<QuestProposalService>(out var service) || !Services.TryGet<GameClock>(out var clock))
            {
                _status.text = "";
                _empty.text = "의뢰 제안 기능을 쓸 수 없습니다.";
                return;
            }

            var plans = service.Current;
            EnsureRows(_listContent, _cards, plans.Count, parent => ProposalCard.Create(parent, this));
            for (int i = 0; i < plans.Count; i++) _cards[i].Bind(plans[i], i, service);

            _empty.gameObject.SetActive(plans.Count == 0);
            _empty.transform.SetAsLastSibling();
            _empty.text = service.Generating
                ? "제안을 만드는 중입니다…"
                : service.EmptyReason ?? "아직 제안이 없습니다. '다시 뽑기'를 눌러 보세요.";

            if (service.Generating) _status.text = "제안을 만드는 중… (LLM 응답을 기다립니다)";
            else if (service.GeneratedAtHour >= 0f)
            {
                float h = service.GeneratedAtHour;
                int day = Mathf.FloorToInt(h / 24f) + 1;
                int hour = Mathf.FloorToInt(h % 24f);
                _status.text = $"요청 {plans.Count}건 · 만든 방식 {(service.Source.Length > 0 ? service.Source : "-")} · {day}일 {hour:00}시 · 하루에 한 번 새로 도착합니다.";
            }
            else _status.text = "";

            _reroll.interactable = service.CanReroll(out _rerollReason);
        }

        void Reroll()
        {
            if (!Services.TryGet<QuestProposalService>(out var service)) return;
            if (!service.Reroll(out var message)) _message.text = UIBuilder.Colored(message, UITheme.Warn);
            else _message.text = "";
            Refresh();
        }

        void OpenBoard()
        {
            if (!Services.TryGet<QuestBoardView>(out var board)) return;
            if (!board.Visible) board.Open();
            else board.BringToFront();
        }

        void Choose(ProposalPlan plan)
        {
            if (!Services.TryGet<QuestProposalService>(out var service)) return;
            bool ok = service.Choose(plan, out var message);
            _message.text = UIBuilder.Colored(message, ok ? UITheme.Good : UITheme.Bad);
            Refresh();
        }

        void Skip(ProposalPlan plan)
        {
            if (!Services.TryGet<QuestProposalService>(out var service)) return;
            service.Dismiss(plan);
            _message.text = "";
            Refresh();
        }

        /// <summary>One proposed quest.</summary>
        class ProposalCard : MonoBehaviour
        {
            Image _bg;
            Text _title, _pitch, _detail;
            Button _choose;
            ProposalPlan _plan;
            QuestProposalView _owner;

            public static ProposalCard Create(Transform parent, QuestProposalView owner)
            {
                var bg = UIBuilder.Panel(parent, "ProposalCard", UITheme.RowBg);
                bg.Size(-1, 128);
                var card = bg.gameObject.AddComponent<ProposalCard>();
                card._bg = bg;
                card._owner = owner;
                var h = UIBuilder.HLayout(bg, 10, 8);
                h.padding = new RectOffset(12, 10, 8, 8);

                var texts = UIBuilder.Rect(bg.transform, "Texts").Size(-1, -1, 1);
                UIBuilder.VLayout(texts, 3, 0);
                card._title = UIBuilder.Label(texts, "", 17).Size(-1, 26);
                card._pitch = UIBuilder.Label(texts, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 38);
                card._pitch.horizontalOverflow = HorizontalWrapMode.Wrap;
                card._pitch.verticalOverflow = VerticalWrapMode.Truncate;
                card._detail = UIBuilder.Label(texts, "", 14, TextAnchor.UpperLeft).Size(-1, 40);
                card._detail.horizontalOverflow = HorizontalWrapMode.Wrap;
                card._detail.verticalOverflow = VerticalWrapMode.Truncate;

                var side = UIBuilder.Rect(bg.transform, "Side").Size(160, -1, 0);
                UIBuilder.VLayout(side, 8, 0);
                UIBuilder.Button(side, "넘기기", () => card._owner.Skip(card._plan), -1, 30, UITheme.ButtonDanger, 14)
                    .Tooltip(() => "이 요청을 목록에서 치웁니다.");
                card._choose = UIBuilder.Button(side, "의뢰 받기", () => card._owner.Choose(card._plan), -1, 44, new Color(0.25f, 0.45f, 0.3f), 16)
                    .Tooltip(() => "게시판에 올립니다. 보상은 의뢰인이 내고, 길드는 끝나면 수수료를 받습니다.");
                return card;
            }

            public void Bind(ProposalPlan p, int index, QuestProposalService service)
            {
                _plan = p;
                _bg.color = index % 2 == 0 ? UITheme.RowBg : UITheme.RowBgAlt;

                var styleColor = p.Style switch
                {
                    ProposalStyle.Safe => UITheme.Good,
                    ProposalStyle.Balanced => UITheme.Accent,
                    _ => UITheme.Warn,
                };
                string title = string.IsNullOrEmpty(p.Title) ? ProposalTexts.FallbackTitle(p) : p.Title;
                _title.text = $"{UIBuilder.Colored("[" + ProposalTexts.StyleLabel(p.Style) + "]", styleColor)} {title}  " +
                              UIBuilder.Colored("위험 " + Quest.DangerLabel(p.Danger), UITheme.Danger(p.Danger));
                _pitch.text = p.Pitch ?? "";

                var riskColor = p.Risk == QuestRisk.Reckless ? UITheme.Bad : p.Risk == QuestRisk.Risky ? UITheme.Warn : UITheme.Good;
                string what = ProposalTexts.WhatLine(p);
                string check = service.Check(p);
                // The item the patron pays besides the gold (P4-133).
                string item = string.IsNullOrEmpty(p.RewardItem.itemId) ? ""
                    : " + " + UIBuilder.Colored(Quests.ErrandRules.ItemCount(Quest.ItemName(p.RewardItem.itemId), p.RewardItem.count), UITheme.Exp);
                _detail.text = $"{what}\n길드 기준 {UIBuilder.Colored(p.Risk, riskColor)} · 추천 {p.Party}명 · 보상 {p.RewardGold}G(길드원 {p.MemberGold}G · " +
                               $"{UIBuilder.Colored($"수수료 {p.FeeGold}G", UITheme.Accent)}) / {p.RewardExp}EXP{item}" +
                               (check != null ? "  " + UIBuilder.Colored(check, UITheme.Bad) : "");
                _choose.interactable = check == null;
            }
        }
    }
}
