using GuildMaster.Visuals;
using GuildMaster.World;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>P4-127: motions drawn in code for single-picture monsters, and trees drawn by depth that clear for whoever is behind.</summary>
    public class SpriteMotionTests
    {
        static bool IsIdentity(SpritePose p) => p.Offset == Vector2.zero && p.Scale == Vector2.one && p.Tilt == 0f;

        [Test]
        public void AMeleeBlow_CrouchesBack_LungesForward_AndComesBack()
        {
            Assert.That(IsIdentity(SpriteMotion.Attack(-0.1f, false)), Is.True, "not begun");
            var crouch = SpriteMotion.Attack(SpriteMotion.AttackSeconds * 0.28f, false);
            Assert.That(crouch.Offset.x, Is.LessThan(0f), "crouches back first");
            Assert.That(crouch.Scale.y, Is.LessThan(1f), "squashed");
            var lunge = SpriteMotion.Attack(SpriteMotion.AttackSeconds * 0.54f, false);
            Assert.That(lunge.Offset.x, Is.GreaterThan(0.2f), "then lunges forward");
            Assert.That(lunge.Tilt, Is.GreaterThan(0f), "leaning into the blow");
            var end = SpriteMotion.Attack(SpriteMotion.AttackSeconds - 0.001f, false);
            Assert.That(end.Offset.x, Is.LessThan(0.01f), "back where it stood");
            Assert.That(IsIdentity(SpriteMotion.Attack(SpriteMotion.AttackSeconds, false)), Is.True, "over");
        }

        [Test]
        public void AShot_DrawsBack_NeverLunges()
        {
            for (float t = 0f; t < SpriteMotion.AttackSeconds; t += 0.02f)
                Assert.That(SpriteMotion.Attack(t, true).Offset.x, Is.LessThanOrEqualTo(0f), $"a shooter stays put or leans back (t={t:0.00})");
            Assert.That(SpriteMotion.Attack(SpriteMotion.AttackSeconds * 0.45f, true).Offset.x, Is.LessThan(-0.05f), "drawn");
        }

        [Test]
        public void HitsSkillsAndBreaths_StayGentle_AndEnd()
        {
            for (float t = 0f; t < 3f; t += 0.05f)
            {
                var p = SpriteMotion.Hit(t) * SpriteMotion.Skill(t) * SpriteMotion.Breath(t, 0.3f);
                Assert.That(p.Scale.x, Is.InRange(0.8f, 1.25f));
                Assert.That(p.Scale.y, Is.InRange(0.8f, 1.25f));
                Assert.That(Mathf.Abs(p.Offset.x), Is.LessThan(0.2f));
            }
            Assert.That(SpriteMotion.Hit(SpriteMotion.HitSeconds * 0.5f).Scale.y, Is.LessThan(0.92f), "a hit squashes");
            Assert.That(SpriteMotion.Skill(SpriteMotion.SkillSeconds * 0.5f).Offset.y, Is.GreaterThan(0.1f), "a skill hops");
            Assert.That(IsIdentity(SpriteMotion.Hit(SpriteMotion.HitSeconds)), Is.True);
            Assert.That(IsIdentity(SpriteMotion.Skill(SpriteMotion.SkillSeconds)), Is.True);
        }

        [Test]
        public void TreesSortWithTheCharacters_SoWhoIsBehindATreeIsBehindIt()
        {
            // A tree on row 100 sits where a unit standing at its trunk would.
            var tree = new Vector2Int(50, 100);
            int treeOrder = MapRenderer.DecorRowOrder(tree.y);
            int OrderAt(float y) => CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(y * 8f);
            Assert.That(OrderAt(100.5f), Is.GreaterThan(treeOrder), "feet below the trunk: drawn over the tree");
            Assert.That(OrderAt(101.5f), Is.LessThan(treeOrder), "feet above it: drawn behind");
            Assert.That(MapRenderer.DecorRowOrder(101), Is.LessThan(treeOrder), "a tree further north is drawn behind this one");

            Assert.That(TreeFade.Hides(tree, new Vector2(50.5f, 101.5f)), Is.True, "just behind the canopy");
            Assert.That(TreeFade.Hides(tree, new Vector2(50.5f, 100.5f)), Is.False, "in front of the trunk");
            Assert.That(TreeFade.Hides(tree, new Vector2(52.5f, 101.5f)), Is.False, "beside it");
            Assert.That(TreeFade.Hides(tree, new Vector2(50.5f, 104f)), Is.False, "well above the crown");
        }
    }
}
