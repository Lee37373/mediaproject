using System.Collections.Generic;
using GuildMaster.Data;
using GuildMaster.Monsters;
using GuildMaster.Nemeses;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>
    /// Nemesis monsters (P4-109, user decisions 2026-10-30): a name picked by the monster's id and never one a living nemesis
    /// of the kind has, a title from what it did, levels for a kill and for one driven off (none for a lord or the dragon), and
    /// a bounty that pays more for a killer.
    /// </summary>
    public class NemesisTests
    {
        static NemesisDeed Deed(string victim, DeedKind kind) => new() { victim_id = "NPC_" + victim, victim = victim, kind = kind };

        [Test]
        public void ItsName_ComesFromItsId_AndStepsPastTakenOnes()
        {
            string a = NemesisRules.PersonalName("M_0042", "goblin", null);
            Assert.That(NemesisRules.PersonalName("M_0042", "goblin", null), Is.EqualTo(a), "the same id, the same name: no dice");
            Assert.That(NemesisRules.NamesFor("goblin"), Does.Contain(a));
            Assert.That(NemesisRules.NamesFor("sand_goblin"), Is.SameAs(NemesisRules.NamesFor("goblin")), "goblins share their names");
            Assert.That(NemesisRules.NamesFor("frost_skull"), Is.SameAs(NemesisRules.NamesFor("skull")));
            Assert.That(NemesisRules.NamesFor("bog_slime"), Is.SameAs(NemesisRules.NamesFor("slime")));

            var taken = new HashSet<string> { a };
            string b = NemesisRules.PersonalName("M_0042", "goblin", taken);
            Assert.That(b, Is.Not.EqualTo(a), "a living nemesis of the kind has it");
            foreach (var n in NemesisRules.NamesFor("skull")) taken.Add(n);
            string c = NemesisRules.PersonalName("M_0001", "skull", taken);
            Assert.That(c, Does.EndWith(" 2"), "past every name, numbered");
        }

        [Test]
        public void ItsTitle_TellsWhatItDid()
        {
            Assert.That(NemesisRules.Title(new List<NemesisDeed>()), Is.Empty);
            Assert.That(NemesisRules.Title(new[] { Deed("레오", DeedKind.Killed) }), Is.EqualTo("레오 잡이"));
            Assert.That(NemesisRules.Title(new[] { Deed("레오", DeedKind.Killed), Deed("미나", DeedKind.DroveOff), Deed("카이", DeedKind.Killed) }),
                Is.EqualTo("레오·카이 잡이"), "kills name it, not the ones it drove off");
            Assert.That(NemesisRules.Title(new[] { Deed("a", DeedKind.Killed), Deed("b", DeedKind.Killed), Deed("c", DeedKind.Killed) }), Is.EqualTo("학살자"));
            Assert.That(NemesisRules.Title(new[] { Deed("레오", DeedKind.DroveOff) }), Is.EqualTo("레오를 쫓아낸"));
            Assert.That(NemesisRules.Title(new[] { Deed("레오", DeedKind.DroveOff), Deed("미남", DeedKind.DroveOff) }), Is.EqualTo("레오·미남을 쫓아낸"));
            Assert.That(NemesisRules.Title(new[] { Deed("a", DeedKind.DroveOff), Deed("b", DeedKind.DroveOff), Deed("c", DeedKind.DroveOff) }), Is.EqualTo("공포의"));

            Assert.That(NemesisRules.FullName("레오 잡이", "고블린 족장", "그릭"), Is.EqualTo("레오 잡이 고블린 족장 그릭"));
            Assert.That(NemesisRules.FullName("레오 잡이", "황금 고블린", null), Is.EqualTo("레오 잡이 황금 고블린"), "no own name for a rare one");
            Assert.That(NemesisRules.SlayerTitle("그릭"), Is.EqualTo("그릭 잡이"));
            Assert.That(NemesisRules.DeedText(Deed("미남", DeedKind.DroveOff)), Is.EqualTo("미남을 쫓아냄"));
        }

        [Test]
        public void ItGrows_TwoForAKill_OneForADriveOff_NeverPastTheCap_NorALord()
        {
            Assert.That(NemesisRules.GrownLevel(5, DeedKind.Killed, true, MonsterLevel.MaxLevel), Is.EqualTo(5 + NemesisRules.KillLevels));
            Assert.That(NemesisRules.GrownLevel(5, DeedKind.DroveOff, true, MonsterLevel.MaxLevel), Is.EqualTo(5 + NemesisRules.DriveOffLevels));
            Assert.That(NemesisRules.GrownLevel(MonsterLevel.MaxLevel - 1, DeedKind.Killed, true, MonsterLevel.MaxLevel), Is.EqualTo(MonsterLevel.MaxLevel));
            Assert.That(NemesisRules.GrownLevel(40, DeedKind.Killed, false, MonsterLevel.MaxLevel), Is.EqualTo(40), "a lord or the dragon does not grow");
        }

        [Test]
        public void ItsBounty_PaysMoreForAKiller_AndForEachKill()
        {
            Assert.That(NemesisRules.BountyFactor(0), Is.LessThan(NemesisRules.BountyFactor(1)));
            Assert.That(NemesisRules.BountyFactor(2), Is.GreaterThan(NemesisRules.BountyFactor(1)));
            Assert.That(NemesisRules.BountyFactor(20), Is.EqualTo(4f), "capped");
            int one = Incidents.IncidentRules.NemesisReward(DangerLevel.Medium, 60f, NemesisRules.BountyFactor(1));
            int plain = Quests.QuestPricing.HuntReward(DangerLevel.Medium, Quests.QuestPricing.HuntKills, 60f);
            Assert.That(one, Is.GreaterThan(plain), "a bounty pays over a hunt of that danger");
        }
    }
}
