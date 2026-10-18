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

        // ---------------- Every incident ----------------

        public static DangerLevel Danger(Incident i) => i.Kind == IncidentKind.Horde ? HordeDanger(i.HordeLevel, i.HordeSize) : DangerLevel.High;

        /// <summary>The incident's own requirement, or null to rate it by its danger like any quest.</summary>
        public static float? Requirement(Incident i) => IsPartyWork(i) && i.Requirement > 0f ? i.Requirement : null;

        /// <summary>Work for a whole party (the recommended level is per member of a full one).</summary>
        public static bool IsPartyWork(Incident i) => i.Kind is IncidentKind.RareMonster or IncidentKind.Horde;

        /// <summary>Who pays for an incident's quest.</summary>
        public static string ClientName(IncidentKind k) => k switch
        {
            IncidentKind.RareMonster => "희귀 몬스터 수집가",
            IncidentKind.Horde => "우리 마을",
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
                _ => $"{QuestBoard.DescribeArea(i.RumourCentre)} 부근에서 무슨 일이 일어났다는 소문입니다.",
            };
        }

        /// <summary>A later rumour of where a moving incident (a horde) is now.</summary>
        public static string RumourUpdateText(Incident i)
            => $"{i.SpeciesName} 무리가 {QuestBoard.DescribeArea(i.RumourCentre)} 부근까지 왔다는 소식입니다.";

        public static string SightedText(Incident i) => i.Kind switch
        {
            IncidentKind.RareMonster => $"{i.TargetName}(Lv.{i.TargetLevel}){Adventurers.MemoryRecorder.Josa(i.TargetName, "을", "를")} 직접 확인했습니다. 의뢰 구역을 그 자리로 옮깁니다.",
            IncidentKind.Horde => $"{i.SpeciesName} 무리(Lv.{i.HordeLevel}, {i.Members.Count}마리)를 직접 확인했습니다. 의뢰 구역이 무리를 따라갑니다.",
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
            _ => null,
        };

        /// <summary>Short headline of the kind ("[희귀 몬스터]").</summary>
        public static string Tag(IncidentKind k) => k switch
        {
            IncidentKind.RareMonster => "[희귀 몬스터]",
            IncidentKind.Horde => "[대규모 무리]",
            IncidentKind.Rescue => "[구출]",
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
                _ => $"[돌발] {q.Species?.displayName} ({where})",
            };
        }
    }
}
