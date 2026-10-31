using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Nemeses
{
    /// <summary>What a nemesis did to a member: killed it, or drove it off near death.</summary>
    public enum DeedKind { Killed, DroveOff }

    /// <summary>One thing a nemesis did that the guild saw (saved with it).</summary>
    [System.Serializable]
    public class NemesisDeed
    {
        public string victim_id;
        public string victim;
        public DeedKind kind;
        public float hour;
    }

    /// <summary>
    /// Nemesis monsters (P4-109, G4 — Shadow of Mordor's nemeses, user decisions 2026-10-30: a monster that kills a member or
    /// drives one off near death, as the guild sees it, gets a name, a title from what it did and levels; it behaves as its
    /// kind always did; the town puts a bounty on it, and the friends of the one it struck want that bounty). Pure: names,
    /// titles, how much it grows, what its bounty pays and how much a grudge counts in a member's choices.
    /// </summary>
    public static class NemesisRules
    {
        /// <summary>Levels a nemesis gains for a member killed, and for one driven off.</summary>
        public const int KillLevels = 2, DriveOffLevels = 1;

        /// <summary>A member who liked the fallen at least this much (a friend, F-2) holds a grudge against its killer.</summary>
        public const int GrudgeFrom = Welfare.StressRules.FriendRelation;

        /// <summary>A grudge-holder values a quest against its nemesis this much more (the bounty, the dragon's or a lord's hunt).</summary>
        public const float RevengeValue = 1.6f;

        /// <summary>A grudge-holder says yes to an invitation against its nemesis this much more often (as the guild's biggest fights, P4-97).</summary>
        public const float RevengeEagerness = 0.3f;

        /// <summary>The bounty pays a five-kill hunt of its danger at that distance times this: more for a killer, more for each kill.</summary>
        public static float BountyFactor(int kills)
            => Mathf.Min(4f, kills > 0 ? 2f + 0.5f * (kills - 1) : 1.5f);

        /// <summary>
        /// While nobody has seen it for this many game hours and it has left the bounty's area, the town hears where it is now
        /// (a rumour's circle, as for a horde on the move).
        /// </summary>
        public const float RumourEvery = 6f;

        /// <summary>Levels it gains for a deed, kept under the cap (a region lord and the dragon never grow: <paramref name="grows"/> false).</summary>
        public static int GrownLevel(int level, DeedKind kind, bool grows, int cap)
            => grows ? Mathf.Min(cap, level + (kind == DeedKind.Killed ? KillLevels : DriveOffLevels)) : level;

        // ---------------- Names ----------------

        static readonly string[] Goblin = { "그릭", "스낙", "우르크", "바즈", "크롤", "기즈", "녹스", "라트", "짜그", "무르그", "보크", "끄룩" };
        static readonly string[] Skull = { "달그락", "덜그럭", "앙상이", "뼈마디", "마른손", "빈눈", "삐걱이", "흰턱" };
        static readonly string[] Slime = { "물컹이", "끈적이", "말랑이", "출렁이", "꿀렁이", "푸딩", "뭉글이", "질척이" };
        static readonly string[] Other = { "흉터", "외눈", "검은발", "찢긴귀", "붉은눈", "부러진이빨" };

        /// <summary>The names a kind's nemeses are given (goblins, skulls, slimes by their id).</summary>
        public static IReadOnlyList<string> NamesFor(string speciesId)
        {
            string id = speciesId ?? "";
            return id.Contains("goblin") ? Goblin : id.Contains("skull") ? Skull : id.Contains("slime") ? Slime : Other;
        }

        /// <summary>
        /// Its own name, picked by its id (not the game's dice, so a nemesis never moves the world's random numbers) and stepping
        /// past names its kind's living nemeses already have; past all of them, numbered ("그릭 2").
        /// </summary>
        public static string PersonalName(string unitId, string speciesId, ICollection<string> taken)
        {
            var names = NamesFor(speciesId);
            int start = (int)(Hash(unitId) % (uint)names.Count);
            for (int k = 0; k < names.Count; k++)
            {
                string n = names[(start + k) % names.Count];
                if (taken == null || !taken.Contains(n)) return n;
            }
            for (int round = 2; ; round++)
            {
                string n = $"{names[start]} {round}";
                if (!taken.Contains(n)) return n;
            }
        }

        /// <summary>FNV-1a over the id (string hashing may differ between runtimes; this does not).</summary>
        static uint Hash(string s)
        {
            uint h = 2166136261;
            if (s != null) foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        // ---------------- Titles ----------------

        /// <summary>
        /// What its deeds made of it: "레오 잡이" for a member killed, "레오·미나 잡이" for two, "학살자" from three; with no kill,
        /// "레오를 쫓아낸", "레오·미나를 쫓아낸", and "공포의" from three driven off.
        /// </summary>
        public static string Title(IReadOnlyList<NemesisDeed> deeds)
        {
            var killed = new List<string>();
            var drove = new List<string>();
            if (deeds != null)
                foreach (var d in deeds)
                    (d.kind == DeedKind.Killed ? killed : drove).Add(d.victim);
            if (killed.Count >= 3) return "학살자";
            if (killed.Count > 0) return string.Join("·", killed) + " 잡이";
            if (drove.Count >= 3) return "공포의";
            if (drove.Count == 0) return "";
            string last = drove[drove.Count - 1];
            return string.Join("·", drove) + Adventurers.MemoryRecorder.Josa(last, "을", "를") + " 쫓아낸";
        }

        /// <summary>Its full name: the title, what it was called, its own name ("레오 잡이 고블린 그릭"; no own name for the rare, the dragon, a lord).</summary>
        public static string FullName(string title, string baseName, string personal)
        {
            string name = string.IsNullOrEmpty(personal) ? baseName : $"{baseName} {personal}";
            return string.IsNullOrEmpty(title) ? name : $"{title} {name}";
        }

        /// <summary>The title a member earns by bringing one down ("그릭 잡이").</summary>
        public static string SlayerTitle(string shortName) => $"{shortName} 잡이";

        /// <summary>A deed in a line of its story ("레오를 쓰러뜨림", "미나를 쫓아냄").</summary>
        public static string DeedText(NemesisDeed d)
            => d.kind == DeedKind.Killed
                ? $"{d.victim}{Adventurers.MemoryRecorder.Josa(d.victim, "을", "를")} 쓰러뜨림"
                : $"{d.victim}{Adventurers.MemoryRecorder.Josa(d.victim, "을", "를")} 쫓아냄";
    }
}
