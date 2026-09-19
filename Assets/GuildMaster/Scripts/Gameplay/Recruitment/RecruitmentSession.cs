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
        public const int MaxPlayerTurns = 6;
        /// <summary>Minimum remaining stay (game hours) while talking.</summary>
        const float TalkStayHours = 3f;

        readonly List<RecruitTurn> _turns = new();

        public Adventurer Visitor { get; }
        public int Affinity { get; private set; }
        public int PlayerTurns { get; private set; }
        public bool Busy { get; private set; }
        public RecruitOutcome Outcome { get; private set; } = RecruitOutcome.Ongoing;
        public bool LastReplyFromLlm { get; private set; }
        public IReadOnlyList<RecruitTurn> Turns => _turns;

        public event System.Action Changed;

        public RecruitmentSession(Adventurer visitor, Transform guildMaster)
        {
            Visitor = visitor;
            Affinity = InitialAffinity(visitor.Traits);
            visitor.InConversation = true;
            visitor.ConversationPartner = guildMaster;
            var clock = Services.Get<GameClock>();
            visitor.VisitorLeaveAtHour = Mathf.Max(visitor.VisitorLeaveAtHour, clock.TotalHours + TalkStayHours);
        }

        /// <summary>40 adjusted by traits' recruit difficulty (loyal +15, arrogant −15, ...).</summary>
        public static int InitialAffinity(TraitSet traits)
            => Mathf.Clamp(StartAffinity - Mathf.RoundToInt((traits?.RecruitDifficulty ?? 0f) * 50f), 10, 70);

        /// <summary>Verdict after a visitor reply (pure; covered by tests).</summary>
        public static RecruitOutcome Judge(int affinity, RecruitDecision decision, int playerTurns)
        {
            if ((decision == RecruitDecision.Join && affinity >= 50) || affinity >= 85) return RecruitOutcome.Joined;
            if (decision == RecruitDecision.Refuse && affinity < 35) return RecruitOutcome.Left;
            if (playerTurns >= MaxPlayerTurns) return affinity >= 60 ? RecruitOutcome.Joined : RecruitOutcome.Left;
            return RecruitOutcome.Ongoing;
        }

        public async Task SendAsync(string playerText, bool proposal)
        {
            if (Busy || Outcome != RecruitOutcome.Ongoing || Visitor == null) return;
            playerText = string.IsNullOrWhiteSpace(playerText) ? (proposal ? "우리 길드에 들어오지 않겠나?" : "...") : playerText.Trim();
            Busy = true;
            _turns.Add(new RecruitTurn { FromPlayer = true, Text = playerText });
            PlayerTurns++;
            Changed?.Invoke();

            var reply = await GetReplyAsync(playerText, proposal);
            if (Visitor == null) { Busy = false; return; } // left or destroyed meanwhile

            Affinity = Mathf.Clamp(Affinity + Mathf.Clamp(reply.AffinityDelta, -RecruitPrompt.MaxDelta, RecruitPrompt.MaxDelta), 0, 100);
            _turns.Add(new RecruitTurn { FromPlayer = false, Text = reply.Text });
            Visitor.Say(reply.Text);
            Busy = false;
            Finish(Judge(Affinity, reply.Decision, PlayerTurns));
            Changed?.Invoke();
        }

        async Task<RuleBasedRecruiter.Reply> GetReplyAsync(string playerText, bool proposal)
        {
            LastReplyFromLlm = false;
            if (Services.TryGet<LlmGateway>(out var gw) && gw.Enabled && gw.IsReady)
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
                if (result.Exchange != null) result.Exchange.Note = "규칙 기반으로 대체: " + (result.Ok ? "parse" : result.Error);
            }
            return RuleBasedRecruiter.Respond(playerText, Visitor.Data.values, Affinity, proposal, GameRandom.Shared);
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
