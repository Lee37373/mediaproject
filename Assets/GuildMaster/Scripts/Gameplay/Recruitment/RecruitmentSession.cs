using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
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
        public const int AutoJoinAffinity = 80;
        /// <summary>
        /// A visitor who says, in the flow of the talk, that they want in joins from this affinity up —
        /// no need to reach 50 first (user request 2026-09-30: "호감도가 50이 넘지 않아도 대화 흐름상
        /// 영입이 가능하게"). Below it they are still wary, whatever the line says.
        /// </summary>
        public const int ConversationJoinFloor = 35;
        /// <summary>
        /// Asking again right after being put off costs this much and can never raise affinity (user
        /// report 2026-09-30: mashing the old proposal button crept affinity up until the visitor joined).
        /// </summary>
        public const int NagPenalty = 4;
        /// <summary>Minimum remaining stay (game hours) while talking.</summary>
        const float TalkStayHours = 3f;

        readonly List<RecruitTurn> _turns = new();
        readonly List<(int afterTurn, string text)> _notes = new();
        readonly List<(int afterTurn, string text, int delta)> _reactions = new();
        /// <summary>The kind of line the player said last and how many of that kind came before it in a row.</summary>
        RecruitReaction? _lastReaction;
        int _reactionStreak;

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
        /// <summary>
        /// "Found out" notes for the transcript: shown after turn <c>afterTurn</c> (a count of <see cref="Turns"/>).
        /// Not part of the conversation the model sees.
        /// </summary>
        public IReadOnlyList<(int afterTurn, string text)> Notes => _notes;
        /// <summary>
        /// How each player line landed ("▲ 관심 있어 하는 이야기", see <see cref="RecruitScoring.Feedback"/>),
        /// shown under the visitor's answer so the player can learn what this person responds to.
        /// </summary>
        public IReadOnlyList<(int afterTurn, string text, int delta)> Reactions => _reactions;
        /// <summary>
        /// True when this talk continues an earlier talk in this window (the guild master has said something to
        /// them before). Town talk also sets the visitor's affinity, so affinity alone does not mean that (user
        /// report 2026-10-18: a first talk opened with "아까 하던 이야기 마저 할까요?").
        /// </summary>
        public bool Resumed { get; }
        /// <summary>First talk with someone the town already spoke well of (town talk raised their affinity).</summary>
        public bool HeardOfGuild { get; }

        public event System.Action Changed;

        readonly List<RecruitChoice> _choices = new();
        readonly HashSet<string> _usedChoices = new();
        /// <summary>
        /// The lines the guild master can pick for the next turn (user decision 2026-10-28: choices plus free typing;
        /// see <see cref="RecruitLines"/>). Empty when the talk is over or no lines are shipped.
        /// </summary>
        public IReadOnlyList<RecruitChoice> Choices => _choices;
        /// <summary>True when the last answer came from the written lines for a chosen line.</summary>
        public bool LastReplyFromChoice { get; private set; }

        /// <summary>Stages to win (Bannerlord-style persuasion, user decision 2026-10-29): 2, or 3 for a hard visitor.</summary>
        public int Stages { get; }
        public int Stage => Visitor?.Data?.recruitStage ?? 0;
        public int StageWins => Visitor?.Data?.recruitWins ?? 0;
        public int StageFails => Visitor?.Data?.recruitFails ?? 0;
        /// <summary>The last argument tried (the same one again right away is less convincing).</summary>
        string _lastArgument;
        /// <summary>What the visitor's last answer calls for: offered first next turn (2026-10-29, RecruitLines.FollowUp).</summary>
        RecruitChoiceKind? _followKind;
        string _followTopic;

        void SetFollow((RecruitChoiceKind? kind, string topic) f)
        {
            _followKind = f.kind;
            _followTopic = f.topic;
        }

        // ---- Questions (user decision 2026-10-30: a roll, and only a few per visitor) ----

        /// <summary>Kind of question → the player turn from which it may be tried again after a dodge.</summary>
        readonly Dictionary<string, int> _askCooldown = new();
        /// <summary>Affinity lost for asking once the visitor has had enough questions.</summary>
        public const int PesterPenalty = 4;

        public int QuestionLimit => Visitor?.Data != null ? VisitorReveal.QuestionLimit(Visitor.Data.mbti) : 0;
        public int QuestionsLeft => Visitor?.Data != null ? Mathf.Max(0, QuestionLimit - Visitor.Data.recruitAsked) : 0;

        static string AskKey(RecruitChoiceKind k) => k switch
        {
            RecruitChoiceKind.AskValues => VisitorReveal.Values,
            RecruitChoiceKind.AskPersonality => VisitorReveal.Personality,
            RecruitChoiceKind.AskTraits => "trait",
            _ => null,
        };

        /// <summary>The chance a question choice gets an honest answer (percent), or -1 for a line that is not one.</summary>
        public int AskChanceOf(RecruitChoice c)
        {
            string key = c != null ? AskKey(c.Kind) : null;
            return key == null ? -1 : VisitorReveal.AskChance(key, Affinity);
        }

        /// <summary>Questions not to offer now: all once the visitor has had enough, and a dodged kind for a while.</summary>
        HashSet<RecruitChoiceKind> NoAsk()
        {
            var no = new HashSet<RecruitChoiceKind>();
            foreach (var k in new[] { RecruitChoiceKind.AskValues, RecruitChoiceKind.AskPersonality, RecruitChoiceKind.AskTraits })
                if (QuestionsLeft <= 0 || (_askCooldown.TryGetValue(AskKey(k), out int until) && PlayerTurns < until)) no.Add(k);
            return no;
        }

        static bool Answers(string revealKey, string asked)
            => asked == "trait" ? revealKey.StartsWith(VisitorReveal.TraitPrefix) : revealKey == asked;

        string KnownValuesNow(AdventurerData d) => VisitorReveal.IsRevealed(d, VisitorReveal.Values) ? d.values : null;

        /// <summary>What the visitor asks about after a stage: what they care about more often than not.</summary>
        static string NextTopic(AdventurerData d)
        {
            if (!string.IsNullOrEmpty(d.values) && System.Array.IndexOf(RecruitLines.AllValues, d.values) >= 0 && GameRandom.Shared.Chance(0.6f))
                return d.values;
            return RecruitLines.AllValues[GameRandom.Shared.Range(0, RecruitLines.AllValues.Length)];
        }

        static string ArgumentKey(RecruitChoice c) => c == null ? null : c.Kind + ":" + (string.IsNullOrEmpty(c.Topic) ? c.Id : c.Topic);

        /// <summary>The odds of an argument for this visitor now (the real ones, shown or not).</summary>
        public PersuadeChance ChanceOf(RecruitChoice c)
        {
            var d = Visitor.Data;
            return RecruitPersuasion.Chance(RecruitLines.ReactionFor(c, d.values, d.traitIds), Affinity, Stage, ArgumentKey(c) == _lastArgument);
        }

        /// <summary>Whether the guild master knows enough about the visitor to see an argument's chance.</summary>
        public bool ChanceKnown(RecruitChoice c)
        {
            var d = Visitor.Data;
            return RecruitLines.ChanceKnown(c, VisitorReveal.IsRevealed(d, VisitorReveal.Values) ? d.values : null, KnownTraits(d),
                                            VisitorReveal.NextHiddenTrait(d) != null);
        }

        static List<string> KnownTraits(AdventurerData d)
        {
            var known = new List<string>();
            if (d.traitIds != null)
                foreach (var id in d.traitIds)
                    if (!string.IsNullOrEmpty(id) && VisitorReveal.IsRevealed(d, VisitorReveal.TraitKey(id))) known.Add(id);
            return known;
        }

        public RecruitmentSession(Adventurer visitor, Transform guildMaster)
        {
            Visitor = visitor;
            // Resume an earlier talk with this visitor: same affinity, turn count and transcript.
            var data = visitor.Data;
            Resumed = data.recruitTurns > 0;
            int initial = InitialAffinity(visitor.Traits);
            Affinity = data.recruitAffinity >= 0 ? data.recruitAffinity : initial;
            HeardOfGuild = !Resumed && data.recruitAffinity > initial;
            PlayerTurns = Mathf.Max(0, data.recruitTurns);
            Stages = RecruitPersuasion.StagesFor(visitor.Traits?.RecruitDifficulty ?? 0f);
            foreach (var line in DialogueLog.Recruit(data))
                _turns.Add(new RecruitTurn { FromPlayer = line.fromPlayer, Text = line.text });
            SaveState();
            visitor.InConversation = true;
            visitor.ConversationPartner = guildMaster;
            var clock = Services.Get<GameClock>();
            visitor.VisitorLeaveAtHour = Mathf.Max(visitor.VisitorLeaveAtHour, clock.TotalHours + TalkStayHours);
            RefreshChoices();
        }

        /// <summary>40 adjusted by traits' recruit difficulty (loyal +15, arrogant −15, ...).</summary>
        public static int InitialAffinity(TraitSet traits)
            => Mathf.Clamp(StartAffinity - Mathf.RoundToInt((traits?.RecruitDifficulty ?? 0f) * 50f), HardestStart, 70);

        /// <summary>
        /// The lowest a visitor starts at, however many "hard to recruit" traits they have (was 10: arrogant
        /// and greedy together started at 15 and were practically impossible, user report 2026-09-30).
        /// See RecruitScoring for the pacing this is tuned to.
        /// </summary>
        public const int HardestStart = 25;

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

        /// <summary>Verdict after a visitor reply (pure; covered by tests). No turn limit and no formal proposal
        /// any more (user request 2026-09-30): the visitor joins when they say so in the flow of the talk
        /// (from <see cref="ConversationJoinFloor"/>) or on their own at <see cref="AutoJoinAffinity"/>.</summary>
        public static RecruitOutcome Judge(int affinity, RecruitDecision decision)
            => Judge(affinity, decision, mayJoin: true, lineDelta: 0);

        /// <summary>
        /// Lines the guild master has to say before a visitor joins without being asked (user report 2026-10-18:
        /// a visitor the town had warmed up joined after a single "안녕").
        /// </summary>
        public const int OwnJoinMinTurns = 3;

        /// <summary>Whether the visitor may say yes now: the guild master asked, or they have talked a while (pure; tested).</summary>
        public static bool MayJoin(bool invited, int playerTurns) => invited || playerTurns >= OwnJoinMinTurns;

        /// <summary>
        /// Verdict with the guards (pure; tested): no joining before <see cref="MayJoin"/>, and never on a line that
        /// landed badly (<paramref name="lineDelta"/> &lt; 0, e.g. "▼ 성의 없다고 느낌").
        /// </summary>
        public static RecruitOutcome Judge(int affinity, RecruitDecision decision, bool mayJoin, int lineDelta)
        {
            bool canJoin = mayJoin && lineDelta >= 0;
            if (canJoin && ((decision == RecruitDecision.Join && affinity >= ConversationJoinFloor) || affinity >= AutoJoinAffinity)) return RecruitOutcome.Joined;
            if (decision == RecruitDecision.Refuse && affinity < 35) return RecruitOutcome.Left;
            return RecruitOutcome.Ongoing;
        }

        /// <summary>
        /// The visitor's opening line in their own speech level: picking up an earlier talk, meeting someone the town
        /// spoke well of, or a plain first meeting (pure; tested).
        /// </summary>
        public static string Greeting(bool resumed, bool heardOfGuild, NpcVoice.Register register)
        {
            int r = (int)register;
            if (resumed) return ResumeGreetings[r];
            return heardOfGuild ? HeardGreetings[r] : FirstGreetings[r];
        }

        /// <summary>This talk's opening line.</summary>
        public string OpeningLine => Visitor?.Data == null ? "무슨 일이시죠?"
            : Greeting(Resumed, HeardOfGuild, NpcVoice.RecruitRegister(Visitor.Data.mbti, Visitor.Data.traitIds));

        // Warm, Formal, Curt, Haughty (NpcVoice.Register order).
        static readonly string[] FirstGreetings = { "안녕하세요! 저한테 무슨 일 있으세요?", "안녕하십니까. 무슨 용건이시죠?", "…무슨 일이죠?", "뭐야, 나한테 볼일 있어?" };
        static readonly string[] HeardGreetings = { "혹시 길드 마스터님이세요? 마을에서 얘기 많이 들었어요!", "길드 마스터님이십니까. 마을에서 말씀 많이 들었습니다.", "길드 마스터죠? 얘기는 들었어요.", "네가 길드 마스터야? 소문은 들었어." };
        static readonly string[] ResumeGreetings = { "아, 또 오셨네요! 아까 얘기 이어서 할까요?", "다시 오셨군요. 하던 이야기를 계속하시지요.", "또 왔네요. 하던 얘기요?", "또 왔어? 아까 하던 얘기나 마저 해." };

        /// <summary>
        /// The player's line and the visitor's answer. <paramref name="proposal"/> is the old formal-offer
        /// path; the recruit window no longer has that button (user request 2026-09-30) — inviting the
        /// visitor is just something said in the talk now.
        /// </summary>
        public Task SendAsync(string playerText, bool proposal = false) => RunTurnAsync(playerText, proposal, null);

        /// <summary>
        /// A line picked from <see cref="Choices"/>: answered from the written lines, no model (user decision
        /// 2026-10-28). Scored, revealed and judged exactly like a typed line.
        /// </summary>
        public Task SendChoiceAsync(RecruitChoice choice)
        {
            if (choice == null || !_choices.Contains(choice)) return Task.CompletedTask;
            if (RecruitLines.IsArgument(choice.Kind)) { Argue(choice); return Task.CompletedTask; }
            return RunTurnAsync(choice.Text, false, choice);
        }

        /// <summary>
        /// An argument, rolled for (user decision 2026-10-29: Bannerlord-style persuasion). Three successes clear a stage
        /// and the last stage won means they join; a third failure in a stage, or a critical one, means they say no for
        /// good and leave.
        /// </summary>
        void Argue(RecruitChoice choice)
        {
            if (Busy || Outcome != RecruitOutcome.Ongoing || Visitor == null) return;
            var d = Visitor.Data;
            string text = choice.Text;
            AddTurn(true, text);
            PlayerTurns++;
            _usedChoices.Add(choice.Id);
            LastReplyFromLlm = false;
            LastFallbackReason = null;
            LastReplyFromChoice = true;

            var reaction = RecruitLines.ReactionFor(choice, d.values, d.traitIds);
            var chance = ChanceOf(choice);
            var roll = RecruitPersuasion.Roll(chance, GameRandom.Shared.Value);
            _lastArgument = ArgumentKey(choice);
            int stage = d.recruitStage, wins = d.recruitWins, fails = d.recruitFails;
            var state = RecruitPersuasion.Advance(roll, Stages, ref stage, ref wins, ref fails);
            d.recruitStage = stage;
            d.recruitWins = wins;
            d.recruitFails = fails;

            var planned = VisitorReveal.BeforeReply(d, text, PlayerTurns, Affinity, out string revealHint);
            int affinityBefore = Affinity;
            int delta = RecruitPersuasion.AffinityDelta(roll);
            Affinity = Mathf.Clamp(Affinity + delta, 0, 100);
            string nextTopic = state == PersuadeState.StageCleared ? NextTopic(d) : null;
            string reply = RecruitLines.Shared.RespondArgument(choice, Info(), roll, state, planned.Select(p => p.Key).ToList(), GameRandom.Shared, nextTopic);
            AddTurn(false, reply);
            _reactions.Add((_turns.Count, RecruitPersuasion.Feedback(roll, chance.Success), delta));
            string said = reply, nextLine = null;
            if (state == PersuadeState.StageCleared)
            {
                _notes.Add((_turns.Count, $"{StageTag} {stage}단계를 넘겼습니다. 남은 단계 {Stages - stage}"));
                // The next stage opens with the visitor's own question, on a line of its own (user report 2026-10-29).
                nextLine = RecruitLines.Shared.StageNext(Info().Register, nextTopic, GameRandom.Shared);
            }
            var after = VisitorReveal.AfterReply(d, reaction,
                valueClash: reaction == RecruitReaction.Clash && RecruitScoring.ClashesWithValue(text, d.values),
                wantedTrait: RecruitLines.WantedTrait(choice, d.traitIds), affinityBefore, Affinity, planned);
            ApplyReveals(planned.Concat(after), revealHint);
            // After what this answer gave away, so the question reads last.
            if (!string.IsNullOrEmpty(nextLine))
            {
                AddTurn(false, nextLine);
                said = reply + " " + nextLine;
            }
            SetFollow(RecruitLines.FollowUp(choice, roll, state, d.values, KnownValuesNow(d), nextTopic));
            SaveState();
            Visitor.Say(said, toFeed: false);
            if (state == PersuadeState.Won) Finish(RecruitOutcome.Joined);
            else if (state == PersuadeState.Lost)
            {
                d.recruitRefused = true;
                Finish(RecruitOutcome.Left);
            }
            RefreshChoices();
            Changed?.Invoke();
        }

        public const string StageTag = "[설득]";

        async Task RunTurnAsync(string playerText, bool proposal, RecruitChoice choice)
        {
            if (Busy || Outcome != RecruitOutcome.Ongoing || Visitor == null) return;
            // Defense in depth: the UI already disables the propose button at low affinity (F-P3-3 rev.).
            if (proposal && Affinity <= MinProposalAffinity) return;
            playerText = string.IsNullOrWhiteSpace(playerText) ? (proposal ? "우리 길드에 들어오지 않겠나?" : "...") : playerText.Trim();
            Busy = true;
            _busyStart = Time.realtimeSinceStartup;
            // Pestering: an invitation right after another one (see NagPenalty).
            bool invited = !proposal && (choice != null ? choice.Kind == RecruitChoiceKind.Invite : RuleBasedRecruiter.IsInvitation(playerText));
            bool nagging = invited && LastPlayerLineWasInvitation();
            AddTurn(true, playerText);
            PlayerTurns++;
            // Typed lines and questions only find things out (user choice 2026-10-29): joining is won by arguments.
            bool mayJoin = false;
            SaveState();
            Changed?.Invoke();

            // What the visitor tells this time by the rules (VisitorReveal, user report 2026-10-07): decided
            // before the reply so the model (or the rule-based line) actually says it.
            // A probing question is a roll, and a visitor only puts up with a few (2026-10-30).
            var vd = Visitor.Data;
            string asked = VisitorReveal.AskedAbout(vd, playerText);
            bool mayAsk = asked == null || QuestionsLeft > 0;
            if (asked != null && mayAsk) vd.recruitAsked++;
            var planned = VisitorReveal.BeforeReply(vd, playerText, PlayerTurns, Affinity, out string revealHint,
                                                    asked != null ? GameRandom.Shared.Value : 0f, mayAsk);
            bool pestered = asked != null && !mayAsk;
            if (asked != null && mayAsk && !planned.Any(p => Answers(p.Key, asked)))
                _askCooldown[asked] = PlayerTurns + VisitorReveal.AskCooldownTurns;
            int affinityBefore = Affinity;
            if (choice != null) _usedChoices.Add(choice.Id);
            var reply = choice != null ? ChoiceReply(choice, planned) : await GetReplyAsync(playerText, proposal, nagging, planned, mayJoin);
            if (Visitor == null) { Busy = false; return; } // left or destroyed meanwhile

            // Scored by kind of line from a fixed table (RecruitScoring, user request 2026-09-30), not by a
            // number the model makes up: the same pitch again is worth less, nagging never pays.
            int streak = _lastReaction == reply.Reaction ? _reactionStreak + 1 : 0;
            int delta = RecruitScoring.Score(reply.Reaction, streak, Visitor.Data.mbti, playerText);
            if (nagging) delta = NaggedDelta(delta);
            // Finding things out does not win anyone over, but rudeness still costs (2026-10-29).
            delta = choice != null ? 0 : Mathf.Min(0, delta);
            if (pestered) delta = Mathf.Min(0, delta) - PesterPenalty;
            _lastReaction = reply.Reaction;
            _reactionStreak = streak;
            Affinity = Mathf.Clamp(Affinity + delta, 0, 100);
            AddTurn(false, reply.Text);
            if (pestered) _reactions.Add((_turns.Count, "▼ 이것저것 캐물어서 귀찮아함 (호감 하락)", delta));
            else if (delta < 0) _reactions.Add((_turns.Count, RecruitScoring.Feedback(reply.Reaction, delta, nagging), delta));
            var after = VisitorReveal.AfterReply(Visitor.Data, reply.Reaction,
                valueClash: reply.Reaction == RecruitReaction.Clash && RecruitScoring.ClashesWithValue(playerText, Visitor.Data.values),
                wantedTrait: choice != null ? RecruitLines.WantedTrait(choice, Visitor.Data.traitIds) : RecruitScoring.WantedTrait(playerText, Visitor.Data.traitIds),
                affinityBefore, Affinity, planned);
            ApplyReveals(planned.Concat(after), revealHint);
            // A typed line has no written thread to follow (the model answered it).
            SetFollow(choice != null ? RecruitLines.FollowUp(choice, null, PersuadeState.Ongoing, Visitor.Data.values, KnownValuesNow(Visitor.Data), null) : (null, null));
            SaveState();
            Visitor.Say(reply.Text, toFeed: false);   // the recruit window shows the transcript
            Busy = false;
            RefreshChoices();
            Changed?.Invoke();
        }

        RecruitLines.VisitorInfo Info()
        {
            var d = Visitor.Data;
            var visitor = Visitor;
            return new RecruitLines.VisitorInfo
            {
                Values = d.values,
                TraitIds = d.traitIds,
                Mbti = d.mbti,
                ClassLabel = AdventurerData.ClassLabel(d.cls),
                Register = NpcVoice.RecruitRegister(d.mbti, d.traitIds),
                TraitName = id => VisitorReveal.TraitName(visitor, id),
            };
        }

        RuleBasedRecruiter.Reply ChoiceReply(RecruitChoice choice, List<RevealStep> planned)
        {
            LastReplyFromLlm = false;
            LastFallbackReason = null;
            LastReplyFromChoice = true;
            return RecruitLines.Shared.Respond(choice, Info(), Affinity, planned.Select(p => p.Key).ToList(), GameRandom.Shared);
        }

        /// <summary>New lines to pick from after each turn, from what the guild master knows about the visitor so far.</summary>
        void RefreshChoices()
        {
            _choices.Clear();
            if (Visitor == null || Visitor.Data == null || Outcome != RecruitOutcome.Ongoing) return;
            var d = Visitor.Data;
            var known = KnownTraits(d);
            _choices.AddRange(RecruitLines.Shared.Offer(new RecruitChoiceState
            {
                FirstTalk = !Resumed,
                PlayerTurns = PlayerTurns,
                KnownValues = VisitorReveal.IsRevealed(d, VisitorReveal.Values) ? d.values : null,
                PersonalityKnown = VisitorReveal.IsRevealed(d, VisitorReveal.Personality),
                HiddenTraitLeft = VisitorReveal.NextHiddenTrait(d) != null,
                KnownTraits = known,
                Used = _usedChoices,
                FollowKind = _followKind,
                FollowTopic = _followTopic,
                NoAsk = NoAsk(),
            }, GameRandom.Shared));
        }

        /// <summary>The visitor's opening line: shown in the transcript, but not saved in the conversation history.</summary>
        public void AddGreeting(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _turns.Add(new RecruitTurn { FromPlayer = false, Text = text });
            Changed?.Invoke();
        }

        /// <summary>A repeated invitation never raises affinity and always costs <see cref="NagPenalty"/> (pure; tested).</summary>
        public static int NaggedDelta(int delta) => Mathf.Min(delta, 0) - NagPenalty;

        string LastVisitorLine()
        {
            for (int i = _turns.Count - 1; i >= 0; i--)
                if (!_turns[i].FromPlayer) return _turns[i].Text;
            return null;
        }

        bool LastPlayerLineWasInvitation()
        {
            for (int i = _turns.Count - 1; i >= 0; i--)
                if (_turns[i].FromPlayer) return RuleBasedRecruiter.IsInvitation(_turns[i].Text);
            return false;
        }

        async Task<RuleBasedRecruiter.Reply> GetReplyAsync(string playerText, bool proposal, bool nagging, List<RevealStep> planned, bool mayJoin)
        {
            LastReplyFromLlm = false;
            LastFallbackReason = null;
            LastReplyFromChoice = false;
            if (!Services.TryGet<LlmGateway>(out var gw)) LastFallbackReason = "LLM 없음";
            else if (!gw.Enabled) LastFallbackReason = "LLM 꺼짐";
            else if (!gw.IsReady) LastFallbackReason = "모델 준비 중";
            else
            {
                var tell = planned.Select(p => VisitorReveal.ForModel(p.Key, Visitor.Data, id => VisitorReveal.TraitName(Visitor, id))).ToList();
                var result = await gw.RequestAsync(RecruitPrompt.BuildRequest(Visitor, Affinity, _turns, proposal, nagging, tell, mayJoin));
                string text = null;
                RecruitPrompt.ResponseDto dto = null;
                bool parsed = result.Ok && LlmJson.TryParse<RecruitPrompt.ResponseDto>(result.Text, out dto) && !string.IsNullOrWhiteSpace(dto.reply);
                if (parsed) text = LlmJson.CleanSentence(dto.reply, RecruitPrompt.ReplyMaxChars, RecruitPrompt.ReplyGrammarChars, false);
                // The model sometimes answers a short line with (part of) its previous answer word for word (live
                // test 2026-10-01); a rule-based reply reads better than a visitor repeating themselves.
                string previous = LastVisitorLine();
                bool repeated = parsed && previous != null && (text == previous || previous.Contains(text));
                if (parsed && !repeated)
                {
                    LastReplyFromLlm = true;
                    var rules = RuleBasedRecruiter.Classify(playerText, Visitor.Data.values, Visitor.Data.traitIds);
                    var reaction = RecruitScoring.Reconcile(RecruitScoring.Parse(dto.reaction), rules);
                    if (result.Exchange != null) result.Exchange.Note = $"영입 대화: {dto.reaction}→{RecruitScoring.Code(reaction)}, {dto.decision}" + (planned.Count > 0 ? ", 털어놓기 " + string.Join("/", planned.Select(p => p.Key)) : "");
                    return new RuleBasedRecruiter.Reply
                    {
                        Text = text,
                        AffinityDelta = RecruitScoring.Base(reaction),
                        Reaction = reaction,
                        Decision = RecruitPrompt.ParseDecision(dto.decision),
                    };
                }
                LastFallbackReason = !result.Ok ? FailureLabel(result.Error) : repeated ? "같은 대답 반복" : "응답 형식 오류";
                if (result.Exchange != null) result.Exchange.Note = "규칙 기반으로 대체: " + (!result.Ok ? result.Error : repeated ? "repeat" : "parse");
            }
            var fallback = RuleBasedRecruiter.Respond(playerText, Visitor.Data.values, Affinity, proposal, GameRandom.Shared,
                AdventurerData.ClassLabel(Visitor.Data.cls), NpcVoice.RecruitRegister(Visitor.Data.mbti, Visitor.Data.traitIds), Visitor.Data.traitIds);
            // The fixed lines say what the rules decided to let out.
            var info = Info();
            // Typed lines never settle it (2026-10-29): a stock yes or no would promise what the talk does not do.
            if (fallback.Decision == RecruitDecision.Join)
                fallback.Text = RecruitLines.Shared.Line("invite.notyet", info.Register, GameRandom.Shared) ?? fallback.Text;
            else if (fallback.Decision == RecruitDecision.Refuse)
                fallback.Text = RecruitLines.Shared.Line("roll.no", info.Register, GameRandom.Shared) ?? fallback.Text;
            foreach (var p in planned)
            {
                // In the visitor's own speech level when the written lines have it (2026-10-28).
                string line = RecruitLines.Shared.Confession(p.Key, info) ?? VisitorReveal.Confession(p.Key, Visitor.Data, id => VisitorReveal.TraitName(Visitor, id));
                if (!string.IsNullOrEmpty(line) && !fallback.Text.Contains(line)) fallback.Text += " " + line;
            }
            return fallback;
        }

        /// <summary>
        /// Marks what this exchange brought out (VisitorReveal rules), each with why, under the visitor's line, and
        /// a hint when the guild master asked too early. Each new fact also publishes a <see cref="VisitorRevealedEvent"/>.
        /// </summary>
        void ApplyReveals(IEnumerable<RevealStep> steps, string hint)
        {
            var d = Visitor.Data;
            foreach (var step in steps)
            {
                if (!VisitorReveal.Reveal(d, step.Key)) continue;
                string label = VisitorReveal.Label(step.Key, Visitor);
                _notes.Add((_turns.Count, $"{RevealedTag} {label} — {step.Reason}"));
                EventBus.Publish(new VisitorRevealedEvent { Visitor = Visitor, Key = step.Key, Label = label });
            }
            if (!string.IsNullOrEmpty(hint)) _notes.Add((_turns.Count, $"{HintTag} {hint}"));
        }

        public const string RevealedTag = "[알게 됨]";
        public const string HintTag = "[아직]";

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
