using System;
using System.Collections.Generic;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Welfare
{
    /// <summary>What a counsel choice is: asking, comforting, praising, small talk, a harsh word, or one aimed at a worry.</summary>
    public enum CounselChoiceKind { Listen, Empathy, Praise, Small, Tough, Touch, Solve }

    [Serializable]
    public class CounselChoiceDto
    {
        public string id;
        public string kind;
        /// <summary>For Touch / Solve: the worry it is about (a <see cref="CounselConcernKind"/> name).</summary>
        public string topic;
        public string text;
    }

    [Serializable]
    public class CounselReplyDto
    {
        public string key;
        /// <summary>Variants per speech level: warm / formal / curt / haughty (NpcVoice.Register).</summary>
        public string[] w, f, c, h;
    }

    [Serializable]
    public class CounselLinesFile
    {
        public CounselChoiceDto[] choices;
        public CounselReplyDto[] replies;
    }

    public class CounselChoice
    {
        public string Id;
        public CounselChoiceKind Kind;
        /// <summary>The worry a Touch / Solve line is about.</summary>
        public CounselConcernKind? Topic;
        /// <summary>The line as written ({subject} still in it).</summary>
        public string Template;
        /// <summary>The line as said (filled in by <see cref="CounselBank.Offer"/>).</summary>
        public string Text;
    }

    /// <summary>
    /// The counselling talk written ahead (user decision 2026-10-28: the model only for what the player types). The
    /// guild master picks one of four lines — before the worry is known, a question, a kind word and two guesses at
    /// what is wrong; once it is known, lines about that worry and an offer of help, now and then a harsh one — and
    /// the member answers in their speech level. Reactions are the same table as typed lines
    /// (<see cref="CounselScoring"/>). Data in Resources/Dialogue/Counsel/*.json; pure apart from loading, tested.
    /// </summary>
    public class CounselBank
    {
        public const string ResourceFolder = "Dialogue/Counsel";
        public const int ChoiceCount = 4;
        /// <summary>How often a harsh line is among the choices once the worry is known.</summary>
        public const float ToughChance = 0.25f;
        /// <summary>How often the real worry is among the two guesses before it is known.</summary>
        public const float RightGuessChance = 0.5f;

        readonly List<CounselChoice> _choices = new();
        readonly Dictionary<string, CounselReplyDto> _replies = new();

        public int ChoiceTotal => _choices.Count;
        public IReadOnlyList<CounselChoice> AllChoices => _choices;

        static CounselBank _shared;
        public static CounselBank Shared => _shared ??= LoadResources();

        public static CounselBank FromJson(params string[] jsons)
        {
            var bank = new CounselBank();
            foreach (var json in jsons)
            {
                if (string.IsNullOrWhiteSpace(json)) continue;
                var file = JsonUtility.FromJson<CounselLinesFile>(json);
                if (file == null) continue;
                if (file.choices != null) foreach (var c in file.choices) bank.Add(c);
                if (file.replies != null) foreach (var r in file.replies) if (!string.IsNullOrEmpty(r?.key)) bank._replies[r.key] = r;
            }
            return bank;
        }

        public static CounselBank LoadResources()
        {
            var texts = new List<string>();
            foreach (var a in Resources.LoadAll<TextAsset>(ResourceFolder)) if (a != null) texts.Add(a.text);
            return FromJson(texts.ToArray());
        }

        void Add(CounselChoiceDto c)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.text) || !Enum.TryParse(c.kind, out CounselChoiceKind kind)) return;
            CounselConcernKind? topic = null;
            if (!string.IsNullOrEmpty(c.topic))
            {
                if (!Enum.TryParse(c.topic, out CounselConcernKind t)) return;
                topic = t;
            }
            _choices.Add(new CounselChoice { Id = c.id ?? c.text, Kind = kind, Topic = topic, Template = c.text, Text = c.text });
        }

        // ---------------- Which lines to offer ----------------

        /// <summary>
        /// Four lines for the next turn (pure; tested). <paramref name="known"/>: the guild master has got to the worry.
        /// Lines naming the friend or rival ({subject}) only come up once the worry is known.
        /// </summary>
        public List<CounselChoice> Offer(CounselConcern concern, bool known, ICollection<string> used, GameRandom rng)
        {
            var result = new List<CounselChoice>();
            void Put(CounselChoice c) { if (c != null && !result.Exists(x => x.Id == c.Id)) result.Add(Fill(c, concern)); }

            if (!known)
            {
                Put(Pick(CounselChoiceKind.Listen, null, used, rng, concern));
                Put(Pick(CounselChoiceKind.Empathy, null, used, rng, concern));
                // Guesses at the worry; grief only when it really is grief (no one guesses at a death that did not happen).
                var others = new List<CounselConcernKind>();
                foreach (CounselConcernKind k in Enum.GetValues(typeof(CounselConcernKind)))
                    if (k != concern.Kind && k != CounselConcernKind.Grief) others.Add(k);
                rng.Shuffle(others);
                var guesses = new List<CounselConcernKind> { rng.Chance(RightGuessChance) ? concern.Kind : others[0], others[1] };
                foreach (var g in guesses) Put(Pick(CounselChoiceKind.Touch, g, used, rng, concern));
            }
            else
            {
                Put(Pick(CounselChoiceKind.Touch, concern.Kind, used, rng, concern) ?? Pick(CounselChoiceKind.Empathy, null, used, rng, concern));
                Put(Pick(CounselChoiceKind.Solve, concern.Kind, used, rng, concern));
                Put(Pick(rng.Chance(0.5f) ? CounselChoiceKind.Praise : CounselChoiceKind.Listen, null, used, rng, concern));
                Put(Pick(rng.Chance(ToughChance) ? CounselChoiceKind.Tough : CounselChoiceKind.Small, null, used, rng, concern));
            }
            foreach (var spare in new[] { CounselChoiceKind.Praise, CounselChoiceKind.Small, CounselChoiceKind.Empathy, CounselChoiceKind.Listen })
                if (result.Count < ChoiceCount) Put(Pick(spare, null, used, rng, concern));
            rng.Shuffle(result);
            return result;
        }

        CounselChoice Pick(CounselChoiceKind kind, CounselConcernKind? topic, ICollection<string> used, GameRandom rng, CounselConcern concern)
        {
            var fresh = new List<CounselChoice>();
            var any = new List<CounselChoice>();
            foreach (var c in _choices)
            {
                if (c.Kind != kind || (topic != null && c.Topic != topic)) continue;
                if (c.Template.Contains("{subject}") && string.IsNullOrEmpty(concern?.Subject)) continue;
                any.Add(c);
                if (used == null || !used.Contains(c.Id)) fresh.Add(c);
            }
            var pool = fresh.Count > 0 ? fresh : any;
            return pool.Count == 0 ? null : rng.Pick(pool);
        }

        static CounselChoice Fill(CounselChoice c, CounselConcern concern) => new()
        {
            Id = c.Id, Kind = c.Kind, Topic = c.Topic, Template = c.Template,
            Text = c.Template.Replace("{subject}", concern?.Subject ?? ""),
        };

        // ---------------- How the member takes it ----------------

        /// <summary>The kind of line (the same table typed lines are scored by) and whether it goes to the real worry.</summary>
        public static (CounselReaction reaction, bool touched) ReactionFor(CounselChoice c, CounselConcern concern)
        {
            var r = c.Kind switch
            {
                CounselChoiceKind.Listen => CounselReaction.Listen,
                CounselChoiceKind.Empathy => CounselReaction.Empathy,
                CounselChoiceKind.Praise => CounselReaction.Praise,
                CounselChoiceKind.Tough => CounselReaction.Dismiss,
                CounselChoiceKind.Touch => CounselReaction.Empathy,
                CounselChoiceKind.Solve => CounselReaction.Solution,
                _ => CounselReaction.Small,
            };
            bool touched = (c.Kind == CounselChoiceKind.Touch || c.Kind == CounselChoiceKind.Solve) && concern != null && c.Topic == concern.Kind;
            return (r, touched);
        }

        /// <summary>
        /// The member's answer (pure given <paramref name="rng"/>; tested). A line that gets to the worry is thanked for
        /// it; a kind word or a question to someone who has not said yet what is wrong gets it out (a wrong guess too:
        /// "그것 때문은 아니에요." and then the real thing); once it is out, they do not repeat it.
        /// </summary>
        public (string text, CounselReaction reaction, bool touched) Respond(CounselChoice c, CounselConcern concern, bool known,
                                                                            NpcVoice.Register register, GameRandom rng)
        {
            var (reaction, touched) = ReactionFor(c, concern);
            string kind = concern.Kind.ToString();
            bool spills = !known && CounselScoring.OpensUp(reaction);
            string text;
            if (touched)
                text = Line((c.Kind == CounselChoiceKind.Solve ? "solved." : "touched.") + kind, register, rng);
            else
                text = c.Kind switch
                {
                    CounselChoiceKind.Touch => Join(Line("wrong.guess", register, rng), spills ? Line("confess." + kind, register, rng) : null),
                    CounselChoiceKind.Solve => Line("solution.miss", register, rng),
                    CounselChoiceKind.Listen => spills ? Line("confess." + kind, register, rng) : Line("listen.known", register, rng),
                    CounselChoiceKind.Empathy => spills ? Line("confess." + kind, register, rng) : Line("empathy.known", register, rng),
                    CounselChoiceKind.Praise => Join(Line("praise", register, rng), spills ? Line("confess." + kind, register, rng) : null),
                    CounselChoiceKind.Tough => Line("dismiss", register, rng),
                    _ => Line("small", register, rng),
                };
            if (string.IsNullOrEmpty(text))
                text = CounselLines.Reply(reaction, concern, known || CounselScoring.OpensUp(reaction), touched, rng);
            return (FillSubject(text, concern), reaction, touched);
        }

        static string Join(string a, string b) => string.IsNullOrEmpty(b) ? a : string.IsNullOrEmpty(a) ? b : a + " " + b;

        static string FillSubject(string text, CounselConcern concern)
            => text?.Replace("{subject}", string.IsNullOrEmpty(concern?.Subject) ? "그 친구" : concern.Subject);

        /// <summary>First words when called in (in the member's speech level); null when the bank has none.</summary>
        public string Opening(CounselConcern concern, bool open, NpcVoice.Register register, GameRandom rng)
            => FillSubject(Line(open ? "opening." + concern.Kind : "opening.closed", register, rng), concern);

        public string Closing(CounselOutcome outcome, NpcVoice.Register register, GameRandom rng)
            => Line("closing." + outcome, register, rng);

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
