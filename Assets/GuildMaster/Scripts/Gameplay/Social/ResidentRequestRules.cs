using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using UnityEngine;

namespace GuildMaster.Social
{
    /// <summary>What a resident asks for (P4-134): things (handed over from the guild's stock, or dug or hunted for by a member),
    /// goods carried to another village, or — the child — a member's tale of adventure.</summary>
    public enum RequestKind { Goods, Delivery, Story }

    public enum RequestStatus { Open, Underway, Done, Failed, Lapsed }

    /// <summary>
    /// The residents' small requests and what the town thinks of the guild (P4-134 F-14, user decision 2026-11-08): asked of the
    /// guild master directly ("!" over the head → the request window → hand the things over from the stock, or send one
    /// member), thanked once for each (an item or gold, and their opinion) and, the better they think of the guild, with a
    /// standing favour from their trade; the town's opinion — the six residents' on average — is what new visitors heard of the
    /// guild before they came. Only a member's failure costs opinion. The numbers and words, pure for EditMode tests.
    /// </summary>
    public static class ResidentRequestRules
    {
        // ---------------- When they ask ----------------

        /// <summary>Requests open at once, all residents together.</summary>
        public const int MaxOpen = 2;
        /// <summary>Chance each game hour (while the residents are up and asking) that one of them asks something.</summary>
        public const float ChancePerHour = 0.12f;
        /// <summary>They ask from waking until this hour (so the guild master has the day to answer).</summary>
        public const int LastAskHour = 18;
        /// <summary>Hours a resident waits after its last request ended before asking again.</summary>
        public const float GapHours = 24f;
        /// <summary>Hours a request stays open; nobody answering, it lapses (no opinion lost — the guild master did nothing wrong).</summary>
        public const float OpenHours = 24f;

        public static bool AskingHour(float hourOfDay) => hourOfDay >= Resident.WakeHour && hourOfDay < LastAskHour;

        // ---------------- What they ask ----------------

        /// <summary>The kinds each resident asks, in the order they are tried when the dice say "any".</summary>
        public static RequestKind[] KindsOf(ResidentJob job) => job switch
        {
            ResidentJob.Baker or ResidentJob.Chief => new[] { RequestKind.Delivery, RequestKind.Goods },
            ResidentJob.Child => new[] { RequestKind.Story, RequestKind.Goods },
            _ => new[] { RequestKind.Goods },
        };

        /// <summary>The thing a resident would ask for by trade, if any (ore for the smith, herbs for the herbalist); others ask a monster's part.</summary>
        public static string TradeItem(ResidentJob job) => job switch
        {
            ResidentJob.Blacksmith => Quests.ErrandRules.Ore,
            ResidentJob.Herbalist => Quests.ErrandRules.Herb,
            _ => null,
        };

        /// <summary>How many: ore 3–5, herbs 3–6, a monster's part 2–4 (the child two — just to see them).</summary>
        public static int WantedCount(ResidentJob job, string itemId, GameRandom rng)
        {
            if (job == ResidentJob.Child) return 2;
            if (itemId == Quests.ErrandRules.Ore) return rng != null ? rng.Range(3, 6) : 4;
            if (itemId == Quests.ErrandRules.Herb) return rng != null ? rng.Range(3, 7) : 4;
            return rng != null ? rng.Range(2, 5) : 3;
        }

        /// <summary>A monster's part a resident asks for has dens within this many cells of town (the ones that trouble it).</summary>
        public const float PartReach = 70f;

        /// <summary>What a delivery carries for this resident (words only).</summary>
        public static string GoodsOf(ResidentJob job) => job == ResidentJob.Baker ? "빵 바구니" : "촌장의 편지";

        /// <summary>A member sent on a request is paid this share of what a village would pay for the same errand (no guild fee).</summary>
        public const float PayFactor = 0.5f;

        // ---------------- Thanks ----------------

        /// <summary>Opinion of the guild a done request wins, and a member's failure costs.</summary>
        public const int OpinionWon = 10, OpinionLost = 6;
        /// <summary>What the resident comes to feel for the member who did it (or told the tale).</summary>
        public const int FeelingForHelper = 8;
        /// <summary>Stress a member sheds for being thanked by a resident.</summary>
        public const float HelperStressRelief = 4f;

        /// <summary>What each request is thanked with once (besides the opinion).</summary>
        public struct Thanks
        {
            /// <summary>An item into the guild's stock, or none.</summary>
            public ItemStack Item;
            public int Gold;
            public int Food;

            public bool IsEmpty => string.IsNullOrEmpty(Item.itemId) && Gold <= 0 && Food <= 0;
        }

        public const int ChiefGold = 60, BakerFood = 8, HerbalistPotions = 2, GuardFlares = 2;

        /// <summary>
        /// The smith a piece of first-tier gear, the herbalist two potions, the chief 60G from the town chest, the baker 8 meals,
        /// the guard two flares, the child something it found (stone shards, ore, herbs — and rarely a crown shard).
        /// <paramref name="roll"/> (0..1) picks the child's find.
        /// </summary>
        public static Thanks ThanksOf(ResidentJob job, float roll) => job switch
        {
            ResidentJob.Blacksmith => new Thanks { Item = new ItemStack(Quests.ErrandRules.Gear(1), 1) },
            ResidentJob.Herbalist => new Thanks { Item = new ItemStack(Quests.ErrandRules.Potion, HerbalistPotions) },
            ResidentJob.Chief => new Thanks { Gold = ChiefGold },
            ResidentJob.Baker => new Thanks { Food = BakerFood },
            ResidentJob.Guard => new Thanks { Item = new ItemStack(Flares.FlareRules.ItemId, GuardFlares) },
            _ => new Thanks { Item = ChildsFind(roll) },
        };

        static ItemStack ChildsFind(float roll)
            => roll < 0.05f ? new ItemStack("crown_shard", 1)
             : roll < 0.15f ? new ItemStack(Quests.ErrandRules.Herb, 4)
             : roll < 0.40f ? new ItemStack(Quests.ErrandRules.Ore, 3)
             : new ItemStack("stone_shard", 3);

        // ---------------- Standing favours ----------------

        /// <summary>Opinion at which each step of a resident's standing favour begins.</summary>
        public static readonly int[] TierAt = { 25, 50, 75 };

        /// <summary>0 (none) to 3 by the resident's opinion of the guild.</summary>
        public static int Tier(int opinion)
        {
            int t = 0;
            foreach (var at in TierAt) if (opinion >= at) t++;
            return t;
        }

        /// <summary>The opinion the next step starts at, or −1 at the top.</summary>
        public static int NextTierAt(int opinion)
        {
            foreach (var at in TierAt) if (opinion < at) return at;
            return -1;
        }

        /// <summary>A cut or a bonus of 10% a step.</summary>
        public static float Share(int tier) => 0.1f * Mathf.Clamp(tier, 0, TierAt.Length);

        /// <summary>The child's word to new visitors: +2 start affinity a step.</summary>
        public static int ChildBrag(int tier) => 2 * Mathf.Clamp(tier, 0, TierAt.Length);

        /// <summary>What each resident's favour does, at this step ("대장간 제작 시간 −20% · 건물 수리비 −20%").</summary>
        public static string FavourText(ResidentJob job, int tier)
        {
            int p = Mathf.RoundToInt(Share(Mathf.Max(1, tier)) * 100f);
            return job switch
            {
                ResidentJob.Blacksmith => $"대장간 제작 시간 −{p}% · 건물 수리비 −{p}%",
                ResidentJob.Herbalist => $"약방 제작 시간 −{p}%",
                ResidentJob.Chief => $"일일 세금 +{p}%",
                ResidentJob.Baker => $"식량 값 −{p}%",
                ResidentJob.Guard => $"성문·함정 수리비 −{p}%",
                _ => $"새 방문자 시작 호감 +{ChildBrag(Mathf.Max(1, tier))} (토비의 길드 자랑)",
            };
        }

        /// <summary>A price after a cut (rounded up, never below 0).</summary>
        public static int Cut(int gold, float share) => Mathf.Max(0, Mathf.CeilToInt(gold * (1f - Mathf.Clamp01(share)) - 0.001f));

        // ---------------- The town's opinion ----------------

        /// <summary>The six residents' opinion of the guild on average (rounded).</summary>
        public static int TownOpinion(IEnumerable<int> opinions)
        {
            int n = 0, sum = 0;
            if (opinions != null) foreach (var o in opinions) { sum += o; n++; }
            return n == 0 ? 0 : Mathf.RoundToInt(sum / (float)n);
        }

        /// <summary>
        /// What new visitors heard of the guild in town (user decision ③: start affinity only): +3 · +6 · +10 from 25 · 50 · 75,
        /// −5 · −10 at −25 · −50 or below, nothing in between (the residents start at 15 on average).
        /// </summary>
        public static int VisitorWord(int townOpinion)
            => townOpinion >= 75 ? 10 : townOpinion >= 50 ? 6 : townOpinion >= 25 ? 3
             : townOpinion <= -50 ? -10 : townOpinion <= -25 ? -5 : 0;

        public static string TownOpinionLabel(int townOpinion)
            => townOpinion >= 50 ? "아주 좋음" : townOpinion >= 25 ? "좋음" : townOpinion > -25 ? "보통" : townOpinion > -50 ? "나쁨" : "아주 나쁨";

        // ---------------- Words ----------------

        public static string Who(ResidentProfile p) => p == null ? "주민" : $"{p.JobLabel} {p.Name}";

        /// <summary>The resident's own words asking for it.</summary>
        public static string AskLine(ResidentJob job, RequestKind kind, string itemName, int count, string village)
        {
            string what = Quests.ErrandRules.ItemCount(itemName, count);
            return (job, kind) switch
            {
                (_, RequestKind.Story) => "모험가 형아! 누나! 진짜 모험 얘기 들려줘! 몬스터랑 싸운 거!",
                (ResidentJob.Baker, RequestKind.Delivery) => $"갓 구운 빵 바구니를 {village}에 전해 줄 사람 없을까요? 식기 전에요!",
                (ResidentJob.Chief, RequestKind.Delivery) => $"{village} 촌장에게 보낼 편지가 있네. 믿을 만한 사람이 전해 주게.",
                (ResidentJob.Blacksmith, _) => $"쇠가 떨어졌어. {what}만 구해다 주면 사례하지.",
                (ResidentJob.Herbalist, _) => $"{what}만 구할 수 있을까요? 약을 지어야 하는데 모자라서요.",
                (ResidentJob.Chief, _) => $"마을 근처 몬스터가 줄었다는 증거가 필요하네. {what}을(를) 가져와 주게.",
                (ResidentJob.Baker, _) => $"{what}! 새 빵에 넣어 보고 싶어요. 구해 주시면 맛있는 걸로 갚을게요!",
                (ResidentJob.Guard, _) => $"성 밖 몬스터를 솎아 주십시오. 증거로 {what}이면 됩니다.",
                _ => $"진짜 몬스터 전리품 보고 싶어! {what}만 보여 줘!",
            };
        }

        /// <summary>The resident thanking the guild.</summary>
        public static string ThanksLine(ResidentJob job) => job switch
        {
            ResidentJob.Blacksmith => "흠. 좋은 물건이군. 이거 받아. 쓸 만할 거다.",
            ResidentJob.Herbalist => "정말 고마워요! 약 지으면 길드에도 나눠 드릴게요.",
            ResidentJob.Chief => "수고했네. 마을 금고에서 사례를 좀 챙겼네.",
            ResidentJob.Baker => "고마워요~! 빵 듬뿍 챙겨 드릴게요!",
            ResidentJob.Guard => "감사합니다. 경비대 몫의 조명탄을 나눠 드리겠습니다.",
            _ => "우와아! 최고야! 이거 내가 주운 건데, 줄게!",
        };

        /// <summary>The resident when the member it was given to failed.</summary>
        public static string LetDownLine(ResidentJob job) => job switch
        {
            ResidentJob.Blacksmith => "…일이 틀어졌다고? 쯧.",
            ResidentJob.Herbalist => "괜찮아요… 다음에 또 부탁드릴게요.",
            ResidentJob.Chief => "길드를 믿었는데, 아쉽게 됐군.",
            ResidentJob.Baker => "아… 빵이 다 식겠네요….",
            ResidentJob.Guard => "실패라니. 보고서에 적어 두겠습니다.",
            _ => "에이… 모험가도 별거 아니네.",
        };

        /// <summary>A member taking the request on.</summary>
        public static string SetOffLine(ResidentProfile p) => $"{p?.Name ?? "주민"} 부탁이라면야. 다녀오겠습니다!";

        /// <summary>The member's tale for the child: its latest moment worth telling, else what it has done.</summary>
        public static string TaleLine(string latestMoment, int level)
            => !string.IsNullOrEmpty(latestMoment) ? $"이건 비밀인데… {latestMoment}"
             : level >= 5 ? "숲 너머엔 말이야, 이빨이 이만한 놈들이 산단다. 그놈들을 내가…"
             : "아직 큰 모험은 없지만… 슬라임한테 밟힐 뻔한 얘기라도 해 줄까?";

        public const string ChildAwedLine = "우와아! 나도 크면 꼭 모험가 될 거야!";

        public const string GaveUpReason = "부탁을 맡은 길드원이 그만두었습니다";
    }
}
