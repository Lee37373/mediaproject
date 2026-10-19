using GuildMaster.Ecosystem;
using GuildMaster.Monsters;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>P4-44: the wilds grow wilder with time — far out, a little all the time, after a quiet start.</summary>
    public class WildsPressureTests
    {
        [Test]
        public void QuietAtFirst_ThenRisingALittleAllTheTime_UpToItsMost()
        {
            Assert.That(WildsPressure.Bonus(0f), Is.EqualTo(0f));
            Assert.That(WildsPressure.Bonus(WildsPressure.GraceDays), Is.EqualTo(0f), "nothing during the grace");
            float a = WildsPressure.Bonus(WildsPressure.GraceDays + 1f), b = WildsPressure.Bonus(WildsPressure.GraceDays + 1.5f);
            Assert.That(a, Is.GreaterThan(0f));
            Assert.That(b, Is.GreaterThan(a), "no steps: half a day later it is higher again");
            Assert.That(WildsPressure.Bonus(1000f), Is.EqualTo(WildsPressure.MaxBonus));
        }

        [Test]
        public void OnlyBeyondTheInnerHalf_AndAllOfItAtTheOutermost()
        {
            const float late = 1000f;
            Assert.That(WildsPressure.LevelBonus(0f, late), Is.EqualTo(0), "innermost sites never change");
            Assert.That(WildsPressure.LevelBonus(WildsPressure.FromDepth, late), Is.EqualTo(0));
            Assert.That(WildsPressure.LevelBonus(1f, late), Is.GreaterThan(0), "the inner land's edge gets some");
            Assert.That(WildsPressure.LevelBonus(1f, late), Is.LessThan(WildsPressure.LevelBonus(1.5f, late)));
            Assert.That(WildsPressure.LevelBonus(2f, late), Is.EqualTo(UnityEngine.Mathf.RoundToInt(WildsPressure.MaxBonus)));
        }

        [Test]
        public void BirthLevel_IsTheDistanceRulePlusTheBonus_WithinTheCap()
        {
            Assert.That(WildsPressure.BirthLevel(1f, 0f), Is.EqualTo(MonsterLevel.BirthLevel(1f)), "day one: the old rule");
            Assert.That(WildsPressure.BirthLevel(1f, 1000f), Is.EqualTo(MonsterLevel.BirthLevel(1f) + WildsPressure.LevelBonus(1f, 1000f)));
            Assert.That(WildsPressure.BirthLevel(2f, 1000f), Is.EqualTo(MonsterLevel.MaxLevel), "the outermost meet the cap");
            Assert.That(WildsPressure.BirthLevel(0.2f, 1000f), Is.EqualTo(MonsterLevel.BirthLevel(0.2f)));
        }

        [Test]
        public void Newcomers_CrowdIn_OnlyOnceTheWildsStir_AndMoreOftenTheWilderItGets()
        {
            Assert.That(WildsPressure.InvasionChancePerHour(WildsPressure.GraceDays), Is.EqualTo(0f));
            float early = WildsPressure.InvasionChancePerHour(WildsPressure.GraceDays + 1f);
            Assert.That(early, Is.InRange(WildsPressure.MinInvasionChance, WildsPressure.MaxInvasionChance));
            Assert.That(WildsPressure.InvasionChancePerHour(WildsPressure.GraceDays + 10f), Is.GreaterThan(early));
            Assert.That(WildsPressure.InvasionChancePerHour(1000f), Is.EqualTo(WildsPressure.MaxInvasionChance).Within(1e-5f));
        }

        [Test]
        public void ThoseLivingThere_GrowWithTheLand_AboutALevelADay_OnlyWhileBehind()
        {
            Assert.That(WildsPressure.GrowsThisHour(20, 25, 0f), Is.True);
            Assert.That(WildsPressure.GrowsThisHour(20, 25, 1f / WildsPressure.CatchUpHours + 0.001f), Is.False, "about once a day");
            Assert.That(WildsPressure.GrowsThisHour(25, 25, 0f), Is.False, "caught up: no more");
            Assert.That(WildsPressure.GrowsThisHour(28, 25, 0f), Is.False, "a stronger one that moved in stays as it is");
        }
    }
}
