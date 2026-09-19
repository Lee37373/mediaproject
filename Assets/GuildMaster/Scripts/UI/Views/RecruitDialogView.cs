using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Player;
using GuildMaster.Recruitment;
using GuildMaster.Traits;
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

        Text _name, _profile, _affinityLabel, _status, _chat;
        Slider _affinity;
        InputField _input;
        Button _send, _propose;
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
            _name = UIBuilder.Label(left.transform, "", 20, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 30);
            _profile = UIBuilder.Label(left.transform, "", 15, TextAnchor.UpperLeft).Size(-1, 150);
            _profile.verticalOverflow = VerticalWrapMode.Overflow;
            _affinityLabel = UIBuilder.Label(left.transform, "", 15).Size(-1, 24);
            _affinity = UIBuilder.Slider(left.transform, 0, 100, RecruitmentSession.StartAffinity, null);
            _affinity.interactable = false;
            _status = UIBuilder.Label(left.transform, "", 15, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 80);
            _status.verticalOverflow = VerticalWrapMode.Overflow;

            // Chat column.
            var right = UIBuilder.Rect(body, "Chat");
            UIBuilder.VLayout(right, 6, 0);
            right.Size(-1, -1, 1, 1);
            var content = UIBuilder.ScrollList(right, out _chatScroll, 0);
            _chatScroll.GetComponent<RectTransform>().Size(-1, -1, 1, 1);
            _chat = UIBuilder.Label(content, "", 16, TextAnchor.UpperLeft);
            _chat.horizontalOverflow = HorizontalWrapMode.Wrap;
            _chat.verticalOverflow = VerticalWrapMode.Overflow;

            var inputRow = UIBuilder.Row(right, 36);
            _input = UIBuilder.Input(inputRow, "방문자에게 할 말을 입력하세요 (Enter: 보내기)", height: 34);
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
                Send(false);
            });
            _send = UIBuilder.Button(inputRow, "보내기", () => Send(false), 90, 34);

            var actions = UIBuilder.Row(right, 36);
            _propose = UIBuilder.Button(actions, "정식 영입 제안", () => Send(true), 160, 34, UITheme.Header);
            UIBuilder.Button(actions, "대화 끝내기", Close, 120, 34, UITheme.ButtonDanger);
        }

        /// <summary>Walks the guild master to the visitor; the window opens within talking range.</summary>
        public void BeginApproach(Adventurer visitor)
        {
            if (visitor == null || !visitor.IsVisitor || !Services.TryGet<PlayerAvatar>(out var avatar)) return;
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
            visitor.Say(_session.Resumed ? "아까 하던 이야기 마저 할까요?" : "무슨 일이시죠?");
            _input.ActivateInputField();
        }

        void EndSession()
        {
            _session?.Abort();
            _session = null;
            if (_holdingClock && Services.TryGet<GameClock>(out var clock)) clock.Release();
            _holdingClock = false;
        }

        async void Send(bool proposal)
        {
            if (_session == null || _session.Busy || _session.Outcome != RecruitOutcome.Ongoing) return;
            string text = _input.text;
            if (!proposal && string.IsNullOrWhiteSpace(text)) return;
            _input.text = "";
            var session = _session;
            var task = session.SendAsync(proposal && string.IsNullOrWhiteSpace(text) ? null : text, proposal);
            Refresh();
            _input.ActivateInputField();   // keep typing while the visitor thinks
            await task;
            if (session == _session && Visible) { Refresh(); _input.ActivateInputField(); }
        }

        protected override void Refresh()
        {
            if (_session == null) return;
            var v = _session.Visitor;
            if (v != null && v.Data != null)
            {
                var d = v.Data;
                _name.text = d.name;
                string traits = v.Traits != null ? TraitSet.Format(v.Traits.Traits, UITheme.Good, UITheme.Bad) : "-";
                _profile.text = $"{AdventurerData.ClassLabel(d.cls)} Lv.{d.level}\n성격 {d.mbti}\n가치관 {UIBuilder.Colored(d.values, UITheme.Accent)}\n특성: {traits}\n" +
                                UIBuilder.Colored("가치관에 맞는 이야기를 하면 호감이 오릅니다.", UITheme.TextMuted);
            }
            _affinity.value = _session.Affinity;
            _affinityLabel.text = $"호감도 {_session.Affinity}/100 · 대화 {_session.PlayerTurns}/{RecruitmentSession.MaxPlayerTurns}";

            bool ongoing = _session.Outcome == RecruitOutcome.Ongoing;
            _status.text = _session.Outcome switch
            {
                RecruitOutcome.Joined => UIBuilder.Colored("영입 성공! 길드에 합류했습니다.", UITheme.Good),
                RecruitOutcome.Left => UIBuilder.Colored("영입 실패. 방문자가 떠납니다.", UITheme.Bad),
                _ => _session.Busy ? "생각 중…" : (_session.PlayerTurns == 0 ? "먼저 말을 걸어 보세요." : (_session.LastReplyFromLlm ? "" : "(규칙 기반 응답)")),
            };
            _send.interactable = ongoing && !_session.Busy;
            _propose.interactable = ongoing && !_session.Busy && _session.PlayerTurns > 0;
            _input.interactable = ongoing;

            int count = _session.Turns.Count;
            if (count == _shownTurns) return;
            _shownTurns = count;
            var sb = new StringBuilder();
            string name = v != null && v.Data != null ? v.Data.name : "방문자";
            foreach (var t in _session.Turns)
            {
                string who = t.FromPlayer ? UIBuilder.Colored("길드 마스터", UITheme.Accent) : UIBuilder.Colored(name, new Color(0.65f, 0.85f, 1f));
                sb.Append(who).Append(": ").Append(t.Text.Replace("<", "＜")).Append("\n\n");
            }
            _chat.text = sb.ToString();
            Canvas.ForceUpdateCanvases();
            _chatScroll.verticalNormalizedPosition = 0f;
        }
    }
}
