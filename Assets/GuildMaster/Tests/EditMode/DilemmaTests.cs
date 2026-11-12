using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Dilemmas;
using GuildMaster.Seasons;
using GuildMaster.Social;
using GuildMaster.Storytelling;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Decision cards (P4-111c, H3 — user decisions 2026-10-30: the outcome only roughly, fixed words, 24 hours, a window of
    /// their own): the catalogue holds together, every choice runs and tells what came of it, the rough hints match what a
    /// choice really does, the words fill in with their particles, and when a card can come. P4-118: with KMS's event cards
    /// folded in — the townsfolk, gear, the duel by levels, and how a result reads.
    /// </summary>
    public class DilemmaTests
    {
        /// <summary>Records what a choice does, with chance answered as told.</summary>
        sealed class Recorder : IDilemmaEffects
        {
            readonly bool _lucky;
            readonly int[] _gold;
            public int GoldIn, GoldOut, FameUp, FameDown, StockIn, StockOut, Departs, TownUp, TownDown, GearUps;
            public bool? Rolled;
            public readonly List<string> FollowUps = new(), Told = new(), Rumours = new();
            public readonly List<string> Residents = new();

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
            public int Level(int actor) => 5;

            public void Standing(int amount, string residentId = null)
            {
                if (amount > 0) TownUp += amount; else TownDown -= amount;
                if (residentId != null) Residents.Add(residentId);
            }

            public void Rumor(string text, int tone = 0) => Rumours.Add(text);
            public void Feel(int actor, Emotion feeling, float hours = 5f) => Assert.That(hours, Is.GreaterThan(0f));

            public bool GearUp(int actor)
            {
                if (_lucky) GearUps++;
                return _lucky;
            }

            public bool Wanderer(bool welcomed) => _lucky;
            public void Outcome(bool success) => Rolled = success;
        }

        static int[] GoldOf(DilemmaDef d) => d.Gold.Select(g => DilemmaRules.ScaledGold(g, 1)).ToArray();

        [Test]
        public void TheCatalogue_HoldsTogether()
        {
            var all = DilemmaRules.All;
            Assert.That(all.Count, Is.InRange(30, 40), "27 of the first batch and 5 from the event cards (P4-118)");
            Assert.That(all.Select(d => d.Id).Distinct().Count(), Is.EqualTo(all.Count), "unique ids");
            foreach (var d in all)
            {
                Assert.That(d.Choices.Length, Is.InRange(1, 4), d.Id + ": the window has room for four");
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
                if (r.TownUp > 0 && r.TownDown == 0) Assert.That(Shows(DilemmaHint.TownUp, DilemmaHint.Risk), Is.True, where + ": the town thinks better of the guild");
                if (r.TownDown > 0 && r.TownUp == 0) Assert.That(Shows(DilemmaHint.TownDown, DilemmaHint.Risk), Is.True, where + ": the town thinks worse of it");
                if (r.GearUps > 0) Assert.That(Shows(DilemmaHint.Growth), Is.True, where + ": better gear");
                if (r.Rolled.HasValue) Assert.That(Shows(DilemmaHint.Risk), Is.True, where + ": only a gamble succeeds or fails");
                foreach (var id in r.Residents) Assert.That(id, Does.StartWith("RES_"), where);
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
            var facts = new DilemmaFacts { Day = 2, Season = Season.Spring, Gold = 500, Members = 4, InTown = true };
            var theft = DilemmaRules.Find("theft");
            Assert.That(DilemmaRules.CanCome(theft, facts), Is.True);
            Assert.That(DilemmaRules.CanCome(theft, new DilemmaFacts { Day = 1, Season = Season.Spring, Gold = 500, Members = 4, InTown = true }), Is.False, "not on day 1");
            Assert.That(DilemmaRules.CanCome(theft, new DilemmaFacts { Day = 2, Gold = 50, Members = 4, InTown = true }), Is.False, "nothing in the vault to miss");
            Assert.That(DilemmaRules.CanCome(theft, new DilemmaFacts { Day = 2, Gold = 500, Members = 4 }), Is.False, "nobody in town saw the thief");
            // From the event cards: a resident's request needs the town's people, the shady trader someone to sell gear to.
            Assert.That(DilemmaRules.CanCome(DilemmaRules.Find("plague"), facts), Is.False, "no residents");
            Assert.That(DilemmaRules.CanCome(DilemmaRules.Find("plague"), new DilemmaFacts { Day = 2, Members = 4, InTown = true, Residents = true }), Is.True);
            Assert.That(DilemmaRules.CanCome(DilemmaRules.Find("contraband"), facts), Is.False, "everyone's gear is as good as it gets");
            Assert.That(DilemmaRules.CanCome(DilemmaRules.Find("contraband"), new DilemmaFacts { Day = 2, Members = 4, GearUp = true }), Is.True);
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

        [Test]
        public void AResult_ReadsByItsRoll_OrByWhatItDid()
        {
            var duel = DilemmaRules.Find("duel");
            Assert.That(DilemmaRules.ToneOf(duel.Choices[0], true), Is.EqualTo(DilemmaTone.Success));
            Assert.That(DilemmaRules.ToneOf(duel.Choices[0], false), Is.EqualTo(DilemmaTone.Failure));
            Assert.That(DilemmaRules.ToneOf(duel.Choices[0], null), Is.EqualTo(DilemmaTone.Neutral), "a gamble not rolled reads plain");
            Assert.That(DilemmaRules.ToneOf(duel.Choices[3], null), Is.EqualTo(DilemmaTone.Bad), "stopping them galls both");
            var feast = DilemmaRules.Find("feast_fund");
            Assert.That(DilemmaRules.ToneOf(feast.Choices[0], null), Is.EqualTo(DilemmaTone.Neutral), "gold for everyone's spirits: a trade");
            Assert.That(DilemmaRules.ToneOf(DilemmaRules.Find("noble").Choices[2], null), Is.EqualTo(DilemmaTone.Good), "turning the noble down: relief");
            Assert.That(DilemmaRules.ToneOf(DilemmaRules.Find("investment").Choices[0], null), Is.EqualTo(DilemmaTone.Neutral), "its end is still to come");
            Assert.That(DilemmaRules.Badge(DilemmaTone.Success), Does.Contain("성공"));
            Assert.That(DilemmaRules.Badge(DilemmaTone.Failure), Does.Contain("실패"));
            Assert.That(DilemmaRules.Badge(DilemmaTone.Neutral), Is.Empty);
            Assert.That(DilemmaRules.IsGood(DilemmaTone.Good) && DilemmaRules.IsBad(DilemmaTone.Bad), Is.True);

            // Over the whole catalogue there is something to win and something to lose.
            int good = 0, bad = 0;
            foreach (var d in DilemmaRules.All)
            foreach (var c in d.Choices)
            {
                var t = DilemmaRules.ToneOf(c, null);
                if (t == DilemmaTone.Good) good++;
                if (t == DilemmaTone.Bad) bad++;
            }
            Assert.That(good, Is.GreaterThan(3));
            Assert.That(bad, Is.GreaterThan(3));
        }

        [Test]
        public void ADuel_GoesToTheStronger_TenPointsALevel()
        {
            Assert.That(DilemmaRules.DuelChance(3, 3), Is.EqualTo(0.5f));
            Assert.That(DilemmaRules.DuelChance(5, 3), Is.EqualTo(0.7f).Within(1e-4f));
            Assert.That(DilemmaRules.DuelChance(1, 20), Is.EqualTo(0.2f));
            Assert.That(DilemmaRules.DuelChance(20, 1), Is.EqualTo(0.8f));
        }

        [Test]
        public void TheEventCards_CameOver_AndTheirLikesBecameOne()
        {
            foreach (var id in new[] { "noble", "plague", "birthday", "toby", "bond" })
                Assert.That(DilemmaRules.Find(id), Is.Not.Null, id);
            Assert.That(DilemmaRules.All.Count(d => d.Id is "duel" or "theft" or "wanderer" or "feast_fund" or "contraband"), Is.EqualTo(5), "one card each");
            // The birthday's title names its member; the residents it is about are the town's.
            var names = new[] { "엘리" };
            Assert.That(DilemmaRules.Fill(DilemmaRules.Find("birthday").Title, names, null, null, null), Is.EqualTo("엘리의 생일"));
            var r = new Recorder(true, GoldOf(DilemmaRules.Find("toby")));
            DilemmaRules.Find("toby").Choices[1].Apply(r);
            Assert.That(r.Residents, Does.Contain(DilemmaRules.Toby));
            Assert.That(r.Rumours, Is.Not.Empty, "the wooden sword is talked about");
            // The shady trader's gear is real on a lucky roll, a fake (and a rumour) on a bad one.
            var trader = DilemmaRules.Find("contraband");
            var lucky = new Recorder(true, GoldOf(trader));
            trader.Choices[1].Apply(lucky);
            Assert.That((lucky.GearUps, lucky.Rolled), Is.EqualTo((1, (bool?)true)));
            var unlucky = new Recorder(false, GoldOf(trader));
            trader.Choices[1].Apply(unlucky);
            Assert.That((unlucky.GearUps, unlucky.Rolled, unlucky.Rumours.Count), Is.EqualTo((0, (bool?)false, 1)));
        }
    }
}
