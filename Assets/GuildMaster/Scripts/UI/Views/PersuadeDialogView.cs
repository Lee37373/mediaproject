using System;
using System.Collections.Generic;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.Persuasion;
using GuildMaster.Quests;
using GuildMaster.Traits;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Quest persuasion and negotiation window (P4-24, P4-25). Opened from the quest board once a quest has stayed
    /// untaken for <see cref="PersuasionRules.OpenAfterHours"/> game hours. Left: the free adventurers in town; right:
    /// the talk with the selected one (LLM replies arrive asynchronously, rule-based when the model is off) and the
    /// gold negotiation: raise the reward with a quick offer, or take / turn down the adventurer's counter-offer.
    /// The game clock is held while a talk is ongoing and released as soon as it ends, so the party
    /// invitation that follows an acceptance is not stalled by an open window.
    /// Closing the window keeps every talk; reopening resumes it.
    /// </summary>
    public class PersuadeDialogView : UIPanel
    {
        class Driver : MonoBehaviour
        {
            public PersuadeDialogView Owner;
            void Update() { if (Owner != null) Owner.DriverTick(); }
        }

        public override string Title => "의뢰 협상";

        static readonly int[] OfferPercents = { 10, 25, 50 };
        protected override float RefreshInterval => 0.25f;

        RectTransform _listContent;
        readonly List<CandidateRow> _rows = new();
        Text _emptyLabel;
        Text _questInfo, _profile, _willLabel, _status, _chat;
        Slider _will;
        InputField _input;
        Button _send, _propose;
        ScrollRect _chatScroll;
        Text _offerLabel, _counterLabel;
        RectTransform _counterRow;
        Button _counterAccept, _counterDecline;
        readonly Button[] _offerButtons = new Button[OfferPercents.Length];

        Quest _quest;
        Adventurer _target;
        PersuasionSession _session;
        int _shownTurns = -1;
        bool _holdingClock;

        protected override void Build(RectTransform body)
        {
            var root = body.GetComponentInParent<Canvas>().rootCanvas.gameObject;
            root.AddComponent<Driver>().Owner = this;

            UIBuilder.HLayout(body, 10, 0, expandWidth: false);

            // ---- Left: who to talk to ----
            var left = UIBuilder.Panel(body, "Candidates", UITheme.PanelBgLight);
            left.Size(290, -1, 0, 1);
            UIBuilder.VLayout(left, 6, 8);
            UIBuilder.Label(left.transform, "설득할 모험가", 17, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 26);
            _listContent = UIBuilder.ScrollList(left.transform, out var listScroll, 4);
            listScroll.GetComponent<RectTransform>().Size(-1, -1, 1, 1);
            _emptyLabel = UIBuilder.Label(_listContent, "지금 설득할 수 있는 모험가가 없습니다.", 15, TextAnchor.MiddleCenter, UITheme.TextMuted).Size(-1, 60);

            // ---- Right: quest, adventurer, talk ----
            var right = UIBuilder.Rect(body, "Talk");
            UIBuilder.VLayout(right, 6, 0);
            right.Size(-1, -1, 1, 1);

            var info = UIBuilder.Panel(right, "Info", UITheme.PanelBgLight);
            info.Size(-1, 140, -1, 0);
            UIBuilder.HLayout(info, 12, 10);
            _questInfo = UIBuilder.Label(info.transform, "", 15, TextAnchor.UpperLeft).Size(-1, -1, 1, 1);
            _questInfo.verticalOverflow = VerticalWrapMode.Overflow;
            _profile = UIBuilder.Label(info.transform, "", 15, TextAnchor.UpperLeft).Size(-1, -1, 1, 1);
            _profile.verticalOverflow = VerticalWrapMode.Overflow;

            _willLabel = UIBuilder.Label(right, "", 15).Size(-1, 24);
            _will = UIBuilder.Slider(right, 0, 100, 0, null);
            _will.interactable = false;

            var content = UIBuilder.ScrollList(right, out _chatScroll, 0);
            _chatScroll.GetComponent<RectTransform>().Size(-1, -1, 1, 1);
            _chat = UIBuilder.Label(content, "", 16, TextAnchor.UpperLeft);
            _chat.horizontalOverflow = HorizontalWrapMode.Wrap;
            _chat.verticalOverflow = VerticalWrapMode.Overflow;

            _status = UIBuilder.Label(right, "", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 26);

            // ---- Gold negotiation (P4-25): the adventurer's asking price, then quick raises ----
            _counterRow = UIBuilder.Row(right, 36);
            _counterLabel = UIBuilder.Label(_counterRow, "", 15, TextAnchor.MiddleLeft, UITheme.Warn).Size(-1, 34, 1, 0);
            _counterAccept = UIBuilder.Button(_counterRow, "역제안 수락", OnAcceptCounter, 120, 34, UITheme.Header);
            _counterAccept.Tooltip(() => "모험가가 부른 금액만큼 보상을 올리고 바로 계약합니다. 금액은 계약이 성사될 때 길드 자금에서 나갑니다.");
            _counterDecline = UIBuilder.Button(_counterRow, "거절", OnDeclineCounter, 70, 34, UITheme.ButtonDanger);
            _counterRow.gameObject.SetActive(false);

            var offerRow = UIBuilder.Row(right, 36);
            _offerLabel = UIBuilder.Label(offerRow, "", 15, TextAnchor.MiddleLeft).Size(-1, 34, 1, 0);
            for (int i = 0; i < OfferPercents.Length; i++)
            {
                int percent = OfferPercents[i];
                _offerButtons[i] = UIBuilder.Button(offerRow, $"+{percent}%", () => OfferPercent(percent), 118, 34, UITheme.Header, 15);
                _offerButtons[i].Tooltip(() => _session == null ? "" :
                    $"보상을 {NegotiationRules.PercentOffer(_session.BaseRewardGold, percent)}G 더 얹겠다고 제안합니다. 모험가가 받아들이면 계약이 성사되고, 그때 길드 자금에서 지급됩니다.");
            }

            var inputRow = UIBuilder.Row(right, 36);
            _input = UIBuilder.Input(inputRow, "모험가를 설득할 말을 입력하세요 (Enter: 보내기)", height: 34);
            _input.characterLimit = 120;
            // Same Enter handling as the recruitment dialogue: end of edit = Enter unless focus was
            // lost to a click or ESC (the Korean IME swallows Enter while composing).
            _input.onEndEdit.AddListener(_ =>
            {
                var kb = Keyboard.current;
                var mouse = Mouse.current;
                bool clickedAway = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed);
                bool cancelled = kb != null && kb.escapeKey.isPressed;
                if (clickedAway || cancelled || !Visible) return;
                Send(false);
            });
            _send = UIBuilder.Button(inputRow, "보내기", () => Send(false), 90, 34);

            var actions = UIBuilder.Row(right, 36);
            _propose = UIBuilder.Button(actions, "정식으로 부탁하기", () => Send(true), 170, 34, UITheme.Header);
            _propose.Tooltip(() => "마지막 부탁입니다. 설득도가 수락 기준을 넘었으면 맡아 주고, 아니면 거절할 수 있습니다.");
            UIBuilder.Button(actions, "대화 끝내기", Close, 120, 34, UITheme.ButtonDanger);
        }

        /// <summary>Opens the window for an untaken quest and selects the adventurer most likely to say yes.</summary>
        public void Begin(Quest quest)
        {
            if (quest == null || !Services.TryGet<PersuasionService>(out var svc)) return;
            _quest = quest;
            var list = svc.Candidates(quest);
            Adventurer pick = null;
            foreach (var a in list)
            {
                var existing = svc.FindSession(quest, a);
                if (existing == null || existing.Outcome == PersuadeOutcome.Ongoing) { pick = a; break; }
            }
            if (pick == null && list.Count > 0) pick = list[0];
            BindTarget(pick);
            Open();
            if (_session != null) _input.ActivateInputField();
        }

        /// <summary>
        /// The window's own GameObject is inactive while it is closed, so the clock hold and the
        /// cleanup on closing run from an always-active driver on the root canvas.
        /// </summary>
        void DriverTick()
        {
            bool hold = Visible && _session != null && _session.Outcome == PersuadeOutcome.Ongoing;
            if (hold != _holdingClock && Services.TryGet<GameClock>(out var clock))
            {
                if (hold) clock.Hold(); else clock.Release();
                _holdingClock = hold;
            }
            // Closed (X, ESC or button): the talks stay in the service; only the view forgets them.
            if (!Visible && (_session != null || _quest != null))
            {
                _session = null;
                _target = null;
                _quest = null;
            }
        }

        void SelectTarget(Adventurer a)
        {
            BindTarget(a);
            Refresh();
            if (_session != null) _input.ActivateInputField();
        }

        void BindTarget(Adventurer a)
        {
            _target = a;
            _session = a != null && _quest != null && Services.TryGet<PersuasionService>(out var svc) ? svc.GetSession(_quest, a) : null;
            _shownTurns = -1;
            if (_input != null) _input.text = "";
        }

        async void Send(bool proposal)
        {
            var session = _session;
            if (session == null || session.Busy || session.Outcome != PersuadeOutcome.Ongoing) return;
            string text = _input.text;
            if (!proposal && string.IsNullOrWhiteSpace(text)) return;
            _input.text = "";
            var task = session.SendAsync(proposal && string.IsNullOrWhiteSpace(text) ? null : text, proposal);
            Refresh();
            _input.ActivateInputField();   // keep typing while the adventurer thinks
            await task;
            if (session == _session && Visible) { Refresh(); _input.ActivateInputField(); }
        }

        void OfferPercent(int percent)
        {
            var session = _session;
            if (session == null) return;
            session.Offer(NegotiationRules.PercentOffer(session.BaseRewardGold, percent));
            Refresh();
        }

        void OnAcceptCounter()
        {
            _session?.AcceptCounter();
            Refresh();
        }

        void OnDeclineCounter()
        {
            _session?.DeclineCounter();
            Refresh();
        }

        protected override void Refresh()
        {
            if (_quest == null || !Services.TryGet<PersuasionService>(out var svc)) return;

            // ---- candidate list ----
            var list = svc.Candidates(_quest);
            if (_target != null && _session != null && !list.Contains(_target)) list.Insert(0, _target);
            if (_target == null && list.Count > 0) BindTarget(list[0]);
            EnsureRows(_listContent, _rows, list.Count, parent => CandidateRow.Create(parent, SelectTarget));
            for (int i = 0; i < list.Count; i++) _rows[i].Bind(list[i], svc.FindSession(_quest, list[i]), list[i] == _target, _quest);
            _emptyLabel.gameObject.SetActive(list.Count == 0);
            _emptyLabel.transform.SetAsLastSibling();

            // ---- quest info ----
            float since = Services.TryGet<GameClock>(out var clock) ? clock.TotalHours - _quest.PostedAtHour : 0f;
            _questInfo.text =
                $"{UIBuilder.Colored($"[{_quest.Id}] {_quest.Title}", UITheme.Accent)}\n" +
                $"위험 {UIBuilder.Colored(Quest.DangerLabel(_quest.Danger), UITheme.Danger(_quest.Danger))}\n" +
                $"보상 {_quest.RewardGold}G{(_session != null && _session.OfferedGold > 0 ? " " + UIBuilder.Colored($"(+{_session.OfferedGold}G 제안 중)", UITheme.Warn) : "")} / {_quest.RewardExp}EXP · 최대 {_quest.MaxParty}명\n" +
                UIBuilder.Colored($"등록 후 {since:0.#}시간째 맡는 사람이 없습니다.", UITheme.TextMuted);

            var who = _target;
            if (_session == null || who == null || who.Data == null)
            {
                _profile.text = "";
                _willLabel.text = "";
                _will.value = 0;
                _status.text = list.Count == 0 ? UIBuilder.Colored("마을에 설득할 수 있는 모험가가 없습니다.", UITheme.Bad) : "왼쪽에서 모험가를 고르세요.";
                _chat.text = "";
                _send.interactable = false;
                _propose.interactable = false;
                _input.interactable = false;
                _counterRow.gameObject.SetActive(false);
                _offerLabel.text = "";
                foreach (var b in _offerButtons) b.interactable = false;
                return;
            }

            // ---- adventurer profile ----
            var d = who.Data;
            string traits = who.Traits != null ? TraitSet.Format(who.Traits.Traits, UITheme.Good, UITheme.Bad) : "-";
            _profile.text =
                $"{UIBuilder.Colored(d.name, UITheme.Accent)}  {AdventurerData.ClassLabel(d.cls)} Lv.{d.level}\n" +
                $"성격 {d.mbti} · 가치관 {UIBuilder.Colored(d.values, UITheme.Accent)}\n" +
                $"특성: {traits}\n" +
                $"체력 {(int)(who.HpRatio * 100f)}% · 소지금 {d.gold}G\n" +
                $"이 의뢰는 {RiskColored(_session.RiskLabel)} 수준";

            // ---- willingness ----
            _will.value = _session.Willingness;
            _willLabel.text = $"설득도 {_session.Willingness}/100 (수락 기준 {_session.Threshold}) · 대화 {_session.PlayerTurns}/{PersuasionRules.MaxPlayerTurns}";

            // ---- status line and controls ----
            bool questOpen = _quest.Status == QuestStatus.Open && _quest.HasFreeSlot;
            bool ongoing = _session.Outcome == PersuadeOutcome.Ongoing && questOpen;
            _status.text = StatusText(_session, questOpen);
            _send.interactable = ongoing && !_session.Busy;
            _propose.interactable = ongoing && !_session.Busy && _session.PlayerTurns > 0;
            _input.interactable = ongoing;

            // ---- gold negotiation ----
            int gold = Services.TryGet<Economy.GuildTreasury>(out var treasury) ? treasury.Gold : 0;
            bool canOffer = ongoing && _session.CanOffer;
            _offerLabel.text = $"보상 협상 · 제안 +{_session.OfferedGold}G / 한도 +{NegotiationRules.RaiseCap(_session.BaseRewardGold)}G · 길드 자금 {gold}G";
            for (int i = 0; i < _offerButtons.Length; i++)
            {
                int amount = NegotiationRules.PercentOffer(_session.BaseRewardGold, OfferPercents[i]);
                _offerButtons[i].SetLabel($"+{amount}G");
                _offerButtons[i].interactable = canOffer && amount <= _session.MaxOffer;
            }
            bool hasCounter = ongoing && _session.Counter > 0;
            _counterRow.gameObject.SetActive(hasCounter);
            if (hasCounter)
            {
                _counterLabel.text = $"{d.name}의 역제안: +{_session.Counter}G를 더 주면 맡겠다";
                _counterAccept.interactable = !_session.Busy && _session.Counter <= _session.MaxOffer;
            }

            // ---- transcript ----
            int count = _session.Turns.Count;
            if (count == _shownTurns) return;
            _shownTurns = count;
            var sb = new StringBuilder();
            if (count == 0)
                sb.Append(UIBuilder.Colored($"{d.name}에게 '{_quest.Title}' 의뢰를 맡아 달라고 설득해 보세요.\n보상, 함께 갈 동료, 안전 대책, 그리고 그의 가치관에 맞는 이야기가 도움이 됩니다.\n아래 '보상 협상' 버튼으로 골드를 더 얹겠다고 제안할 수도 있고, 마음이 기울면 모험가가 직접 금액을 부르기도 합니다.", UITheme.TextMuted));
            foreach (var t in _session.Turns)
            {
                string speaker = t.FromPlayer ? UIBuilder.Colored("길드 마스터", UITheme.Accent) : UIBuilder.Colored(d.name, new Color(0.65f, 0.85f, 1f));
                sb.Append(speaker).Append(": ").Append(t.Text.Replace("<", "＜")).Append("\n\n");
            }
            _chat.text = sb.ToString();
            Canvas.ForceUpdateCanvases();
            _chatScroll.verticalNormalizedPosition = 0f;
        }

        static string StatusText(PersuasionSession s, bool questOpen)
        {
            string name = s.Target != null && s.Target.Data != null ? s.Target.Data.name : "모험가";
            switch (s.Outcome)
            {
                case PersuadeOutcome.Accepted:
                    return s.OfferedGold > 0
                        ? UIBuilder.Colored($"{name}이(가) 보상 +{s.OfferedGold}G 조건으로 의뢰를 맡았습니다!", UITheme.Good)
                        : UIBuilder.Colored($"{name}이(가) 설득에 넘어가 의뢰를 맡았습니다!", UITheme.Good);
                case PersuadeOutcome.NoFunds: return UIBuilder.Colored($"길드 자금이 부족해 {name}과(와)의 계약이 무산되었습니다.", UITheme.Bad);
                case PersuadeOutcome.Refused: return UIBuilder.Colored($"{name}이(가) 끝내 거절했습니다. 다른 모험가를 찾아보세요.", UITheme.Bad);
                case PersuadeOutcome.Unavailable: return UIBuilder.Colored($"{name}은(는) 지금 이 의뢰를 맡을 수 없습니다.", UITheme.Bad);
            }
            if (!questOpen) return UIBuilder.Colored("이 의뢰는 더 이상 모집 중이 아닙니다.", UITheme.Bad);
            if (s.Busy) return "생각 중…";
            if (s.PlayerTurns == 0) return "먼저 말을 걸어 보세요.";
            if (!s.AcceptAllowed) return UIBuilder.Colored(s.RefusalReason, UITheme.Warn);
            if (s.Willingness >= s.Threshold) return UIBuilder.Colored("마음이 기운 것 같습니다 — '정식으로 부탁하기'를 눌러 보세요.", UITheme.Good);
            if (s.LastDecision == PersuadeDecision.Accept) return UIBuilder.Colored("거의 넘어왔습니다. 한두 마디만 더 설득해 보세요.", UITheme.Warn);
            return s.LastReplyFromLlm ? "" : "(규칙 기반 응답)";
        }

        static string RiskColored(string label)
        {
            var color = label == QuestRisk.Reckless ? UITheme.Bad : label == QuestRisk.Risky ? UITheme.Warn : UITheme.Good;
            return UIBuilder.Colored(label, color);
        }

        /// <summary>One adventurer in the candidate list.</summary>
        class CandidateRow : MonoBehaviour
        {
            Button _button;
            Text _text;
            Adventurer _who;
            Action<Adventurer> _onClick;

            public static CandidateRow Create(Transform parent, Action<Adventurer> onClick)
            {
                CandidateRow row = null;
                var button = UIBuilder.Button(parent, "", () => row._onClick?.Invoke(row._who), -1, 52, null, 14);
                row = button.gameObject.AddComponent<CandidateRow>();
                row._button = button;
                row._onClick = onClick;
                row._text = button.GetComponentInChildren<Text>();
                row._text.alignment = TextAnchor.MiddleLeft;
                return row;
            }

            public void Bind(Adventurer a, PersuasionSession session, bool selected, Quest quest)
            {
                _who = a;
                var colors = _button.colors;
                colors.normalColor = selected ? UITheme.Header : UITheme.Button;
                colors.selectedColor = colors.normalColor;
                _button.colors = colors;

                string state = session == null ? "대화 전" : session.Outcome switch
                {
                    PersuadeOutcome.Accepted => UIBuilder.Colored("수락", UITheme.Good),
                    PersuadeOutcome.Refused => UIBuilder.Colored("거절", UITheme.Bad),
                    PersuadeOutcome.Unavailable => UIBuilder.Colored("불가", UITheme.Bad),
                    PersuadeOutcome.NoFunds => UIBuilder.Colored("자금 부족", UITheme.Bad),
                    _ => session.PlayerTurns == 0 ? "대화 전" : $"설득 중 {session.Willingness}/{session.Threshold}",
                };
                string risk = session != null ? session.RiskLabel : DecisionPrompt.RiskLabel(a, quest);
                _text.text = $"{a.Data.name}  Lv.{a.Data.level} {AdventurerData.ClassLabel(a.Data.cls)}\n" +
                             $"{RiskColored(risk)} · {state}";
            }
        }
    }
}
