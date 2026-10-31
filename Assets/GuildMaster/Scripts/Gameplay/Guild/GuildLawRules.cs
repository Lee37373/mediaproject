using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GuildMaster.Guild
{
    /// <summary>The guild's rules (P4-112, H2).</summary>
    public enum GuildLaw { Loot, Split, Wounded, NightMarch, Training, Holiday, RiskyWork, Upkeep, DeepWoods, Shop }

    /// <summary>One way a rule can be set: its name, what it does, whom it pleases or galls, and what they say when it comes.</summary>
    public sealed class LawOption
    {
        public string Label;
        public string Effect;
        /// <summary>Mood it gives every member, before the ones below.</summary>
        public float Everyone;
        /// <summary>A trait id ("greedy") or a value ("돈") and the mood it adds for a member who has it.</summary>
        public (string who, float mood)[] Feelings = Array.Empty<(string, float)>();
        /// <summary>Said by those it pleases, and by those it galls, when the rule is set this way.</summary>
        public string[] Pleased = Array.Empty<string>(), Displeased = Array.Empty<string>();
    }

    /// <summary>A rule: what it is about, its two or three ways, the one it starts at, and the rank that opens it.</summary>
    public sealed class LawDef
    {
        public GuildLaw Law;
        public string Name, Text;
        public LawOption[] Options;
        public int Default;
        public GuildRank OpensAt;
        /// <summary>Only newcomers have a feeling about it (the training rule is theirs).</summary>
        public bool NewcomersOnly;
    }

    /// <summary>How a member feels about the rules as they stand: the sum it likes and the sum it does not, each held within its cap.</summary>
    public struct LawMood
    {
        public float Liked, Disliked;
        public List<string> LikedNames, DislikedNames;
    }

    /// <summary>
    /// The guild's rules (P4-112, H2 — Frostpunk's book of laws; user decisions 2026-10-31: all ten, five from the start and one
    /// more at each rank E to A, a rule changed cannot be changed again for three days, and how members feel about them goes no
    /// further than their mood). Pure: the rules and their ways, what opens them, the numbers each way sets, and how a member's
    /// traits and values take them. <see cref="GuildLaws"/> holds the guild's choices; the systems ask it for their numbers.
    /// Every rule starts at the way the game played before it, and that way pleases or galls nobody.
    /// </summary>
    public static class GuildLawRules
    {
        /// <summary>Game hours a rule stays as it was set (user decision: three days).</summary>
        public const float LockHours = 72f;
        /// <summary>A member's liking for the rules adds at most this to its mood, and its dislike takes at most this.</summary>
        public const float LikedCap = 6f, DislikedCap = -8f;
        /// <summary>A member whose feeling about a change is at least this says so.</summary>
        public const float ReactAt = 1.5f;
        /// <summary>At most this many speak up when a rule changes.</summary>
        public const int MaxReactions = 4;

        // ---------------- What each way sets ----------------

        /// <summary>Loot owed to the guild: the share of its price the guild still pays the finder.</summary>
        public const float LootFeeShare = 0.2f;

        /// <summary>The share of a party's pool split evenly (the rest by contribution): even, half and half, by deeds.</summary>
        public static float SplitEqualShare(int option) => option switch { 0 => 1f, 2 => 0f, _ => 0.5f };

        /// <summary>A member in town rests rather than go out below this health and above this fatigue (by the wounded rule).</summary>
        public static float RestBelowHp(int option) => option == 1 ? 0.9f : 0.6f;
        public static float RestAboveFatigue(int option) => option == 1 ? 40f : 70f;

        public const int NightPress = 0, NightFree = 1, NightCamp = 2;

        /// <summary>The guild's holiday is the last day of each season (days 5, 10, 15, …).</summary>
        public static bool IsHolidayDay(int day) => day > 0 && day % Seasons.SeasonRules.DaysPerSeason == 0;
        /// <summary>Stress eases this much faster in town on a holiday.</summary>
        public const float HolidayRelief = 1.5f;
        /// <summary>Why nobody goes out on a holiday (the reason a member gives).</summary>
        public const string HolidayRefusal = "오늘은 길드 휴일이라 쉰다";

        /// <summary>Keeping to the trails: what a known deep-wood cell adds to a careful walk, and the least caution it is walked with.</summary>
        public const float TrailsRisk = 6f, TrailsCaution = 1f;

        /// <summary>The guild shop's prices against the usual: a discount, the usual, dear.</summary>
        public static float ShopFactor(int option) => option switch { 0 => 0.85f, 2 => 1.25f, _ => 1f };

        // ---------------- The rules ----------------

        static LawOption O(string label, string effect, float everyone = 0f, (string, float)[] feelings = null, string[] pleased = null, string[] displeased = null)
            => new()
            {
                Label = label, Effect = effect, Everyone = everyone, Feelings = feelings ?? Array.Empty<(string, float)>(),
                Pleased = pleased ?? Array.Empty<string>(), Displeased = displeased ?? Array.Empty<string>(),
            };

        static string[] S(params string[] lines) => lines;

        public static readonly IReadOnlyList<LawDef> All = new[]
        {
            new LawDef
            {
                Law = GuildLaw.Loot, Name = "전리품", Text = "밖에서 주운 전리품이 누구 것인지", Default = 0, OpensAt = GuildRank.F,
                Options = new[]
                {
                    O("개인 소유", "길드가 전리품을 매입가대로 사 줍니다.",
                        pleased: S("그렇지, 내가 주운 건 내 거야!", "이제야 제대로 벌겠네."), displeased: S("길드 살림은 누가 챙기고?")),
                    O("길드 귀속", $"전리품은 길드 것입니다. 길드는 값의 {LootFeeShare * 100f:0}%만 수고비로 줍니다.",
                        feelings: new[] { ("greedy", -4f), ("돈", -3f), ("arrogant", -2f), ("loyal", 2f), ("동료", 1f) },
                        pleased: S("길드를 위해서라면야.", "다 같이 쓰는 거지."),
                        displeased: S("목숨 걸고 주운 걸 다 내놓으라고?", "수고비 몇 푼이 뭐야..."))
                },
            },
            new LawDef
            {
                Law = GuildLaw.Split, Name = "파티 분배", Text = "파티가 함께 번 돈을 나누는 방식", Default = 1, OpensAt = GuildRank.B,
                Options = new[]
                {
                    O("균등", "파티 몫을 모두 똑같이 나눕니다. 불공평하다고 떠나는 사람이 없습니다.",
                        feelings: new[] { ("동료", 2f), ("sociable", 1f), ("loyal", 1f), ("greedy", -2f), ("arrogant", -2f) },
                        pleased: S("똑같이 나누는 게 맞지.", "누가 더 했는지 따질 필요 없어."), displeased: S("내가 제일 많이 싸웠는데 똑같이?")),
                    O("반반", "절반은 똑같이, 절반은 활약한 만큼 나눕니다.",
                        pleased: S("이 정도면 공평하지."), displeased: S("어중간하네.")),
                    O("공헌", "활약한 만큼만 나눕니다. 적게 받은 사람이 파티를 떠나기 쉽습니다.",
                        feelings: new[] { ("greedy", 2f), ("arrogant", 2f), ("명예", 1f), ("동료", -3f), ("sociable", -2f) },
                        pleased: S("일한 만큼 받는 거지!", "실력대로 가자고."), displeased: S("동료끼리 너무 야박한데...", "이러다 파티 다 깨지겠어."))
                },
            },
            new LawDef
            {
                Law = GuildLaw.Wounded, Name = "부상자 출격", Text = "다치거나 지친 길드원이 다시 나가는 때", Default = 0, OpensAt = GuildRank.F,
                Options = new[]
                {
                    O("자율", "체력 60% 아래이거나 피로 70을 넘으면 쉽니다.",
                        pleased: S("이 정도 상처쯤이야!"), displeased: S("다친 채로 나가다 큰일 나지...")),
                    O("완치 뒤 출격", "체력 90% 아래이거나 피로 40을 넘으면 마을에서 쉬고, 그동안은 초대에도 응하지 않습니다.",
                        feelings: new[] { ("coward", 2f), ("안전", 2f), ("lazy", 2f), ("brave", -2f), ("diligent", -2f), ("명예", -1f) },
                        pleased: S("푹 쉬고 나가야 안 죽지.", "쉬라니 좋네."), displeased: S("이 정도는 다친 것도 아니라고!", "언제까지 쉬란 거야?"))
                },
            },
            new LawDef
            {
                Law = GuildLaw.NightMarch, Name = "밤 행군", Text = "멀리 나가 있을 때 밤을 보내는 방식", Default = NightFree, OpensAt = GuildRank.F,
                Options = new[]
                {
                    O("강행", "밤에도 쉬지 않고 일을 계속합니다(다쳤을 때 야영은 그대로).",
                        feelings: new[] { ("diligent", 2f), ("brave", 1f), ("lazy", -3f), ("coward", -3f), ("안전", -2f) },
                        pleased: S("밤이라고 쉴 틈이 어딨어!"), displeased: S("이 어둠 속을 걸으라고?", "밤엔 좀 자자...")),
                    O("자율", "이끄는 사람이 조심성에 따라 야영할지 정합니다.",
                        pleased: S("알아서 판단하게 해 줘서 고마워."), displeased: S("그때그때 정하라니 불안한데.")),
                    O("야영", "마을에서 멀리 나가 있으면 밤마다 모닥불을 피우고 쉽니다.",
                        feelings: new[] { ("coward", 2f), ("안전", 2f), ("lazy", 2f), ("brave", -2f), ("diligent", -1f), ("모험", -1f) },
                        pleased: S("밤엔 불 옆이 최고지.", "어두울 땐 쉬어야지."), displeased: S("밤마다 쉬다간 일이 안 끝나!"))
                },
            },
            new LawDef
            {
                Law = GuildLaw.Training, Name = "신입 훈련", Text = "마을 밖 몬스터보다 약한 신입이 하는 일", Default = 0, OpensAt = GuildRank.E, NewcomersOnly = true,
                Options = new[]
                {
                    O("자유", "신입도 맞는 의뢰가 있으면 맡고, 없을 때만 훈련합니다.",
                        pleased: S("드디어 밖에 나가 볼 수 있겠다!"), displeased: S("훈련이 더 나았는데...")),
                    O("의무 훈련", "훈련소가 훈련을 줄 수 있으면(2레벨부터) 신입은 의뢰와 사냥 대신 훈련만 합니다.",
                        feelings: new[] { ("quick_learner", 3f), ("지식", 2f), ("diligent", 1f), ("lazy", -3f), ("모험", -2f), ("brave", -2f), ("arrogant", -2f) },
                        pleased: S("기초부터 제대로 배워야지.", "열심히 하면 금방 늘겠지!"), displeased: S("허수아비만 두들기라고?", "나도 밖에 나가고 싶은데!"))
                },
            },
            new LawDef
            {
                Law = GuildLaw.Holiday, Name = "정기 휴일", Text = "길드 전체가 쉬는 날", Default = 0, OpensAt = GuildRank.A,
                Options = new[]
                {
                    O("없음", "쉬는 날이 따로 없습니다.",
                        pleased: S("쉬는 날 없이 벌어야지!"), displeased: S("휴일이 없어졌다니...")),
                    O("계절 마지막 날", $"5일마다(계절의 마지막 날) 아무도 새 일을 나가지 않습니다. 마을에 있으면 스트레스가 {HolidayRelief:0.#}배로 풀립니다.",
                        everyone: 1f, feelings: new[] { ("lazy", 3f), ("sociable", 2f), ("동료", 1f), ("diligent", -2f), ("greedy", -2f), ("돈", -1f) },
                        pleased: S("휴일이다! 실컷 쉬어야지.", "다 같이 쉬는 날이라니 좋다."), displeased: S("하루를 그냥 버리라고?"))
                },
            },
            new LawDef
            {
                Law = GuildLaw.RiskyWork, Name = "위험 의뢰", Text = "위험도 '높음' 의뢰를 맡는 방식", Default = 0, OpensAt = GuildRank.D,
                Options = new[]
                {
                    O("자율", "혼자서도 할 만하다고 보면 혼자 맡습니다.",
                        pleased: S("혼자서도 충분하다고!"), displeased: S("혼자 갔다가 무슨 일이라도 나면...")),
                    O("파티만", "위험도 '높음' 의뢰는 같이 갈 사람이 없으면 내려놓습니다.",
                        feelings: new[] { ("sociable", 2f), ("coward", 2f), ("안전", 2f), ("동료", 1f), ("loner", -3f), ("arrogant", -3f), ("brave", -1f) },
                        pleased: S("위험한 일은 같이 가야지.", "든든하네."), displeased: S("혼자서도 할 수 있는데!", "남이랑 같이 가라고? 귀찮게..."))
                },
            },
            new LawDef
            {
                Law = GuildLaw.Upkeep, Name = "생활비", Text = "식비(하루 한 번)와 마을 숙박비를 누가 내는지", Default = 0, OpensAt = GuildRank.F,
                Options = new[]
                {
                    O("본인 부담", "길드원이 내고, 그 돈은 길드 수입이 됩니다.",
                        pleased: S("길드 살림도 생각해야지."), displeased: S("다시 밥값을 내라고?", "공짜 밥이 좋았는데...")),
                    O("길드 부담", "길드원은 식비와 숙박비를 내지 않습니다(그만큼 길드 수입이 줄어듭니다).",
                        everyone: 2f, feelings: new[] { ("greedy", 2f), ("돈", 2f) },
                        pleased: S("밥값 굳었다!", "길드가 먹여 주다니 최고야."))
                },
            },
            new LawDef
            {
                Law = GuildLaw.DeepWoods, Name = "깊은 숲", Text = "독 안개와 덫이 있는 깊은 숲을 지나는 방식", Default = 0, OpensAt = GuildRank.C,
                Options = new[]
                {
                    O("자율", "조심성에 따라 숲길로 돌아가거나 가로지릅니다.",
                        pleased: S("숲쯤이야, 알아서 갈게."), displeased: S("깊은 숲을 그냥 다닌다고?")),
                    O("숲길만", "급할 때(귀환·후퇴·마을 방어)가 아니면 아는 깊은 숲을 피하고, 그 안의 둥지로 사냥을 가지 않습니다.",
                        feelings: new[] { ("coward", 2f), ("안전", 2f), ("모험", -3f), ("brave", -2f), ("지식", -1f) },
                        pleased: S("숲길로 다니는 게 안전하지."), displeased: S("숲 안쪽이 궁금한데!", "돌아가면 한참이잖아."))
                },
            },
            new LawDef
            {
                Law = GuildLaw.Shop, Name = "길드 가게", Text = "길드원에게 파는 물약·조명탄·장비 값", Default = 1, OpensAt = GuildRank.F,
                Options = new[]
                {
                    O("할인", $"평소 값의 {ShopFactor(0) * 100f:0}%에 팝니다. 장비를 일찍 사지만 길드 수입이 줄어듭니다.",
                        everyone: 1f, feelings: new[] { ("greedy", 2f), ("돈", 2f) },
                        pleased: S("싸게 판다니 지금 사야지!"), displeased: S("이렇게 팔아서 남긴 하나?")),
                    O("정가", "평소 값에 팝니다.",
                        pleased: S("값이 제자리로 돌아왔네."), displeased: S("할인이 끝났다고?")),
                    O("비싸게", $"평소 값의 {ShopFactor(2) * 100f:0}%에 팝니다. 길드 수입은 늘지만 장비를 늦게 삽니다.",
                        everyone: -2f, feelings: new[] { ("greedy", -2f), ("돈", -2f) },
                        displeased: S("값이 왜 이렇게 올랐어?", "길드가 우리 등골을 빼먹네."))
                },
            },
        };

        public static LawDef Def(GuildLaw law) => All[(int)law];

        public static int Count => All.Count;

        /// <summary>The rule is the guild's to set at this rank.</summary>
        public static bool IsOpen(GuildLaw law, GuildRank rank) => rank >= Def(law).OpensAt;

        /// <summary>The rules a rank opens (none for F: those are there from the start).</summary>
        public static IEnumerable<LawDef> OpenedAt(GuildRank rank) => rank == GuildRank.F ? Enumerable.Empty<LawDef>() : All.Where(d => d.OpensAt == rank);

        /// <summary>"전리품: 길드 귀속" for logs and thoughts.</summary>
        public static string Describe(GuildLaw law, int option) => $"{Def(law).Name} {Def(law).Options[option].Label}";

        // ---------------- How members take them ----------------

        static bool Has(IReadOnlyList<string> traits, string values, string who)
        {
            if (values == who) return true;
            if (traits == null) return false;
            foreach (var t in traits) if (t == who) return true;
            return false;
        }

        /// <summary>How a member with these traits and value feels about the rule set this way (0 = nothing either way).</summary>
        public static float Feeling(GuildLaw law, int option, IReadOnlyList<string> traits, string values, bool newcomer)
        {
            var def = Def(law);
            if (def.NewcomersOnly && !newcomer) return 0f;
            if (option < 0 || option >= def.Options.Length) return 0f;
            var o = def.Options[option];
            float f = o.Everyone;
            foreach (var (who, mood) in o.Feelings)
                if (Has(traits, values, who)) f += mood;
            return f;
        }

        /// <summary>A member's feelings about all the rules as they stand: what it likes and dislikes, each within its cap.</summary>
        public static LawMood Mood(IReadOnlyList<int> choices, IReadOnlyList<string> traits, string values, bool newcomer)
        {
            var m = new LawMood { LikedNames = new List<string>(), DislikedNames = new List<string>() };
            for (int i = 0; i < Count && i < choices.Count; i++)
            {
                float f = Feeling((GuildLaw)i, choices[i], traits, values, newcomer);
                if (f > 0f) { m.Liked += f; m.LikedNames.Add(Describe((GuildLaw)i, choices[i])); }
                else if (f < 0f) { m.Disliked += f; m.DislikedNames.Add(Describe((GuildLaw)i, choices[i])); }
            }
            m.Liked = Mathf.Min(m.Liked, LikedCap);
            m.Disliked = Mathf.Max(m.Disliked, DislikedCap);
            return m;
        }

        /// <summary>"생활비 길드 부담", or "생활비 길드 부담 외 2" when there are more.</summary>
        public static string Names(IReadOnlyList<string> names)
            => names == null || names.Count == 0 ? "" : names.Count == 1 ? names[0] : $"{names[0]} 외 {names.Count - 1}";

        static readonly Dictionary<string, string> TraitNames = new()
        {
            ["brave"] = "용감함", ["coward"] = "겁많음", ["greedy"] = "탐욕스러움", ["loyal"] = "충성스러움", ["sociable"] = "사교적",
            ["loner"] = "외톨이", ["lazy"] = "게으름", ["diligent"] = "근면함", ["arrogant"] = "오만함", ["quick_learner"] = "빠른 학습자",
        };

        static string WhoName(string who) => TraitNames.TryGetValue(who, out var n) ? n : $"{who} 중시";

        /// <summary>"반김: 탐욕스러움·돈 중시 · 꺼림: 충성스러움" for the rules window (everyone's share first).</summary>
        public static string Who(GuildLaw law, int option)
        {
            var def = Def(law);
            var o = def.Options[option];
            var likes = o.Feelings.Where(f => f.mood > 0f).Select(f => WhoName(f.who)).ToList();
            var dislikes = o.Feelings.Where(f => f.mood < 0f).Select(f => WhoName(f.who)).ToList();
            if (o.Everyone > 0f) likes.Insert(0, "모두");
            if (o.Everyone < 0f) dislikes.Insert(0, "모두");
            var parts = new List<string>();
            if (likes.Count > 0) parts.Add("반김: " + string.Join("·", likes));
            if (dislikes.Count > 0) parts.Add("꺼림: " + string.Join("·", dislikes));
            string who = parts.Count == 0 ? "아무도 신경 쓰지 않음" : string.Join(" · ", parts);
            return def.NewcomersOnly && parts.Count > 0 ? who + " (신입만)" : who;
        }
    }
}
