using System;
using System.Collections.Generic;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Recruitment
{
    /// <summary>What a recruit choice is: small talk, a question about the visitor, a pitch or the invitation.</summary>
    public enum RecruitChoiceKind { Greet, About, AskValues, AskPersonality, AskTraits, Guild, Pitch, Trait, Invite }

    [Serializable]
    public class RecruitChoiceDto
    {
        public string id;
        public string kind;
        /// <summary>The value a pitch is about ("돈"…), or the trait id a trait pitch offers something to.</summary>
        public string topic;
        public string text;
    }

    [Serializable]
    public class RecruitReplyDto
    {
        public string key;
        /// <summary>Variants per speech level: warm / formal / curt / haughty (NpcVoice.Register).</summary>
        public string[] w, f, c, h;
    }

    [Serializable]
    public class RecruitLinesFile
    {
        public RecruitChoiceDto[] choices;
        public RecruitReplyDto[] replies;
    }

    /// <summary>One line the guild master can pick in the recruit window.</summary>
    public class RecruitChoice
    {
        public string Id;
        public RecruitChoiceKind Kind;
        public string Topic;
        public string Text;
    }

    /// <summary>What the choices need to know about the talk so far (only what the guild master knows).</summary>
    public struct RecruitChoiceState
    {
        public bool FirstTalk;
        public int PlayerTurns;
        /// <summary>The visitor's values once found out, else null.</summary>
        public string KnownValues;
        public bool PersonalityKnown;
        public bool HiddenTraitLeft;
        /// <summary>Trait ids already found out.</summary>
        public IReadOnlyCollection<string> KnownTraits;
        /// <summary>Choice ids already said in this talk (not offered again while others are left).</summary>
        public ICollection<string> Used;
        /// <summary>
        /// What the visitor's last answer calls for (user report 2026-10-29: the choices did not follow what the visitor
        /// had just said): a line of this kind (and topic) is offered first. Null for none.
        /// </summary>
        public RecruitChoiceKind? FollowKind;
        public string FollowTopic;
    }

    /// <summary>
    /// The recruit talk written ahead (user decision 2026-10-28: "캐릭터 영입대화시 선택지 제공 + 직접 대화도
    /// 가능하게", the model only for what the player types). The guild master picks one of four lines; the
    /// visitor answers from the reply sets in their speech level (<see cref="NpcVoice.RecruitRegister"/>), and the
    /// kind of line decides the reaction by the same table as typed lines (<see cref="RecruitScoring"/>). Data in
    /// Resources/Dialogue/Recruit/*.json; pure apart from <see cref="LoadResources"/>, covered by tests.
    /// </summary>
    public class RecruitLines
    {
        public const string ResourceFolder = "Dialogue/Recruit";
        public static readonly string[] AllValues = { "돈", "명예", "동료", "모험", "안전", "지식" };
        /// <summary>How many lines the window offers each turn.</summary>
        public const int ChoiceCount = 4;
        /// <summary>From this many lines the invitation is among the choices.</summary>
        public const int InviteFromTurn = 2;

        readonly List<RecruitChoice> _choices = new();
        readonly Dictionary<string, RecruitReplyDto> _replies = new();

        public int ChoiceTotal => _choices.Count;
        public int ReplyTotal => _replies.Count;
        public IReadOnlyList<RecruitChoice> AllChoices => _choices;
        public bool HasReply(string key) => key != null && _replies.ContainsKey(key);

        static RecruitLines _shared;
        /// <summary>The shipped lines, loaded once.</summary>
        public static RecruitLines Shared => _shared ??= LoadResources();

        public static RecruitLines FromJson(params string[] jsons)
        {
            var bank = new RecruitLines();
            foreach (var json in jsons)
            {
                if (string.IsNullOrWhiteSpace(json)) continue;
                var file = JsonUtility.FromJson<RecruitLinesFile>(json);
                if (file == null) continue;
                if (file.choices != null) foreach (var c in file.choices) bank.Add(c);
                if (file.replies != null) foreach (var r in file.replies) if (!string.IsNullOrEmpty(r?.key)) bank._replies[r.key] = r;
            }
            return bank;
        }

        public static RecruitLines LoadResources()
        {
            var texts = new List<string>();
            foreach (var a in Resources.LoadAll<TextAsset>(ResourceFolder)) if (a != null) texts.Add(a.text);
            return FromJson(texts.ToArray());
        }

        bool Add(RecruitChoiceDto c)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.text) || !Enum.TryParse(c.kind, out RecruitChoiceKind kind)) return false;
            _choices.Add(new RecruitChoice { Id = c.id ?? c.text, Kind = kind, Topic = c.topic ?? "", Text = c.text });
            return true;
        }

        // ---------------- Which lines to offer ----------------

        /// <summary>
        /// Arguments are rolled for (Bannerlord-style persuasion, user decision 2026-10-29): pitches to a value, lines a
        /// trait would like, and what the guild is like. Everything else only finds things out.
        /// </summary>
        public static bool IsArgument(RecruitChoiceKind k)
            => k == RecruitChoiceKind.Pitch || k == RecruitChoiceKind.Trait || k == RecruitChoiceKind.Guild;

        /// <summary>
        /// Whether the guild master knows enough to judge this argument's chance: a pitch once the visitor's values are
        /// known, a trait line once that trait is known (or every trait is), the guild always.
        /// </summary>
        public static bool ChanceKnown(RecruitChoice c, string knownValues, IReadOnlyCollection<string> knownTraits, bool hiddenTraitLeft)
        {
            if (c == null) return false;
            return c.Kind switch
            {
                RecruitChoiceKind.Guild => true,
                RecruitChoiceKind.Pitch => knownValues != null,
                RecruitChoiceKind.Trait => !hiddenTraitLeft || (knownTraits != null && Contains(knownTraits, c.Topic)),
                _ => true,
            };
        }

        static bool Contains(IReadOnlyCollection<string> list, string id)
        {
            foreach (var x in list) if (x == id) return true;
            return false;
        }

        /// <summary>
        /// Four lines for the next turn (pure; tested): three arguments — a pitch (their own value most of the time once
        /// known), another pitch or something a trait would like, the guild or one more pitch — and one line that only
        /// finds things out (a greeting first, then a question about what is still hidden). No invitation: the visitor
        /// joins when the last stage is won (user decision 2026-10-29).
        /// </summary>
        public List<RecruitChoice> Offer(RecruitChoiceState s, GameRandom rng)
        {
            var result = new List<RecruitChoice>();
            var topics = new List<string>(AllValues);
            rng.Shuffle(topics);
            void Put(RecruitChoice c) { if (c != null && !result.Contains(c)) result.Add(c); }

            // Meeting someone for the first time (user report 2026-10-29: pitches straight after "무슨 일이세요?" did
            // not follow): answer the greeting, ask about them, or tell them about the guild — no sales talk yet.
            if (s.FirstTalk && s.PlayerTurns == 0)
            {
                Put(Pick(RecruitChoiceKind.Greet, null, s.Used, rng));
                Put(Pick(RecruitChoiceKind.About, null, s.Used, rng));
                Put(Pick(RecruitChoiceKind.Guild, null, s.Used, rng));
                Put(Pick(s.KnownValues == null ? RecruitChoiceKind.AskValues : RecruitChoiceKind.Pitch, s.KnownValues, s.Used, rng));
                return result;
            }

            // What the visitor just asked or brought up comes first, in the slot of its kind.
            var follow = s.FollowKind.HasValue ? Pick(s.FollowKind.Value, s.FollowTopic, s.Used, rng) : null;
            bool followArgues = follow != null && IsArgument(follow.Kind);
            if (followArgues) Put(follow);
            if (follow != null && follow.Kind == RecruitChoiceKind.Pitch) topics.Remove(follow.Topic);

            string first = s.KnownValues != null && topics.Contains(s.KnownValues) && rng.Chance(0.7f) ? s.KnownValues : topics[0];
            topics.Remove(first);
            Put(Pick(RecruitChoiceKind.Pitch, first, s.Used, rng));

            RecruitChoice second = null;
            if (s.KnownTraits != null && s.KnownTraits.Count > 0 && rng.Chance(0.5f))
                second = Pick(RecruitChoiceKind.Trait, rng.Pick(new List<string>(s.KnownTraits)), s.Used, rng);
            else if (rng.Chance(0.35f))
                second = Pick(RecruitChoiceKind.Trait, null, s.Used, rng);
            Put(second ?? Pick(RecruitChoiceKind.Pitch, topics[0], s.Used, rng));
            Put(rng.Chance(0.4f) ? Pick(RecruitChoiceKind.Guild, null, s.Used, rng) : Pick(RecruitChoiceKind.Pitch, topics[1], s.Used, rng));
            if (result.Count < ChoiceCount - 1) Put(Pick(RecruitChoiceKind.Pitch, topics[2], s.Used, rng));
            if (result.Count > ChoiceCount - 1) result.RemoveRange(ChoiceCount - 1, result.Count - (ChoiceCount - 1));
            // Shuffled, but the follow-up stays on top.
            if (followArgues)
            {
                result.Remove(follow);
                rng.Shuffle(result);
                result.Insert(0, follow);
            }
            else rng.Shuffle(result);

            if (follow != null && !followArgues)
            {
                result.Insert(0, follow);
                return result;
            }
            var questions = new List<RecruitChoiceKind>();
            if (s.KnownValues == null) questions.Add(RecruitChoiceKind.AskValues);
            if (s.HiddenTraitLeft) questions.Add(RecruitChoiceKind.AskTraits);
            if (!s.PersonalityKnown) questions.Add(RecruitChoiceKind.AskPersonality);
            if (s.PlayerTurns < 3) questions.Add(RecruitChoiceKind.About);
            if (questions.Count > 0) Put(Pick(rng.Pick(questions), null, s.Used, rng));
            else Put(Pick(RecruitChoiceKind.Pitch, topics[3], s.Used, rng));
            return result;
        }

        /// <summary>A line of this kind (and topic), preferring ones not said yet; null when there is none.</summary>
        RecruitChoice Pick(RecruitChoiceKind kind, string topic, ICollection<string> used, GameRandom rng)
        {
            var fresh = new List<RecruitChoice>();
            var any = new List<RecruitChoice>();
            foreach (var c in _choices)
            {
                if (c.Kind != kind || (topic != null && c.Topic != topic)) continue;
                any.Add(c);
                if (used == null || !used.Contains(c.Id)) fresh.Add(c);
            }
            var pool = fresh.Count > 0 ? fresh : any;
            return pool.Count == 0 ? null : rng.Pick(pool);
        }

        // ---------------- How the visitor takes it ----------------

        /// <summary>What kind of line it was for this visitor (pure; tested) — the same table typed lines are scored by.</summary>
        public static RecruitReaction ReactionFor(RecruitChoice c, string values, IEnumerable<string> traitIds)
        {
            if (c == null) return RecruitReaction.Friendly;
            switch (c.Kind)
            {
                case RecruitChoiceKind.Pitch:
                    if (c.Topic == values) return RecruitReaction.ValueMatch;
                    if (RecruitScoring.WantedTrait(c.Text, traitIds) != null) return RecruitReaction.TraitMatch;
                    return RecruitScoring.ClashesWithValue(c.Text, values) ? RecruitReaction.Clash : RecruitReaction.Friendly;
                case RecruitChoiceKind.Trait:
                    return HasTrait(traitIds, c.Topic) ? RecruitReaction.TraitMatch : RecruitReaction.Friendly;
                default:
                    return RecruitReaction.Friendly;
            }
        }

        /// <summary>The trait this line offers something to, if the visitor has it (for VisitorReveal).</summary>
        public static string WantedTrait(RecruitChoice c, IEnumerable<string> traitIds)
        {
            if (c == null) return null;
            if (c.Kind == RecruitChoiceKind.Trait) return HasTrait(traitIds, c.Topic) ? c.Topic : null;
            return RecruitScoring.WantedTrait(c.Text, traitIds);
        }

        static bool HasTrait(IEnumerable<string> traitIds, string id)
        {
            if (traitIds == null || string.IsNullOrEmpty(id)) return false;
            foreach (var t in traitIds) if (t == id) return true;
            return false;
        }

        /// <summary>Who the visitor is, for <see cref="Respond"/>.</summary>
        public struct VisitorInfo
        {
            public string Values;
            public IEnumerable<string> TraitIds;
            public string Mbti;
            public string ClassLabel;
            public NpcVoice.Register Register;
            public Func<string, string> TraitName;
        }

        /// <summary>
        /// The visitor's answer to a chosen line (pure given <paramref name="rng"/>; tested). <paramref name="planned"/>
        /// are the VisitorReveal keys decided before the reply: a question that brings something out is answered
        /// ("음, 굳이 말하자면요.") and the confession follows in the visitor's voice; asked too early, they dodge.
        /// An invitation is accepted from <see cref="RuleBasedRecruiter.InviteJoinAffinity"/>, like a typed one.
        /// </summary>
        public RuleBasedRecruiter.Reply Respond(RecruitChoice c, VisitorInfo v, int affinity, IReadOnlyCollection<string> planned, GameRandom rng)
        {
            var reaction = ReactionFor(c, v.Values, v.TraitIds);
            int after = affinity + RecruitScoring.Base(reaction);
            var decision = c.Kind == RecruitChoiceKind.Invite && after >= RuleBasedRecruiter.InviteJoinAffinity
                ? RecruitDecision.Join : RecruitDecision.Undecided;

            string key = decision == RecruitDecision.Join ? "join" : c.Kind switch
            {
                RecruitChoiceKind.Invite => "invite.notyet",
                RecruitChoiceKind.Greet => "greet",
                RecruitChoiceKind.About => "about",
                RecruitChoiceKind.Guild => "guild",
                RecruitChoiceKind.AskValues => Brings(planned, VisitorReveal.Values) ? "ask.open" : "ask.dodge",
                RecruitChoiceKind.AskPersonality => Brings(planned, VisitorReveal.Personality) ? "ask.open" : "ask.dodge",
                RecruitChoiceKind.AskTraits => BringsTrait(planned) ? "ask.open" : "ask.dodge",
                RecruitChoiceKind.Pitch => (c.Topic == v.Values ? "keen." : "meh.") + c.Topic,
                RecruitChoiceKind.Trait => reaction == RecruitReaction.TraitMatch ? "trait.hit" : "trait.miss",
                _ => "guild",
            };
            string text = key == "trait.hit" ? TraitHit(c, v, rng) : Line(key, v.Register, rng);
            if (text == null) return RuleBasedRecruiter.Respond(c.Text, v.Values, affinity, false, rng, v.ClassLabel, v.Register, v.TraitIds);
            text = Tic(v.TraitIds, text.Replace("{class}", string.IsNullOrEmpty(v.ClassLabel) ? "모험가" : v.ClassLabel), rng);
            if (decision != RecruitDecision.Join && planned != null)
                foreach (var k in planned)
                {
                    string said = Confession(k, v);
                    if (!string.IsNullOrEmpty(said) && !text.Contains(said)) text += " " + said;
                }
            return new RuleBasedRecruiter.Reply { Text = text, AffinityDelta = RecruitScoring.Base(reaction), Decision = decision, Reaction = reaction };
        }

        /// <summary>
        /// The visitor's answer to an argument after the roll (pure given <paramref name="rng"/>; tested): keen on what they
        /// care about, plainer when a general line worked, unmoved or offended when it did not; "one more thing" when a
        /// stage is cleared, the yes when the last one is, the no when the talk is lost.
        /// </summary>
        public string RespondArgument(RecruitChoice c, VisitorInfo v, PersuadeRoll roll, PersuadeState state,
                                      IReadOnlyCollection<string> planned, GameRandom rng, string nextTopic = null)
        {
            var reaction = ReactionFor(c, v.Values, v.TraitIds);
            string key;
            if (state == PersuadeState.Won) key = "join";
            else if (RecruitPersuasion.IsSuccess(roll))
                key = reaction == RecruitReaction.ValueMatch ? "keen." + c.Topic
                    : reaction == RecruitReaction.TraitMatch ? "trait.hit"
                    : roll == PersuadeRoll.CriticalSuccess ? "roll.great" : "roll.ok";
            else key = roll == PersuadeRoll.CriticalFailure ? "roll.offended" : "roll.no";
            string text = (key == "trait.hit" ? TraitHit(c, v, rng) : Line(key, v.Register, rng)) ?? "…";
            text = Tic(v.TraitIds, text, rng);
            if (state == PersuadeState.Lost) return Join(text, Line("refuse", v.Register, rng));
            if (state != PersuadeState.Won && planned != null)
                foreach (var k in planned)
                {
                    string said = Confession(k, v);
                    if (!string.IsNullOrEmpty(said) && !text.Contains(said)) text += " " + said;
                }
            return text;
        }

        /// <summary>
        /// What the visitor's answer calls for next (pure; tested): their own question answered with a pitch on it, the
        /// guild they want to hear more about, what they care about once it is out. Null when the answer closes the topic.
        /// </summary>
        public static (RecruitChoiceKind? kind, string topic) FollowUp(RecruitChoice c, PersuadeRoll? roll, PersuadeState state,
                                                                       string visitorValues, string knownValues, string nextTopic)
        {
            if (c == null || state == PersuadeState.Won || state == PersuadeState.Lost) return (null, null);
            if (state == PersuadeState.StageCleared && nextTopic != null) return (RecruitChoiceKind.Pitch, nextTopic);
            if (roll.HasValue)
            {
                if (!RecruitPersuasion.IsSuccess(roll.Value)) return (null, null);
                // "보수 얘기라면 귀가 번쩍! 얼마나 되는데요?" → more on the same.
                if (c.Kind == RecruitChoiceKind.Pitch && c.Topic == visitorValues) return (RecruitChoiceKind.Pitch, c.Topic);
                // "길드 생활이 어떤지 더 듣고 싶어요."
                if (c.Kind == RecruitChoiceKind.Guild) return (RecruitChoiceKind.Guild, null);
                return (null, null);
            }
            switch (c.Kind)
            {
                case RecruitChoiceKind.Greet: return (RecruitChoiceKind.About, null);
                case RecruitChoiceKind.About:
                    return knownValues == null ? (RecruitChoiceKind.AskValues, null) : (RecruitChoiceKind.Pitch, knownValues);
                case RecruitChoiceKind.AskValues:
                    return knownValues != null ? (RecruitChoiceKind.Pitch, knownValues) : (null, null);
                default: return (null, null);
            }
        }

        /// <summary>
        /// What the visitor says when a stage is won (2026-10-29): what they want to hear about next, as a line of its own
        /// after the stage note (user report: the move to the next stage went unsaid when it was tacked onto the answer).
        /// </summary>
        public string StageNext(NpcVoice.Register register, string topic, GameRandom rng)
            => (topic != null ? Line("stage.next." + topic, register, rng) : null) ?? Line("stage.next", register, rng);

        /// <summary>The answer when a line won over a trait: as that trait would say it (2026-10-29), else the shared one.</summary>
        string TraitHit(RecruitChoice c, VisitorInfo v, GameRandom rng)
        {
            string id = WantedTrait(c, v.TraitIds);
            return (id != null ? Line("trait.hit." + id, v.Register, rng) : null) ?? Line("trait.hit", v.Register, rng);
        }

        /// <summary>Habits that show in how some visitors talk, whatever the line (2026-10-29): sometimes, not every time.</summary>
        public const float TicChance = 0.3f;

        static readonly (string trait, string tic)[] Tics = { ("lazy", "하암… "), ("sluggish", "음… 그러니까… "), ("coward", "저, 저기… ") };

        /// <summary>A trait's verbal habit in front of the answer now and then (pure given <paramref name="rng"/>; none without one).</summary>
        public static string Tic(IEnumerable<string> traitIds, string text, GameRandom rng)
        {
            if (rng == null || traitIds == null || string.IsNullOrEmpty(text) || text.StartsWith("…")) return text;
            foreach (var (trait, tic) in Tics)
            {
                if (!HasTrait(traitIds, trait)) continue;
                if (text.Contains(tic.Trim().TrimEnd('…', ' ')) || !rng.Chance(TicChance)) return text;
                return tic + text;
            }
            return text;
        }

        static string Join(string a, string b) => string.IsNullOrEmpty(b) ? a : string.IsNullOrEmpty(a) ? b : a + " " + b;

        static bool Brings(IReadOnlyCollection<string> planned, string key)
        {
            if (planned == null) return false;
            foreach (var p in planned) if (p == key) return true;
            return false;
        }

        static bool BringsTrait(IReadOnlyCollection<string> planned)
        {
            if (planned == null) return false;
            foreach (var p in planned) if (p != null && p.StartsWith(VisitorReveal.TraitPrefix)) return true;
            return false;
        }

        /// <summary>
        /// The visitor letting something out, in their own speech level (the rule-based typed path uses it too);
        /// null when the bank has no line for it.
        /// </summary>
        public string Confession(string key, VisitorInfo v)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (key == VisitorReveal.Values) return Line("confess.values." + v.Values, v.Register, null);
            if (key == VisitorReveal.Personality)
            {
                bool outgoing = !string.IsNullOrEmpty(v.Mbti) && char.ToUpperInvariant(v.Mbti[0]) == 'E';
                return Line("confess.personality." + (outgoing ? "E" : "I"), v.Register, null);
            }
            if (key.StartsWith(VisitorReveal.TraitPrefix))
            {
                string id = key.Substring(VisitorReveal.TraitPrefix.Length);
                return Line("confess.trait", v.Register, null)?.Replace("{trait}", v.TraitName?.Invoke(id) ?? id);
            }
            return null;
        }

        /// <summary>A line of this reply set in this speech level (warm when that level has none); null if missing.</summary>
        public string Line(string key, NpcVoice.Register register, GameRandom rng)
        {
            if (key == null || !_replies.TryGetValue(key, out var r)) return null;
            string[] lines = register switch
            {
                NpcVoice.Register.Formal => r.f,
                NpcVoice.Register.Curt => r.c,
                NpcVoice.Register.Haughty => r.h,
                _ => r.w,
            };
            if (lines == null || lines.Length == 0) lines = r.w;
            if (lines == null || lines.Length == 0) return null;
            return rng == null ? lines[0] : rng.Pick(lines);
        }
    }
}
