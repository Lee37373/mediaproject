using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Monsters
{
    /// <summary>
    /// The dragon (P4-93, user decisions 2026-10-20): one, asleep on its hoard at a lair far out from the town. Some
    /// days it wakes and flies the map at random, breathing fire on whatever it meets — the guild, monsters, the
    /// villages, our town (no limit: the town hall can fall). On the wing only shots reach it; enough of them bring it
    /// down to fight on the ground, then it takes off again. It can be killed asleep or awake and never comes back.
    /// Pure numbers and rules (the decisions marked "내가 정한 것" in PLAN P4-93 are tuned in play).
    /// </summary>
    public static class Dragon
    {
        /// <summary>Beyond the region lords (35) and the ordinary cap (30); it never grows.</summary>
        public const int Level = 40;
        // P4-94 (user decision 2026-10-20: eight members in their twenties win it): test fights at the lair, eight
        // members gear 2 with no stat buildings. ×20 HP, ×4 attack, +5 defence: Lv22 wore it to half in a day and fell
        // back worn out. ×10/×6: Lv20 won with nobody hurt much (the priests healed the slow blows). ×10/×7 and ×10/×8:
        // four died and the rest fell back. ×11/×6.5: Lv20 won with one dead (23 game hours), Lv18 lost two and fell back.
        public const float HpMultiplier = 11f;
        public const float AttackMultiplier = 6.5f;
        public const float DefenseBonus = 0f;
        public const int ExpMultiplier = 15;

        // ---------------- Waking ----------------

        /// <summary>Asleep through these first days whatever the roll (the town has nothing to answer it with yet).</summary>
        public const int GraceDays = 2;
        /// <summary>Chance, each day after the grace, that it wakes some time that day (about one day in five).</summary>
        public const float WakeChance = 0.2f;
        public const float MinFlightHours = 6f;
        public const float MaxFlightHours = 10f;
        /// <summary>Random places on the map it flies by before going home.</summary>
        public const int Waypoints = 3;
        /// <summary>Waypoints stay this far inside the edge of the land.</summary>
        public const int EdgeMargin = 12;

        // ---------------- On the wing ----------------

        /// <summary>Cells a second (scaled time) — far faster than anything that walks.</summary>
        public const float FlightSpeed = 7f;
        /// <summary>
        /// It banks round at most this many degrees a second (so it flies in arcs, not lines) and, on a long way, weaves this many
        /// degrees either side of its course at this pace (radians a second of the weave's phase).
        /// </summary>
        public const float TurnRate = 80f;
        public const float WeaveAngle = 30f;
        public const float WeaveSpeed = 0.8f;
        /// <summary>What it sees from the sky (over forests: nothing hides from above).</summary>
        public const float AirSight = 12f;
        /// <summary>It circles this far from what it burns.</summary>
        public const float HoverDistance = 4f;
        /// <summary>
        /// The longest it lingers over one place burning people or buildings, and over a pack of monsters (the clock runs
        /// for the place, not each target: when one falls it takes the next there). Then it flies on and leaves that place
        /// alone a while (it got stuck over one den before, burning the pack one by one).
        /// </summary>
        public const float EncounterSeconds = 20f;
        public const float MonsterEncounterSeconds = 10f;
        public const float AvoidRadius = 16f;
        public const float AvoidSeconds = 40f;
        /// <summary>
        /// Shots that hurt it this much (share of its health) since it last took off bring it down to fight on the
        /// ground (user decision: "원거리 공격으로 어느 정도 피해를 입으면 내려와서 근접도").
        /// </summary>
        public const float LandAfterDamage = 0.08f;
        /// <summary>Seconds it fights on the ground before taking off again.</summary>
        public const float LandedSeconds = 20f;
        /// <summary>Below this share of its health it flies home and sleeps it off.</summary>
        public const float RetreatBelow = 0.25f;
        /// <summary>Health back a game hour while asleep (share of the most).</summary>
        public const float SleepRegenPerHour = 0.04f;
        /// <summary>Woken at home: the flight round its lair that follows the fight there.</summary>
        public const float GuardRadius = 18f;
        public const float GuardHours = 2f;

        // ---------------- Fire ----------------

        public const float BreathRange = 7f;
        public const float BreathRadius = 2.6f;
        public const float BreathWindup = 0.9f;
        public const float BreathCooldown = 4f;
        /// <summary>Of its attack, on everyone in the blast.</summary>
        public const float BreathDamage = 1.6f;
        /// <summary>Flat damage to a building, gate or village in the blast (their durability has no HP scale).</summary>
        public const float StructureDamage = 60f;

        // ---------------- The hoard (user decisions 2026-10-20) ----------------

        /// <summary>Gold on its hoard when the world begins; it grows by <see cref="HoardDaily"/> each midnight while it lives.</summary>
        public const int HoardStart = 400;
        public const int HoardDaily = 80;
        /// <summary>Taken from the guild's treasury each time its fire catches our buildings, and from a village it burns.</summary>
        public const int PlunderTown = 40;
        public const int PlunderVillage = 30;
        /// <summary>Its gold is carried off in sacks of this worth (item <c>dragon_gold</c>).</summary>
        public const int SackValue = 100;
        /// <summary>What one raider can carry off at a time: sacks of gold, and things off the pile.</summary>
        public const int TakeSacks = 3;
        public const int TakeItems = 2;
        /// <summary>This close to the lair a raider reaches into the hoard.</summary>
        public const float TakeReach = 1.8f;
        /// <summary>
        /// Sneaking past it asleep (user decision: "비운 틈 + 몰래"): every <see cref="SneakCheckSeconds"/> a member this
        /// close may wake it, and a hand in the hoard while it sleeps more likely.
        /// </summary>
        public const float SneakRadius = 6f;
        public const float SneakCheckSeconds = 2f;
        public const float SneakWakeChance = 0.1f;
        public const float TakeWakeChance = 0.3f;
        /// <summary>Robbed (user decision: it hunts the thief): how long it keeps after the one who took from it.</summary>
        public const float ChaseHours = 6f;
        /// <summary>Hoard (as the guild last saw it) worth a trip, for a free hunter, and for the town's quest.</summary>
        public const int RaidMinValue = 200;
        public const int QuestMinValue = 300;
        /// <summary>A brave or greedy band leader on a free hunt, the lair this near, plans a raid this often (once a trip).</summary>
        public const float RaidPlanChance = 0.35f;
        public const float RaidRange = 160f;
        /// <summary>Seen on the wing this recently, the guild takes it to be away from home.</summary>
        public const float AwayHours = 4f;
        /// <summary>
        /// The strength a group needs to read its hunt as "적당" (<c>Adventurers.QuestRisk</c>), above a lord's 90. P4-94: 110
        /// read the eight Lv22s who win it as 위험 and too green (recommended Lv29); at 95 eight Lv22 gear 2 are 적당, eight Lv20
        /// (who win with a death) 위험, recommended Lv25.
        /// </summary>
        public const float RiskRequirement = 95f;

        /// <summary>What a raider takes now: sacks of its gold (whole sacks only) and things off the pile.</summary>
        public static int SacksTaken(int gold) => Mathf.Clamp(gold / SackValue, 0, TakeSacks);

        /// <summary>Who would go for the hoard on their own: the brave or the greedy, never a coward.</summary>
        public static bool WouldRaid(ICollection<string> traits)
            => traits != null && !traits.Contains("coward") && (traits.Contains("brave") || traits.Contains("greedy"));

        /// <summary>Hoard size as drawn: a small pile, a heap, a mound, a mountain of gold (sprite scale).</summary>
        public static float HoardScale(int value) => value <= 0 ? 0f : value < 300 ? 0.6f : value < 1000 ? 0.85f : value < 2500 ? 1f : 1.2f;

        // ---------------- The lair ----------------

        /// <summary>The lair keeps this far from dens, villages and ruin entrances.</summary>
        public const float LairClearance = 15f;
        /// <summary>A lair by the mountains is preferred: a mountain cell this close.</summary>
        public const int MountainNear = 4;
        /// <summary>Guild members this close when it falls took part in the fight, besides those who hit it.</summary>
        public const float ParticipantRadius = 15f;

        /// <summary>The day's own random numbers: the map seed and the day, nothing shared (a seeded world stays the same).</summary>
        public static GameRandom DayStream(int mapSeed, int day) => new(unchecked((mapSeed ^ 0x6D2A91F) + day * 7919));

        /// <summary>The day's plan: whether it wakes, at what hour of the day, and for how long it flies.</summary>
        public static bool WakesOn(int mapSeed, int day, out float hourOfDay, out float flightHours)
        {
            var rng = DayStream(mapSeed, day);
            float roll = rng.Value;
            hourOfDay = rng.Range(0f, 24f);
            flightHours = rng.Range(MinFlightHours, MaxFlightHours);
            return day > GraceDays && roll < WakeChance;
        }

        /// <summary>Its round for a day it wakes: random cells of the land, inside the edge margin (same stream, after the plan).</summary>
        public static List<Vector2> Route(int mapSeed, int day, int width, int surfaceHeight)
        {
            var rng = DayStream(mapSeed, day);
            for (int i = 0; i < 3; i++) _ = rng.Value; // the plan's three draws come first (WakesOn)
            var route = new List<Vector2>();
            for (int i = 0; i < Waypoints; i++)
                route.Add(new Vector2(rng.Range(EdgeMargin, width - EdgeMargin), rng.Range(EdgeMargin, surfaceHeight - EdgeMargin)));
            return route;
        }

        /// <summary>
        /// The lair: the open cell (grass or sand) farthest from the town, inside the edge margin, clear of every den,
        /// village and ruin entrance, preferring one by the mountains. No random draws. Null when nothing fits.
        /// </summary>
        public static Vector2Int? PickLair(MapData map, IReadOnlyList<Vector2Int> keepClear)
        {
            Vector2Int? best = null, bestByMountains = null;
            float bestD = -1f, bestMountainD = -1f;
            var town = map.TownCenter;
            float clear2 = LairClearance * LairClearance;
            for (int y = EdgeMargin; y < map.SurfaceHeight - EdgeMargin; y += 2)
            for (int x = EdgeMargin; x < map.Width - EdgeMargin; x += 2)
            {
                var t = map[x, y];
                if (t != TileType.Grass && t != TileType.Sand) continue;
                float d = (new Vector2Int(x, y) - town).sqrMagnitude;
                if (d <= bestD && d <= bestMountainD) continue;
                bool clear = true;
                foreach (var k in keepClear)
                    if ((k - new Vector2Int(x, y)).sqrMagnitude < clear2) { clear = false; break; }
                if (!clear) continue;
                if (d > bestD) { bestD = d; best = new Vector2Int(x, y); }
                if (d > bestMountainD && ByMountains(map, x, y)) { bestMountainD = d; bestByMountains = new Vector2Int(x, y); }
            }
            return bestByMountains ?? best;
        }

        static bool ByMountains(MapData map, int x, int y)
        {
            for (int dy = -MountainNear; dy <= MountainNear; dy++)
            for (int dx = -MountainNear; dx <= MountainNear; dx++)
                if (map.InBounds(x + dx, y + dy) && map[x + dx, y + dy] == TileType.Mountain) return true;
            return false;
        }

        /// <summary>Flies down to fight: shots since it last took off have hurt it this much.</summary>
        public static bool ShouldLand(float damageSinceTakeOff, float maxHp) => maxHp > 0f && damageSinceTakeOff >= maxHp * LandAfterDamage;

        /// <summary>The title a member who took part in the kill carries.</summary>
        public static string Title(string name) => $"{name}{Adventurers.MemoryRecorder.Josa(name, "을", "를")} 쓰러뜨린 자";
    }
}
