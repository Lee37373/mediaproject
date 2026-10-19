using System.Linq;
using GuildMaster.Adventurers;
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
            CombatStats M(string id, int level = 1) => FreeHunt.StatsOf(db.monsters.Find(m => m.id == id), level);
            var goblin = M("goblin");
            var warrior3 = Member(AdventurerClass.Warrior, 3);

            Assert.That(CanTake(M("slime"), 1, Member(AdventurerClass.Warrior, 1)), Is.True, "a slime is anyone's");
            Assert.That(CanTake(goblin, 1, warrior3), Is.True, "a lone goblin is fair game");
            // The target the user agreed on: a Lv3 alone does not take on a pack of three, a party of three does.
            Assert.That(CanTake(goblin, 3, warrior3), Is.False, "a Lv3 alone does not walk into a pack");
            Assert.That(CanTake(goblin, 3, warrior3, Member(AdventurerClass.Ranger, 3), Member(AdventurerClass.Mage, 3)), Is.True);

            // The founding party can hunt goblins by the walls, but not a pack born far out (P4-58: the
            // level is the strength), nor walk into a full den.
            var founders = new[] { Member(AdventurerClass.Warrior, 1), Member(AdventurerClass.Ranger, 1),
                                   Member(AdventurerClass.Priest, 1), Member(AdventurerClass.Mage, 1) };
            Assert.That(FreeHunt.CanTake(founders, null, goblin, 3), Is.True);
            Assert.That(FreeHunt.CanTake(founders, null, M("goblin_warrior"), 1), Is.True);
            Assert.That(FreeHunt.CanTake(founders, null, M("goblin", 12), 3), Is.False);
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

            float priestAlone = Reach(Member(AdventurerClass.Priest, 1));
            float warriorAlone = Reach(Member(AdventurerClass.Warrior, 1));
            float founders = Reach(Member(AdventurerClass.Warrior, 1), Member(AdventurerClass.Ranger, 1),
                                   Member(AdventurerClass.Priest, 1), Member(AdventurerClass.Mage, 1));
            float veterans = Reach(Member(AdventurerClass.Warrior, 10, 2), Member(AdventurerClass.Ranger, 10, 2),
                                   Member(AdventurerClass.Priest, 10, 2), Member(AdventurerClass.Mage, 10, 2));

            Assert.That(priestAlone, Is.GreaterThanOrEqualTo(FreeHunt.Radius), "never less than the old reach");
            Assert.That(founders, Is.GreaterThan(warriorAlone), "a party goes further than one member");
            // P4-58: the level (distance) is the strength, so reach is where the born level becomes too much.
            Assert.That(founders, Is.LessThan(FreeHunt.MaxRadius), "the founding party does not head for the edge");
            Assert.That(veterans, Is.GreaterThan(founders));
            Assert.That(veterans, Is.LessThanOrEqualTo(FreeHunt.MaxRadius));
        }

        [Test]
        public void OnlyABandThatTakesTheInnerEdge_GoesIntoTheOuterLand()
        {
            // P4-81: the 512 map's land past the inner edge is born 15 → 25.
            var db = DefaultContent.Build();
            const float outermost = 350f;
            float Reach(params CombatStats[] band) => FreeHunt.RoamRadius(band, null, db.monsters, outermost);
            float founders = Reach(Member(AdventurerClass.Warrior, 1), Member(AdventurerClass.Ranger, 1),
                                   Member(AdventurerClass.Priest, 1), Member(AdventurerClass.Mage, 1));
            float legends = Reach(Member(AdventurerClass.Warrior, 30, 3), Member(AdventurerClass.Ranger, 30, 3),
                                  Member(AdventurerClass.Priest, 30, 3), Member(AdventurerClass.Mage, 30, 3));
            Assert.That(founders, Is.EqualTo(FreeHunt.RoamRadius(new[] { Member(AdventurerClass.Warrior, 1), Member(AdventurerClass.Ranger, 1),
                                   Member(AdventurerClass.Priest, 1), Member(AdventurerClass.Mage, 1) }, null, db.monsters)),
                        "the early game reaches as far as on the 256 map");
            Assert.That(legends, Is.GreaterThan(FreeHunt.MaxRadius));
            Assert.That(legends, Is.LessThanOrEqualTo(outermost));
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

            // P4-43: nothing is spared, so even the last one is worth the walk.
            Assert.That(FreeHunt.DenScore(slime, 1, 5f), Is.GreaterThan(0f));
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

        [Test]
        public void FarSitesBreedSlower_AndTheGuildsEstimateKnowsIt()
        {
            // User decision 2026-09-22: the strong kinds out at the edge fill up slowly and creep in.
            float F(float d) => Ecosystem.EcosystemManager.GrowthFactor(d, 20f, 180f);
            Assert.That(F(20f), Is.EqualTo(1f).Within(1e-4f), "the ring by the town breeds at full rate");
            Assert.That(F(180f), Is.EqualTo(Ecosystem.EcosystemManager.EdgeGrowthFactor).Within(1e-4f), "the edge at the lowest");
            Assert.That(F(100f), Is.LessThan(F(50f)).And.GreaterThan(F(150f)));
            var slime = DefaultContent.Build().monsters.Find(m => m.id == "slime");
            Assert.That(FreeHunt.ExpectedDenCount(slime, 2, 12f, 0.4f), Is.LessThan(FreeHunt.ExpectedDenCount(slime, 2, 12f)));
        }

        [Test]
        public void WeakDensRefillFasterRatherThanHoldingMore()
        {
            // P4-41 (user decision): more spawns means a shorter wait, not bigger nests.
            var db = DefaultContent.Build();
            MonsterDefinition M(string id) => db.monsters.Find(m => m.id == id);
            foreach (var id in new[] { "slime", "stone_slime", "skull" })
            {
                var s = M(id);
                Assert.That(s.nestCapacity, Is.LessThanOrEqualTo(8), $"{id} nest grew in size again");
                // Hours from three left (or two in a nest of six) until the den is full again.
                int pop = s.nestCapacity >= 8 ? 3 : 2, hours = 0;
                float acc = 0f;
                while (pop < s.nestCapacity && hours < 100)
                {
                    hours++;
                    acc += s.growthPerHour * pop * (1f - pop / (float)s.nestCapacity);
                    while (acc >= 1f && pop < s.nestCapacity) { acc -= 1f; pop++; }
                }
                Assert.That(hours, Is.LessThanOrEqualTo(20), $"{id} takes {hours} h to refill");
            }
        }

        [Test]
        public void AFreshStoneSlimeDenIsWorthHunting_AndAThinnedDenIsWorthALookLater()
        {
            // P4-42, user report 2026-09-22: "early on they never fight stone slimes".
            var db = DefaultContent.Build();
            MonsterDefinition M(string id) => db.monsters.Find(m => m.id == id);
            var stone = M("stone_slime");
            Assert.That(FreeHunt.DenScore(stone, stone.initialPopulation, 40f), Is.GreaterThan(0f));

            // A den left with a few has bred back when nobody has looked for a while...
            var slime = M("slime");
            Assert.That(FreeHunt.ExpectedDenCount(slime, 2, 0f), Is.EqualTo(2));
            Assert.That(FreeHunt.ExpectedDenCount(slime, 2, 12f), Is.GreaterThan(3));
            Assert.That(FreeHunt.ExpectedDenCount(slime, 2, 500f), Is.InRange(slime.nestCapacity - 1, slime.nestCapacity));
            // ...but an emptied one stays empty in the guild's reckoning: who moves in, and when,
            // cannot be foretold (user decision 2026-09-22: no refill by the same kind).
            Assert.That(FreeHunt.ExpectedDenCount(slime, 0, 48f), Is.EqualTo(0));
        }

        [Test]
        public void TheTownSeesRaidersBeforeTheyReachTheWall()
        {
            // P4-41: town vision reaches past the wall (half size) so raids are seen coming.
            var balance = UnityEngine.ScriptableObject.CreateInstance<GuildMaster.Session.GameBalance>();
            Assert.That(balance.townSightRadius, Is.GreaterThanOrEqualTo(balance.townHalfSize + 5));
            // Nests still have to be found by walking out: none may sit inside that sight.
            Assert.That(balance.minNestDistanceFromTown, Is.GreaterThan(balance.townSightRadius));
        }
    }
}
