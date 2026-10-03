using System.Collections.Generic;
using GuildMaster.Units;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// What an adventurer can learn and how much they can hold (P4-34, 2026-09-20 user decision).
    /// Everything here is a pure rule so the choice can be tested without a scene.
    /// </summary>
    public static class SkillCatalog
    {
        /// <summary>Nobody carries more than this; learning a fourth means forgetting one.</summary>
        public const int MaxSkills = 3;

        /// <summary>
        /// What each class can learn, most fitting first. The rule-based fallback follows this
        /// order, and it is also the order the candidates are offered to the model in.
        /// </summary>
        public static IReadOnlyList<string> LearnPool(AdventurerClass cls) => cls switch
        {
            AdventurerClass.Warrior => new[] { "taunt", "whirlwind", "charge", "slam" },
            AdventurerClass.Ranger => new[] { "pierce", "stealth", "blink", "charge", "slam" },
            AdventurerClass.Priest => new[] { "massheal", "heal", "blink", "slam" },
            _ => new[] { "fireball", "warp", "blink", "slam", "charge" },
        };

        /// <summary>Skills of this class not learned yet, in pool order. Empty when there is nothing left.</summary>
        public static List<string> Candidates(AdventurerClass cls, IReadOnlyList<string> known)
        {
            var list = new List<string>();
            foreach (var id in LearnPool(cls))
                if (IndexOf(known, id) < 0) list.Add(id);
            return list;
        }

        /// <summary>True when taking one more would go over the cap, so something has to go.</summary>
        public static bool MustForget(IReadOnlyList<string> known)
            => known != null && known.Count >= MaxSkills;

        /// <summary>
        /// Fallback pick when the model is unavailable or answers badly: learn the most fitting
        /// candidate, and give up the one that suits the class least (last in the pool order;
        /// anything off-pool goes first).
        /// </summary>
        public static string RuleForget(AdventurerClass cls, IReadOnlyList<string> known)
        {
            if (known == null || known.Count == 0) return null;
            var pool = LearnPool(cls);
            string worst = null;
            int worstRank = int.MinValue;
            foreach (var id in known)
            {
                int rank = IndexOf(pool, id);
                if (rank < 0) rank = int.MaxValue; // not a class skill at all: drop it first
                if (rank <= worstRank) continue;
                worstRank = rank;
                worst = id;
            }
            return worst;
        }

        /// <summary>
        /// The whole rule-based choice: the most fitting candidate, giving up the least fitting known
        /// skill when full — but only when the new one fits better than what it replaces. Otherwise
        /// nothing changes (P4-73: a mage dropped blink for slam, then slam for blink, every level).
        /// </summary>
        public static (string learn, string forget) RuleChoice(AdventurerClass cls, IReadOnlyList<string> known, IReadOnlyList<string> candidates)
        {
            if (candidates == null || candidates.Count == 0) return (null, null);
            string learn = candidates[0];
            if (!MustForget(known)) return (learn, null);
            string forget = RuleForget(cls, known);
            var pool = LearnPool(cls);
            int forgetRank = IndexOf(pool, forget);
            if (forgetRank >= 0 && IndexOf(pool, learn) >= forgetRank) return (null, null);
            return (learn, forget);
        }

        /// <summary>Position of an id in a list, or -1 (IReadOnlyList has no IndexOf).</summary>
        public static int IndexOf(IReadOnlyList<string> list, string id)
        {
            if (list == null || id == null) return -1;
            for (int i = 0; i < list.Count; i++) if (list[i] == id) return i;
            return -1;
        }

        /// <summary>Korean name of a skill id, for logs and prompts ("" when unknown).</summary>
        public static string NameOf(string id) => SkillFactory.Create(id)?.Name ?? "";

        /// <summary>Player-facing explanation of a skill id ("" when unknown).</summary>
        public static string DescriptionOf(string id) => SkillFactory.Create(id)?.Description ?? "";
    }
}
