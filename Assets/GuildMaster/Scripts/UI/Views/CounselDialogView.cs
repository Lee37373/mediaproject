using System.Collections.Generic;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Welfare;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Counselling talk window (면담, user request 2026-10-01): the guild master talks a stressed member through what
    /// weighs on them instead of pressing a button. Opened from the care window's 면담 button. Stress moves line by
    /// line (<see cref="CounselScoring"/>) and every answer shows how the line landed. The game is paused while it is
    /// open, like the recruit talk; closing the window ends the talk.
    /// </summary>
    public class CounselDialogView : UIPanel
    {
        class Driver : MonoBehaviour
        {
            public CounselDialogView Owner;
            void Update() { if (Owner != null) Owner.DriverTick(); }
        }

        public override string Title => "면담";
        protected override float RefreshInterval => 0.25f;

        Text _name, _profile, _stressLabel, _concern, _status, _chat;
        Slider _stress;
        InputField _input;
        Button _send;
        /// <summary>The lines to pick from (user decision 2026-10-28: choices plus free typing).</summary>
        readonly List<Button> _choiceButtons = new();
        ScrollRect _chatScroll;

        CounselSession _session;
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
            _profile = UIBuilder.Label(left.transform, "", 15, TextAnchor.UpperLeft).Size(-1, 70);
            _profile.verticalOverflow = VerticalWrapMode.Overflow;
            _stressLabel = UIBuilder.Label(left.transform, "", 15).Size(-1, 24);
            _stress = UIBuilder.Slider(left.transform, 0, 100, 0, null);
            _stress.interactable = false;
            _concern = UIBuilder.Label(left.transform, "", 15, TextAnchor.UpperLeft).Size(-1, 112); // 90 → 112: failure count (2026-10-31)
            _concern.verticalOverflow = VerticalWrapMode.Overflow;
            _status = UIBuilder.Label(left.transform, "", 15, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 60);
            _status.verticalOverflow = VerticalWrapMode.Overflow;

            var right = UIBuilder.Rect(body, "Chat");
            UIBuilder.VLayout(right, 6, 0);
            right.Size(-1, -1, 1, 1);
            var content = UIBuilder.ScrollList(right, out _chatScroll, 0);
            _chatScroll.GetComponent<RectTransform>().Size(-1, -1, 1, 1);
            _chat = UIBuilder.Label(content, "", 16, TextAnchor.UpperLeft);
            _chat.horizontalOverflow = HorizontalWrapMode.Wrap;
            _chat.verticalOverflow = VerticalWrapMode.Overflow;

            // Lines written ahead, answered without the model (CounselBank); typing below still goes to the model.
            for (int i = 0; i < CounselBank.ChoiceCount; i++)
            {
                int index = i;
                var choice = UIBuilder.Button(right, "", () => Choose(index), -1, 32, null, 15);
                var text = choice.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                text.rectTransform.Stretch(12, 8, 0, 0);
                _choiceButtons.Add(choice);
            }

            var inputRow = UIBuilder.Row(right, 36);
            _input = UIBuilder.Input(inputRow, "직접 말하기 — 하고 싶은 말을 입력하세요 (Enter: 보내기)", height: 34);
            _input.characterLimit = 120;
            // Same Enter handling as the recruit window (the Korean IME consumes Enter while composing).
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
            UIBuilder.Button(actions, "면담 마치기", Close, 120, 34, UITheme.ButtonDanger);
        }

        /// <summary>Starts a talk with this member (the care window checks the cooldown first).</summary>
        public void Show(Adventurer member)
        {
            if (member == null || member.Data == null || !Services.TryGet<GuildWelfare>(out var welfare)) return;
            EndSession();
            _session = new CounselSession(member, welfare, Services.TryGet<GameClock>(out var c) ? c : null);
            if (!_holdingClock && Services.TryGet<GameClock>(out var clock)) { clock.Hold(); _holdingClock = true; }
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
            if (_session == null || _session.Busy || _session.Outcome != CounselOutcome.Ongoing) return;
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

        /// <summary>One of the offered lines, answered from the written lines (no waiting on the model).</summary>
        async void Choose(int index)
        {
            if (_session == null || _session.Busy || _session.Outcome != CounselOutcome.Ongoing) return;
            var offered = _session.Choices;
            if (index < 0 || index >= offered.Count) return;
            var session = _session;
            var task = session.SendChoiceAsync(offered[index]);
            Refresh();
            await task;
            if (session == _session && Visible) Refresh();
        }

        protected override void Refresh()
        {
            if (_session == null) return;
            var m = _session.Member;
            if (m != null && m.Data != null)
            {
                var d = m.Data;
                _name.text = d.name;
                _profile.text = $"{AdventurerData.ClassLabel(d.cls)} Lv.{d.level} · {d.mbti} · 가치관 {d.values}";
                var level = StressRules.LevelOf(d.stress);
                _stress.value = d.stress;
                int change = Mathf.RoundToInt(_session.StartStress - d.stress);
                string delta = change > 0 ? UIBuilder.Colored($" (-{change})", UITheme.Good) : change < 0 ? UIBuilder.Colored($" (+{-change})", UITheme.Bad) : "";
                _stressLabel.text = $"스트레스 {d.stress:0} {UIBuilder.Colored(StressRules.Label(level), MemberCareView.LevelColor(level))}{delta}";
            }
            _concern.text = _session.ConcernKnown
                ? "고민: " + UIBuilder.Colored(_session.Concern.Label, UITheme.Warn)
                : "고민: " + UIBuilder.Colored("???", UITheme.TextMuted) + "\n" +
                  UIBuilder.Colored("무엇이 힘든지 물어보고 들어 주세요. 진짜 고민을 짚으면 훨씬 잘 풀립니다.", UITheme.TextMuted);
            // 2026-10-31: kind lines can fall flat (stress up); three and the member walks out.
            _concern.text += "\n" + UIBuilder.Colored($"헛말 {_session.Fails}/{CounselOdds.FailLimit}", _session.Fails > 0 ? UITheme.Bad : UITheme.TextMuted)
                             + UIBuilder.Colored(" (와닿지 않으면 스트레스↑, 3번이면 자리를 뜸)", UITheme.TextMuted);

            bool ongoing = _session.Outcome == CounselOutcome.Ongoing;
            _status.text = _session.Outcome switch
            {
                CounselOutcome.Relieved => UIBuilder.Colored("마음이 한결 가벼워졌습니다.", UITheme.Good),
                CounselOutcome.Walkout => UIBuilder.Colored("마음이 상해 자리를 떴습니다.", UITheme.Bad),
                CounselOutcome.Ended => UIBuilder.Colored("면담이 끝났습니다.", UITheme.TextMuted),
                _ => _session.Busy ? "생각 중…" : _session.PlayerTurns == 0 ? "아래에서 할 말을 고르거나 직접 입력해 보세요."
                    : _session.LastReplyFromLlm || _session.LastReplyFromChoice ? "" : "(규칙 기반 응답" + (string.IsNullOrEmpty(_session.LastFallbackReason) ? "" : ": " + _session.LastFallbackReason) + ")",
            };
            _send.interactable = ongoing && !_session.Busy;
            var choices = _session.Choices;
            for (int i = 0; i < _choiceButtons.Count; i++)
            {
                bool show = ongoing && i < choices.Count;
                _choiceButtons[i].gameObject.SetActive(show);
                if (!show) continue;
                _choiceButtons[i].SetLabel($"{i + 1}. {choices[i].Text} {ChanceTag(choices[i])}");
                _choiceButtons[i].interactable = !_session.Busy;
            }
            _input.interactable = ongoing;

            int count = _session.Turns.Count + _session.Reactions.Count + _session.Notes.Count;
            if (count == _shown) return;
            _shown = count;
            var sb = new StringBuilder();
            string name = m != null && m.Data != null ? m.Data.name : "길드원";
            var reactions = _session.Reactions;
            var notes = _session.Notes;
            int shown = 0, react = 0, note = 0;
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
                for (; note < notes.Count && notes[note].afterTurn <= shown; note++)
                    sb.Append(UIBuilder.Colored("[털어놓음] " + notes[note].text, UITheme.Warn)).Append("\n\n");
            }
            _chat.text = sb.ToString();
            Canvas.ForceUpdateCanvases();
            _chatScroll.verticalNormalizedPosition = 0f;
        }

        /// <summary>The odds next to a choice, like the recruit talk: "(성공 65%)", "(성공 ??%)" before the worry is known, "(위험)" for a harsh line.</summary>
        string ChanceTag(CounselChoice c)
        {
            if (c.Kind == CounselChoiceKind.Tough) return UIBuilder.Colored("(위험)", UITheme.Bad);
            if (_session.ChanceHidden(c)) return UIBuilder.Colored("(성공 ??%)", UITheme.TextMuted);
            int chance = _session.ChanceOf(c);
            var color = chance >= 65 ? UITheme.Good : chance >= 40 ? UITheme.Warn : UITheme.Bad;
            return UIBuilder.Colored($"(성공 {chance}%)", color);
        }
    }
}
