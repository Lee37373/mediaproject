using System.Collections.Generic;
using GuildMaster.Director;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The spectator director's rules (P4-129, H7): which moment the camera shows and when it moves on, the order of the
    /// big foes, and the chronicle (no line twice on a day, the oldest out first).
    /// </summary>
    public class HighlightTests
    {
        [Test]
        public void Pick_TakesTheBest_HoldsAShownMoment_AndOnlyAMuchBetterOneCutsIn()
        {
            Assert.That(HighlightRules.Pick(new List<float>(), -1, 0f), Is.EqualTo(-1), "nothing going on");
            Assert.That(HighlightRules.Pick(new List<float> { 50, 70, 60 }, -1, 0f), Is.EqualTo(1), "the best when nothing is shown");
            // Shown for 2 s: a slightly better one waits, a much better one cuts in.
            Assert.That(HighlightRules.Pick(new List<float> { 60, 70 }, 0, 2f), Is.EqualTo(0));
            Assert.That(HighlightRules.Pick(new List<float> { 60, 60 + HighlightRules.InterruptMargin }, 0, 2f), Is.EqualTo(1));
            // After the hold a smaller lead is enough, a tie is not.
            Assert.That(HighlightRules.Pick(new List<float> { 60, 66 }, 0, HighlightRules.MinHold + 1f), Is.EqualTo(1));
            Assert.That(HighlightRules.Pick(new List<float> { 60, 62 }, 0, HighlightRules.MinHold + 1f), Is.EqualTo(0));
            Assert.That(HighlightRules.Pick(new List<float> { 60 }, 0, 100f), Is.EqualTo(0), "alone it stays");
        }

        [Test]
        public void BigFoes_RankDragonOverLordOverNemesis_AndTheTownsDangerAboveAll()
        {
            Assert.That(HighlightRules.BigFoe(true, false, false, true, true), Is.EqualTo(HighlightKind.Dragon));
            Assert.That(HighlightRules.BigFoe(false, true, false, false, true), Is.EqualTo(HighlightKind.Lord), "a lord is also its lair's boss");
            Assert.That(HighlightRules.BigFoe(false, false, true, true, false), Is.EqualTo(HighlightKind.Nemesis));
            Assert.That(HighlightRules.BigFoe(false, false, false, false, false), Is.Null);
            Assert.That(HighlightRules.Score(HighlightKind.Dragon), Is.GreaterThan(HighlightRules.Score(HighlightKind.Lord)));
            Assert.That(HighlightRules.Score(HighlightKind.Lord), Is.GreaterThan(HighlightRules.Score(HighlightKind.Nemesis)));
            Assert.That(HighlightRules.Score(HighlightKind.DragonOverTown), Is.GreaterThan(HighlightRules.Score(HighlightKind.Dragon)));
            Assert.That(HighlightRules.Score(HighlightKind.Retreat), Is.LessThan(HighlightRules.Score(HighlightKind.LowHp)));
            Assert.That(HighlightRules.RaidScore(3), Is.LessThan(HighlightRules.RaidScore(12)));
            Assert.That(HighlightRules.RaidScore(100), Is.EqualTo(HighlightRules.Score(HighlightKind.Raid) + 20f), "capped");
            foreach (HighlightKind k in System.Enum.GetValues(typeof(HighlightKind)))
                Assert.That(HighlightRules.Title(k), Is.Not.Empty, k.ToString());
        }

        [Test]
        public void Chronicle_WritesALineOncePerDay_KeepsTheLatest_AndNamesGroups()
        {
            var c = new List<ChronicleEntry>();
            Assert.That(HighlightRules.Write(c, 30f, "지대 군주 토벌전"), Is.True);
            Assert.That(HighlightRules.Write(c, 35f, "지대 군주 토벌전"), Is.False, "the same day");
            Assert.That(HighlightRules.Write(c, 50f, "지대 군주 토벌전"), Is.True, "the next day");
            Assert.That(HighlightRules.Write(c, 50f, " "), Is.False);
            Assert.That(c[0].Format(), Is.EqualTo("[2일 6시] 지대 군주 토벌전"));
            for (int i = 0; i < HighlightRules.ChronicleCapacity + 5; i++) HighlightRules.Write(c, 100f + i, "줄 " + i);
            Assert.That(c.Count, Is.EqualTo(HighlightRules.ChronicleCapacity));
            Assert.That(c[c.Count - 1].text, Is.EqualTo("줄 " + (HighlightRules.ChronicleCapacity + 4)));

            Assert.That(HighlightRules.Names(new[] { "토르" }), Is.EqualTo("토르"));
            Assert.That(HighlightRules.Names(new[] { "토르", "미라" }), Is.EqualTo("토르와 미라"));
            Assert.That(HighlightRules.Names(new[] { "레온", "미라" }), Is.EqualTo("레온과 미라"));
            Assert.That(HighlightRules.Names(new[] { "토르", "미라", "레온" }), Is.EqualTo("토르 외 2명"));
        }
    }
}
