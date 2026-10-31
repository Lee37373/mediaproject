using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Data;
using GuildMaster.Town;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>P4-87: the guild's facilities — the weakened base, the stat buildings, the marks, the dormitory (P4-92), more than one of a kind.</summary>
    public class FacilityTests
    {
        static List<int> L(params int[] levels) => new(levels);

        [Test]
        public void WithoutStatBuildings_MembersAreWeaker_AndTheThreeAtLevel3_AreTheOldStrength()
        {
            var none = FacilityRules.MemberScale(L(), L(), L());
            Assert.That((none.Attack, none.Defense, none.MaxHp), Is.EqualTo((FacilityRules.BaseStatFactor, FacilityRules.BaseStatFactor, FacilityRules.BaseStatFactor)));
            Assert.That(FacilityRules.BaseStatFactor, Is.InRange(0.6f, 0.85f), "about a quarter off (user decision 2026-10-19)");
            var full = FacilityRules.MemberScale(L(3), L(3), L(3));
            Assert.That(full.Attack, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(full.Defense, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(full.MaxHp, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(full.Mean, Is.EqualTo(1f).Within(1e-4f));
            var one = FacilityRules.MemberScale(L(1), L(), L(2));
            Assert.That(one.Attack, Is.EqualTo(FacilityRules.BaseStatFactor + (1f - FacilityRules.BaseStatFactor) / 3f).Within(1e-4f), "a third of the gap a level");
            Assert.That(one.MaxHp, Is.EqualTo(FacilityRules.BaseStatFactor + 2f * (1f - FacilityRules.BaseStatFactor) / 3f).Within(1e-4f));
            Assert.That(one.Defense, Is.EqualTo(FacilityRules.BaseStatFactor));
        }

        [Test]
        public void MoreOfAKind_AddUpLess_StrongestFirst_ThreeAtMost_AndCostMore()
        {
            float bonus3 = FacilityRules.StatBonusAt(3);
            Assert.That(FacilityRules.Combined(L(3, 3), FacilityRules.StatBonusAt), Is.EqualTo(bonus3 * 1.5f).Within(1e-5f), "the second counts half");
            Assert.That(FacilityRules.Combined(L(1, 3), FacilityRules.StatBonusAt), Is.EqualTo(bonus3 + FacilityRules.StatBonusAt(1) * 0.5f).Within(1e-5f), "the stronger one counts whole");
            Assert.That(FacilityRules.Combined(L(3, 3, 3, 3), FacilityRules.StatBonusAt), Is.EqualTo(bonus3 * 1.75f).Within(1e-5f), "a fourth adds nothing");
            Assert.That(FacilityRules.MemberScale(L(3, 3), L(3, 3), L(3, 3)).Attack, Is.GreaterThan(1f), "two of each: a little past the old strength");
            Assert.That(FacilityRules.MaxCopies(BuildingType.Armory), Is.EqualTo(3));
            Assert.That(FacilityRules.MaxCopies(BuildingType.Dormitory), Is.EqualTo(1));
            Assert.That(FacilityRules.ScaledGold(400, 0), Is.EqualTo(400));
            Assert.That(FacilityRules.ScaledGold(400, 1), Is.EqualTo(600), "the second ×1.5");
            Assert.That(FacilityRules.ScaledGold(1000, 2), Is.EqualTo(2250), "the third ×2.25");
            Assert.That(FacilityRules.ScaledCount(6, 1), Is.EqualTo(9));
        }

        [Test]
        public void Marks_AreTenPercentALevel_FourSeconds_AndAddUpLessToo()
        {
            Assert.That(FacilityRules.AfflictionSeconds, Is.EqualTo(4f));
            Assert.That(FacilityRules.AfflictionOf(L(1)), Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(FacilityRules.AfflictionOf(L(3)), Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(FacilityRules.AfflictionOf(L(3, 3, 3)), Is.EqualTo(0.525f).Within(1e-5f));
            Assert.That(FacilityRules.AfflictionOf(L()), Is.EqualTo(0f));
        }

        [Test]
        public void Dormitory_SpeedsUpStressReliefInTown_HalfAgainALevel_AndThereIsOneOnly()
        {
            Assert.That(FacilityRules.EffectOf(BuildingType.Dormitory), Is.EqualTo(FacilityEffect.StressRelief));
            Assert.That(new[] { FacilityRules.StressReliefAt(0), FacilityRules.StressReliefAt(1), FacilityRules.StressReliefAt(2), FacilityRules.StressReliefAt(3) },
                        Is.EqualTo(new[] { 1f, 1.5f, 2f, 2.5f }), "user decision 2026-10-19");
            Assert.That(FacilityRules.EffectText(BuildingType.Dormitory, 3), Does.Contain("×2.5"));
            Assert.That(FacilityService.TownStressRelief, Is.EqualTo(1f), "no game running: no relief");
        }

        [Test]
        public void TheSevenBuildings_HaveTheirEffects_DataAndPrices()
        {
            Assert.That(FacilityRules.All, Has.Length.EqualTo(7));
            var db = DefaultContent.Build();
            db.BuildIndex();
            foreach (var t in FacilityRules.All)
            {
                Assert.That(FacilityRules.IsFacility(t), Is.True, t.ToString());
                var def = db.Building(t);
                Assert.That(def, Is.Not.Null, t.ToString());
                Assert.That(def.MaxLevel, Is.EqualTo(3), t.ToString());
                Assert.That(def.Level(1).effectText, Is.Not.Empty);
                foreach (var lv in def.levels)
                    foreach (var c in lv.itemCosts) Assert.That(db.Item(c.itemId), Is.Not.Null, $"{t}: {c.itemId}");
            }
            var armory = db.Building(BuildingType.Armory);
            Assert.That((armory.Level(1).goldCost, armory.Level(2).goldCost, armory.Level(3).goldCost), Is.EqualTo((400, 1000, 2000)), "user decision 2026-10-19");
            var dorm = db.Building(BuildingType.Dormitory);
            Assert.That((dorm.Level(1).goldCost, dorm.Level(2).goldCost, dorm.Level(3).goldCost), Is.EqualTo((800, 1500, 2500)), "dearer than the others (user decision 2026-10-19)");
            Assert.That(FacilityRules.IsFacility(BuildingType.Watchtower), Is.False);
            Assert.That(FacilityRules.IsFacility(BuildingType.GuildHall), Is.False);
            Assert.That(FacilityRules.EffectText(BuildingType.PoisonLab, 2), Does.Contain("20%"));
            foreach (var t in FacilityRules.All) Assert.That(System.Array.IndexOf(ConstructionService.Placeable, t), Is.GreaterThanOrEqualTo(0), $"{t} in the build window");
        }

        [Test]
        public void Stats_TakeTheScale_BeforeTraits()
        {
            var d = new AdventurerData { cls = AdventurerClass.Warrior, level = 5, gearTier = 1 };
            var old = AdventurerStats.Compute(d, null);
            var weak = AdventurerStats.Compute(d, null, FacilityRules.Unbuilt);
            Assert.That(weak.Attack, Is.EqualTo(old.Attack * FacilityRules.BaseStatFactor).Within(0.01f));
            Assert.That(weak.Defense, Is.EqualTo(old.Defense * FacilityRules.BaseStatFactor).Within(0.01f));
            Assert.That(weak.MaxHp, Is.EqualTo(UnityEngine.Mathf.Round(old.MaxHp * FacilityRules.BaseStatFactor)).Within(1f));
            Assert.That(weak.AttackCooldown, Is.EqualTo(old.AttackCooldown), "speed is not cut");
            Assert.That(AdventurerStats.Compute(d, null, StatScale.One).Attack, Is.EqualTo(old.Attack), "one = the old strength");
            Assert.That(AdventurerStats.Power(d), Is.EqualTo(AdventurerStats.RawPower(d)), "no guild in a pure test: power unscaled");
        }
    }
}
