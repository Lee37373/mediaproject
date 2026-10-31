using System.Linq;
using GuildMaster.Data;
using GuildMaster.Town;
using GuildMaster.Units;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The town's outer defences (P4-113, H10 — user decisions 2026-10-31: arrow towers and traps, beyond the walls and no
    /// more than 15 cells out): where they may go, what a tower of each level does, what a trap does, and who steps round one.
    /// </summary>
    public class DefenceTests
    {
        static readonly RectInt Town = new(100, 100, 23, 23);

        [Test]
        public void Defences_GoBeyondTheWalls_NoMoreThanFifteenCellsOut()
        {
            Assert.That(DefenceRules.CellsBeyondWalls(new Vector2Int(110, 110), Town), Is.EqualTo(0), "inside");
            Assert.That(DefenceRules.CellsBeyondWalls(new Vector2Int(100, 110), Town), Is.EqualTo(0), "on the wall");
            Assert.That(DefenceRules.CellsBeyondWalls(new Vector2Int(99, 110), Town), Is.EqualTo(1), "right outside");
            Assert.That(DefenceRules.CellsBeyondWalls(new Vector2Int(110, 123 + 4), Town), Is.EqualTo(5), "north");
            Assert.That(DefenceRules.CellsBeyondWalls(new Vector2Int(90, 85), Town), Is.EqualTo(15), "a corner: the further way counts");

            Assert.That(DefenceRules.OutOfReach(new RectInt(98, 110, 2, 2), Town), Is.Null, "two cells out");
            Assert.That(DefenceRules.OutOfReach(new RectInt(99, 110, 2, 2), Town), Does.Contain("성벽 밖에만"), "half on the wall");
            Assert.That(DefenceRules.OutOfReach(new RectInt(85, 110, 1, 1), Town), Is.Null, "fifteen out");
            Assert.That(DefenceRules.OutOfReach(new RectInt(84, 110, 1, 1), Town), Does.Contain($"{DefenceRules.Reach}칸 안"), "sixteen out");
            Assert.That(DefenceRules.IsDefence(BuildingType.ArrowTower), Is.True);
            Assert.That(DefenceRules.IsDefence(BuildingType.Watchtower), Is.False, "the watchtower goes anywhere, as before");
        }

        [Test]
        public void TheArrowTower_ShootsHarderFasterAndFurther_ByLevel_AndIsBuiltAndRaisedLikeAFacility()
        {
            for (int lv = 2; lv <= DefenceRules.TowerLevels; lv++)
            {
                Assert.That(DefenceRules.TowerDamage(lv), Is.GreaterThan(DefenceRules.TowerDamage(lv - 1)));
                Assert.That(DefenceRules.TowerCooldown(lv), Is.LessThan(DefenceRules.TowerCooldown(lv - 1)));
                Assert.That(DefenceRules.TowerRange(lv), Is.GreaterThan(DefenceRules.TowerRange(lv - 1)));
            }
            Assert.That(DefenceRules.TowerSight(1), Is.GreaterThan(DefenceRules.TowerRange(1)), "it sees what it shoots");

            var db = DefaultContent.Build();
            db.BuildIndex();
            var def = db.Building(BuildingType.ArrowTower);
            Assert.That(def.MaxLevel, Is.EqualTo(DefenceRules.TowerLevels));
            for (int lv = 1; lv <= def.MaxLevel; lv++)
            {
                var l = def.Level(lv);
                Assert.That(l.footprint, Is.EqualTo(2));
                Assert.That(l.sightRadius, Is.EqualTo(DefenceRules.TowerSight(lv)));
                Assert.That(l.effectText, Does.Contain($"사거리 {DefenceRules.TowerRange(lv):0}칸"));
                Assert.That(l.itemCosts.All(c => db.Item(c.itemId) != null), Is.True);
                if (lv > 1)
                {
                    Assert.That(l.goldCost, Is.GreaterThan(def.Level(lv - 1).goldCost));
                    Assert.That(l.maxHp, Is.GreaterThan(def.Level(lv - 1).maxHp));
                }
            }
            Assert.That(ConstructionService.Placeable, Does.Contain(BuildingType.ArrowTower), "in the build window");
            Assert.That(BuildingNames.Of(BuildingType.ArrowTower), Is.EqualTo("화살탑"));
        }

        [Test]
        public void ATrap_Hurts_AndOnlyTheSkilledAndBetter_StepRoundIt()
        {
            Assert.That(DefenceRules.TrapHit(200f), Is.EqualTo(200f * DefenceRules.TrapDamage));
            Assert.That(DefenceRules.TrapHit(2f), Is.EqualTo(1f), "at least one");
            Assert.That(DefenceRules.DodgeChance(CombatTier.Instinct), Is.Zero);
            Assert.That(DefenceRules.DodgeChance(CombatTier.Novice), Is.Zero);
            Assert.That(DefenceRules.DodgeChance(CombatTier.Skilled), Is.GreaterThan(0f));
            Assert.That(DefenceRules.DodgeChance(CombatTier.Veteran), Is.GreaterThan(DefenceRules.DodgeChance(CombatTier.Skilled)));
            Assert.That(DefenceRules.DodgeChance(CombatTier.Heroic), Is.GreaterThan(DefenceRules.DodgeChance(CombatTier.Veteran)).And.LessThan(1f));
            Assert.That(DefenceRules.TrapCharges, Is.GreaterThan(1));
            Assert.That(DefenceRules.TrapRearmGold, Is.LessThan(DefenceRules.TrapGold), "setting a spent one again costs less than a new one");
        }
    }
}
