using System.Linq;
using GuildMaster.Data;
using GuildMaster.Flares;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>Flares (P4-94): where a member sends one, when it shoots, what the light covers, and where they come from.</summary>
    public class FlareTests
    {
        [Test]
        public void Aim_AtTheLastSeenSpot_NoFartherThanAThrow_SoTheShooterStandsInTheLight()
        {
            var from = new Vector2(10f, 10f);
            Assert.That(FlareRules.AimPoint(from, new Vector2(16f, 10f)), Is.EqualTo(new Vector2(16f, 10f)));
            var far = FlareRules.AimPoint(from, new Vector2(40f, 10f));
            Assert.That(Vector2.Distance(from, far), Is.EqualTo(FlareRules.MaxThrow).Within(1e-4f));
            Assert.That(far.y, Is.EqualTo(10f).Within(1e-4f), "along the line to the foe");
            Assert.That(FlareRules.Lights(far, from), Is.True, "the shooter is lit too");
            Assert.That(FlareRules.Radius, Is.GreaterThan(9f), "wider than anyone's own sight");
        }

        [Test]
        public void Light_CoversItsRadius_AndNoMore()
        {
            var c = new Vector2(100f, 100f);
            Assert.That(FlareRules.Lights(c, c + new Vector2(FlareRules.Radius - 0.1f, 0f)), Is.True);
            Assert.That(FlareRules.Lights(c, c + new Vector2(0f, FlareRules.Radius + 0.1f)), Is.False);
        }

        [Test]
        public void Shooting_NeedsOneInHand_TheWaitOver_AnUnlitSpot_AndAThreat()
        {
            Assert.That(FlareRules.WouldFire(1, 10f, 5f, alreadyLit: false, threat: true), Is.True);
            Assert.That(FlareRules.WouldFire(0, 10f, 5f, false, true), Is.False, "none carried");
            Assert.That(FlareRules.WouldFire(1, 4f, 5f, false, true), Is.False, "the last shot is too recent");
            Assert.That(FlareRules.WouldFire(1, 10f, 5f, true, true), Is.False, "already in a flare's light");
            Assert.That(FlareRules.WouldFire(1, 10f, 5f, false, false), Is.False, "a slime that wandered off");
        }

        [Test]
        public void Content_AToolMadeAtTheApothecary_FromTheNearRingsDrops()
        {
            var db = DefaultContent.Build();
            var flare = db.items.First(i => i.id == FlareRules.ItemId);
            Assert.That(flare.category, Is.EqualTo(ItemCategory.Tool));
            Assert.That(flare.displayName, Is.EqualTo("조명탄"));
            var recipe = db.recipes.First(r => r.outputItemId == FlareRules.ItemId);
            Assert.That(recipe.station, Is.EqualTo(Town.BuildingType.Apothecary));
            Assert.That(recipe.requiredTier, Is.EqualTo(1));
            Assert.That(recipe.inputs.Select(i => i.itemId), Is.EquivalentTo(new[] { "slime_jelly", "stone_shard" }));
            Assert.That(FlareRules.Carry, Is.EqualTo(2));
        }
    }
}
