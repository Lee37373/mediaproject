using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Data;
using GuildMaster.Units;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// What the ground round a fight adds (P4-104b, G3): the dens next door that pile in, known deep woods asking more of a hunt
    /// and of a free hunt's fight, and careful walkers keeping to the trails through them.
    /// </summary>
    public class AreaRiskTests
    {
        static MonsterDefinition Kind(bool pack, bool aggressive)
        {
            var m = ScriptableObject.CreateInstance<MonsterDefinition>();
            m.packHunter = pack;
            m.aggressiveToHumans = aggressive;
            return m;
        }

        [Test]
        public void OnlyKindsThatGangUp_PileInFromNextDoor_InProportionToWhatWasSeen()
        {
            var slime = Kind(false, false);
            var goblin = Kind(true, false);
            var skull = Kind(false, true);
            try
            {
                Assert.That(AreaRisk.ExtraFoes(slime, 12), Is.Zero, "slimes come one at a time");
                Assert.That(AreaRisk.ExtraFoes(goblin, 10), Is.EqualTo(3), "a third of ten, rounded up");
                Assert.That(AreaRisk.ExtraFoes(skull, 1), Is.EqualTo(1), "one that comes at people counts");
                Assert.That(AreaRisk.ExtraFoes(goblin, 0), Is.Zero, "nothing seen next door");
            }
            finally
            {
                Object.DestroyImmediate(slime);
                Object.DestroyImmediate(goblin);
                Object.DestroyImmediate(skull);
            }
        }

        [Test]
        public void TheCrowd_RaisesWhatAHuntAsks_InProportion_AtMostTwice()
        {
            Assert.That(AreaRisk.CrowdFactor(0, 5), Is.EqualTo(1f));
            Assert.That(AreaRisk.CrowdFactor(3, 6), Is.EqualTo(1.5f).Within(1e-5f), "three more on six");
            Assert.That(AreaRisk.CrowdFactor(2, 1), Is.EqualTo(1f + 2f / FreeHunt.AssumedPackSize).Within(1e-5f), "a small hunt still meets a pack");
            Assert.That(AreaRisk.CrowdFactor(30, 3), Is.EqualTo(AreaRisk.MaxCrowdFactor), "never more than twice");
        }

        [Test]
        public void InKnownDeepWoods_AFreeHuntTakesOnlyAFightWellWithinItsMeans()
        {
            // Plenty of health: the cost grows a little a foe, so some count falls between the budgets.
            var band = new List<CombatStats> { new() { MaxHp = 1000, Attack = 10, Defense = 2, AttackCooldown = 1f } };
            var foe = new CombatStats { MaxHp = 40, Attack = 6, Defense = 1, AttackCooldown = 1f };
            // A fight between the two budgets: more than the deep woods allow, within what the open does.
            int foes = 1;
            while (foes < 50 && FreeHunt.ExpectedLoss(band, null, foe, foes) <= FreeHunt.AcceptableLoss * DeepWoodRules.HuntLossShare) foes++;
            Assert.That(FreeHunt.ExpectedLoss(band, null, foe, foes), Is.LessThanOrEqualTo(FreeHunt.AcceptableLoss), "the test's fight fits the open budget");
            Assert.That(FreeHunt.CanTake(band, null, foe, foes), Is.True, "taken in the open");
            Assert.That(FreeHunt.CanTake(band, null, foe, foes, DeepWoodRules.HuntLossShare), Is.False, "not in the deep woods");
        }

        /// <summary>Grass with a belt of deep woods down the middle and a trail (grass) across it far to the south.</summary>
        static GridMap DeepBeltWithATrail()
        {
            var map = new MapData(40, 30);
            for (int i = 0; i < map.Tiles.Length; i++) map.Tiles[i] = TileType.Grass;
            for (int y = 0; y < 30; y++)
            for (int x = 18; x <= 21; x++)
            {
                if (y == 25 || y == 26) continue;
                map[x, y] = TileType.Forest;
                map.Deep[map.Index(x, y)] = true;
            }
            return new GridMap(map);
        }

        static int DeepCells(GridMap grid, List<Vector2Int> path)
        {
            int n = 0;
            foreach (var c in path) if (grid.IsDeep(c)) n++;
            return n;
        }

        [Test]
        public void ThroughDeepWoods_AnOrdinaryPartyTakesTheTrail_AHurriedOneCutsThrough()
        {
            var grid = DeepBeltWithATrail();
            var risk = new RouteRisk(grid.Data);
            float deep = risk.Multiplier(19, 8, RouteChoice.Normal);
            Assert.That(deep, Is.EqualTo(1f + RouteChoice.Normal * (RouteRisk.Of(grid.Data, 19, 8) + DeepWoodRules.RouteRisk)).Within(1e-4f),
                "the woods' own risk and the deep woods' on top");
            Assert.That(risk.Multiplier(19, 8, RouteChoice.Hurried), Is.EqualTo(1f), "no detour in a hurry");
            Assert.That(risk.Multiplier(19, 8, RouteChoice.FromCover), Is.LessThan(1f), "a pack of the woods still takes them as a shortcut");

            var pf = new Pathfinder(grid);
            var from = new Vector2Int(2, 8);
            var to = new Vector2Int(37, 8);
            var hurried = new List<Vector2Int>();
            var ordinary = new List<Vector2Int>();
            Assert.IsTrue(pf.FindPath(from, to, hurried, true, null, RouteChoice.Hurried));
            Assert.IsTrue(pf.FindPath(from, to, ordinary, true, null, RouteChoice.Normal));
            Assert.That(DeepCells(grid, hurried), Is.GreaterThan(0), "the shortest way is through the woods");
            Assert.That(DeepCells(grid, ordinary), Is.Zero, "an ordinary party walks round by the trail");
        }
    }
}
