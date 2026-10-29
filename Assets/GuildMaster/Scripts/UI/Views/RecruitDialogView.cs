using System.Collections.Generic;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Player;
using GuildMaster.Recruitment;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Recruitment dialogue window (P3-3). <see cref="BeginApproach"/> walks the guild master to the
    /// visitor and opens the window within talking range; replies arrive asynchronously. The game is
    /// paused while the talk is open (user request) so the player can focus on it.
    /// Closing the window ends the talk without a verdict.
    /// </summary>
    public class RecruitDialogView : UIPanel
    {
        const float TalkRange = 3f;
        const float RepathSeconds = 1f;
        const float ApproachTimeoutSeconds = 30f;
        /// <summary>Extra stay (game hours) granted while the guild master walks over.</summary>
        const float WaitStayHours = 3f;

        float _approachStarted;

        class Driver : MonoBehaviour
        {
            public RecruitDialogView Owner;
            void Update() { if (Owner != null) Owner.DriverTick(); }
        }

        public override string Title => "영입 대화";
        protected override float RefreshInterval => 0.25f;

        Text _name, _profile, _help, _affinityLabel, _progress, _status, _chat;
        Slider _affinity;
        InputField _input;
        Button _send;
        /// <summary>The lines to pick from (user decision 2026-10-28: choices plus free typing).</summary>
        readonly List<Button> _choiceButtons = new();
        ScrollRect _chatScroll;

        Adventurer _approaching;
        float _repathTimer;
        RecruitmentSession _session;
        int _shownTurns = -1;
        bool _holdingClock;

        protected override void Build(RectTransform body)
        {
            var root = body.GetComponentInParent<Canvas>().rootCanvas.gameObject;
            root.AddComponent<Driver>().Owner = this;

            UIBuilder.HLayout(body, 10, 0, expandWidth: false);

            // Profile column.
            var left = UIBuilder.Panel(body, "Profile", UITheme.PanelBgLight);
            left.Size(300, -1, 0, 1);
            UIBuilder.VLayout(left, 6, 10);
            // Gauges on top and the long help text last, in the space that is left (user report 2026-10-29: the help
            // text ran over the affinity gauge).
            _name = UIBuilder.Label(left.transform, "", 20, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 30);
            _affinityLabel = UIBuilder.Label(left.transform, "", 15).Size(-1, 22);
            _affinity = UIBuilder.Slider(left.transform, 0, 100, RecruitmentSession.StartAffinity, null);
            _affinity.interactable = false;
            // Persuasion stages (user decision 2026-10-29, Bannerlord-style).
            _progress = UIBuilder.Label(left.transform, "", 15, TextAnchor.MiddleLeft, UITheme.Accent).Size(-1, 22);
            _profile = UIBuilder.Label(left.transform, "", 15, TextAnchor.UpperLeft).Size(-1, 66);
            _status = UIBuilder.Label(left.transform, "", 14, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 40);
            _help = UIBuilder.Label(left.transform, "", 13, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, -1, -1, 1);
            _help.horizontalOverflow = HorizontalWrapMode.Wrap;
            _help.verticalOverflow = VerticalWrapMode.Truncate;

            // Chat column.
            var right = UIBuilder.Rect(body, "Chat");
            UIBuilder.VLayout(right, 6, 0);
            right.Size(-1, -1, 1, 1);
            var content = UIBuilder.ScrollList(right, out _chatScroll, 0);
            _chatScroll.GetComponent<RectTransform>().Size(-1, -1, 1, 1);
            _chat = UIBuilder.Label(content, "", 16, TextAnchor.UpperLeft);
            _chat.horizontalOverflow = HorizontalWrapMode.Wrap;
            _chat.verticalOverflow = VerticalWrapMode.Overflow;

            // Lines written ahead, answered without the model (RecruitLines); typing below still goes to the model.
            for (int i = 0; i < RecruitLines.ChoiceCount; i++)
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
            // End of edit = Enter unless focus was lost to a click or ESC. (Checking enterKey.wasPressedThisFrame
            // missed Enter: the field handles it on a different frame, and the Korean IME consumes it while composing.)
            _input.onEndEdit.AddListener(_ =>
            {
                var kb = Keyboard.current;
                var mouse = Mouse.current;
                bool clickedAway = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed);
                bool cancelled = kb != null && kb.escapeKey.isPressed;
                if (clickedAway || cancelled || !Visible) return;
                Send();
            });
            _send = UIBuilder.Button(inputRow, "보내기", () => Send(), 90, 34);

            // No "정식 영입 제안" button any more (user request 2026-09-30): mashing it crept affinity up
            // until the visitor joined. Inviting is now something said in the talk, and a visitor who likes
            // the guild master enough joins on their own (RecruitmentSession.Judge).
            var actions = UIBuilder.Row(right, 36);
            UIBuilder.Button(actions, "대화 끝내기", Close, 120, 34, UITheme.ButtonDanger);
        }

        /// <summary>Walks the guild master to the visitor; the window opens within talking range.</summary>
        public void BeginApproach(Adventurer visitor)
        {
            if (visitor == null || !visitor.IsVisitor || !Services.TryGet<PlayerAvatar>(out var avatar)) return;
            // Said no once: never again (user decision 2026-10-29).
            if (visitor.Data != null && visitor.Data.recruitRefused)
            {
                visitor.Say("이미 거절했잖아요.", toFeed: false);
                return;
            }
            EndSession();
            CancelApproach();
            _approaching = visitor;
            _approachStarted = Time.unscaledTime;
            _repathTimer = 0f;
            // The visitor waits for the guild master instead of wandering off or leaving.
            visitor.InConversation = true;
            visitor.ConversationPartner = avatar.transform;
            visitor.VisitorLeaveAtHour = Mathf.Max(visitor.VisitorLeaveAtHour, Services.Get<GameClock>().TotalHours + WaitStayHours);
        }

        /// <summary>
        /// The panel's own GameObject is inactive while the window is closed, so approach and
        /// close detection run from an always-active driver on the root canvas.
        /// </summary>
        void DriverTick()
        {
            TickApproach();
            // Window closed (X, ESC or button) while talking: end without a verdict.
            if (_session != null && !Visible) EndSession();
        }

        void CancelApproach()
        {
            if (_approaching != null && _session == null)
            {
                _approaching.InConversation = false;
                _approaching.ConversationPartner = null;
            }
            _approaching = null;
        }

        void TickApproach()
        {
            if (_approaching == null) return;
            if (!_approaching.IsVisitor || !_approaching.IsAlive) { _approaching = null; return; }
            if (!Services.TryGet<PlayerAvatar>(out var avatar)) { CancelApproach(); return; }
            if (Time.unscaledTime - _approachStarted > ApproachTimeoutSeconds) { CancelApproach(); return; }
            float dist = Vector2.Distance(avatar.transform.position, _approaching.Position);
            if (dist <= TalkRange)
            {
                avatar.Mover.Stop();
                StartSession(_approaching, avatar.transform);
                _approaching = null;
                return;
            }
            _repathTimer -= Time.unscaledDeltaTime;
            if (_repathTimer > 0f) return;
            _repathTimer = RepathSeconds;
            avatar.MoveTo(_approaching.Position, null, TalkRange * 0.6f);
        }

        void StartSession(Adventurer visitor, Transform guildMaster)
        {
            _session = new RecruitmentSession(visitor, guildMaster);
            if (!_holdingClock) { Services.Get<GameClock>().Hold(); _holdingClock = true; }
            _shownTurns = -1;
            _input.text = "";
            Open();
            // In the visitor's own speech level, and "picking up where we left off" only when there was a talk before
            // (user report 2026-10-18).
            string greeting = _session.OpeningLine;
            _session.AddGreeting(greeting);          // shown in this window...
            visitor.Say(greeting, toFeed: false);    // ...and as a speech bubble, but not in the bottom-left feed
            _input.ActivateInputField();
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
            if (_session == null || _session.Busy || _session.Outcome != RecruitOutcome.Ongoing) return;
            string text = _input.text;
            if (string.IsNullOrWhiteSpace(text)) return;
            _input.text = "";
            var session = _session;
            var task = session.SendAsync(text);
            Refresh();
            _input.ActivateInputField();   // keep typing while the visitor thinks
            await task;
            if (session == _session && Visible) { Refresh(); _input.ActivateInputField(); }
        }

        /// <summary>
        /// "(성공 65%)" for an argument when the guild master can judge it, "(성공 ??%)" when not yet, "(질문)" for a line
        /// that only finds things out.
        /// </summary>
        string ChanceTag(RecruitChoice c)
        {
            if (c.Kind == RecruitChoiceKind.Greet) return UIBuilder.Colored("(인사)", UITheme.TextMuted);
            if (!RecruitLines.IsArgument(c.Kind)) return UIBuilder.Colored("(질문)", UITheme.TextMuted);
            if (!_session.ChanceKnown(c)) return UIBuilder.Colored("(성공 ??%)", UITheme.TextMuted);
            int p = _session.ChanceOf(c).Success;
            var color = p >= 65 ? UITheme.Good : p >= 40 ? UITheme.Warn : UITheme.Bad;
            return UIBuilder.Colored($"(성공 {p}%)", color);
        }

        /// <summary>One of the offered lines, answered from the written lines (no waiting on the model).</summary>
        async void Choose(int index)
        {
            if (_session == null || _session.Busy || _session.Outcome != RecruitOutcome.Ongoing) return;
            var offered = _session.Choices;
            if (index < 0 || index >= offered.Count) return;
            var session = _session;
            var task = session.SendChoiceAsync(offered[index]);
            Refresh();
            await task;
            if (session == _session && Visible) Refresh();
        }

        /// <summary>
        /// "성격 ESFJ · 가치관 ???" as far as the guild master knows the visitor: revealed facts as they are,
        /// the rest as "???" so the player can see how much is left to find out. Members show everything.
        /// </summary>
        public static string KnownTemperament(Adventurer a)
        {
            var d = a.Data;
            bool all = !a.IsVisitor;
            string mbti = all || VisitorReveal.IsRevealed(d, VisitorReveal.Personality) ? d.mbti : HiddenMark;
            string values = all || VisitorReveal.IsRevealed(d, VisitorReveal.Values) ? d.values : HiddenMark;
            return $"성격 {mbti} · 가치관 {values}";
        }

        /// <summary>The traits likewise: revealed ones coloured (good/bad), the others "???".</summary>
        public static string KnownTraits(Adventurer a)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (a.Traits != null)
                foreach (var t in a.Traits.Traits)
                {
                    if (t == null) continue;
                    parts.Add(!a.IsVisitor || VisitorReveal.IsRevealed(a.Data, VisitorReveal.TraitKey(t.id))
                        ? UIBuilder.Colored(t.displayName, t.isPositive ? UITheme.Good : UITheme.Bad)
                        : HiddenMark);
                }
            return parts.Count == 0 ? "-" : string.Join(", ", parts);
        }

        static string HiddenMark => UIBuilder.Colored("???", UITheme.TextMuted);

        protected override void Refresh()
        {
            if (_session == null) return;
            var v = _session.Visitor;
            if (v != null && v.Data != null)
            {
                var d = v.Data;
                _name.text = d.name;
                // Personality (MBTI), values and traits start hidden (user request 2026-09-23) and come back
                // one by one as the talk brings them out (user request 2026-09-29, see VisitorReveal).
                // How to find out what is still hidden, right under it (user report 2026-10-07: when things came out
                // was unclear). The rules are VisitorReveal's.
                var how = new StringBuilder();
                if (!VisitorReveal.IsRevealed(d, VisitorReveal.Personality))
                    how.Append("\n· 성격: ").Append(VisitorReveal.HowToFind(VisitorReveal.Personality));
                if (!VisitorReveal.IsRevealed(d, VisitorReveal.Values))
                    how.Append("\n· 가치관: ").Append(VisitorReveal.HowToFind(VisitorReveal.Values));
                if (VisitorReveal.NextHiddenTrait(d) != null)
                    how.Append("\n· 특성: ").Append(VisitorReveal.HowToFind("trait"));
                _profile.text = $"{AdventurerData.ClassLabel(d.cls)} Lv.{d.level}\n" + KnownTemperament(v) + "\n특성: " + KnownTraits(v);
                _help.text = (how.Length > 0 ? "알아내는 법" + how + "\n\n" : "") +
                             "설득: 한 단계에 3번 성공하면 다음 단계, 마지막 단계를 넘기면 합류. " +
                             "한 단계에서 3번 실패하거나 크게 실패하면 거절하고 다시는 영입할 수 없습니다. " +
                             "가치관·특성을 알아내면 성공 확률이 보입니다.";
            }
            _affinity.value = _session.Affinity;
            _affinityLabel.text = $"호감도 {_session.Affinity}/100 · 대화 {_session.PlayerTurns}회";
            _progress.text = RecruitPersuasion.Progress(_session.Stages, _session.Stage, _session.StageWins, _session.StageFails);

            bool ongoing = _session.Outcome == RecruitOutcome.Ongoing;
            _status.text = _session.Outcome switch
            {
                RecruitOutcome.Joined => UIBuilder.Colored("영입 성공! 길드에 합류했습니다.", UITheme.Good),
                RecruitOutcome.Left => UIBuilder.Colored("영입 실패. 거절하고 떠납니다. 다시는 영입할 수 없습니다.", UITheme.Bad),
                _ => _session.Busy ? $"생각 중… ({_session.BusySeconds:0}초)" : (_session.PlayerTurns == 0 ? "아래에서 할 말을 고르거나 직접 입력해 보세요."
                    : (_session.LastReplyFromLlm || _session.LastReplyFromChoice ? "" : "(규칙 기반 응답" + (string.IsNullOrEmpty(_session.LastFallbackReason) ? "" : ": " + _session.LastFallbackReason) + ")")),
            };
            _send.interactable = ongoing && !_session.Busy;
            var choices = _session.Choices;
            for (int i = 0; i < _choiceButtons.Count; i++)
            {
                bool show = ongoing && i < choices.Count;
                _choiceButtons[i].gameObject.SetActive(show);
                if (!show) continue;
                _choiceButtons[i].SetLabel($"{i + 1}. {choices[i].Text}  {ChanceTag(choices[i])}");
                _choiceButtons[i].interactable = !_session.Busy;
            }
            _input.interactable = ongoing;

            // Redraw when a line or a "found out" note arrives.
            int count = _session.Turns.Count + _session.Notes.Count + _session.Reactions.Count;
            if (count == _shownTurns) return;
            _shownTurns = count;
            var sb = new StringBuilder();
            string name = v != null && v.Data != null ? v.Data.name : "방문자";
            var notes = _session.Notes;
            var reactions = _session.Reactions;
            int shown = 0, note = 0, react = 0;
            foreach (var t in _session.Turns)
            {
                string who = t.FromPlayer ? UIBuilder.Colored("길드 마스터", UITheme.Accent) : UIBuilder.Colored(name, new Color(0.65f, 0.85f, 1f));
                sb.Append(who).Append(": ").Append(t.Text.Replace("<", "＜")).Append("\n\n");
                shown++;
                // How the player's line landed (RecruitScoring.Feedback): a direction, never the number.
                for (; react < reactions.Count && reactions[react].afterTurn <= shown; react++)
                {
                    var r = reactions[react];
                    var color = r.delta > 0 ? UITheme.Good : r.delta < 0 ? UITheme.Bad : UITheme.TextMuted;
                    sb.Append(UIBuilder.Colored("   " + r.text, color)).Append("\n\n");
                }
                // What this exchange gave away and why (VisitorReveal), or what was asked too early, right under it.
                for (; note < notes.Count && notes[note].afterTurn <= shown; note++)
                {
                    string text = notes[note].text;
                    var color = text.StartsWith(RecruitmentSession.RevealedTag) ? UITheme.Good
                              : text.StartsWith(RecruitmentSession.StageTag) ? UITheme.Accent : UITheme.TextMuted;
                    sb.Append(UIBuilder.Colored(text, color)).Append("\n\n");
                }
            }
            _chat.text = sb.ToString();
            Canvas.ForceUpdateCanvases();
            _chatScroll.verticalNormalizedPosition = 0f;
        }
    }
}
