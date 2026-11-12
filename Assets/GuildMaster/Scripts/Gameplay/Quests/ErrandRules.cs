using GuildMaster.Core;
using GuildMaster.Data;
using UnityEngine;

namespace GuildMaster.Quests
{
    /// <summary>
    /// The errands (P4-133 F-5, user decision 2026-11-08): gathering ore or herbs, bringing in a monster's part, and carrying
    /// goods to another village — posted by the other villages (<see cref="ErrandPoster"/>) and offered on the proposal cards —
    /// and the item those clients pay besides the gold (rare ones included), handed to the member who did most. The numbers
    /// and words, pure so they are covered by EditMode tests.
    /// </summary>
    public static class ErrandRules
    {
        public static bool IsErrand(QuestType t) => t is QuestType.Gather or QuestType.Collect or QuestType.Deliver;

        /// <summary>A quest fought like a hunt in an area round a den (a hunt, or bringing in a part of its kind).</summary>
        public static bool IsHuntLike(QuestType t) => t is QuestType.Hunt or QuestType.Collect;

        public static string KindLabel(QuestType t) => t switch
        {
            QuestType.Gather => "채집",
            QuestType.Collect => "납품",
            QuestType.Deliver => "배달",
            QuestType.Escort => "호위",
            _ => "토벌",
        };

        // ---------------- Villages' errands ----------------

        /// <summary>Game hours after a village's errand ended before it posts another.</summary>
        public const float VillageGapHours = 18f;
        /// <summary>Chance each hour that a village with nothing on the board (past the gap) posts an errand.</summary>
        public const float VillageChancePerHour = 0.06f;
        /// <summary>A village asks for the parts of a den within this many cells of it (the monsters that trouble it).</summary>
        public const float CollectReach = 80f;
        /// <summary>How often each kind is picked when more than one can be asked for.</summary>
        public const float DeliverWeight = 0.4f, GatherWeight = 0.3f, CollectWeight = 0.3f;

        /// <summary>Fewest items an errand asks for: below that it is not posted.</summary>
        public const int MinCount = 2;

        /// <summary>Items a village asks for: four to six.</summary>
        public static int VillageCount(GameRandom rng) => rng != null ? rng.Range(4, 7) : 5;

        /// <summary>
        /// Items asked when <paramref name="supply"/> is all the guild can reach (0 = cannot be asked): never more than that,
        /// never fewer than <see cref="MinCount"/>.
        /// </summary>
        public static int Count(int wanted, int supply)
        {
            int n = Mathf.Min(wanted, supply);
            return n < MinCount ? 0 : n;
        }

        /// <summary>What the known gathering spots of a kind give at the least: their smallest yield each.</summary>
        public static int GatherSupply(int readySpots, bool ore)
            => Mathf.Max(0, readySpots) * (ore ? Places.PlaceRules.OreMin : Places.PlaceRules.HerbMin);

        /// <summary>…less what errands already asked of them still need (<paramref name="spokenFor"/>): never below nothing.</summary>
        public static int GatherSupply(int readySpots, bool ore, int spokenFor)
            => Mathf.Max(0, GatherSupply(readySpots, ore) - Mathf.Max(0, spokenFor));

        /// <summary>
        /// A member gathering for itself leaves the spots of a kind alone while taking one more would leave the errands
        /// on the board short (<paramref name="spokenFor"/>: what they still need).
        /// </summary>
        public static bool KeptForErrands(int readySpots, bool ore, int spokenFor)
            => spokenFor > 0 && GatherSupply(readySpots - 1, ore) < spokenFor;

        /// <summary>The part a species is asked for: its likeliest loot, as many per kill as it drops on average.</summary>
        public static float PerKill(LootEntry e) => e == null ? 0f : Mathf.Clamp01(e.chance) * (Mathf.Max(1, e.min) + Mathf.Max(e.min, e.max)) * 0.5f;

        /// <summary>A kind's part worth asking for comes at this many a kill at least.</summary>
        public const float MinPerKill = 0.3f;

        /// <summary>
        /// The part of this kind a client asks for (the most per kill, at least <see cref="MinPerKill"/>), or null: never a
        /// one-of-a-kind item (a crown a pack leader wears) nor a dragon's.
        /// </summary>
        public static LootEntry CollectEntry(MonsterDefinition m)
        {
            if (m == null || m.isDragon || m.isRegionLord) return null;
            LootEntry best = null;
            float bestPer = MinPerKill - 0.0001f;
            foreach (var e in m.loot)
            {
                if (e?.item == null || e.item.IsUnique) continue;
                float per = PerKill(e);
                if (per <= bestPer) continue;
                bestPer = per;
                best = e;
            }
            return best;
        }

        /// <summary>Kills it will likely take to bring in this many at this many a kill.</summary>
        public static int CollectKills(int count, float perKill) => Mathf.Max(1, Mathf.CeilToInt(count / Mathf.Max(0.1f, perKill) - 0.001f));

        /// <summary>Parts a den can give: what was seen there (two at least) a kill each, and a little over (they breed).</summary>
        public static int CollectSupply(int observed, float perKill) => Mathf.FloorToInt(Mathf.Max(2, observed) * perKill * 1.2f);

        // ---------------- What they pay (the client's total, fee included) ----------------

        static float GatherBase(DangerLevel d) => d switch { DangerLevel.Low => 20f, DangerLevel.Medium => 50f, _ => 110f };
        static float DeliverBase(DangerLevel d) => d switch { DangerLevel.Low => 45f, DangerLevel.Medium => 90f, _ => 170f };

        /// <summary>
        /// Gathering: the goods themselves (the members do not sell them) a quarter over their worth, and the walk to where they
        /// grow by its danger and distance. <paramref name="factor"/> scales it (the proposal styles).
        /// </summary>
        public static int GatherGold(int itemValue, int count, DangerLevel danger, float distance, float factor = 1f)
            => QuestPricing.RoundGold((Mathf.Max(0, itemValue) * Mathf.Max(1, count) * 1.25f + GatherBase(danger) * QuestPricing.FarFactor(distance)) * factor);

        /// <summary>Bringing in a part: the hunt it takes (the likely kills) and the parts' worth (they are not sold).</summary>
        public static int CollectGold(DangerLevel danger, int kills, int itemValue, int count, float distance, float factor = 1f)
            => QuestPricing.RoundGold(QuestPricing.HuntReward(danger, kills, distance, factor) + Mathf.Max(0, itemValue) * Mathf.Max(1, count));

        /// <summary>Carrying goods: the road's danger at the far end, and its length.</summary>
        public static int DeliverGold(DangerLevel danger, float distance, float factor = 1f)
            => QuestPricing.RoundGold(DeliverBase(danger) * QuestPricing.FarFactor(distance) * factor);

        /// <summary>EXP besides the gold: a hunt's for bringing in parts (they are fought for), less for a walk or a dig.</summary>
        public static int Exp(QuestType t, int gold)
            => t == QuestType.Collect ? QuestPricing.RewardExp(gold) : Mathf.Max(5, Mathf.RoundToInt(QuestPricing.RewardExp(gold) * 0.6f / 5f) * 5);

        /// <summary>Most members the client takes: a dig wants few hands (one, two, three), the road and a hunt as the town's.</summary>
        public static int PartyFor(QuestType t, DangerLevel d)
            => t == QuestType.Gather ? (int)d + 1 : QuestPricing.PartyFor(d);

        // ---------------- The item besides the gold ----------------

        public const string Potion = DefaultContent.Potion;
        public const string Ore = Places.PlaceRules.OreId;
        public const string Herb = Places.PlaceRules.HerbId;
        public static string Gear(int tier) => $"gear_t{Mathf.Clamp(tier, 1, 3)}";

        /// <summary>Rare things a client near home may part with.</summary>
        public static readonly string[] RareNear = { "royal_jelly", "crown_shard" };
        /// <summary>Rare things from far and dangerous work.</summary>
        public static readonly string[] RareFar = { Monsters.RareMonster.TrophyId, Incidents.IncidentRules.RelicId, "dragon_scale", "crown_shard" };

        /// <summary>Distance from town past which a middling client may part with the far rare things.</summary>
        public const float FarRareDistance = 150f;

        /// <summary>
        /// The item a client pays besides the gold (user decision ③ "희귀한 것까지"): mostly potions, flares, ore or herbs —
        /// more the more dangerous — gear now and then (first tier for 낮음, up to the third for 높음), and rarely a rare thing
        /// (3% · 8% · 15%: the far ones only from far or dangerous work).
        /// </summary>
        public static ItemStack PickReward(DangerLevel d, float distance, GameRandom rng)
        {
            rng ??= new GameRandom(Mathf.RoundToInt(distance * 31f) + (int)d);
            float r = rng.Value;
            switch (d)
            {
                case DangerLevel.Low:
                    if (r < 0.03f) return new ItemStack(rng.Pick(RareNear), 1);
                    if (r < 0.13f) return new ItemStack(Gear(1), 1);
                    break;
                case DangerLevel.Medium:
                    if (r < 0.08f) return new ItemStack(rng.Pick(distance >= FarRareDistance ? RareFar : RareNear), 1);
                    if (r < 0.18f) return new ItemStack(Gear(2), 1);
                    if (r < 0.40f) return new ItemStack(Gear(1), 1);
                    break;
                default:
                    if (r < 0.15f) return new ItemStack(rng.Pick(RareFar), 1);
                    if (r < 0.25f) return new ItemStack(Gear(3), 1);
                    if (r < 0.55f) return new ItemStack(Gear(2), 1);
                    break;
            }
            return Common(d, rng);
        }

        /// <summary>The everyday part of the item pool, by danger step (0, 1, 2).</summary>
        static ItemStack Common(DangerLevel d, GameRandom rng)
        {
            int step = Mathf.Clamp((int)d, 0, 2);
            float r = rng.Value;
            if (r < 0.40f) return new ItemStack(Potion, 1 + step + rng.Range(0, 2));
            if (r < 0.60f) return new ItemStack(Flares.FlareRules.ItemId, 1 + step);
            if (r < 0.80f) return new ItemStack(Ore, 2 + step * 2 + rng.Range(0, 2));
            return new ItemStack(Herb, 3 + step * 2 + rng.Range(0, 3));
        }

        /// <summary>Potions a member keeps from a reward (it buys two itself); the rest it sells.</summary>
        public const int PotionKeep = 4;

        // ---------------- Deliveries ----------------

        /// <summary>Close enough to the village's doorstep to hand the goods over (cells).</summary>
        public const float DeliverReach = 2.5f;

        /// <summary>What a delivery carries (words only).</summary>
        public static readonly string[] Goods = { "약품 상자", "편지 꾸러미", "곡식 자루", "대장간 연장", "옷감 묶음", "등잔 기름", "씨앗 자루" };

        public static string PickGoods(GameRandom rng) => rng != null ? rng.Pick(Goods) : Goods[0];

        // ---------------- Words ----------------

        public const string GoodsLostReason = "배달 짐을 잃었습니다";
        public const string DestinationFellReason = "배달할 마을이 무너졌습니다";
        public const string NothingLeftReason = "더 모을 곳이 없습니다";

        /// <summary>"철광석 x5", "고블린 귀 x4".</summary>
        public static string ItemCount(string name, int count) => count > 1 ? $"{name} x{count}" : name;

        public static string GatheredLine(string itemName, int got, int progress, int required)
            => progress >= required ? $"{itemName} 다 모았다! 돌아가자." : $"{itemName} {got}개 챙겼다. ({progress}/{required})";

        public static string CollectedLine(string itemName, int progress, int required)
            => progress >= required ? $"{itemName} 다 모았다!" : $"{itemName} 하나 더! ({progress}/{required})";

        public const string DeliveredLine = "짐을 무사히 전했습니다!";
        public const string WaitForRegrowLine = "다 캐 갔네... 다시 자랄 때까지 기다리자.";

        /// <summary>What a member says on getting the item: kept, worn, or to be sold.</summary>
        public static string RewardLine(string itemName, int count, string how) => $"보상으로 {ItemCount(itemName, count)}을(를) 받았다! {how}".TrimEnd();

        // ---------------- Shared helpers ----------------

        /// <summary>The items' worth (base value × count), 0 for nothing.</summary>
        public static int Worth(ItemDefinition item, int count) => item == null ? 0 : item.baseValue * Mathf.Max(0, count);
    }
}
