using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Legacy;
using GuildMaster.Units;
using NUnit.Framework;

namespace GuildMaster.Tests
{
    /// <summary>The memorial wall's rules (2026-11-15): the line on the stone, heroes, last words, friends' words, anniversaries, heirlooms.</summary>
    public class MemorialRulesTests
    {
        static FallenRecord Rec(int id = 1, int level = 10, int kills = 20, int quests = 5, string values = "명예", string killer = "오크")
            => new() { Id = id, Name = "루나", ClassLabel = "마법사", Cls = AdventurerClass.Mage, Values = values, Level = level, Kills = kills, QuestsDone = quests, Day = 5, KillerName = killer };

        [Test]
        public void Epitaph_IsTheSameEveryTime_AndNeverEmpty()
        {
            var r = Rec();
            Assert.That(MemorialRules.Epitaph(r), Is.EqualTo(MemorialRules.Epitaph(r)));
            foreach (var v in new[] { "돈", "명예", "동료", "모험", "안전", "지식", "?" })
                Assert.That(MemorialRules.Epitaph(Rec(values: v)), Is.Not.Empty, v);
        }

        [Test]
        public void Epitaph_ATitleComesFirst_ThenAGreatTally_ThenYouth()
        {
            var titled = Rec(kills: 500);
            titled.Titles.Add("잿불 거인 바흐람을 쓰러뜨린 자");
            Assert.That(MemorialRules.Epitaph(titled), Does.Contain("잿불 거인"));
            Assert.That(MemorialRules.Epitaph(Rec(kills: 130)), Does.Contain("130"));
            Assert.That(MemorialRules.Epitaph(Rec(level: 3, kills: 5)), Does.Contain("신참").Or.Contain("막내").Or.Contain("첫 모험"));
        }

        [Test]
        public void Hero_ByTitle_Kills_Quests_OrLevel_AndNotOtherwise()
        {
            Assert.That(MemorialRules.IsHero(Rec()), Is.False);
            var titled = Rec();
            titled.Titles.Add("X를 쓰러뜨린 자");
            Assert.That(MemorialRules.HeroReason(titled), Is.EqualTo("X를 쓰러뜨린 자"));
            Assert.That(MemorialRules.IsHero(Rec(kills: MemorialRules.HeroKills)), Is.True);
            Assert.That(MemorialRules.IsHero(Rec(quests: MemorialRules.HeroQuests)), Is.True);
            Assert.That(MemorialRules.IsHero(Rec(level: MemorialRules.HeroLevel)), Is.True);
        }

        [Test]
        public void LastWords_AndRemembrances_AreNeverEmpty_AndAGrudgeNamesTheKiller()
        {
            foreach (var v in new[] { "돈", "명예", "동료", "모험", "안전", "지식", "?" })
                Assert.That(MemorialRules.LastWords(Rec(values: v)), Is.Not.Empty, v);
            bool named = false;
            for (int seed = 0; seed < 40; seed++)
            {
                Assert.That(MemorialRules.Remembrance("루나", "그롬", 60, seed), Is.Not.Empty);
                Assert.That(MemorialRules.Remembrance("루나", null, 20, seed), Is.Not.Empty);
                named |= MemorialRules.Remembrance("루나", "그롬", 60, seed).Contains("그롬");
            }
            Assert.That(named, Is.True, "a close friend may swear to pay the killer back");
        }

        [Test]
        public void Anniversary_ComesOnceAYear()
        {
            int year = MemorialRules.AnniversaryDays;
            Assert.That(MemorialRules.IsAnniversary(5, 5), Is.False, "not the day they fell");
            Assert.That(MemorialRules.IsAnniversary(5, 5 + year), Is.True);
            Assert.That(MemorialRules.IsAnniversary(5, 6 + year), Is.False);
            Assert.That(MemorialRules.IsAnniversary(5, 5 + 2 * year), Is.True);
            Assert.That(MemorialRules.Years(5, 5 + 2 * year), Is.EqualTo(2));
            Assert.That(MemorialRules.DaysToAnniversary(5, 5), Is.EqualTo(year));
            Assert.That(MemorialRules.DaysToAnniversary(5, 5 + year - 3), Is.EqualTo(3));
            Assert.That(MemorialRules.DaysToAnniversary(5, 5 + year), Is.EqualTo(0));
        }

        [Test]
        public void Heirlooms_RaiseAttackAndHealth_UpToTheCap()
        {
            var s = new CombatStats { Attack = 100f, MaxHp = 1000f, Defense = 10f };
            var d = new AdventurerData();
            Assert.That(MemorialRules.WithHeirlooms(s, d).Attack, Is.EqualTo(100f));
            d.heirlooms.Add("a");
            var one = MemorialRules.WithHeirlooms(s, d);
            Assert.That(one.Attack, Is.EqualTo(100f * (1f + MemorialRules.HeirloomAttack)).Within(0.001f));
            Assert.That(one.MaxHp, Is.EqualTo(UnityEngine.Mathf.Round(1000f * (1f + MemorialRules.HeirloomHp))));
            Assert.That(one.Defense, Is.EqualTo(10f));
            for (int i = 0; i < 10; i++) d.heirlooms.Add("b" + i);
            Assert.That(MemorialRules.WithHeirlooms(s, d).Attack, Is.EqualTo(100f * (1f + MemorialRules.HeirloomAttack * MemorialRules.MaxHeirlooms)).Within(0.001f));
            Assert.That(MemorialRules.WithHeirlooms(s, null).Attack, Is.EqualTo(100f));
        }

        [Test]
        public void Heirs_FriendsFirst_ClosestFirst_ThenTheSameClass()
        {
            var rel = new Dictionary<string, int> { ["far"] = 0, ["friend"] = 20, ["best"] = 80, ["same"] = 5, ["foe"] = -30 };
            var same = new HashSet<string> { "same" };
            var ranked = MemorialRules.RankHeirs(new[] { "far", "friend", "best", "same", "foe" }, k => rel[k], k => same.Contains(k), 4);
            Assert.That(ranked, Is.EqualTo(new[] { "best", "friend", "same", "far" }));
        }
    }
}
