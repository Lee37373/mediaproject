using System;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Director;
using GuildMaster.Session;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>P6-3: the release's speeds, which spectator moments pause for what, and the effects tab's two buttons.</summary>
    public class CalmSettingsTests
    {
        [Test]
        public void ReleaseBuild_HasPauseAndX1Only_TheEditorEverySpeed()
        {
            bool fast = GameClock.FastSpeeds;
            var go = new GameObject("Clock");
            try
            {
                var clock = go.AddComponent<GameClock>();
                GameClock.FastSpeeds = false;
                Assert.That(GameClock.MaxSpeedIndex, Is.EqualTo(1));
                clock.SetSpeed(4);
                Assert.That(clock.SpeedIndex, Is.EqualTo(1), "x8 is refused: x1");
                clock.SetSpeed(0);
                Assert.That(clock.SpeedIndex, Is.EqualTo(0), "pause stays");
                GameClock.FastSpeeds = true;
                Assert.That(GameClock.MaxSpeedIndex, Is.EqualTo(GameClock.SpeedScales.Length - 1));
                clock.SetSpeed(4);
                Assert.That(clock.SpeedIndex, Is.EqualTo(4));
            }
            finally
            {
                GameClock.FastSpeeds = fast;
                UnityEngine.Object.DestroyImmediate(go);
                Time.timeScale = 1f;
            }
        }

        [Test]
        public void SpectatorMoments_PauseAsTheirKind_AndOnesThatAreOverDoNot()
        {
            Assert.That(EventPause.Of(HighlightKind.Dragon), Is.EqualTo(PauseTrigger.Dragon));
            Assert.That(EventPause.Of(HighlightKind.DragonOverTown), Is.EqualTo(PauseTrigger.Dragon));
            foreach (var k in new[] { HighlightKind.Lord, HighlightKind.Nemesis, HighlightKind.Rare, HighlightKind.Boss })
                Assert.That(EventPause.Of(k), Is.EqualTo(PauseTrigger.StrongFoe), k.ToString());
            Assert.That(EventPause.Of(HighlightKind.LowHp), Is.EqualTo(PauseTrigger.MemberDanger));
            Assert.That(EventPause.Of(HighlightKind.Wipe), Is.EqualTo(PauseTrigger.MemberDanger));
            Assert.That(EventPause.Of(HighlightKind.Horde), Is.EqualTo(PauseTrigger.Raid));
            Assert.That(EventPause.Of(HighlightKind.GateBroken), Is.EqualTo(PauseTrigger.Raid));
            // The gate fight is the raid already paused for when sighted; a last stand or a retreat is over.
            Assert.That(EventPause.Of(HighlightKind.Raid), Is.Null);
            Assert.That(EventPause.Of(HighlightKind.LastStand), Is.Null);
            Assert.That(EventPause.Of(HighlightKind.Retreat), Is.Null);

            var all = (PauseTrigger[])Enum.GetValues(typeof(PauseTrigger));
            Assert.That(all.Select(EventPause.Label).Distinct().Count(), Is.EqualTo(all.Length), "a row name each");
            Assert.That(all.Select(EventPause.Short).Distinct().Count(), Is.EqualTo(all.Length), "a HUD word each");
            Assert.That(all.All(t => EventPause.Short(t).Length <= 3), Is.True, "short enough beside the clock");
            Assert.That(all.All(t => EventPause.Describe(t).Length > 0), Is.True);
        }

        [Test]
        public void CalmAll_TurnsEveryEffectDown_AndDefaultsBringThemBack()
        {
            float shake = GameSettings.ShakeStrength;
            bool hitStop = GameSettings.HitStop, calm = GameSettings.CalmFlashes, reward = GameSettings.RewardEffects;
            try
            {
                GameSettings.CalmAllEffects();
                Assert.That(GameSettings.ShakeStrength, Is.EqualTo(0f));
                Assert.That(GameSettings.HitStop, Is.False);
                Assert.That(GameSettings.CalmFlashes, Is.True);
                Assert.That(GameSettings.RewardEffects, Is.False);
                GameSettings.DefaultEffects();
                Assert.That(GameSettings.ShakeStrength, Is.EqualTo(1f));
                Assert.That(GameSettings.HitStop, Is.True);
                Assert.That(GameSettings.CalmFlashes, Is.False);
                Assert.That(GameSettings.RewardEffects, Is.True);
                GameSettings.ShakeStrength = 3f;
                Assert.That(GameSettings.ShakeStrength, Is.EqualTo(1f), "kept within 0..1");
            }
            finally
            {
                GameSettings.ShakeStrength = shake;
                GameSettings.HitStop = hitStop;
                GameSettings.CalmFlashes = calm;
                GameSettings.RewardEffects = reward;
            }
        }
    }
}
