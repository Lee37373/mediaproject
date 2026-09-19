using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Town;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    public class RaidTests
    {
        static MonsterDefinition Def(string id, DangerLevel danger)
        {
            var d = ScriptableObject.CreateInstance<MonsterDefinition>();
            d.id = id;
            d.danger = danger;
            d.canRaid = true;
            return d;
        }

        [Test]
        public void Schedule_Day3ThenEvery3Days()
        {
            Assert.That(WaveManager.WaveOnDay(1, 3, 3), Is.EqualTo(0));
            Assert.That(WaveManager.WaveOnDay(3, 3, 3), Is.EqualTo(1));
            Assert.That(WaveManager.WaveOnDay(4, 3, 3), Is.EqualTo(0));
            Assert.That(WaveManager.WaveOnDay(6, 3, 3), Is.EqualTo(2));
            Assert.That(WaveManager.WaveOnDay(12, 3, 3), Is.EqualTo(4));
        }

        [Test]
        public void Compose_SizeFollowsWaveAndPopulation()
        {
            var goblin = Def("goblin", DangerLevel.Low);
            var orc = Def("orc", DangerLevel.Medium);
            var pool = new List<(MonsterDefinition, int)> { (goblin, 18), (orc, 6) };
            var wave = WaveManager.Compose(1, pool, new GameRandom(1));
            // 4 + 2×1 + 24/6 = 10
            Assert.That(wave.Count, Is.EqualTo(10));
            Assert.That(wave.All(d => d == goblin || d == orc), Is.True);
        }

        [Test]
        public void Compose_ExtinctOrEmptySpeciesNeverCome()
        {
            var goblin = Def("goblin", DangerLevel.Low);
            var orc = Def("orc", DangerLevel.Medium);
            var wave = WaveManager.Compose(2, new List<(MonsterDefinition, int)> { (goblin, 12), (orc, 0) }, new GameRandom(2));
            Assert.That(wave.Count, Is.EqualTo(4 + 4 + 2));
            Assert.That(wave.All(d => d == goblin), Is.True);
            Assert.That(WaveManager.Compose(3, new List<(MonsterDefinition, int)>(), new GameRandom(3)), Is.Empty);
        }

        [Test]
        public void Compose_OgresJoinFromWave4()
        {
            var goblin = Def("goblin", DangerLevel.Low);
            var ogre = Def("ogre", DangerLevel.High);
            var pool = new List<(MonsterDefinition, int)> { (goblin, 6), (ogre, 1) };
            Assert.That(WaveManager.Compose(3, pool, new GameRandom(4)).Count(d => d == ogre), Is.EqualTo(0));
            Assert.That(WaveManager.Compose(4, pool, new GameRandom(4)).Count(d => d == ogre), Is.EqualTo(1));
        }
    }
}
