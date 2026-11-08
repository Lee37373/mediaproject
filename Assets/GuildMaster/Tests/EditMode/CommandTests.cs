using GuildMaster.Commands;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// The guild master's orders (P4-132, H6 — user decisions 2026-11-08: a cheer, a focus, a rally flag, a retreat horn; one
    /// gauge; heard near the master; followed by mood and nature): what stops an order, how the gauge fills, who follows, and
    /// what the bar and the log say.
    /// </summary>
    public class CommandTests
    {
        [Test]
        public void AnOrder_WaitsForTheMaster_ThenItsCooldown_ThenTheGauge()
        {
            Assert.That(CommandRules.CanAfford(MasterCommand.Cheer, true, 100f, 0f), Is.EqualTo(CommandRefusal.None));
            Assert.That(CommandRules.CanAfford(MasterCommand.Cheer, false, 100f, 0f), Is.EqualTo(CommandRefusal.Down));
            Assert.That(CommandRules.CanAfford(MasterCommand.Cheer, true, 100f, 0.5f), Is.EqualTo(CommandRefusal.Cooldown));
            Assert.That(CommandRules.CanAfford(MasterCommand.Retreat, true, CommandRules.Cost(MasterCommand.Retreat) - 1f, 0f),
                        Is.EqualTo(CommandRefusal.Power));
            Assert.That(CommandRules.CanAfford(MasterCommand.Retreat, true, CommandRules.Cost(MasterCommand.Retreat), 0f),
                        Is.EqualTo(CommandRefusal.None), "exactly enough is enough");
            // A full gauge pays for any three orders, not all four: it is spent with thought.
            float all = 0f;
            foreach (var c in CommandRules.All) all += CommandRules.Cost(c);
            Assert.That(all, Is.GreaterThan(CommandRules.MaxPower));
            foreach (var c in CommandRules.All) Assert.That(CommandRules.Why(CommandRules.CanAfford(c, true, 0f, 0f), c, 0f, 0f), Is.Not.Empty);
        }

        [Test]
        public void TheGauge_FillsWithTime_UpToItsTop_AndNotBackwards()
        {
            Assert.That(CommandRules.Regen(10f, 2f), Is.EqualTo(10f + 2f * CommandRules.PowerPerHour).Within(1e-4f));
            Assert.That(CommandRules.Regen(95f, 10f), Is.EqualTo(CommandRules.MaxPower));
            Assert.That(CommandRules.Regen(40f, -3f), Is.EqualTo(40f), "a clock put back takes nothing");
            // Empty to full in about a day.
            float hours = CommandRules.MaxPower / CommandRules.PowerPerHour;
            Assert.That(hours, Is.InRange(18f, 30f));
        }

        [Test]
        public void Following_ThreeInFourAtAnEvenMood_AllInHighSpirits_HalfAtBreakingPoint()
        {
            Assert.That(CommandRules.ObeyChance(MasterCommand.Cheer, 50f, null, null), Is.EqualTo(0.75f).Within(1e-4f));
            Assert.That(CommandRules.ObeyChance(MasterCommand.Focus, 80f, null, null), Is.EqualTo(1f));
            Assert.That(CommandRules.ObeyChance(MasterCommand.Focus, 25f, null, null), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(CommandRules.ObeyChance(MasterCommand.Focus, 0f, new[] { "arrogant" }, null), Is.EqualTo(0.1f).Within(1e-4f),
                        "never quite nobody");
            Assert.That(CommandRules.ObeyChance(MasterCommand.Rally, 50f, new[] { "loyal" }, null), Is.EqualTo(0.95f).Within(1e-4f));
        }

        [Test]
        public void Natures_TheBraveHateToFallBack_CowardsHateToCharge_TheSociableWarmToACheer()
        {
            float plain = CommandRules.ObeyChance(MasterCommand.Retreat, 40f, null, null);
            Assert.That(CommandRules.ObeyChance(MasterCommand.Retreat, 40f, new[] { "brave" }, "명예"), Is.LessThan(plain));
            Assert.That(CommandRules.ObeyChance(MasterCommand.Retreat, 40f, new[] { "coward" }, "안전"), Is.GreaterThan(plain));
            float focus = CommandRules.ObeyChance(MasterCommand.Focus, 40f, null, null);
            Assert.That(CommandRules.ObeyChance(MasterCommand.Focus, 40f, new[] { "brave" }, null), Is.GreaterThan(focus));
            Assert.That(CommandRules.ObeyChance(MasterCommand.Focus, 40f, new[] { "coward" }, null), Is.LessThan(focus));
            float cheer = CommandRules.ObeyChance(MasterCommand.Cheer, 40f, null, null);
            Assert.That(CommandRules.ObeyChance(MasterCommand.Cheer, 40f, new[] { "sociable" }, null), Is.GreaterThan(cheer));
            Assert.That(CommandRules.ObeyChance(MasterCommand.Cheer, 40f, new[] { "loner" }, null), Is.LessThan(cheer));
            Assert.That(CommandRules.ObeyChance(MasterCommand.Rally, 40f, null, "동료"), Is.GreaterThan(CommandRules.ObeyChance(MasterCommand.Rally, 40f, null, null)));
        }

        [Test]
        public void Words_TheBarAndTheLog_SayHowManyHeardAndWhoDidNot()
        {
            Assert.That(CommandRules.Summary(MasterCommand.Cheer, 3, new string[0]), Is.EqualTo("응원: 3명 모두 힘을 냈습니다."));
            Assert.That(CommandRules.Summary(MasterCommand.Retreat, 4, new[] { "로웬", "베일" }),
                        Is.EqualTo("후퇴 나팔: 4명 중 2명이 물러납니다. (로웬, 베일은(는) 따르지 않음)"));
            Assert.That(CommandRules.Hours(1.5f), Is.EqualTo("1.5시간"));
            Assert.That(CommandRules.Hours(0.25f), Is.EqualTo("15분"));
            foreach (var c in CommandRules.All)
            {
                Assert.That(CommandRules.Key(c), Is.EqualTo(((int)c + 5).ToString()));
                for (int roll = 0; roll < 6; roll++)
                {
                    Assert.That(CommandRules.ObeyLine(c, roll), Is.Not.Empty);
                    Assert.That(CommandRules.RefuseLine(c, roll), Does.StartWith("("), "refusals are muttered");
                }
            }
        }
    }
}
