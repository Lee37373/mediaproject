using System.Collections.Generic;
using UnityEngine;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Units;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Rules for hunting without a quest (P4-37, user request 2026-09-20): with an empty board the
    /// guild used to stand in the plaza all day. Members now walk out on their own, kill what they
    /// can handle and bring the loot back. Pure, so the decisions can be tested.
    /// </summary>
    public static class FreeHunt
    {
        /// <summary>How far from the town centre a free hunt roams at the least (user decision: 40 cells, wide).</summary>
        public const float Radius = 40f;
        /// <summary>How far the strongest band roams: the far edge where the slime king lives.</summary>
        public const float MaxRadius = 180f; // P4-53: the map doubled (256), the far edge moved out with it

        /// <summary>
        /// How far this band dares to roam (P4-38, user decision 2026-09-21: "wider as they grow").
        /// At a flat 40 cells the hunt never left the slime rings — P5-1 puts goblins and skulls at
        /// 40–70 — so nothing else was ever met. Everyone knows the dangerous kinds live further out
        /// (<see cref="MonsterDefinition.distanceBand"/>, 0 = by the walls, 1 = the edge); where their
        /// dens are still has to be found. The band goes as far out as the furthest species it could
        /// take on as a pack.
        /// </summary>
        public static float RoamRadius(IReadOnlyList<CombatStats> band, IReadOnlyList<float> hp, IEnumerable<MonsterDefinition> species)
        {
            float frontier = 0f;
            if (species != null)
                foreach (var s in species)
                    if (s != null && s.distanceBand > frontier && CanTake(band, hp, StatsOf(s), ExpectedEnemies(1, s, 0)))
                        frontier = s.distanceBand;
            return Mathf.Lerp(Radius, MaxRadius, frontier);
        }
        /// <summary>Game hours one trip lasts before heading back to sell what it found.</summary>
        public const float MaxHours = 8f;
        /// <summary>
        /// Items worth walking home with. This is the real brake on a free hunt: the walk back,
        /// the sale and the walk out again cost game hours, so the countryside gets a breather
        /// between rounds. Counted in items, not stacks — loot merges by id, so a hunter with
        /// twenty jellies still carries one stack (first measured run: the slimes were wiped out
        /// in two game hours because the "full bag" never triggered).
        /// </summary>
        public const int FullBag = 12;

        /// <summary>Leave before the trip turns into a funeral: nobody ordered this hunt.</summary>
        public const float GoHomeHpRatio = 0.6f;
        /// <summary>Allies this close count towards the strength used to judge a fight.</summary>
        public const float AllyRadius = 8f;

        /// <summary>
        /// Whether this member should set off with a band of this size. A priest's attack is a
        /// candle — it heals a party, it does not clear a field — so it stays home rather than
        /// hunt alone (user report 2026-09-20: "the healer is going out on its own, that makes no
        /// sense"). Everyone else may go alone if nobody else feels like it.
        /// </summary>
        public static bool WorthGoing(AdventurerClass cls, int bandSize)
            => bandSize >= 2 || cls != AdventurerClass.Priest;

        /// <summary>
        /// Share of the band's HP a fight may be expected to cost and still be picked (user
        /// decision: avoid what we cannot handle). Below <see cref="GoHomeHpRatio"/>'s margin, so a
        /// fight the band takes on does not by itself send it home.
        /// </summary>
        public const float AcceptableLoss = 0.5f;

        /// <summary>Average hit multiplier from critical strikes.</summary>
        static float CritFactor => 1f + Combat.CritChance * (Combat.CritMultiplier - 1f);

        /// <summary>
        /// Share of the band's current HP that beating <paramref name="foes"/> of these is expected
        /// to cost (P4-38). Worked out from the real combat numbers with the real damage formula, so
        /// the judgement moves with the balance: the old one divided a quest-scale "power" by the
        /// head count and refused every pack a levelled member could in fact beat alone (user report
        /// 2026-09-21: "a free hunt never takes on anything above a slime").
        ///
        /// The band focuses one foe at a time (P4-27) while every foe still standing hits back, so
        /// the damage taken grows with the triangle number of the pack: 1 + 2 + … + n kill-times.
        /// Healing, skills and the pack's call for help are left out — it is a rough estimate that
        /// errs on neither side by much.
        /// </summary>
        public static float ExpectedLoss(IReadOnlyList<CombatStats> band, IReadOnlyList<float> hp, CombatStats foe, int foes)
        {
            if (band == null || band.Count == 0) return float.PositiveInfinity;
            foes = Mathf.Max(1, foes);
            float ourDps = 0f, ourHp = 0f, ourDef = 0f;
            for (int i = 0; i < band.Count; i++)
            {
                var s = band[i];
                ourDps += Combat.Mitigate(s.Attack, foe.Defense) * CritFactor / Mathf.Max(0.1f, s.AttackCooldown);
                ourHp += hp != null && i < hp.Count ? hp[i] : s.MaxHp;
                ourDef += s.Defense;
            }
            if (ourHp <= 0f) return float.PositiveInfinity;
            ourDef /= band.Count;
            float foeDps = Combat.Mitigate(foe.Attack, ourDef) * CritFactor / Mathf.Max(0.1f, foe.AttackCooldown);
            float killTime = foe.MaxHp / Mathf.Max(0.01f, ourDps);
            float taken = foeDps * killTime * foes * (foes + 1) * 0.5f;
            return taken / ourHp;
        }

        /// <summary>Whether a fight is worth picking: its expected cost is within <see cref="AcceptableLoss"/>.</summary>
        public static bool CanTake(IReadOnlyList<CombatStats> band, IReadOnlyList<float> hp, CombatStats foe, int foes)
            => ExpectedLoss(band, hp, foe, foes) <= AcceptableLoss;

        /// <summary>A den's last count above this adds nothing to its appeal: a trip only takes so many.</summary>
        public const int DenCountCap = 6;

        /// <summary>
        /// How many the guild expects at a den it last counted <paramref name="lastCount"/> at,
        /// <paramref name="hoursSince"/> ago (P4-42): everyone knows how fast a kind breeds, so a
        /// den left with its last few is worth a look again later. The old count stayed put until
        /// someone happened by. An emptied den is known to be settled again after
        /// <see cref="EcosystemManager.RefillHours"/> (P4-43).
        /// </summary>
        public static int ExpectedDenCount(MonsterDefinition species, int lastCount, float hoursSince, float growthFactor = 1f)
        {
            if (species == null || hoursSince <= 0f) return Mathf.Max(0, lastCount);
            if (lastCount <= 0)
            {
                if (hoursSince < EcosystemManager.RefillHours) return 0;
                lastCount = EcosystemManager.RefillCount;
                hoursSince -= EcosystemManager.RefillHours;
            }
            float pop = lastCount, cap = Mathf.Max(1, species.nestCapacity);
            for (float h = 0f; h < hoursSince && pop < cap; h += 1f)
            {
                float rate = species.growthPerHour * growthFactor * pop * (1f - pop / cap);
                if (pop < 2f) rate *= 0.35f;
                pop += Mathf.Max(0f, rate) * Mathf.Min(1f, hoursSince - h);
            }
            return Mathf.Clamp(Mathf.FloorToInt(pop), lastCount, (int)cap);
        }
        /// <summary>Chance to go scouting even with a good den known, so new dens keep being found.</summary>
        public const float ScoutChance = 0.15f;

        /// <summary>
        /// How worth walking to a known den is (P4-40, user report 2026-09-21: "they hardly fight
        /// early"): EXP on offer over the walk. A measured run spent 55% of its time hunting and 3%
        /// fighting — random spots across a 65-cell circle mostly land on empty ground far out.
        /// </summary>
        public static float DenScore(MonsterDefinition species, int lastCount, float distance)
            => species == null || lastCount <= 0 ? 0f
             : species.expValue * Mathf.Min(lastCount, DenCountCap) / (8f + Mathf.Max(0f, distance));

        /// <summary>
        /// How far from town to scout: uniform in distance, not in area. Area-uniform points pile up
        /// at the rim, so the near rings — where the weak prey lives — were hardly ever visited.
        /// </summary>
        public static float ScoutDistance(float t01, float reach) => Mathf.Lerp(12f, Mathf.Max(12f, reach), Mathf.Clamp01(t01));

        /// <summary>A species' stats before any gear it picked up, for a den judged from afar.</summary>
        public static CombatStats StatsOf(MonsterDefinition def) => new CombatStats
        {
            MaxHp = Combat.ScaledHp(def.maxHp), Attack = def.attack, Defense = def.defense,
            AttackRange = def.attackRange, AttackCooldown = def.attackCooldown,
            MoveSpeed = def.moveSpeed, SightRadius = def.sightRadius,
        };

        /// <summary>A pack hunter never travels alone: assume at least this many (user request).</summary>
        public const int AssumedPackSize = 3;
        /// <summary>Share of a nearby den that would actually pile in; the rest stays home.</summary>
        public const float NestJoinFraction = 0.5f;

        /// <summary>
        /// How many to plan for after spotting some (user request 2026-09-20: "seeing one should
        /// tell them there are probably more"). Sight is narrow and packs spread out, so what is
        /// visible is a floor, not the count: a pack hunter brings friends, and standing by a den
        /// means part of it can join in. <paramref name="nestNearby"/> is what the guild last
        /// counted there — it comes from <see cref="GuildIntel"/>, so it is what the whole guild has
        /// seen between them, not just this member.
        ///
        /// Only species that actually gang up get the extra: slimes neither hunt in packs nor come
        /// at people, so a den of them is still one slime at a time, and reckoning with the whole
        /// den made a measured run refuse every fight there was.
        /// </summary>
        public static int ExpectedEnemies(int seen, MonsterDefinition species, int nestNearby)
        {
            int expected = Mathf.Max(1, seen);
            if (species == null || (!species.packHunter && !species.aggressiveToHumans)) return expected;
            if (species.packHunter) expected = Mathf.Max(expected, AssumedPackSize);
            return Mathf.Max(expected, Mathf.CeilToInt(nestNearby * NestJoinFraction));
        }

        /// <summary>
        /// Time to walk home. A newly posted quest wins: the player's orders come before a hunt the
        /// member picked for itself. Hurt counts too, and well before the normal retreat threshold —
        /// a quest is worth bleeding for, an afternoon's hunting is not.
        /// </summary>
        public static bool ShouldGoHome(float hoursHunting, int lootItems, bool questAvailable, float hpRatio = 1f)
            => questAvailable || hoursHunting >= MaxHours || lootItems >= FullBag || hpRatio < GoHomeHpRatio;
    }
}
