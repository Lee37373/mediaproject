using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Data;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Visuals;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>The dragon (P4-93): its waking, its round, its lair, its hoard, its content and how its quests are weighed.</summary>
    public class DragonTests
    {
        [Test]
        public void Waking_SleepsThroughTheGrace_IsTheSameForASeedAndDay_AndComesAboutOneDayInFive()
        {
            for (int day = 1; day <= Dragon.GraceDays; day++)
                Assert.That(Dragon.WakesOn(42, day, out _, out _), Is.False, $"day {day} is grace");
            Assert.That(Dragon.WakesOn(42, 9, out float h1, out float f1), Is.EqualTo(Dragon.WakesOn(42, 9, out float h2, out float f2)));
            Assert.That(h1, Is.EqualTo(h2));
            Assert.That(f1, Is.EqualTo(f2));
            int woke = 0, days = 0;
            for (int seed = 1; seed <= 20; seed++)
            for (int day = Dragon.GraceDays + 1; day <= Dragon.GraceDays + 50; day++)
            {
                days++;
                if (!Dragon.WakesOn(seed, day, out float hour, out float flight)) continue;
                woke++;
                Assert.That(hour, Is.InRange(0f, 24f));
                Assert.That(flight, Is.InRange(Dragon.MinFlightHours, Dragon.MaxFlightHours));
            }
            float share = woke / (float)days;
            Assert.That(share, Is.InRange(Dragon.WakeChance * 0.75f, Dragon.WakeChance * 1.25f), $"woke {woke}/{days}");
        }

        [Test]
        public void Route_IsTheDaysOwn_InsideTheEdgeOfTheLand()
        {
            var a = Dragon.Route(7, 12, 512, 512);
            var b = Dragon.Route(7, 12, 512, 512);
            Assert.That(a.Count, Is.EqualTo(Dragon.Waypoints));
            Assert.That(a, Is.EqualTo(b));
            Assert.That(Dragon.Route(7, 13, 512, 512), Is.Not.EqualTo(a), "another day, another round");
            foreach (var p in a)
            {
                Assert.That(p.x, Is.InRange(Dragon.EdgeMargin, 512 - Dragon.EdgeMargin));
                Assert.That(p.y, Is.InRange(Dragon.EdgeMargin, 512 - Dragon.EdgeMargin));
            }
        }

        static MapData OpenMap()
        {
            var map = new MapData(200, 200);
            for (int i = 0; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Grass;
            map.TownCenter = new Vector2Int(100, 100);
            return map;
        }

        [Test]
        public void Lair_IsTheFarthestOpenCell_ByTheMountains_ClearOfDens()
        {
            var map = OpenMap();
            // Mountains by the south-west corner: the lair goes there, though the north-east corner is as far.
            for (int y = 10; y < 16; y++) for (int x = 10; x < 16; x++) map[x, y] = TileType.Mountain;
            var lair = Dragon.PickLair(map, new List<Vector2Int>());
            Assert.That(lair, Is.Not.Null);
            Assert.That(map[lair.Value.x, lair.Value.y], Is.EqualTo(TileType.Grass));
            Assert.That(Vector2Int.Distance(lair.Value, new Vector2Int(12, 12)), Is.LessThan(12f), $"by the mountains: {lair}");
            // A den right there pushes it off at least the clearance.
            var den = lair.Value;
            var moved = Dragon.PickLair(map, new List<Vector2Int> { den });
            Assert.That(Vector2Int.Distance(moved.Value, den), Is.GreaterThanOrEqualTo(Dragon.LairClearance));
            Assert.That(Dragon.PickLair(map, new List<Vector2Int>()), Is.EqualTo(lair), "no random draws");
        }

        [Test]
        public void Landing_Hoard_AndRaiders()
        {
            Assert.That(Dragon.ShouldLand(799f, 10000f), Is.False);
            Assert.That(Dragon.ShouldLand(800f, 10000f), Is.True);
            Assert.That(Dragon.SacksTaken(0), Is.EqualTo(0));
            Assert.That(Dragon.SacksTaken(Dragon.SackValue * 2 + 50), Is.EqualTo(2), "whole sacks only");
            Assert.That(Dragon.SacksTaken(Dragon.SackValue * 10), Is.EqualTo(Dragon.TakeSacks), "as many as one can carry");
            Assert.That(Dragon.WouldRaid(new List<string> { "brave" }), Is.True);
            Assert.That(Dragon.WouldRaid(new List<string> { "greedy" }), Is.True);
            Assert.That(Dragon.WouldRaid(new List<string> { "brave", "coward" }), Is.False, "never a coward");
            Assert.That(Dragon.WouldRaid(new List<string> { "diligent" }), Is.False);
            Assert.That(Dragon.HoardScale(0), Is.EqualTo(0f), "nothing left: nothing drawn");
            float last = 0f;
            foreach (int v in new[] { 100, 500, 1500, 5000 })
            {
                Assert.That(Dragon.HoardScale(v), Is.GreaterThanOrEqualTo(last));
                last = Dragon.HoardScale(v);
            }
        }

        [Test]
        public void Content_OneDragon_NoHabitat_ItsFireAndItsHeart()
        {
            var db = DefaultContent.Build();
            var dragons = db.monsters.Where(m => m.isDragon).ToList();
            Assert.That(dragons.Count, Is.EqualTo(1));
            var d = dragons[0];
            Assert.That(d.regions, Is.Empty, "it lives nowhere but its own lair");
            Assert.That(d.habitatBiomes, Is.Empty);
            Assert.That(d.aggressiveToHumans, Is.True);
            Assert.That(d.skillIds, Does.Contain("dragon_breath"));
            Assert.That(Units.SkillFactory.Create("dragon_breath"), Is.InstanceOf<Units.DragonBreathSkill>());
            Assert.That(d.loot.Any(l => l.item.id == "dragon_heart" && l.chance >= 1f && l.item.IsUnique), Is.True);
            Assert.That(db.items.First(i => i.id == "dragon_gold").baseValue, Is.EqualTo(Dragon.SackValue));
        }

        [Test]
        public void Quests_AHuntForItIsPartyWorkBeyondALord_TheHoardsIsDangerous()
        {
            var db = DefaultContent.Build();
            var dragon = db.monsters.First(m => m.isDragon);
            var hunt = new Quest("Q_1", new QuestDraft { Type = QuestType.Hunt, Species = dragon, Count = 1, RewardGold = 2000, MaxParty = 8 }, 0f);
            Assert.That(QuestRisk.IsDragonWork(hunt), Is.True);
            Assert.That(QuestRisk.IsPartyWork(hunt), Is.True);
            Assert.That(QuestRisk.Requirement(hunt), Is.EqualTo(Dragon.RiskRequirement));
            Assert.That(hunt.MaxParty, Is.EqualTo(RegionLords.QuestParty), "two parties' worth, like a lord");
            Assert.That(hunt.Title, Does.StartWith("[드래곤]"));
            var raid = new Quest("Q_2", new QuestDraft { Type = QuestType.Treasure, RewardGold = 200, MaxParty = 4 }, 0f);
            Assert.That(QuestRisk.IsDragonWork(raid), Is.False);
            Assert.That(raid.Title, Does.StartWith("[드래곤 둥지]"));
        }

        [Test]
        public void Strength_EightInTheirTwenties_ReadItAsTheFightTheyCanWin()
        {
            // P4-94 (user decision: eight members in their twenties win it), with no stat buildings (×0.75): eight Lv22 gear 2
            // won the test fights, eight Lv20 won with a death, eight Lv18 lost.
            static string Read(int level, int gear, int party)
                => QuestRisk.Label(QuestRisk.MarginAgainst((level + gear * 1.5f) * Town.FacilityRules.BaseStatFactor, party, Dragon.RiskRequirement));
            Assert.That(Read(22, 2, 8), Is.EqualTo(QuestRisk.Fair));
            Assert.That(Read(20, 2, 8), Is.EqualTo(QuestRisk.Risky));
            Assert.That(Read(17, 1, 8), Is.EqualTo(QuestRisk.Reckless));
            Assert.That(Read(25, 2, 4), Is.EqualTo(QuestRisk.Reckless), "half a party is no match");
            Assert.That(Dragon.RiskRequirement, Is.GreaterThan(RegionLords.RiskRequirement), "still beyond a lord");
            Assert.That(Dragon.Level, Is.GreaterThan(RegionLords.Level));
        }

        [Test]
        public void Art_EveryPoseHasItsFrames_TheFireAndTheHoard()
        {
            foreach (DragonPose pose in System.Enum.GetValues(typeof(DragonPose)))
                for (int f = 0; f < DragonArt.FrameCount(pose); f++)
                {
                    var px = DragonArt.PaintFrame(pose, f);
                    Assert.That(px.Length, Is.EqualTo(DragonArt.Width * DragonArt.Height));
                    Assert.That(px.Count(c => c.a > 0), Is.GreaterThan(500), $"{pose} {f} is drawn");
                }
            Assert.That(DragonArt.PaintFlame(0).Count(c => c.a > 0), Is.GreaterThan(100));
            Assert.That(DragonArt.PaintHoard().Count(c => c.a > 0), Is.GreaterThan(500));
        }
    }
}
