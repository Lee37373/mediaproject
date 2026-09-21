using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Ecosystem;
using GuildMaster.Data;
using GuildMaster.Traits;
using GuildMaster.Units;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>Hunting with an empty quest board (P4-37, user request 2026-09-20).</summary>
    public class FreeHuntTests
    {
        static CombatStats Member(AdventurerClass cls, int level, int gear = 0)
            => AdventurerStats.Compute(new AdventurerData { cls = cls, level = level, gearTier = gear }, null);

        static bool CanTake(CombatStats foe, int foes, params CombatStats[] band)
            => FreeHunt.CanTake(band, null, foe, foes);

        [Test]
        public void ItPicksFightsItCanWin_AndLeavesTheRestAlone()
        {
            // P4-38: judged on the real stats and damage formula, so the judgement matches what
            // actually happens in the fight (user report 2026-09-21: nothing above slimes was hunted).
            var db = DefaultContent.Build();
            CombatStats M(string id) => FreeHunt.StatsOf(db.monsters.Find(m => m.id == id));
            var goblin = M("goblin");
            var warrior3 = Member(AdventurerClass.Warrior, 3);

            Assert.That(CanTake(M("slime"), 1, Member(AdventurerClass.Warrior, 1)), Is.True, "a slime is anyone's");
            Assert.That(CanTake(goblin, 1, warrior3), Is.True, "a lone goblin is fair game");
            // The target the user agreed on: a Lv3 alone does not take on a pack of three, a party of three does.
            Assert.That(CanTake(goblin, 3, warrior3), Is.False, "a Lv3 alone does not walk into a pack");
            Assert.That(CanTake(goblin, 3, warrior3, Member(AdventurerClass.Ranger, 3), Member(AdventurerClass.Mage, 3)), Is.True);

            // The founding party can hunt goblins, but not goblin warriors by the pack, nor walk into a full den.
            var founders = new[] { Member(AdventurerClass.Warrior, 1), Member(AdventurerClass.Ranger, 1),
                                   Member(AdventurerClass.Priest, 1), Member(AdventurerClass.Mage, 1) };
            Assert.That(FreeHunt.CanTake(founders, null, goblin, 3), Is.True);
            Assert.That(FreeHunt.CanTake(founders, null, M("goblin_warrior"), 1), Is.True);
            Assert.That(FreeHunt.CanTake(founders, null, M("goblin_warrior"), 3), Is.False);
            Assert.That(FreeHunt.CanTake(founders, null, goblin, 10), Is.False);

            // Growing stronger shows: a geared Lv5 takes what a Lv3 would not.
            Assert.That(CanTake(goblin, 3, Member(AdventurerClass.Warrior, 5, 1)), Is.True);
            // And a hurt band thinks twice.
            var hurt = new[] { warrior3.MaxHp * 0.2f };
            Assert.That(FreeHunt.CanTake(new[] { warrior3 }, hurt, goblin, 1), Is.False);
        }

        [Test]
        public void TheHuntRoamsFurtherAsTheBandGrowsStronger()
        {
            // P4-38, user decision 2026-09-21: at a flat 40 cells only the slime rings were in reach.
            var db = DefaultContent.Build();
            float Reach(params CombatStats[] band) => FreeHunt.RoamRadius(band, null, db.monsters);
            var skull = db.monsters.Find(m => m.id == "skull");

            float priestAlone = Reach(Member(AdventurerClass.Priest, 1));
            float warriorAlone = Reach(Member(AdventurerClass.Warrior, 1));
            float founders = Reach(Member(AdventurerClass.Warrior, 1), Member(AdventurerClass.Ranger, 1),
                                   Member(AdventurerClass.Priest, 1), Member(AdventurerClass.Mage, 1));
            float veterans = Reach(Member(AdventurerClass.Warrior, 10, 2), Member(AdventurerClass.Ranger, 10, 2),
                                   Member(AdventurerClass.Priest, 10, 2), Member(AdventurerClass.Mage, 10, 2));

            Assert.That(priestAlone, Is.GreaterThanOrEqualTo(FreeHunt.Radius), "never less than the old reach");
            Assert.That(founders, Is.GreaterThan(warriorAlone), "a party goes further than one member");
            Assert.That(founders, Is.GreaterThanOrEqualTo(Mathf.Lerp(FreeHunt.Radius, FreeHunt.MaxRadius, skull.distanceBand)),
                        "the founding party reaches the skull ring");
            Assert.That(veterans, Is.GreaterThan(founders));
            Assert.That(veterans, Is.LessThanOrEqualTo(FreeHunt.MaxRadius));
        }

        [Test]
        public void TheHuntGoesWhereTheFightsAre()
        {
            // P4-40, user report 2026-09-21: 55% of the time hunting, 3% fighting.
            var db = DefaultContent.Build();
            MonsterDefinition M(string id) => db.monsters.Find(m => m.id == id);
            var slime = M("slime");
            var skull = M("skull");

            Assert.That(FreeHunt.DenScore(slime, 0, 10f), Is.EqualTo(0f), "an emptied den is not worth the walk");
            Assert.That(FreeHunt.DenScore(slime, 6, 10f), Is.GreaterThan(FreeHunt.DenScore(slime, 6, 40f)), "nearer is better");
            Assert.That(FreeHunt.DenScore(slime, 6, 20f), Is.GreaterThan(FreeHunt.DenScore(slime, 2, 20f)), "fuller is better");
            Assert.That(FreeHunt.DenScore(slime, 30, 20f), Is.EqualTo(FreeHunt.DenScore(slime, 20, 20f)),
                        "a trip only takes so many");

            // The last few of a den are left to breed (first measured run: slimes gone on day 3).
            Assert.That(FreeHunt.SpareAtDen(FreeHunt.LeaveAtDen), Is.True);
            Assert.That(FreeHunt.SpareAtDen(FreeHunt.LeaveAtDen + 1), Is.False);
            Assert.That(FreeHunt.DenScore(slime, FreeHunt.LeaveAtDen, 5f), Is.EqualTo(0f), "nothing left worth taking there");
            Assert.That(FreeHunt.DenScore(skull, 4, 30f), Is.GreaterThan(FreeHunt.DenScore(slime, 4, 30f)), "richer prey is worth more");

            // Scouting is spread evenly out to the reach, so the near rings get walked too.
            float reach = 65f;
            Assert.That(FreeHunt.ScoutDistance(0f, reach), Is.EqualTo(12f).Within(0.01f), "just outside the walls");
            Assert.That(FreeHunt.ScoutDistance(1f, reach), Is.EqualTo(reach).Within(0.01f));
            Assert.That(FreeHunt.ScoutDistance(0.5f, reach), Is.EqualTo((12f + reach) / 2f).Within(0.01f), "uniform in distance, not area");
        }

        [Test]
        public void SeeingOneTellsThemThereAreProbablyMore()
        {
            // User request 2026-09-20: spotting an enemy should suggest the rest of its pack.
            var db = DefaultContent.Build();
            MonsterDefinition M(string id) => db.monsters.Find(m => m.id == id);
            var goblin = M("goblin");   // packHunter
            var slime = M("slime");     // not

            Assert.That(FreeHunt.ExpectedEnemies(1, goblin, 0), Is.EqualTo(FreeHunt.AssumedPackSize), "one goblin means a pack");
            Assert.That(FreeHunt.ExpectedEnemies(1, slime, 0), Is.EqualTo(1), "a slime is just a slime");
            Assert.That(FreeHunt.ExpectedEnemies(5, goblin, 0), Is.EqualTo(5), "what is visible is a floor, never a ceiling");
            // What the guild counted in the den nearby counts too, whoever counted it — but only
            // part of it piles in, and only for species that gang up at all.
            Assert.That(FreeHunt.ExpectedEnemies(1, goblin, 8), Is.EqualTo(4));
            Assert.That(FreeHunt.ExpectedEnemies(1, slime, 8), Is.EqualTo(1),
                        "slimes neither hunt in packs nor come at people: a den of them is still one at a time");

            // And that is what keeps a Lv1 out of a pack it cannot beat.
            var priest = Member(AdventurerClass.Priest, 1);
            Assert.That(CanTake(FreeHunt.StatsOf(slime), FreeHunt.ExpectedEnemies(1, slime, 0), priest), Is.True);
            Assert.That(CanTake(FreeHunt.StatsOf(goblin), FreeHunt.ExpectedEnemies(1, goblin, 0), priest), Is.False,
                        "a Lv1 priest died to exactly this before the pack was counted");
        }

        [Test]
        public void AHealerDoesNotGoOutAlone()
        {
            // User report 2026-09-20: "the healer is going hunting on its own, that makes no sense".
            Assert.That(FreeHunt.WorthGoing(AdventurerClass.Priest, 1), Is.False);
            Assert.That(FreeHunt.WorthGoing(AdventurerClass.Priest, 2), Is.True, "with a party it is exactly where it belongs");
            Assert.That(FreeHunt.WorthGoing(AdventurerClass.Warrior, 1), Is.True);
            Assert.That(FreeHunt.WorthGoing(AdventurerClass.Ranger, 1), Is.True);
        }

        [Test]
        public void ASelfChosenHuntStopsBeforeItEmptiesTheCountryside()
        {
            // First measured run: three members hunting all day wiped the slimes out in two game
            // hours, which also cut off the potion supply.
            Assert.That(FreeHunt.Spare(EcologyStatus.Healthy), Is.False);
            Assert.That(FreeHunt.Spare(EcologyStatus.Thinning), Is.False, "thinning alone is the milder warning, not a ban");
            Assert.That(FreeHunt.Spare(EcologyStatus.Endangered), Is.True);

            var db = DefaultContent.Build();
            var slime = db.monsters.Find(m => m.id == "slime");
            // Judged on what the guild counted, not on the simulation (information rule).
            Assert.That(FreeHunt.Spare(EcologyWarning.Status(slime, observed: 15, knownNests: 3)), Is.False);
            Assert.That(FreeHunt.Spare(EcologyWarning.Status(slime, observed: 6, knownNests: 3)), Is.False);
            Assert.That(FreeHunt.Spare(EcologyWarning.Status(slime, observed: 2, knownNests: 3)), Is.True, "a last handful is left alone");
            Assert.That(FreeHunt.Spare(EcologyWarning.Status(slime, observed: 0, knownNests: 0)), Is.False,
                        "with no nest found the guild has no basis to spare anything");
        }

        [Test]
        public void ThePlayersOrdersInterruptAHuntItChoseItself()
        {
            Assert.That(FreeHunt.ShouldGoHome(1f, 0, questAvailable: true), Is.True, "a posted quest comes first");
            Assert.That(FreeHunt.ShouldGoHome(1f, 0, questAvailable: false), Is.False);
            Assert.That(FreeHunt.ShouldGoHome(FreeHunt.MaxHours, 0, false), Is.True, "the trip has a length");
            Assert.That(FreeHunt.ShouldGoHome(1f, FreeHunt.FullBag, false), Is.True, "a full bag is worth selling");
            // Counted in items: loot merges by id, so stacks would almost never reach the cap and
            // the trip would never end (first measured run).
            var bag = new AdventurerData();
            for (int i = 0; i < FreeHunt.FullBag; i++) bag.AddLoot("slime_jelly", 1);
            Assert.That(bag.loot.Count, Is.EqualTo(1));
            Assert.That(FreeHunt.ShouldGoHome(1f, bag.LootCount, false), Is.True);
            // Hurt goes home well before the normal retreat threshold: nobody ordered this trip.
            Assert.That(FreeHunt.ShouldGoHome(1f, 0, false, hpRatio: 0.5f), Is.True);
            Assert.That(FreeHunt.ShouldGoHome(1f, 0, false, hpRatio: 0.9f), Is.False);
            Assert.That(FreeHunt.GoHomeHpRatio, Is.GreaterThan(TraitSet.BaseRetreatThreshold));
        }

        [Test]
        public void WhoStaysInTownWithNothingPosted()
        {
            var db = DefaultContent.Build();
            db.BuildIndex();
            var plain = new AdventurerData { level = 3, potions = 1, values = "명예" };
            Assert.That(RuleBasedDecisionMaker.StayChance(plain, null), Is.EqualTo(0f), "most members go out");

            var greenhorn = new AdventurerData { level = 1, potions = 0, values = "명예" };
            var stay = RuleBasedDecisionMaker.StayChance(greenhorn, null);
            Assert.That(stay, Is.GreaterThan(0f), "a beginner with no potion thinks twice");
            Assert.That(stay, Is.LessThan(1f), "but never for good — hunting is how it earns the gold for one");

            var coward = new AdventurerData { level = 3, potions = 1, values = "안전" };
            var timid = TraitSet.FromIds(new[] { "coward" }, db);
            Assert.That(RuleBasedDecisionMaker.StayChance(coward, timid), Is.GreaterThan(0.5f));

            var brave = new AdventurerData { level = 3, potions = 1, values = "명예" };
            Assert.That(RuleBasedDecisionMaker.StayChance(brave, TraitSet.FromIds(new[] { "brave" }, db)), Is.EqualTo(0f));
        }

        [Test]
        public void HuntIsAnActionBothDecisionPathsKnow()
        {
            Assert.That(DecisionPrompt.Actions, Does.Contain("hunt"));
            StringAssert.Contains("hunt", DecisionPrompt.BuildGrammar(Enumerable.Empty<string>()));

            var dto = new DecisionPrompt.ResponseDto { action = "hunt", reason = "의뢰가 없다", say = "나가 보지." };
            Assert.That(LlmDecisionMaker.TryInterpret(dto, 1f, new string[0], false, out var d, out _), Is.True);
            Assert.That(d.Action, Is.EqualTo(AdventurerAction.Hunt));

            // Walking out is as risky as a quest, so it needs the same health.
            Assert.That(LlmDecisionMaker.TryInterpret(dto, 0.1f, new string[0], false, out _, out var reject), Is.False);
            Assert.That(reject, Is.EqualTo("unsafe"));
        }

        [Test]
        public void TheNearRingsRegrowFasterThanTheFarOnes()
        {
            // P4-37 raised growth because free hunting keeps clearing the nests nearest town.
            var db = DefaultContent.Build();
            MonsterDefinition M(string id) => db.monsters.Find(m => m.id == id);
            Assert.That(M("slime").growthPerHour, Is.GreaterThan(M("stone_slime").growthPerHour));
            Assert.That(M("goblin").growthPerHour, Is.GreaterThan(M("goblin_warrior").growthPerHour));
            Assert.That(M("goblin_warrior").growthPerHour, Is.GreaterThan(M("slime_king").growthPerHour));
            // A hunted nest has to refill inside a day or two, or the near ring runs dry.
            foreach (var id in new[] { "slime", "stone_slime", "goblin", "skull" })
            {
                var s = M(id);
                float peakPerDay = s.growthPerHour * (s.nestCapacity / 2f) * 0.5f * 24f;
                Assert.That(peakPerDay, Is.GreaterThan(2f), $"{id} regrows too slowly to be hunted every day");
            }
        }
    }
}
