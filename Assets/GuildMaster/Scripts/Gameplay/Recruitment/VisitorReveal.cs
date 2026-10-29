using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;

namespace GuildMaster.Recruitment
{
    /// <summary>Published when talking to a visitor brought out one of their hidden facts.</summary>
    public struct VisitorRevealedEvent
    {
        public Adventurer Visitor;
        /// <summary><see cref="VisitorReveal.Values"/>, <see cref="VisitorReveal.Personality"/> or "trait:&lt;id&gt;".</summary>
        public string Key;
        /// <summary>Ready to show, e.g. "가치관: 돈".</summary>
        public string Label;
    }

    /// <summary>One fact a visitor gives away, and why (shown next to it: "관심사를 건드리자 눈빛이 달라짐").</summary>
    public struct RevealStep
    {
        public string Key;
        public string Reason;

        public RevealStep(string key, string reason) { Key = key; Reason = reason; }
    }

    /// <summary>
    /// What the guild master has found out about a visitor, and the rules for finding out more. A visitor's
    /// personality (MBTI), values and traits start hidden in the recruit window and the inspector card and stay
    /// known once out (<see cref="AdventurerData.revealed"/>).
    ///
    /// Rewritten 2026-10-07 (user report: "대화 시 정보가 드러나는 포인트가 애매모호함"). The model used to say what
    /// a line had revealed and the code checked it against word lists, so the same words revealed something one
    /// time and nothing the next. Now fixed rules decide, the window shows them next to each "???", and every
    /// reveal says why:
    /// <list type="bullet">
    /// <item>성격: after <see cref="PersonalityTurns"/> lines from the guild master (the way they talk gives it
    /// away), or at once when asked what kind of person they are.</item>
    /// <item>가치관: a line that touches what they care about (<see cref="RecruitReaction.ValueMatch"/>) or goes
    /// against it; or asked what matters to them once affinity is at least <see cref="ValuesAskAffinity"/>.</item>
    /// <item>특성: a line offering what a trait wants (<see cref="RecruitReaction.TraitMatch"/>); affinity passing
    /// <see cref="OpenUpAffinity"/> (they open up, one trait each time); or asked about strengths and weaknesses
    /// once affinity is at least <see cref="TraitsAskAffinity"/>.</item>
    /// </list>
    /// What is decided before the reply (asked, enough talk) is passed to the model so the line says it.
    /// Pure logic (no Unity), covered by EditMode tests.
    /// </summary>
    public static class VisitorReveal
    {
        public const string None = "none";
        public const string Values = "values";
        public const string Personality = "personality";
        public const string TraitPrefix = "trait:";

        /// <summary>Guild master lines after which the visitor's manner has shown their personality.</summary>
        public const int PersonalityTurns = 3;
        /// <summary>Affinity from which a visitor answers honestly what matters to them.</summary>
        public const int ValuesAskAffinity = 40;
        /// <summary>Affinity from which a visitor answers honestly about their strengths and weaknesses.</summary>
        public const int TraitsAskAffinity = 50;
        /// <summary>Passing each of these, a visitor opens up about one more trait by themselves.</summary>
        public static readonly int[] OpenUpAffinity = { 55, 70 };

        public static string TraitKey(string traitId) => TraitPrefix + traitId;

        public static bool IsRevealed(AdventurerData d, string key)
            => d?.revealed != null && !string.IsNullOrEmpty(key) && d.revealed.Contains(key);

        /// <summary>Marks a fact as known. True only the first time (so the caller announces it once).</summary>
        public static bool Reveal(AdventurerData d, string key)
        {
            if (d == null || string.IsNullOrEmpty(key) || key == None) return false;
            d.revealed ??= new List<string>();
            if (d.revealed.Contains(key)) return false;
            d.revealed.Add(key);
            return true;
        }

        /// <summary>The first of the visitor's traits still hidden, or null.</summary>
        public static string NextHiddenTrait(AdventurerData d)
            => d?.traitIds?.FirstOrDefault(id => !string.IsNullOrEmpty(id) && !IsRevealed(d, TraitKey(id)));

        // ---------------- What the guild master asked ----------------

        static readonly string[] PersonalityQuestions = { "성격", "어떤 사람", "어떤 분", "어떤 타입", "어떤 스타일", "MBTI", "mbti" };
        static readonly string[] ValuesQuestions =
        {
            "중요", "소중", "목표", "원하는", "원해", "바라는", "꿈이", "꿈은", "왜 모험", "모험가가 된", "뭘 위해", "무엇을 위해", "가치관", "제일 좋", "가장 좋",
        };
        static readonly string[] TraitQuestions = { "장점", "단점", "특기", "잘하는", "잘하세요", "못하는", "약점", "강점", "자신 있", "자신있" };

        static bool Asks(string text, string[] words)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (var w in words) if (text.Contains(w)) return true;
            return false;
        }

        public static bool AsksPersonality(string text) => Asks(text, PersonalityQuestions);
        public static bool AsksValues(string text) => Asks(text, ValuesQuestions);
        public static bool AsksTraits(string text) => Asks(text, TraitQuestions);

        // ---------------- The rules ----------------

        /// <summary>
        /// Decided before the visitor answers, so the model can say it: personality after enough talk or when asked,
        /// values or a trait when asked and liked enough. <paramref name="hint"/> explains a question that was asked
        /// too early ("호감 40 이상이면 털어놓음"), or is null.
        /// </summary>
        public static List<RevealStep> BeforeReply(AdventurerData d, string playerText, int playerTurns, int affinity, out string hint)
        {
            hint = null;
            var steps = new List<RevealStep>();
            if (d == null) return steps;
            if (!IsRevealed(d, Personality) && !string.IsNullOrEmpty(d.mbti))
            {
                if (AsksPersonality(playerText)) steps.Add(new RevealStep(Personality, "성격을 묻자 스스로 이야기함"));
                else if (playerTurns >= PersonalityTurns) steps.Add(new RevealStep(Personality, "몇 마디 나누니 말투와 태도에서 드러남"));
            }
            if (!IsRevealed(d, Values) && !string.IsNullOrEmpty(d.values) && AsksValues(playerText))
            {
                if (affinity >= ValuesAskAffinity) steps.Add(new RevealStep(Values, "무엇이 중요한지 묻자 털어놓음"));
                else hint = $"가치관을 물었지만 아직 경계해서 얼버무림 (호감 {ValuesAskAffinity} 이상이면 털어놓음)";
            }
            string trait = NextHiddenTrait(d);
            if (trait != null && AsksTraits(playerText))
            {
                if (affinity >= TraitsAskAffinity) steps.Add(new RevealStep(TraitKey(trait), "장단점을 묻자 털어놓음"));
                else hint ??= $"장단점을 물었지만 아직 말하지 않음 (호감 {TraitsAskAffinity} 이상이면 털어놓음)";
            }
            return steps;
        }

        /// <summary>
        /// What the answered line gave away: touching (or crossing) what they care about shows their values, offering
        /// what a trait wants shows that trait (<paramref name="wantedTrait"/>, or the first hidden one when only the
        /// model heard it), and passing an <see cref="OpenUpAffinity"/> mark makes them open up about one more trait.
        /// <paramref name="planned"/> are the steps already decided before the reply (not repeated).
        /// </summary>
        public static List<RevealStep> AfterReply(AdventurerData d, RecruitReaction reaction, bool valueClash, string wantedTrait,
                                                  int affinityBefore, int affinityAfter, IEnumerable<RevealStep> planned = null)
        {
            var steps = new List<RevealStep>();
            if (d == null) return steps;
            var taken = new HashSet<string>(planned?.Select(s => s.Key) ?? Enumerable.Empty<string>());
            void Add(string key, string reason)
            {
                if (key == null || IsRevealed(d, key) || !taken.Add(key)) return;
                steps.Add(new RevealStep(key, reason));
            }
            if (!string.IsNullOrEmpty(d.values))
            {
                if (reaction == RecruitReaction.ValueMatch) Add(Values, "관심사를 건드리자 눈빛이 달라짐");
                else if (valueClash) Add(Values, "싫어하는 이야기에 발끈하며 속내가 드러남");
            }
            if (reaction == RecruitReaction.TraitMatch)
            {
                // The trait the words were for, and only that one (user report 2026-10-29: a skill pitch to a marksman
                // already known "revealed" his laziness as if he had warmed to it). Only a line the model judged, with
                // no trait word the rules can see, falls back to the first hidden trait.
                string id = wantedTrait != null
                    ? (d.traitIds != null && d.traitIds.Contains(wantedTrait) ? wantedTrait : null)
                    : FirstHiddenExcept(d, taken);
                if (id != null) Add(TraitKey(id), "좋아할 만한 이야기에 반응하며 드러남");
            }
            foreach (int mark in OpenUpAffinity)
            {
                if (affinityBefore >= mark || affinityAfter < mark) continue;
                string id = FirstHiddenExcept(d, taken);
                if (id != null) Add(TraitKey(id), "마음을 열고 스스로 털어놓음");
            }
            return steps;
        }

        static string FirstHiddenExcept(AdventurerData d, HashSet<string> taken)
            => d.traitIds?.FirstOrDefault(id => !string.IsNullOrEmpty(id) && !IsRevealed(d, TraitKey(id)) && !taken.Contains(TraitKey(id)));

        // ---------------- Words for the window and the model ----------------

        /// <summary>How the player can find out what is still hidden (shown next to the "???").</summary>
        public static string HowToFind(string kind) => kind switch
        {
            Personality => $"{PersonalityTurns}마디 이상 대화하거나 성격을 물어보기",
            Values => $"관심사를 건드리기(▲관심) · 호감 {ValuesAskAffinity}↑에서 무엇이 중요한지 묻기",
            _ => $"좋아할 이야기 하기(▲마음에 듦) · 호감 {string.Join("/", OpenUpAffinity)} 돌파 · 호감 {TraitsAskAffinity}↑에서 장단점 묻기",
        };

        /// <summary>What the model is told to let out in this reply ("네가 가장 중요하게 여기는 것(돈)").</summary>
        public static string ForModel(string key, AdventurerData d, System.Func<string, string> traitName = null)
        {
            if (key == Values) return $"네가 가장 중요하게 여기는 것({d.values})";
            if (key == Personality) return "네가 어떤 성격인지";
            if (key != null && key.StartsWith(TraitPrefix))
            {
                string id = key.Substring(TraitPrefix.Length);
                return $"네 특징 '{traitName?.Invoke(id) ?? id}'";
            }
            return "";
        }

        /// <summary>A plain first-person line saying it (rule-based replies).</summary>
        public static string Confession(string key, AdventurerData d, System.Func<string, string> traitName = null)
        {
            if (key == Values) return d.values switch
            {
                "돈" => "솔직히 전 벌이가 제일 중요해요.",
                "명예" => "전 이름을 떨치는 게 제일 중요해요.",
                "동료" => "전 믿을 수 있는 동료가 제일 중요해요.",
                "모험" => "전 새로운 곳을 탐험하는 게 제일 좋아요.",
                "안전" => "전 무엇보다 안전이 제일이에요.",
                "지식" => "전 뭔가를 배우고 알아가는 게 제일 좋아요.",
                _ => "제겐 나름 중요한 게 있어요.",
            };
            if (key == Personality)
                return !string.IsNullOrEmpty(d.mbti) && char.ToUpperInvariant(d.mbti[0]) == 'E'
                    ? "전 원래 사람 만나서 떠드는 걸 좋아해요." : "전 원래 말수가 적은 편이에요.";
            if (key != null && key.StartsWith(TraitPrefix))
                return $"사실 저, '{traitName?.Invoke(key.Substring(TraitPrefix.Length)) ?? key.Substring(TraitPrefix.Length)}' 소리 좀 들어요.";
            return "";
        }

        /// <summary>
        /// "가치관: 돈", "성격: ESFJ", "특성: 오만함". <paramref name="traitName"/> maps a trait id to its display
        /// name (the id itself when null).
        /// </summary>
        public static string Label(string key, AdventurerData d, System.Func<string, string> traitName = null)
        {
            if (d == null || string.IsNullOrEmpty(key)) return "";
            if (key == Values) return "가치관: " + d.values;
            if (key == Personality) return "성격: " + d.mbti;
            if (key.StartsWith(TraitPrefix))
            {
                string id = key.Substring(TraitPrefix.Length);
                return "특성: " + (traitName?.Invoke(id) ?? id);
            }
            return key;
        }

        public static string TraitName(Adventurer a, string id)
            => a?.Traits?.Traits.FirstOrDefault(t => t != null && t.id == id)?.displayName ?? id;

        public static string Label(string key, Adventurer a) => Label(key, a?.Data, id => TraitName(a, id));
    }
}
