using System.Collections.Generic;
using GuildMaster.Units;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// What an adventurer can learn and how much they can hold (P4-34, 2026-09-20 user decision).
    /// Everything here is a pure rule so the choice can be tested without a scene.
    /// P4-126 (user decision 2026-11-04): skills come in grades opened by level (<see cref="RequiredLevel"/>) — a higher
    /// grade pushes out a lower one — and within a grade each adventurer has its own taste (<see cref="Fit"/>), so two of a
    /// class need not carry the same three.
    /// </summary>
    public static class SkillCatalog
    {
        /// <summary>Nobody carries more than this; learning a fourth means forgetting one.</summary>
        public const int MaxSkills = 3;

        /// <summary>Levels that open the adept and master grades (the basic grade is open from the start).</summary>
        public const int AdeptLevel = 8, MasterLevel = 15;

        /// <summary>How far a personal taste can move a skill along its class order (in places of the pool).</summary>
        public const float TasteSpread = 2.5f;
        /// <summary>
        /// How many places each grade is worth: a higher grade mostly wins, but a basic skill that suits someone very well
        /// can stay over an adept one that suits them badly — so not every Lv20 of a class ends up with the same three.
        /// </summary>
        public const float GradeWeight = 2f;

        /// <summary>
        /// What each class can learn, most fitting first within each grade (basic, then adept, then master).
        /// </summary>
        public static IReadOnlyList<string> LearnPool(AdventurerClass cls) => cls switch
        {
            AdventurerClass.Warrior => new[] { "taunt", "whirlwind", "charge", "slam", "ironwall", "execute", "warcry" },
            AdventurerClass.Ranger => new[] { "pierce", "stealth", "blink", "charge", "slam", "poison_arrow", "snare", "volley" },
            AdventurerClass.Priest => new[] { "massheal", "heal", "blink", "slam", "holy_shield", "smite", "bless" },
            _ => new[] { "fireball", "warp", "blink", "slam", "charge", "chain_lightning", "frost_ring", "meteor" },
        };

        /// <summary>Grade of a skill: 0 basic, 1 adept, 2 master.</summary>
        public static int Grade(string id) => id switch
        {
            "ironwall" or "execute" or "poison_arrow" or "snare" or "holy_shield" or "smite" or "chain_lightning" or "frost_ring" => 1,
            "warcry" or "volley" or "bless" or "meteor" => 2,
            _ => 0,
        };

        /// <summary>The level an adventurer needs to learn this skill.</summary>
        public static int RequiredLevel(string id) => Grade(id) switch { 2 => MasterLevel, 1 => AdeptLevel, _ => 1 };

        /// <summary>Skills of this class not learned yet, in pool order. Empty when there is nothing left.</summary>
        public static List<string> Candidates(AdventurerClass cls, IReadOnlyList<string> known)
        {
            var list = new List<string>();
            foreach (var id in LearnPool(cls))
                if (IndexOf(known, id) < 0) list.Add(id);
            return list;
        }

        /// <summary>The candidates this level opens.</summary>
        public static List<string> Candidates(AdventurerClass cls, IReadOnlyList<string> known, int level)
        {
            var list = Candidates(cls, known);
            list.RemoveAll(id => RequiredLevel(id) > level);
            return list;
        }

        /// <summary>True when taking one more would go over the cap, so something has to go.</summary>
        public static bool MustForget(IReadOnlyList<string> known)
            => known != null && known.Count >= MaxSkills;

        /// <summary>
        /// How well a skill suits this adventurer, lower is better: its place in the class order within its grade, moved
        /// by up to <see cref="TasteSpread"/> places by a taste fixed for the person (<paramref name="person"/>, the
        /// adventurer's id; null or empty = the plain class order), less <see cref="GradeWeight"/> a grade. Off-pool skills
        /// suit nobody.
        /// </summary>
        public static float Fit(AdventurerClass cls, string id, string person)
        {
            var pool = LearnPool(cls);
            int rank = IndexOf(pool, id);
            if (rank < 0) return float.MaxValue;
            int grade = Grade(id), first = 0;
            while (Grade(pool[first]) != grade) first++;
            return rank - first + (string.IsNullOrEmpty(person) ? 0f : Taste(person, id) * TasteSpread) - grade * GradeWeight;
        }

        /// <summary>A number in [0, 1) fixed for a person and a skill (FNV-1a: the same in every run and build).</summary>
        public static float Taste(string person, string id)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in person) h = (h ^ c) * 16777619;
                h = (h ^ '|') * 16777619;
                foreach (char c in id) h = (h ^ c) * 16777619;
                h ^= h >> 15;
                h *= 2246822519;
                h ^= h >> 13;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>A is the better one to have than b (a better <see cref="Fit"/>).</summary>
        static bool Better(AdventurerClass cls, string a, string b, string person) => Fit(cls, a, person) < Fit(cls, b, person);

        /// <summary>
        /// The one to give up: anything off the pool first, then the worst fit (grade counted).
        /// </summary>
        public static string RuleForget(AdventurerClass cls, IReadOnlyList<string> known, string person = null)
        {
            if (known == null || known.Count == 0) return null;
            var pool = LearnPool(cls);
            string worst = null;
            foreach (var id in known)
            {
                if (IndexOf(pool, id) < 0) return id; // not a class skill at all: drop it first
                if (worst == null || Better(cls, worst, id, person)) worst = id;
            }
            return worst;
        }

        /// <summary>
        /// The whole rule-based choice: the best candidate this level opens, giving up the worst known skill when full —
        /// but only when the new one is better than what it replaces (its fit, grade counted).
        /// Otherwise nothing changes (P4-73: a mage dropped blink for slam, then slam for blink, every level — fixed tastes
        /// cannot do that, the one given up is never better than what stays).
        /// </summary>
        public static (string learn, string forget) RuleChoice(AdventurerClass cls, IReadOnlyList<string> known, IReadOnlyList<string> candidates,
                                                               int level, string person = null)
        {
            string learn = null;
            if (candidates != null)
                foreach (var id in candidates)
                    if (RequiredLevel(id) <= level && (learn == null || Better(cls, id, learn, person))) learn = id;
            if (learn == null) return (null, null);
            if (!MustForget(known)) return (learn, null);
            string forget = RuleForget(cls, known, person);
            if (IndexOf(LearnPool(cls), forget) >= 0 && !Better(cls, learn, forget, person)) return (null, null);
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
