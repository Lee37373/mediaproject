using System.Collections.Generic;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Welfare;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Tribute talk window (상납금, user decision 2026-10-29): the guild master talks a member out of some of their
    /// gold — picked lines or typed ones, answered by the model (<see cref="TributeSession"/>). Shows how willing
    /// the member is, how sour, and what they offer; "받기" takes the offer. The game is paused while it is open,
    /// like the counselling talk. Opened from the care window.
    /// </summary>
    public class TributeDialogView : UIPanel
    {
        class Driver : MonoBehaviour
        {
            public TributeDialogView Owner;
            void Update() { if (Owner != null) Owner.DriverTick(); }
        }

        public override string Title => "상납금";
        protected override float RefreshInterval => 0.25f;

        Text _name, _profile, _purse, _willLabel, _sour, _offer, _status, _chat;
        Slider _will;
        InputField _input;
        Button _send, _accept;
        readonly List<Button> _choiceButtons = new();
        ScrollRect _chatScroll;

        TributeSession _session;
        int _shown = -1;
        bool _holdingClock;

        protected override void Build(RectTransform body)
        {
            var root = body.GetComponentInParent<Canvas>().rootCanvas.gameObject;
            root.AddComponent<Driver>().Owner = this;

            UIBuilder.HLayout(body, 10, 0, expandWidth: false);

            var left = UIBuilder.Panel(body, "Profile", UITheme.PanelBgLight);
            left.Size(300, -1, 0, 1);
            UIBuilder.VLayout(left, 6, 10);
            _name = UIBuilder.Label(left.transform, "", 20, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 30);
            _profile = UIBuilder.Label(left.transform, "", 15, TextAnchor.UpperLeft).Size(-1, 48);
            _profile.verticalOverflow = VerticalWrapMode.Overflow;
            _purse = UIBuilder.Label(left.transform, "", 15).Size(-1, 24);
            _willLabel = UIBuilder.Label(left.transform, "", 15).Size(-1, 24);
            _will = UIBuilder.Slider(left.transform, 0, 100, 0, null);
            _will.interactable = false;
            _sour = UIBuilder.Label(left.transform, "", 15).Size(-1, 24);
            _offer = UIBuilder.Label(left.transform, "", 17, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 30);
            _accept = UIBuilder.Button(left.transform, "받기", Accept, -1, 38, UITheme.Button, 16);
            _status = UIBuilder.Label(left.transform, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 110);
            _status.verticalOverflow = VerticalWrapMode.Overflow;

            var right = UIBuilder.Rect(body, "Chat");
            UIBuilder.VLayout(right, 6, 0);
            right.Size(-1, -1, 1, 1);
            var content = UIBuilder.ScrollList(right, out _chatScroll, 0);
            _chatScroll.GetComponent<RectTransform>().Size(-1, -1, 1, 1);
            _chat = UIBuilder.Label(content, "", 16, TextAnchor.UpperLeft);
            _chat.horizontalOverflow = HorizontalWrapMode.Wrap;
            _chat.verticalOverflow = VerticalWrapMode.Overflow;

            for (int i = 0; i < TributeLines.ChoiceCount; i++)
            {
                int index = i;
                var choice = UIBuilder.Button(right, "", () => Choose(index), -1, 32, null, 15);
                var text = choice.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                text.rectTransform.Stretch(12, 8, 0, 0);
                _choiceButtons.Add(choice);
            }

            var inputRow = UIBuilder.Row(right, 36);
            _input = UIBuilder.Input(inputRow, "직접 말하기 — 돈을 내라고 설득해 보세요 (Enter: 보내기)", height: 34);
            _input.characterLimit = 120;
            // Same Enter handling as the counselling window (the Korean IME consumes Enter while composing).
            _input.onEndEdit.AddListener(_ =>
            {
                var kb = Keyboard.current;
                var mouse = Mouse.current;
                bool clickedAway = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed);
                bool cancelled = kb != null && kb.escapeKey.isPressed;
                if (clickedAway || cancelled || !Visible) return;
                Send();
            });
            _send = UIBuilder.Button(inputRow, "보내기", Send, 90, 34);

            var actions = UIBuilder.Row(right, 36);
            UIBuilder.Button(actions, "그만두기", Close, 120, 34, UITheme.ButtonDanger);
        }

        /// <summary>Starts a talk with this member (the care window checks <see cref="TributeService.CanAsk"/> first).</summary>
        public void Show(Adventurer member)
        {
            if (member == null || member.Data == null || !Services.TryGet<TributeService>(out var service)) return;
            EndSession();
            Services.TryGet<GameClock>(out var clock);
            Services.TryGet<GuildTreasury>(out var treasury);
            Services.TryGet<GuildWelfare>(out var welfare);
            _session = new TributeSession(member, service, treasury, welfare, clock);
            if (!_holdingClock && clock != null) { clock.Hold(); _holdingClock = true; }
            _shown = -1;
            _input.text = "";
            Open();
            _input.ActivateInputField();
        }

        void DriverTick()
        {
            if (_session != null && !Visible) EndSession();
        }

        void EndSession()
        {
            _session?.Abort();
            _session = null;
            if (_holdingClock && Services.TryGet<GameClock>(out var clock)) clock.Release();
            _holdingClock = false;
        }

        async void Send()
        {
            if (_session == null || _session.Busy || !_session.Talk.CanTalk) return;
            string text = _input.text;
            if (string.IsNullOrWhiteSpace(text)) return;
            _input.text = "";
            var session = _session;
            var task = session.SendAsync(text);
            Refresh();
            _input.ActivateInputField();
            await task;
            if (session == _session && Visible) { Refresh(); _input.ActivateInputField(); }
        }

        async void Choose(int index)
        {
            if (_session == null || _session.Busy || !_session.Talk.CanTalk) return;
            var offered = _session.Choices;
            if (index < 0 || index >= offered.Count) return;
            var session = _session;
            var task = session.SendChoiceAsync(offered[index]);
            Refresh();
            await task;
            if (session == _session && Visible) Refresh();
        }

        void Accept()
        {
            if (_session == null) return;
            _session.Accept();
            Refresh();
        }

        static string TacticTag(TributeTactic t) => t switch
        {
            TributeTactic.Plead => "[사정]",
            TributeTactic.Reason => "[명분]",
            TributeTactic.Praise => "[칭찬]",
            TributeTactic.Bargain => "[보답]",
            TributeTactic.Pressure => "[협박]",
            _ => "",
        };

        protected override void Refresh()
        {
            if (_session == null) return;
            var talk = _session.Talk;
            var m = _session.Member;
            if (m != null && m.Data != null)
            {
                var d = m.Data;
                _name.text = d.name;
                _profile.text = $"{AdventurerData.ClassLabel(d.cls)} Lv.{d.level} · {d.mbti} · 가치관 {d.values}\n" +
                                UIBuilder.Colored($"스트레스 {d.stress:0}", MemberCareView.LevelColor(StressRules.LevelOf(d.stress)));
                _purse.text = $"소지금 {d.gold}G" + UIBuilder.Colored($" (최대 {talk.Pot}G까지 낼 수 있음)", UITheme.TextMuted);
            }
            _will.value = talk.Will;
            _willLabel.text = "낼 마음: " + UIBuilder.Colored(TributeRules.WillLabel(talk.Will), talk.Will >= 50 ? UITheme.Good : talk.Will >= 25 ? UITheme.Warn : UITheme.Bad);
            string sour = new string('●', Mathf.Min(talk.Sour, TributeRules.WalkoutSour)) + new string('○', Mathf.Max(0, TributeRules.WalkoutSour - talk.Sour));
            _sour.text = "원망 " + UIBuilder.Colored(sour, talk.Sour > 0 ? UITheme.Bad : UITheme.TextMuted)
                         + UIBuilder.Colored($"  · 대화 {talk.PlayerTurns}/{TributeRules.MaxTurns}", UITheme.TextMuted);

            bool ongoing = talk.Outcome == TributeOutcome.Ongoing;
            int offerGold = talk.OfferGold;
            _offer.text = talk.Outcome == TributeOutcome.Paid ? $"받은 돈: {_session.Taken}G"
                        : ongoing && offerGold > 0 ? $"제안: {offerGold}G" : "제안: 없음";
            _accept.SetLabel(offerGold > 0 && ongoing ? $"받기 ({offerGold}G)" : "받기");
            _accept.interactable = ongoing && offerGold > 0 && !_session.Busy;

            _status.text = talk.Outcome switch
            {
                TributeOutcome.Paid => UIBuilder.Colored($"{_session.Taken}G를 받아 길드 금고에 넣었습니다.", UITheme.Good),
                TributeOutcome.Walkout => UIBuilder.Colored("원망이 쌓여 자리를 박차고 나갔습니다. 한 푼도 받지 못했습니다.", UITheme.Bad),
                TributeOutcome.Ended => UIBuilder.Colored("대화를 끝냈습니다.", UITheme.TextMuted),
                _ => _session.Busy ? "생각 중…"
                    : talk.Final ? UIBuilder.Colored("더는 말을 들어 주지 않습니다. 제안을 받거나 그만두세요.", UITheme.Warn)
                    : talk.PlayerTurns == 0 ? "사정·명분·칭찬·보답 약속으로 마음을 움직이세요. 협박하면 빨리 뜯어내지만 스트레스와 원망이 쌓이고, 원망이 세 칸 차면 나가 버립니다."
                    : _session.LastReplyFromLlm ? "제안이 마음에 들면 언제든 '받기'를 누르세요."
                    : "(규칙 기반 응답" + (string.IsNullOrEmpty(_session.LastFallbackReason) ? "" : ": " + _session.LastFallbackReason) + ")",
            };

            bool canTalk = talk.CanTalk && !_session.Busy;
            _send.interactable = canTalk;
            _input.interactable = talk.CanTalk;
            var choices = _session.Choices;
            for (int i = 0; i < _choiceButtons.Count; i++)
            {
                bool show = talk.CanTalk && i < choices.Count;
                _choiceButtons[i].gameObject.SetActive(show);
                if (!show) continue;
                _choiceButtons[i].SetLabel($"{i + 1}. {TacticTag(choices[i].Tactic)} {choices[i].Text}");
                _choiceButtons[i].interactable = canTalk;
            }

            int count = _session.Turns.Count + _session.Reactions.Count;
            if (count == _shown) return;
            _shown = count;
            var sb = new StringBuilder();
            string name = m != null && m.Data != null ? m.Data.name : "길드원";
            var reactions = _session.Reactions;
            int shown = 0, react = 0;
            foreach (var t in _session.Turns)
            {
                string who = t.FromPlayer ? UIBuilder.Colored("길드 마스터", UITheme.Accent) : UIBuilder.Colored(name, new Color(0.65f, 0.85f, 1f));
                sb.Append(who).Append(": ").Append(t.Text.Replace("<", "＜")).Append("\n\n");
                shown++;
                for (; react < reactions.Count && reactions[react].afterTurn <= shown; react++)
                {
                    var r = reactions[react];
                    var color = r.delta > 0 ? UITheme.Good : r.delta < 0 ? UITheme.Bad : UITheme.TextMuted;
                    sb.Append(UIBuilder.Colored("   " + r.text, color)).Append("\n\n");
                }
            }
            _chat.text = sb.ToString();
            Canvas.ForceUpdateCanvases();
            _chatScroll.verticalNormalizedPosition = 0f;
        }
    }
}
