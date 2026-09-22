using GuildMaster.Monsters;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>When a fighting monster turns for home (MonsterLeash, gathered from the states 2026-09-22).</summary>
    public class MonsterLeashTests
    {
        const float Radius = 14f;

        static bool Returns(float fromHome, bool raiding = false, bool chasing = false, bool hit = false, bool monsterFight = false)
            => MonsterLeash.ShouldReturn(fromHome, Radius, raiding, chasing, hit, monsterFight);

        [Test]
        public void InsideTheLeash_ItFightsOn()
        {
            Assert.That(Returns(10f), Is.False);
            Assert.That(Returns(Radius + 1f), Is.True, "past the radius with nothing to hold it");
        }

        [Test]
        public void ChasingALiveTarget_ReachesTwiceAsFar()
        {
            // So a pursuer can be lured into another pack's territory.
            Assert.That(Returns(Radius * 1.5f, chasing: true), Is.False);
            Assert.That(Returns(Radius * MonsterLeash.PursuitFactor + 1f, chasing: true), Is.True);
        }

        [Test]
        public void BeingHit_OrTradingBlowsWithAMonster_KeepsItThereHoweverFar()
        {
            Assert.That(Returns(100f, chasing: true, hit: true), Is.False, "hit just now: fights back");
            // P4-46: a lured monster far from home used to walk off mid-brawl.
            Assert.That(Returns(100f, chasing: true, monsterFight: true), Is.False);
        }

        [Test]
        public void Raiders_AreNeverLeashed()
        {
            Assert.That(Returns(500f, raiding: true), Is.False);
        }
    }
}
