using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Dilemmas;
using GuildMaster.Seasons;
using GuildMaster.Storytelling;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Decision cards (P4-111c, H3 — user decisions 2026-10-30: the outcome only roughly, fixed words, 24 hours, a window of
    /// their own): the catalogue holds together, every choice runs and tells what came of it, the rough hints match what a
    /// choice really does, the words fill in with their particles, and when a card can come.
    /// </summary>
    public class DilemmaTests
    {
        /// <summary>Records what a choice does, with chance answered as told.</summary>
        sealed class Recorder : IDilemmaEffects
        {
            readonly bool _lucky;
            readonly int[] _gold;
            public int GoldIn, GoldOut, FameUp, FameDown, StockIn, StockOut, Departs;
            public readonly List<string> FollowUps = new(), Told = new();

            public Recorder(bool lucky, int[] gold)
            {
                _lucky = lucky;
                _gold = gold;
            }

            public int G(int index) => index < _gold.Length ? _gold[index] : 0;
            public bool Chance(float p) => _lucky;
            public void Gold(int amount) { if (amount > 0) GoldIn += amount; else GoldOut -= amount; }
            public void Stock(string itemId, int count) { if (count > 0) StockIn += count; else StockOut -= count; }
            public void Think(int actor, ThoughtDef def, int about = -1) => Assert.That(def, Is.Not.Null);
            public void ThinkOthers(ThoughtDef def) => Assert.That(def, Is.Not.Null);
            public void Stress(int actor, float amount) { }
            public void Fatigue(int actor, float amount) { }
            public void Fame(int amount) { if (amount > 0) FameUp += amount; else FameDown -= amount; }
            public void Relate(int a, int b, int delta) { }
            public void Exp(int actor, int amount) => Assert.That(amount, Is.GreaterThan(0));
            public void Depart(int actor, string notice) { Departs++; Told.Add(notice); }
            public bool Visitor() => _lucky;
            public bool RuinRumour() => _lucky;
            public bool RareRumour() => _lucky;
            public void RepairGates() { }
            public void CutTax(float share) => GoldOut += 1;
            public void FollowUp(string id, float hours) => FollowUps.Add(id);
            public void Tell(string text) => Told.Add(text);
        }

        static int[] GoldOf(DilemmaDef d) => d.Gold.Select(g => DilemmaRules.ScaledGold(g, 1)).ToArray();

        [Test]
        public void TheCatalogue_HoldsTogether()
        {
            var all = DilemmaRules.All;
            Assert.That(all.Count, Is.InRange(20, 30), "a first batch of 20 to 30");
            Assert.That(all.Select(d => d.Id).Distinct().Count(), Is.EqualTo(all.Count), "unique ids");
            foreach (var d in all)
            {
                Assert.That(d.Choices.Length, Is.InRange(1, 3), d.Id);
                Assert.That(d.Default, Is.InRange(0, d.Choices.Length - 1), d.Id);
                Assert.That(d.Choices[d.Default].Cost, Is.EqualTo(-1), d.Id + ": the default needs no gold in the vault");
                Assert.That(d.Choices[d.Default].NeedsItem, Is.Null, d.Id + ": nor things");
                foreach (var c in d.Choices)
                {
                    Assert.That(c.Apply, Is.Not.Null, d.Id);
                    Assert.That(c.Cost, Is.LessThan(d.Gold.Length), d.Id + ": its cost is one of the card's amounts");
                }
                Assert.That(d.FollowUpOnly || d.Choices.Length >= 2, Is.True, d.Id + ": a dealt card is a choice");
            }
            Assert.That(all.Count(d => d.Season.HasValue), Is.EqualTo(4), "one card for each season");
        }

        [Test]
        public void EveryChoice_Runs_TellsWhatCameOfIt_AndItsHintsMatchWhatItDoes()
        {
            foreach (var d in DilemmaRules.All)
            for (int i = 0; i < d.Choices.Length; i++)
            foreach (bool lucky in new[] { true, false })
            {
                var c = d.Choices[i];
                var r = new Recorder(lucky, GoldOf(d));
                c.Apply(r);
                string where = $"{d.Id}[{i}] lucky={lucky}";
                Assert.That(r.Told, Is.Not.Empty, where + ": says what came of it");
                bool Shows(params DilemmaHint[] any) => any.Any(h => c.Hints.Contains(h));
                if (r.GoldOut > 0) Assert.That(Shows(DilemmaHint.GoldDown, DilemmaHint.Risk), Is.True, where + ": gold out");
                if (r.GoldIn > 0 && r.GoldOut == 0) Assert.That(Shows(DilemmaHint.GoldUp, DilemmaHint.Risk), Is.True, where + ": gold in");
                if (r.FameUp > 0) Assert.That(Shows(DilemmaHint.FameUp), Is.True, where + ": fame up");
                if (r.FameDown > 0) Assert.That(Shows(DilemmaHint.FameDown, DilemmaHint.Risk), Is.True, where + ": fame down");
                if (r.StockIn > 0) Assert.That(Shows(DilemmaHint.StockUp), Is.True, where + ": stock in");
                if (r.StockOut > 0) Assert.That(Shows(DilemmaHint.StockDown), Is.True, where + ": stock out");
                if (r.Departs > 0) Assert.That(Shows(DilemmaHint.Leaving), Is.True, where + ": someone leaves");
                foreach (var id in r.FollowUps)
                {
                    var next = DilemmaRules.Find(id);
                    Assert.That(next?.FollowUpOnly, Is.True, where + ": follows with " + id);
                    Assert.That(Shows(DilemmaHint.Later, DilemmaHint.Risk), Is.True, where + ": something later");
                }
            }
        }

        [Test]
        public void Words_FillIn_WithTheirParticles_AndNothingIsLeftUnfilled()
        {
            var names = new[] { "베일", "미라" };
            Assert.That(DilemmaRules.Fill("{0}이(가) {1}을(를) {1}와(과) {0}은(는)", names, null, null, null), Is.EqualTo("베일이 미라를 미라와 베일은"));
            Assert.That(DilemmaRules.Fill("{v}이(가) {s}을(를) {g0}G", null, new[] { 60 }, "다리안 마을", "고블린"), Is.EqualTo("다리안 마을이 고블린을 60G"));
            Assert.That(DilemmaRules.Fill("{v}", null, null, null, null), Is.EqualTo("이웃 마을"), "no village known: a neighbour");
            foreach (var d in DilemmaRules.All)
            {
                var gold = GoldOf(d);
                Assert.That(DilemmaRules.Fill(d.Text, names, gold, "다리안 마을", "고블린"), Does.Not.Contain("{"), d.Id);
                foreach (var c in d.Choices)
                    Assert.That(DilemmaRules.Fill(c.Label, names, gold, "다리안 마을", "고블린"), Does.Not.Contain("{"), d.Id);
                foreach (var c in d.Choices) Assert.That(DilemmaRules.Hints(c.Hints), Is.Not.Empty, d.Id);
            }
        }

        [Test]
        public void Gold_GrowsWithTheDays_UpToThreeTimes()
        {
            Assert.That(DilemmaRules.ScaledGold(100, 1), Is.EqualTo(100));
            Assert.That(DilemmaRules.ScaledGold(100, 11), Is.EqualTo(180));
            Assert.That(DilemmaRules.ScaledGold(100, 200), Is.EqualTo(300));
            Assert.That(DilemmaRules.ScaledGold(60, 3) % 5, Is.EqualTo(0), "to the nearest 5");
        }

        [Test]
        public void ACard_ComesFromDayTwo_InItsSeason_WhenItsPeopleAndThingsAreThere()
        {
            var facts = new DilemmaFacts { Day = 2, Season = Season.Spring, Gold = 500, Members = 4 };
            var theft = DilemmaRules.Find("theft");
            Assert.That(DilemmaRules.CanCome(theft, facts), Is.True);
            Assert.That(DilemmaRules.CanCome(theft, new DilemmaFacts { Day = 1, Season = Season.Spring, Gold = 500, Members = 4 }), Is.False, "not on day 1");
            Assert.That(DilemmaRules.CanCome(theft, new DilemmaFacts { Day = 2, Gold = 50, Members = 4 }), Is.False, "nothing in the vault to miss");
            Assert.That(DilemmaRules.CanCome(DilemmaRules.Find("duel"), facts), Is.False, "no rivals");
            Assert.That(DilemmaRules.CanCome(DilemmaRules.Find("duel"), new DilemmaFacts { Day = 2, Members = 4, Rivals = true }), Is.True);
            Assert.That(DilemmaRules.CanCome(DilemmaRules.Find("firewood"), facts), Is.False, "a winter card in spring");
            Assert.That(DilemmaRules.CanCome(DilemmaRules.Find("spring_recruits"), facts), Is.True);
            Assert.That(DilemmaRules.Weight(DilemmaRules.Find("spring_recruits")), Is.GreaterThan(DilemmaRules.Weight(theft)), "in season, more likely");
            Assert.That(DilemmaRules.CanCome(DilemmaRules.Find("investment_result"), facts), Is.False, "a follow-up is never dealt on its own");
            Assert.That(DilemmaRules.CanCome(DilemmaRules.Find("aid"), facts), Is.False, "no village known");
            Assert.That(DilemmaRules.CanCome(DilemmaRules.Find("cub"), facts), Is.False, "no kind known");
            // The storyteller deals more in the quiet.
            Assert.That(DilemmaRules.DealChance(StoryPhase.Calm), Is.GreaterThan(DilemmaRules.DealChance(StoryPhase.Relax)));
            Assert.That(DilemmaRules.DealChance(StoryPhase.Relax), Is.GreaterThan(DilemmaRules.DealChance(StoryPhase.Recovery)));
        }

        [Test]
        public void AChoice_NeedsWhatItCosts()
        {
            var investment = DilemmaRules.Find("investment");
            var gold = GoldOf(investment);
            Assert.That(DilemmaRules.Affordable(investment.Choices[0], gold, gold[0] - 1, _ => 0), Is.False);
            Assert.That(DilemmaRules.Affordable(investment.Choices[0], gold, gold[0], _ => 0), Is.True);
            var aid = DilemmaRules.Find("aid");
            Assert.That(DilemmaRules.Affordable(aid.Choices[0], GoldOf(aid), 0, _ => 2), Is.False, "three potions");
            Assert.That(DilemmaRules.Affordable(aid.Choices[0], GoldOf(aid), 0, _ => 3), Is.True);
        }
    }
}
