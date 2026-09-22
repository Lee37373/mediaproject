using System.Collections.Generic;
using GuildMaster.Monsters;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>P4-28: skilled+ packs surround a shared target and regroup when outnumbered.</summary>
    public class PackTacticsTests
    {
        static float Wrap(float a) => Mathf.Repeat(a + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;

        [Test]
        public void Encircle_TwoFromTheEast_TakeNorthAndSouth()
        {
            var bearings = new List<float> { -0.1f, 0.1f };
            float a0 = PackTactics.EncircleAngle(bearings, 0), a1 = PackTactics.EncircleAngle(bearings, 1);
            Assert.That(Wrap(a0), Is.EqualTo(-Mathf.PI / 2f).Within(1e-3f), "the one slightly south flanks south");
            Assert.That(Wrap(a1), Is.EqualTo(Mathf.PI / 2f).Within(1e-3f), "the other flanks north");
        }

        [Test]
        public void Encircle_SpreadsEvenly_KeepingAngularOrder()
        {
            var bearings = new List<float> { 0.3f, 0f, -0.2f, 0.1f };
            var slots = new List<float>();
            for (int i = 0; i < bearings.Count; i++) slots.Add(PackTactics.EncircleAngle(bearings, i));
            // Sorted by bearing: -0.2 (i2), 0 (i1), 0.1 (i3), 0.3 (i0) → consecutive slots 90° apart.
            Assert.That(slots[1] - slots[2], Is.EqualTo(Mathf.PI / 2f).Within(1e-3f));
            Assert.That(slots[3] - slots[1], Is.EqualTo(Mathf.PI / 2f).Within(1e-3f));
            Assert.That(slots[0] - slots[3], Is.EqualTo(Mathf.PI / 2f).Within(1e-3f));
        }

        [Test]
        public void Encircle_WorksAcrossTheAngleSeam()
        {
            // Both come from the west (bearings near ±π): slots north and south, not both on one side.
            var bearings = new List<float> { Mathf.PI - 0.1f, -Mathf.PI + 0.1f };
            float y0 = Mathf.Sin(PackTactics.EncircleAngle(bearings, 0)), y1 = Mathf.Sin(PackTactics.EncircleAngle(bearings, 1));
            Assert.That(y0 * y1, Is.LessThan(-0.9f), "one north, one south");
        }

        [Test]
        public void Regroup_OnlyWhenHurtAndOutnumbered()
        {
            Assert.That(PackTactics.ShouldRegroup(0.3f, 2, 0), Is.True);
            Assert.That(PackTactics.ShouldRegroup(0.3f, 2, 2), Is.False, "pack mates at its side: keep fighting");
            Assert.That(PackTactics.ShouldRegroup(0.6f, 3, 0), Is.False, "not hurt enough");
        }
    }
}
