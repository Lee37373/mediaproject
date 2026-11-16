using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Legacy
{
    /// <summary>
    /// The memorial wall's own rules (2026-11-15, user picked layout A and ideas 2–6: "추모의 벽이 너무 슴슴"): the line cut in
    /// each stone, who counts as a hero of the guild, a fallen member's last words, what the friends say of them, the
    /// anniversaries, and the relics handed on to the living. Pure — names, numbers and words only — so it is covered by
    /// EditMode tests; <see cref="GuildLegacy"/> applies it to the world.
    /// </summary>
    public static class MemorialRules
    {
        static string Josa(string word, string withBatchim, string withoutBatchim) => MemoryRecorder.Josa(word, withBatchim, withoutBatchim);

        /// <summary>Picks one wording by a seed, the same one every time (no shared dice: the wall reads the same after loading).</summary>
        public static string Pick(int seed, params string[] options)
        {
            if (options == null || options.Length == 0) return "";
            return options[(int)((uint)(seed * 2654435761u) % (uint)options.Length)];
        }

        // ---------------- Heroes ----------------

        /// <summary>A hero: one who carried a title (a region lord, a great deed), or fought long and hard for the guild.</summary>
        public const int HeroKills = 150, HeroQuests = 30, HeroLevel = 25;
        /// <summary>Fame for the guild when a hero falls (a member's death costs <see cref="Guild.GuildRankRules.MemberDied"/>).</summary>
        public const int HeroFame = 15;

        /// <summary>Why this fallen member is a hero of the guild, or null when not.</summary>
        public static string HeroReason(FallenRecord r)
        {
            if (r == null) return null;
            if (r.Titles.Count > 0) return r.Titles[0];
            if (r.Kills >= HeroKills) return $"몬스터 {r.Kills}마리를 쓰러뜨림";
            if (r.QuestsDone >= HeroQuests) return $"의뢰 {r.QuestsDone}건을 완수함";
            if (r.Level >= HeroLevel) return $"Lv.{r.Level}까지 오른 베테랑";
            return null;
        }

        public static bool IsHero(FallenRecord r) => HeroReason(r) != null;

        // ---------------- The line on the stone ----------------

        /// <summary>
        /// One line for the stone, from what they did: a title first, then a great tally, then how young they were, then how
        /// loved, then what they lived by. Always the same line for the same record.
        /// </summary>
        public static string Epitaph(FallenRecord r)
        {
            if (r == null) return "";
            string cls = string.IsNullOrEmpty(r.ClassLabel) ? "모험가" : r.ClassLabel;
            int seed = r.Id * 31 + r.Day;
            if (r.Titles.Count > 0)
                return Pick(seed, $"{r.Titles[0]}, 여기 잠들다", $"'{r.Titles[0]}'라 불린 {cls}");
            if (r.Kills >= 100)
                return Pick(seed, $"몬스터 {r.Kills}마리를 쓰러뜨린 {cls}", $"{r.Kills}번의 싸움에서 이긴 {cls}");
            if (r.QuestsDone >= 20)
                return Pick(seed, $"의뢰 {r.QuestsDone}건을 해낸 믿음직한 {cls}", $"맡은 일은 끝까지 해낸 {cls}");
            if (r.Level <= 5)
                return Pick(seed, $"꿈을 다 펼치지 못한 신참 {cls}", "너무 일찍 떠난 길드의 막내", $"첫 모험을 막 시작했던 {cls}");
            if (r.FriendIds.Count >= 3)
                return Pick(seed, $"동료 {r.FriendIds.Count}명이 아끼던 {cls}", $"늘 누군가의 곁에 있던 {cls}");
            return r.Values switch
            {
                "명예" => Pick(seed, $"명예를 목숨보다 아낀 {cls}", $"한 번도 등을 보이지 않은 {cls}"),
                "동료" => Pick(seed, $"동료 곁을 끝까지 지킨 {cls}", $"누구보다 동료를 아낀 {cls}"),
                "모험" => Pick(seed, $"새로운 땅을 늘 그리던 {cls}", $"지도 끝을 꿈꾸던 {cls}"),
                "돈" => Pick(seed, $"금화 소리를 사랑한 {cls}", $"한몫 잡는 날을 꿈꾸던 {cls}"),
                "안전" => Pick(seed, $"조심스러웠지만 끝내 물러서지 않은 {cls}", $"마을의 평온을 바란 {cls}"),
                "지식" => Pick(seed, $"세상의 모든 것을 알고 싶어 한 {cls}", $"몬스터 도감을 채우던 {cls}"),
                _ => Pick(seed, $"길드를 위해 잠든 {cls}", $"우리와 함께 걸었던 {cls}"),
            };
        }

        // ---------------- Last words ----------------

        /// <summary>Something said this recently (game hours) before the fall is remembered as the last words.</summary>
        public const float LastWordsHours = 0.5f;

        /// <summary>When nothing was heard: a line in keeping with what they lived by.</summary>
        public static string LastWords(FallenRecord r)
        {
            if (r == null) return "";
            int seed = r.Id * 17 + r.Level;
            bool foe = !string.IsNullOrEmpty(r.KillerName);
            return r.Values switch
            {
                "명예" => Pick(seed, "물러서지 마… 끝까지…", foe ? "이 정도로… 쓰러질 줄 알고…" : "부끄럽지 않게… 싸웠어.", "등은… 보이지 않았어."),
                "동료" => Pick(seed, "먼저 가… 내가 막을게.", "다들… 무사하지?", "내 몫까지… 부탁해."),
                "모험" => Pick(seed, "저 너머엔… 뭐가 있었을까.", "아직… 못 가 본 데가 많은데.", "지도… 마저 그려 줘."),
                "돈" => Pick(seed, "내 몫은… 길드에 남겨 줘.", "이번 건… 꽤 짭짤했는데.", "외상값은… 없던 걸로 해 줘."),
                "안전" => Pick(seed, "그러니까… 무리하지 말자고 했잖아.", "마을은… 괜찮겠지?", "문… 꼭 닫아 둬."),
                "지식" => Pick(seed, "이 녀석… 도감에 적어 둬.", "아직… 알고 싶은 게 많은데.", "약점은… 왼쪽이야…"),
                _ => Pick(seed, "미안해… 먼저 갈게.", "길드에… 안부 전해 줘.", "조금만… 쉴게."),
            };
        }

        // ---------------- What the friends say ----------------

        /// <summary>Friends whose words are kept on the stone (the closest first).</summary>
        public const int Remembrances = 2;

        /// <summary>What a friend says of the fallen: warmer the closer they were, and a word for the killer when it is known.</summary>
        public static string Remembrance(string dead, string killer, int relation, int seed)
        {
            string d = dead ?? "그 녀석";
            if (relation >= 40)
                return Pick(seed,
                    $"{d} 없었으면 난 벌써 죽은 목숨이었어.",
                    $"{d}… 같이 마시기로 한 술은 어쩌라고.",
                    $"아직도 {d}{Josa(d, "이", "가")} 문을 열고 들어올 것만 같아.",
                    string.IsNullOrEmpty(killer) ? $"{d}, 네 몫까지 살게." : $"{killer}… 그놈은 내가 꼭 갚는다.");
            return Pick(seed,
                $"{d}{Josa(d, "은", "는")} 좋은 동료였어.",
                $"{d}의 웃음소리가 아직 귀에 남아 있어.",
                $"{d}, 거기선 편히 쉬어.",
                $"{d}{Josa(d, "과", "와")} 함께한 의뢰, 잊지 않을게.");
        }

        // ---------------- Anniversaries ----------------

        /// <summary>A fallen member's day comes round once a year (<see cref="Seasons.SeasonRules.DaysPerYear"/> days).</summary>
        public static int AnniversaryDays => Seasons.SeasonRules.DaysPerYear;
        /// <summary>The hour of the day the friends remember them.</summary>
        public const int AnniversaryHour = 18;
        /// <summary>Stress eased for those who were fond of the fallen (half again for friends).</summary>
        public const float AnniversaryRelief = 6f, AnniversaryFriendRelief = 10f;

        /// <summary>Whether today is their anniversary (one year, two years… after the day they fell).</summary>
        public static bool IsAnniversary(int diedDay, int today) => today > diedDay && (today - diedDay) % AnniversaryDays == 0;

        /// <summary>Years since they fell on an anniversary day.</summary>
        public static int Years(int diedDay, int today) => Mathf.Max(0, (today - diedDay) / AnniversaryDays);

        /// <summary>Days until the next anniversary (counted from today; 0 = today).</summary>
        public static int DaysToAnniversary(int diedDay, int today)
        {
            int since = Mathf.Max(0, today - diedDay);
            int left = AnniversaryDays - since % AnniversaryDays;
            return left == AnniversaryDays && since > 0 ? 0 : left;
        }

        // ---------------- Heirlooms ----------------

        /// <summary>Heirlooms that count for one member, and what each gives (a share of attack and of health).</summary>
        public const int MaxHeirlooms = 3;
        public const float HeirloomAttack = 0.04f, HeirloomHp = 0.04f;
        /// <summary>Those offered as heirs on the wall: friends of the fallen first, then fellow members of the same class.</summary>
        public const int HeirChoices = 4;

        /// <summary>
        /// What is handed on: the fallen's own weapon or holy symbol, by class (once something of theirs came home — what
        /// actually dropped is often a potion or a horn, which makes a poor heirloom).
        /// </summary>
        public static string Keepsake(AdventurerClass cls) => cls switch
        {
            AdventurerClass.Ranger => "활",
            AdventurerClass.Mage => "지팡이",
            AdventurerClass.Priest => "성표",
            _ => "검",
        };

        /// <summary>"루나의 지팡이" — what an heir carries (kept in <see cref="AdventurerData.heirlooms"/>).</summary>
        public static string HeirloomName(string dead, string item) => $"{dead}의 {item}";

        /// <summary>The title an heir is known by.</summary>
        public static string HeirTitle(string dead) => $"{dead}의 뜻을 이은 자";

        /// <summary>Stats with the member's heirlooms counted (at most <see cref="MaxHeirlooms"/>).</summary>
        public static CombatStats WithHeirlooms(CombatStats s, AdventurerData d)
        {
            int n = d?.heirlooms == null ? 0 : Mathf.Min(MaxHeirlooms, d.heirlooms.Count);
            if (n <= 0) return s;
            s.Attack *= 1f + HeirloomAttack * n;
            s.MaxHp = Mathf.Round(s.MaxHp * (1f + HeirloomHp * n));
            return s;
        }

        /// <summary>
        /// The order heirs are offered in: the closest friends first (by how they felt when the fallen died), then the same class,
        /// then the rest; alive members only, those who already carry this record's heirloom left out by the caller.
        /// </summary>
        public static List<T> RankHeirs<T>(IEnumerable<T> members, System.Func<T, int> relation, System.Func<T, bool> sameClass, int take = HeirChoices)
        {
            var list = new List<T>(members);
            list.Sort((a, b) =>
            {
                int ra = relation(a), rb = relation(b);
                bool fa = ra >= Welfare.StressRules.FriendRelation, fb = rb >= Welfare.StressRules.FriendRelation;
                if (fa != fb) return fa ? -1 : 1;
                if (fa && ra != rb) return rb.CompareTo(ra);
                bool ca = sameClass(a), cb = sameClass(b);
                if (ca != cb) return ca ? -1 : 1;
                return rb.CompareTo(ra);
            });
            if (list.Count > take) list.RemoveRange(take, list.Count - take);
            return list;
        }

        // ---------------- Thoughts ----------------

        public static readonly ThoughtDef Heirloom = new()
        {
            Id = "heirloom", Label = "{0}의 유품을 물려받았다", Mood = 10f, Hours = 240f, Tone = ThoughtTone.People, Log = true,
            Inner = new[] { "({0}, 네 몫까지 싸울게.)", "(이건… {0}의 것이었지.)" },
        };

        public static readonly ThoughtDef Anniversary = new()
        {
            Id = "anniversary", Label = "{0}의 기일을 기렸다", Mood = 4f, Hours = 24f, Tone = ThoughtTone.People,
            Inner = new[] { "({0}, 벌써 1년이네.)", "(잘 지내지, {0}?)" },
        };

        public static readonly ThoughtDef HeroRemembered = new()
        {
            Id = "hero_remembered", Label = "영웅 {0}의 뜻을 잇는다", Mood = 3f, Hours = 72f, Tone = ThoughtTone.Glory,
            Inner = new[] { "({0}처럼 싸우자.)" },
        };

        // ---------------- Words for the wall and the log ----------------

        public static string HeirloomGiven(string heir, string dead, string item)
            => $"{heir}{Josa(heir, "이", "가")} {dead}의 {item}{Josa(item, "을", "를")} 물려받았습니다. (공격·체력 +{HeirloomAttack * 100f:0}%)";

        public const string AnniversaryTitle = "기일";

        public static string AnniversaryNotice(string dead, int years, int gathered)
            => gathered > 0
                ? $"{dead}{Josa(dead, "이", "가")} 잠든 지 {years}년. 길드원 {gathered}명이 그를 기렸습니다."
                : $"{dead}{Josa(dead, "이", "가")} 잠든 지 {years}년이 되었습니다.";

        public const string HeroTitle = "길드의 영웅";

        public static string HeroNotice(string dead, string reason)
            => $"{dead}{Josa(dead, "은", "는")} 길드의 영웅으로 기억됩니다 — {reason}. (명성 +{HeroFame})";
    }
}
