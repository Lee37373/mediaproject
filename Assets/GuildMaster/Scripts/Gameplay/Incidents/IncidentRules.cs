using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using UnityEngine;

namespace GuildMaster.Incidents
{
    /// <summary>
    /// The numbers of the sudden incidents (P4-85, user decisions 2026-10-18; started from the recommended values, to be
    /// tuned by measurement). Pure, so EditMode tests cover them.
    /// </summary>
    public static class IncidentRules
    {
        // ---------------- Rumours: the guild hears of every incident, roughly where ----------------

        /// <summary>Radius (cells) of the circle a rumour puts an incident in.</summary>
        public const float RumourRadius = 20f;
        /// <summary>Game hours between an incident and its rumour reaching the town.</summary>
        public const float RumourDelayMin = 0.5f, RumourDelayMax = 2f;
        /// <summary>Once the guild has seen it, the area shrinks round where it was seen.</summary>
        public const float SightedRadius = 10f;

        /// <summary>
        /// How far off the true spot the rumour's centre may be so that what moves about within <paramref name="roam"/> of
        /// it still stays inside the circle (at least a little off, so the circle is never the spot).
        /// </summary>
        public static float RumourOffset(float roam) => Mathf.Max(2f, RumourRadius - roam - 2f);

        /// <summary>The rumour's centre: the true spot moved a random way by up to <paramref name="maxOffset"/>.</summary>
        public static Vector2 RumourCentre(Vector2 truth, float maxOffset, GameRandom rng)
        {
            float angle = rng.Range(0f, Mathf.PI * 2f);
            float dist = maxOffset * Mathf.Sqrt(rng.Value);
            return truth + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
        }

        public static float RumourDelay(GameRandom rng) => rng.Range(RumourDelayMin, RumourDelayMax);

        // ---------------- Rare monster ----------------

        /// <summary>Chance per game hour that a rare variant appears (about once in three days).</summary>
        public const float RareChancePerHour = 1f / 72f;
        /// <summary>Not before this game day.</summary>
        public const int RareFirstDay = 2;
        /// <summary>Game hours it lingers before it is gone.</summary>
        public const float RareStayHours = 12f;
        /// <summary>It keeps this close to where it appeared (the circle is drawn so it never leaves it).</summary>
        public const float RareRoam = 8f;
        /// <summary>It appears at least this far from the town...</summary>
        public const float RareMinDistance = 40f;
        /// <summary>...in the inner land this often, else beyond it (P4-81 depth).</summary>
        public const float RareInnerShare = 0.6f;
        /// <summary>...and this far clear of any den's territory and of the villages, so it does not start a brawl the moment it appears.</summary>
        public const float RareNestMargin = 10f, RareVillageMargin = 20f;
        /// <summary>Its hunt pays a five-kill 높음 hunt at that distance times this ("a big reward").</summary>
        public const float RareRewardFactor = 2.5f;
        /// <summary>Most members on its hunt.</summary>
        public const int RareParty = 4;
        /// <summary>What it asks of a party per level of its own (a full party of about its level reaches "적당").</summary>
        public const float RareRequirementPerLevel = 2.5f;

        /// <summary>The client's total for the rare hunt (fee included) at that distance from town.</summary>
        public static int RareReward(float distance)
            => QuestPricing.HuntReward(DangerLevel.High, QuestPricing.HuntKills, distance, RareRewardFactor);

        /// <summary>What the rare hunt asks of a party: the level it is said to be (the land's + the bonus) × <see cref="RareRequirementPerLevel"/>.</summary>
        public static float RareRequirement(int landLevel) => RareRequirementPerLevel * RareMonster.Level(landLevel);

        // ---------------- Horde ----------------

        /// <summary>Chance per game hour that a horde sets out (about once in four days).</summary>
        public const float HordeChancePerHour = 1f / 96f;
        /// <summary>Not before this game day.</summary>
        public const int HordeFirstDay = 4;
        /// <summary>How many walk in (more than any den holds).</summary>
        public const int HordeMin = 10, HordeMax = 16;
        /// <summary>Born this many levels above the site it heads for.</summary>
        public const int HordeLevelBonus = 2;
        /// <summary>It heads for the empty site nearest the town at least this far out (the dens' own minimum).</summary>
        public const float HordeMinDistance = 20f;
        /// <summary>
        /// It sets out this far from its site, towards a map edge (measurement 2026-10-18: from the edge itself a Lv3 horde
        /// had the outer land's dens to cross and died there within hours). On the march it starts no fights.
        /// </summary>
        public const float HordeWalk = 90f;
        /// <summary>Game hours between rumours of where it is now, while the guild has not seen it.</summary>
        public const float HordeRumourEvery = 2f;
        /// <summary>Share of it the quest asks to bring down ("intercepted"): the rest may still get there.</summary>
        public const float HordeShare = 0.7f;
        /// <summary>Its quest pays a hunt of all of it, raised as a defence is (urgent).</summary>
        public const float HordeRewardFactor = Villages.VillageRules.DefenceRewardFactor;
        /// <summary>What it asks of a party: level × head count × this (a full party of 1.3 × its level takes twelve).</summary>
        public const float HordeRequirementPerHead = 0.3f;
        public const int HordeParty = 4;

        // A hair under, so seven in ten of ten stays seven (0.7f × 10 is a hair over 7).
        public static int HordeKills(int size) => Mathf.Max(1, Mathf.CeilToInt(size * HordeShare - 1e-3f));

        public static float HordeRequirement(int level, int size) => Mathf.Max(1, level) * Mathf.Max(1, size) * HordeRequirementPerHead;

        public static DangerLevel HordeDanger(int level, int size) => QuestBoard.HuntDanger(level, size);

        public static int HordeReward(int level, int size, float distance)
            => QuestPricing.HuntReward(HordeDanger(level, size), size, distance, HordeRewardFactor);

        /// <summary>A head count the way a rumour tells it: rounded to five ("about fifteen").</summary>
        public static int RumouredCount(int size) => Mathf.Max(5, Mathf.RoundToInt(size / 5f) * 5);

        // ---------------- Rescue ----------------

        /// <summary>Chance per game hour that a traveller is ambushed on the road (about every other day).</summary>
        public const float RescueChancePerHour = 1f / 48f;
        public const int RescueFirstDay = 2;
        /// <summary>The ambush happens this far from our town, on the road to a village at least a little farther.</summary>
        public const float RescueMinDistance = 40f, RescueMaxDistance = 90f;
        /// <summary>How many fall on the traveller.</summary>
        public const int AmbushMin = 2, AmbushMax = 4;
        /// <summary>
        /// Game hours the traveller holds out against them (the rescue's time limit). Measurement 2026-10-18: at 4 (the
        /// recommended start) one rescue in seven was in time — the rumour alone takes up to two hours.
        /// </summary>
        public const float RescueHoldHours = 6f;
        /// <summary>Its quest pays a hunt of the ambushers there times this (at 2, 56-60 G near the town: nobody took them).</summary>
        public const float RescueRewardFactor = 3f;
        /// <summary>
        /// Never less than this (the client's total, fee included): P4-90, user decision 2026-10-19 — at ×3 a 낮음 rescue
        /// near the town still paid about 70 G and nobody took it.
        /// </summary>
        public const int RescueMinReward = 150;
        /// <summary>Share of travellers heading for our town (the rest leave it for the village).</summary>
        public const float RescueToTownShare = 0.6f;
        /// <summary>Ambushers left once it is over scatter after this many game hours, when nobody sees them.</summary>
        public const float StragglerHours = 3f;

        public static int RescueReward(DangerLevel danger, int ambushers, float distance)
            => Mathf.Max(RescueMinReward, QuestPricing.HuntReward(danger, ambushers, distance, RescueRewardFactor));

        /// <summary>
        /// Hit points that last <paramref name="hours"/> against these blows (each attack / cooldown, a defenceless target,
        /// criticals on average): the time limit of a rescue is how long the traveller holds out.
        /// </summary>
        public static float HoldOutHp(System.Collections.Generic.IEnumerable<(float attack, float cooldown)> blows, float hours, float realSecondsPerHour)
        {
            float dps = 0f;
            foreach (var (attack, cooldown) in blows)
                dps += Units.Combat.Mitigate(attack, 0f) * (1f + Units.Combat.CritChance * (Units.Combat.CritMultiplier - 1f)) / Mathf.Max(0.1f, cooldown);
            return dps * hours * realSecondsPerHour;
        }

        static readonly string[] TravellerNames = { "하란", "미로", "세나", "도윤", "라엘", "보리", "이안", "유나", "테스", "카이" };
        static readonly string[] TravellerRoles = { "상인", "여행자", "순례자", "행상" };

        public static string TravellerName(GameRandom rng)
            => $"{TravellerRoles[rng.Range(0, TravellerRoles.Length)]} {TravellerNames[rng.Range(0, TravellerNames.Length)]}";

        /// <summary>What a traveller's cart spills when it falls.</summary>
        public static readonly Data.ItemStack[] Cargo = { new(Data.DefaultContent.Potion, 2), new("iron_ore", 2) };

        // ---------------- Ruin ----------------

        /// <summary>Chance per game hour that word of a ruin reaches the town (about once in three days).</summary>
        public const float RuinChancePerHour = 1f / 80f;
        public const int RuinFirstDay = 2;
        /// <summary>At most this many ruins rumoured and not yet looted at once (a ruin waits for its explorers).</summary>
        public const int MaxOpenRuins = 2;
        /// <summary>Guardians in each room after the first, and in the treasure room.</summary>
        public const int GuardsPerRoom = 2, TreasureGuards = 3;
        /// <summary>Born this many levels above the land round the way down.</summary>
        public const int GuardLevelBonus = 2;
        /// <summary>The chest opens once no guardian is this close to it.</summary>
        public const float ChestClearRadius = 6f;
        public const int RuinParty = 4;
        /// <summary>What the treasure is: relics (more of them from the outer land) and a piece of gear by the guardians' level.</summary>
        public const string RelicId = "ancient_relic";

        public static int RuinReward(int level, int guards, float distance)
            => QuestPricing.HuntReward(QuestBoard.HuntDanger(level, guards), guards, distance);

        public static float RuinRequirement(int level, int guards) => HordeRequirement(level, guards);

        /// <summary>The chest's contents: relics (two from the outer land, one from the inner) and the guardians' tier of gear.</summary>
        public static void AddTreasure(bool outer, int level, System.Collections.Generic.List<Data.ItemStack> loot)
        {
            loot.Add(new Data.ItemStack(RelicId, outer ? 2 : 1));
            loot.Add(new Data.ItemStack($"gear_t{RareMonster.GearTier(level)}", 1));
        }

        static readonly System.Collections.Generic.Dictionary<World.Region, string[]> RuinNames = new()
        {
            { World.Region.Grassland, new[] { "잊힌 지하실", "풀숲 아래 신전" } },
            { World.Region.Desert, new[] { "모래에 묻힌 신전", "태양의 지하묘지" } },
            { World.Region.Snow, new[] { "얼어붙은 지하묘지", "서리 왕의 무덤" } },
            { World.Region.Swamp, new[] { "가라앉은 사원", "이끼 낀 제단" } },
            { World.Region.Wasteland, new[] { "잿빛 요새 터", "무너진 탑의 지하" } },
        };

        /// <summary>Its region's names in turn; past them, numbered ("모래에 묻힌 신전 2").</summary>
        public static string RuinName(World.RuinSite site)
        {
            if (!RuinNames.TryGetValue(site.Region, out var names)) return "오래된 유적";
            int k = site.RegionOrdinal;
            return k < names.Length ? names[k] : $"{names[k % names.Length]} {k / names.Length + 1}";
        }

        public static string RuinProgress(Incident i)
            => i.ChestOpened ? "보물 확보" : i.Sighted ? $"탐사 중 · 수호자 {i.Members.Count}마리 남음(추정)" : "입구 찾는 중";

        // ---------------- Nemesis bounty (P4-109) ----------------

        /// <summary>The bounty's danger: a hunt of its level and the pack it was seen with (it is fought among them).</summary>
        public static DangerLevel NemesisDanger(int level, int pack) => QuestBoard.HuntDanger(level, Mathf.Max(1, pack));

        /// <summary>The town's total for the bounty (fee included): a five-kill hunt of that danger at that distance, times the factor.</summary>
        public static int NemesisReward(DangerLevel danger, float distance, float factor)
            => QuestPricing.HuntReward(danger, QuestPricing.HuntKills, distance, factor);

        // ---------------- Every incident ----------------

        public static DangerLevel Danger(Incident i) => i.Kind switch
        {
            IncidentKind.Horde => HordeDanger(i.HordeLevel, i.HordeSize),
            IncidentKind.Rescue => QuestBoard.HuntDanger(i.TargetLevel, Mathf.Max(1, i.Members.Count)),
            IncidentKind.Ruin => QuestBoard.HuntDanger(i.TargetLevel, Mathf.Max(1, i.Members.Count)),
            IncidentKind.Nemesis => NemesisDanger(i.TargetLevel, i.PackSize),
            _ => DangerLevel.High,
        };

        /// <summary>The incident's own requirement, or null to rate it by its danger like any quest.</summary>
        public static float? Requirement(Incident i) => IsPartyWork(i) && i.Requirement > 0f ? i.Requirement : null;

        /// <summary>Work for a whole party (the recommended level is per member of a full one).</summary>
        public static bool IsPartyWork(Incident i) => i.Kind is IncidentKind.RareMonster or IncidentKind.Horde or IncidentKind.Ruin;

        /// <summary>Who pays for an incident's quest.</summary>
        public static string ClientName(IncidentKind k) => k switch
        {
            IncidentKind.RareMonster => "희귀 몬스터 수집가",
            IncidentKind.Horde or IncidentKind.Nemesis => "우리 마을",
            IncidentKind.Rescue => "여행자 가족",
            _ => "학자회",
        };

        // ---------------- What the guild is told (log, notifications) ----------------

        /// <summary>The word with 이/가 (을/를...) chosen by its last syllable.</summary>
        static string With(string word, string withBatchim, string withoutBatchim)
            => word + Adventurers.MemoryRecorder.Josa(word, withBatchim, withoutBatchim);

        public static string RumourText(Incident i)
        {
            string client = With(ClientName(i.Kind), "이", "가");
            return i.Kind switch
            {
                IncidentKind.RareMonster => i.Sighted
                    ? $"{QuestBoard.DescribeArea(i.RumourCentre)}에서 {With(i.TargetName, "이", "가")} 목격되었습니다. {client} 사라지기 전에 잡아 달라는 의뢰를 냈습니다."
                    : $"{QuestBoard.DescribeArea(i.RumourCentre)} 부근에 {With(i.TargetName, "이", "가")} 나타났다는 소문입니다. {client} 사라지기 전에 잡아 달라는 의뢰를 냈습니다.",
                IncidentKind.Horde => i.Sighted
                    ? $"{QuestBoard.DescribeArea(i.RumourCentre)}에서 {i.SpeciesName} 큰 무리(약 {i.HordeSize}마리)가 마을 쪽으로 오는 것이 보입니다. {client} 도착하기 전에 막아 달라는 의뢰를 냈습니다."
                    : $"{QuestBoard.DescribeArea(i.RumourCentre)} 부근에서 {i.SpeciesName} 큰 무리(약 {RumouredCount(i.HordeSize)}마리)가 마을 쪽으로 오고 있다는 소문입니다. {client} 도착하기 전에 막아 달라는 의뢰를 냈습니다.",
                IncidentKind.Rescue => i.Sighted
                    ? $"{QuestBoard.DescribeArea(i.RumourCentre)}에서 {With(i.TargetName, "이", "가")} 몬스터에게 습격받고 있습니다. {client} 구해 달라는 의뢰를 냈습니다. 오래 버티지 못합니다."
                    : $"{QuestBoard.DescribeArea(i.RumourCentre)} 부근에서 {With(i.TargetName, "이", "가")} 몬스터에게 습격받고 있다는 소식입니다. {client} 구해 달라는 의뢰를 냈습니다. 오래 버티지 못합니다.",
                IncidentKind.Ruin => i.Sighted
                    ? $"{QuestBoard.DescribeArea(i.RumourCentre)}에서 '{i.TargetName}'의 입구를 찾았습니다. {client} 안을 탐사해 달라는 의뢰를 냈습니다."
                    : $"{QuestBoard.DescribeArea(i.RumourCentre)} 부근에 '{i.TargetName}'(이)라는 오래된 유적이 있다는 소문입니다. {client} 안을 탐사해 달라는 의뢰를 냈습니다.",
                IncidentKind.Nemesis => $"{client} 원수 {i.TargetName}(Lv.{i.TargetLevel})에게 현상금을 걸었습니다. {QuestBoard.DescribeArea(i.RumourCentre)}에서 찾아보라고 합니다.",
                _ => $"{QuestBoard.DescribeArea(i.RumourCentre)} 부근에서 무슨 일이 일어났다는 소문입니다.",
            };
        }

        /// <summary>The headline of its rumour ("[대규모 무리] 소문"; a bounty is no rumour, the guild saw it).</summary>
        public static string RumourHeadline(Incident i) => i.Kind == IncidentKind.Nemesis ? Tag(i.Kind) : $"{Tag(i.Kind)} 소문";

        /// <summary>A later rumour of where a moving incident (a horde, a nemesis out of sight) is now.</summary>
        public static string RumourUpdateText(Incident i) => i.Kind == IncidentKind.Nemesis
            ? $"원수 {With(i.TargetName, "이", "가")} {QuestBoard.DescribeArea(i.RumourCentre)} 부근에서 보였다는 소문입니다. 현상금 의뢰 구역을 옮깁니다."
            : $"{i.SpeciesName} 무리가 {QuestBoard.DescribeArea(i.RumourCentre)} 부근까지 왔다는 소식입니다.";

        public static string SightedText(Incident i) => i.Kind switch
        {
            IncidentKind.RareMonster => $"{i.TargetName}(Lv.{i.TargetLevel}){Adventurers.MemoryRecorder.Josa(i.TargetName, "을", "를")} 직접 확인했습니다. 의뢰 구역을 그 자리로 옮깁니다.",
            IncidentKind.Horde => $"{i.SpeciesName} 무리(Lv.{i.HordeLevel}, {i.Members.Count}마리)를 직접 확인했습니다. 의뢰 구역이 무리를 따라갑니다.",
            IncidentKind.Rescue => $"습격받는 {With(i.TargetName, "을", "를")} 찾았습니다. {i.SpeciesName} {i.Members.Count}마리가 둘러싸고 있습니다.",
            IncidentKind.Ruin => $"'{i.TargetName}'의 입구를 찾았습니다. 의뢰 구역을 입구로 옮깁니다.",
            _ => "소문의 현장을 확인했습니다.",
        };

        /// <summary>How it ended, as the guild learns it (null when there is nothing to say).</summary>
        public static string EndedText(Incident i) => (i.Kind, i.Outcome) switch
        {
            (IncidentKind.RareMonster, IncidentOutcome.Resolved) => $"{With(i.TargetName, "을", "를")} 쓰러뜨렸습니다!",
            (IncidentKind.RareMonster, IncidentOutcome.SettledByOthers) => $"{With(i.TargetName, "이", "가")} 쓰러졌다는 소식입니다.",
            (IncidentKind.RareMonster, IncidentOutcome.Expired) => $"{With(i.TargetName, "이", "가")} 자취를 감췄습니다.",
            (IncidentKind.Horde, IncidentOutcome.Resolved) => $"{i.SpeciesName} 무리를 막아냈습니다!",
            (IncidentKind.Horde, IncidentOutcome.SettledByOthers) => $"{i.SpeciesName} 무리가 흩어졌다는 소식입니다.",
            (IncidentKind.Horde, IncidentOutcome.Expired) => $"{i.SpeciesName} 무리가 마을 근처에 자리를 잡았다는 소문입니다.",
            (IncidentKind.Rescue, IncidentOutcome.Resolved) => $"{With(i.TargetName, "을", "를")} 구해 냈습니다!",
            (IncidentKind.Rescue, IncidentOutcome.SettledByOthers) => $"{With(i.TargetName, "이", "가")} 무사히 길을 떠났다는 소식입니다.",
            (IncidentKind.Rescue, IncidentOutcome.Expired) => $"{With(i.TargetName, "이", "가")} 끝내 목숨을 잃었다는 소식입니다.",
            (IncidentKind.Ruin, IncidentOutcome.Resolved) => $"'{i.TargetName}'의 보물을 찾아냈습니다!",
            (IncidentKind.Nemesis, IncidentOutcome.Resolved) => $"원수 {With(i.TargetName, "을", "를")} 쓰러뜨려 현상금을 받았습니다!",
            (IncidentKind.Nemesis, IncidentOutcome.SettledByOthers) => $"원수 {With(i.TargetName, "이", "가")} 쓰러져 현상금이 내려갔습니다.",
            _ => null,
        };

        /// <summary>Short headline of the kind ("[희귀 몬스터]").</summary>
        public static string Tag(IncidentKind k) => k switch
        {
            IncidentKind.RareMonster => "[희귀 몬스터]",
            IncidentKind.Horde => "[대규모 무리]",
            IncidentKind.Rescue => "[구출]",
            IncidentKind.Nemesis => "[현상금]",
            _ => "[유적]",
        };

        public static string QuestTitle(Quest q)
        {
            var i = q.Incident;
            string where = QuestBoard.DescribeArea(q.Point) + (i.Sighted ? "" : " 부근");
            return i.Kind switch
            {
                IncidentKind.RareMonster => $"[희귀 몬스터] {i.TargetName} 토벌 ({where})",
                IncidentKind.Horde => $"[대규모 무리] {i.SpeciesName} 무리 요격 x{q.RequiredCount} ({where})",
                IncidentKind.Rescue => $"[구출] {i.TargetName} 구출 ({where})",
                IncidentKind.Ruin => $"[유적 탐사] {i.TargetName} ({where})",
                IncidentKind.Nemesis => $"[현상금] {i.TargetName} 토벌 ({where})",
                _ => $"[돌발] {q.Species?.displayName} ({where})",
            };
        }
    }
}
