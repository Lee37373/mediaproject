using System.Collections.Generic;
using System.Threading.Tasks;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.LLM;
using GuildMaster.Traits;
using UnityEngine;

namespace GuildMaster.Recruitment
{
    public enum RecruitDecision { Undecided, Join, Refuse }
    public enum RecruitOutcome { Ongoing, Joined, Left }

    public struct RecruitTurn
    {
        public bool FromPlayer;
        public string Text;
    }

    public struct RecruitmentEndedEvent { public Adventurer Visitor; public RecruitOutcome Outcome; public int Turns; }

    /// <summary>
    /// One recruitment conversation between the guild master and a visitor: affinity, transcript,
    /// LLM (or rule-based) replies and the join/leave verdict. Asynchronous; the game keeps running.
    /// </summary>
    public class RecruitmentSession
    {
        public const int StartAffinity = 40;
        /// <summary>
        /// At or below this affinity a formal recruitment proposal cannot be made (user request 2026-09-23,
        /// replacing the old fixed 6-turn conversation limit: talk is now unlimited, but a visitor who has
        /// soured on the guild master won't hear a formal offer until affinity recovers).
        /// </summary>
        public const int MinProposalAffinity = 10;
        /// <summary>Affinity at which a visitor joins on their own, whatever the explicit decision. Also the
        /// threshold ambient town small talk (BanterService) auto-joins at, so a visitor who is simply
        /// well-liked by the guild can join without ever hearing a formal proposal (user request 2026-09-23).</summary>
        public const int AutoJoinAffinity = 85;
        /// <summary>Minimum remaining stay (game hours) while talking.</summary>
        const float TalkStayHours = 3f;

        readonly List<RecruitTurn> _turns = new();

        public Adventurer Visitor { get; }
        public int Affinity { get; private set; }
        public int PlayerTurns { get; private set; }
        public bool Busy { get; private set; }
        float _busyStart;
        /// <summary>How long the visitor has been "thinking" (0 when idle).</summary>
        public float BusySeconds => Busy ? Time.realtimeSinceStartup - _busyStart : 0f;
        /// <summary>Why the last reply was rule-based instead of LLM (null when it came from the LLM).</summary>
        public string LastFallbackReason { get; private set; }
        public RecruitOutcome Outcome { get; private set; } = RecruitOutcome.Ongoing;
        public bool LastReplyFromLlm { get; private set; }
        public IReadOnlyList<RecruitTurn> Turns => _turns;
        /// <summary>True when this talk continues an earlier one (transcript loaded from the visitor).</summary>
        public bool Resumed { get; }

        public event System.Action Changed;

        public RecruitmentSession(Adventurer visitor, Transform guildMaster)
        {
            Visitor = visitor;
            // Resume an earlier talk with this visitor: same affinity, turn count and transcript.
            var data = visitor.Data;
            Resumed = data.recruitAffinity >= 0;
            Affinity = Resumed ? data.recruitAffinity : InitialAffinity(visitor.Traits);
            PlayerTurns = Resumed ? data.recruitTurns : 0;
            foreach (var line in DialogueLog.Recruit(data))
                _turns.Add(new RecruitTurn { FromPlayer = line.fromPlayer, Text = line.text });
            SaveState();
            visitor.InConversation = true;
            visitor.ConversationPartner = guildMaster;
            var clock = Services.Get<GameClock>();
            visitor.VisitorLeaveAtHour = Mathf.Max(visitor.VisitorLeaveAtHour, clock.TotalHours + TalkStayHours);
        }

        /// <summary>40 adjusted by traits' recruit difficulty (loyal +15, arrogant −15, ...).</summary>
        public static int InitialAffinity(TraitSet traits)
            => Mathf.Clamp(StartAffinity - Mathf.RoundToInt((traits?.RecruitDifficulty ?? 0f) * 50f), 10, 70);

        /// <summary>
        /// Applies one small-talk affinity nudge to a visitor's persistent recruit affinity (used by ambient
        /// town chat in <see cref="Adventurers.BanterService"/>, so a visitor can also warm to the guild
        /// without ever opening a formal talk). Seeds from traits on first contact (current &lt; 0) exactly
        /// like a freshly opened recruit dialogue would, so it makes no difference which happens first.
        /// </summary>
        public static int NextRecruitAffinity(int current, TraitSet traits, int delta)
        {
            int start = current < 0 ? InitialAffinity(traits) : current;
            return Mathf.Clamp(start + delta, 0, 100);
        }

        /// <summary>Verdict after a visitor reply (pure; covered by tests). No turn limit: talk is unlimited,
        /// so the conversation only ends on an explicit join/refuse or affinity reaching AutoJoinAffinity.</summary>
        public static RecruitOutcome Judge(int affinity, RecruitDecision decision)
        {
            if ((decision == RecruitDecision.Join && affinity >= 50) || affinity >= AutoJoinAffinity) return RecruitOutcome.Joined;
            if (decision == RecruitDecision.Refuse && affinity < 35) return RecruitOutcome.Left;
            return RecruitOutcome.Ongoing;
        }

        public async Task SendAsync(string playerText, bool proposal)
        {
            if (Busy || Outcome != RecruitOutcome.Ongoing || Visitor == null) return;
            // Defense in depth: the UI already disables the propose button at low affinity (F-P3-3 rev.).
            if (proposal && Affinity <= MinProposalAffinity) return;
            playerText = string.IsNullOrWhiteSpace(playerText) ? (proposal ? "우리 길드에 들어오지 않겠나?" : "...") : playerText.Trim();
            Busy = true;
            _busyStart = Time.realtimeSinceStartup;
            AddTurn(true, playerText);
            PlayerTurns++;
            SaveState();
            Changed?.Invoke();

            var reply = await GetReplyAsync(playerText, proposal);
            if (Visitor == null) { Busy = false; return; } // left or destroyed meanwhile

            Affinity = Mathf.Clamp(Affinity + Mathf.Clamp(reply.AffinityDelta, -RecruitPrompt.MaxDelta, RecruitPrompt.MaxDelta), 0, 100);
            AddTurn(false, reply.Text);
            SaveState();
            Visitor.Say(reply.Text, toFeed: false);   // the recruit window shows the transcript
            Busy = false;
            Finish(Judge(Affinity, reply.Decision));
            Changed?.Invoke();
        }

        /// <summary>The visitor's opening line: shown in the transcript, but not saved in the conversation history.</summary>
        public void AddGreeting(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _turns.Add(new RecruitTurn { FromPlayer = false, Text = text });
            Changed?.Invoke();
        }

        async Task<RuleBasedRecruiter.Reply> GetReplyAsync(string playerText, bool proposal)
        {
            LastReplyFromLlm = false;
            LastFallbackReason = null;
            if (!Services.TryGet<LlmGateway>(out var gw)) LastFallbackReason = "LLM 없음";
            else if (!gw.Enabled) LastFallbackReason = "LLM 꺼짐";
            else if (!gw.IsReady) LastFallbackReason = "모델 준비 중";
            else
            {
                var result = await gw.RequestAsync(RecruitPrompt.BuildRequest(Visitor, Affinity, _turns, proposal));
                if (result.Ok && LlmJson.TryParse<RecruitPrompt.ResponseDto>(result.Text, out var dto) && !string.IsNullOrWhiteSpace(dto.reply))
                {
                    LastReplyFromLlm = true;
                    if (result.Exchange != null) result.Exchange.Note = $"영입 대화: 호감 {dto.affinity_delta:+0;-0;0}, {dto.decision}";
                    return new RuleBasedRecruiter.Reply
                    {
                        Text = LlmJson.CleanSentence(dto.reply, RecruitPrompt.ReplyMaxChars, RecruitPrompt.ReplyGrammarChars, false),
                        AffinityDelta = dto.affinity_delta,
                        Decision = RecruitPrompt.ParseDecision(dto.decision),
                    };
                }
                LastFallbackReason = !result.Ok ? FailureLabel(result.Error) : "응답 형식 오류";
                if (result.Exchange != null) result.Exchange.Note = "규칙 기반으로 대체: " + (result.Ok ? "parse" : result.Error);
            }
            return RuleBasedRecruiter.Respond(playerText, Visitor.Data.values, Affinity, proposal, GameRandom.Shared,
                AdventurerData.ClassLabel(Visitor.Data.cls));
        }

        static string FailureLabel(string error) => error switch
        {
            "timeout" => "시간 초과",
            "busy" => "AI가 다른 작업 중",
            "queue full" => "대기열 가득",
            "offline" => "AI 꺼짐",
            "empty" => "빈 응답",
            _ => error,
        };

        void AddTurn(bool fromPlayer, string text)
        {
            _turns.Add(new RecruitTurn { FromPlayer = fromPlayer, Text = text });
            float hour = Services.TryGet<GameClock>(out var clock) ? clock.TotalHours : 0f;
            DialogueLog.AddRecruit(Visitor.Data, hour, fromPlayer, text);
        }

        void SaveState()
        {
            if (Visitor == null) return;
            Visitor.Data.recruitAffinity = Affinity;
            Visitor.Data.recruitTurns = PlayerTurns;
        }

        void Finish(RecruitOutcome outcome)
        {
            Outcome = outcome;
            if (outcome == RecruitOutcome.Ongoing) return;
            if (outcome == RecruitOutcome.Joined
                && !(Services.TryGet<GuildRoster>(out var roster) && roster.AddExisting(Visitor)))
                outcome = Outcome = RecruitOutcome.Left; // roster full
            if (outcome == RecruitOutcome.Left) SendAway();
            EventBus.Publish(new RecruitmentEndedEvent { Visitor = Visitor, Outcome = outcome, Turns = PlayerTurns });
        }

        /// <summary>Ends the talk without a verdict (player closed the window).</summary>
        public void Abort()
        {
            if (Visitor == null || Outcome != RecruitOutcome.Ongoing) return;
            Visitor.InConversation = false;
            Visitor.ConversationPartner = null;
        }

        void SendAway()
        {
            Visitor.InConversation = false;
            Visitor.ConversationPartner = null;
            Visitor.VisitorLeaveAtHour = 0f; // leave right away
        }
    }
}
