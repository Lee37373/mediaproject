using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Session;
using GuildMaster.Town;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// Reward effects in the world (P4-106 H8, user decision 2026-10-27: buildings and level-ups): a building finished or
    /// upgraded sparkles and raises dust; a member levelling up stands in a pillar of light under its floating "LEVEL UP".
    /// Through <see cref="FxService"/>, so nothing off screen is drawn; fewer sparkles at the top speed; off in the settings
    /// (<see cref="GameSettings.RewardEffects"/>). No sound (the events are where it would go).
    /// </summary>
    public class RewardFx : MonoBehaviour
    {
        static readonly Color Gold = new(1f, 0.86f, 0.35f);
        static readonly Color Dust = new(0.72f, 0.62f, 0.48f, 0.7f);
        const int Sparkles = 10;
        const float SparkleSeconds = 1.1f;

        FxService _fx;
        GameClock _clock;
        /// <summary>Sparkles waiting their moment (real time), so they twinkle one after another.</summary>
        readonly List<(float at, Vector2 where, float size)> _pending = new();

        public void Init(FxService fx, GameClock clock)
        {
            _fx = fx;
            _clock = clock;
            EventBus.Subscribe<BuildingBuiltEvent>(OnBuilt);
            EventBus.Subscribe<BuildingLevelChangedEvent>(OnLevel);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<BuildingBuiltEvent>(OnBuilt);
            EventBus.Unsubscribe<BuildingLevelChangedEvent>(OnLevel);
            EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);
        }

        static bool On => GameSettings.RewardEffects;

        bool Fastest => _clock != null && _clock.SpeedIndex >= GameClock.SpeedScales.Length - 1;

        void OnBuilt(BuildingBuiltEvent e) => Celebrate(e.Building);

        void OnLevel(BuildingLevelChangedEvent e)
        {
            if (e.Level >= 2) Celebrate(e.Building);
        }

        /// <summary>Dust round its foot, then sparkles over it one after another.</summary>
        void Celebrate(Building b)
        {
            if (!On || b == null || _fx == null) return;
            var fp = b.Footprint;
            var foot = new Vector2(fp.center.x, fp.yMin + 0.3f);
            float width = Mathf.Max(2f, fp.width);
            _fx.Ring(foot, width * 0.75f, Dust, 0.6f);
            _fx.Ring(foot, width * 0.5f, Dust, 0.45f);
            int n = Fastest ? Sparkles / 3 : Sparkles;
            float now = Time.unscaledTime;
            for (int i = 0; i < n; i++)
            {
                var p = new Vector2(Random.Range(fp.xMin, fp.xMax), Random.Range(fp.yMin + 0.5f, fp.yMax + 1.5f));
                _pending.Add((now + SparkleSeconds * i / n, p, Random.Range(0.5f, 0.9f)));
            }
        }

        /// <summary>A pillar of light from its feet, a ring on the ground and a few rising sparks.</summary>
        void OnLevelUp(AdventurerLevelUpEvent e)
        {
            var a = e.Adventurer;
            if (!On || a == null || !a.IsAlive || _fx == null) return;
            var feet = a.Position;
            _fx.Beam(feet, feet + new Vector2(0f, 3.4f), new Color(Gold.r, Gold.g, Gold.b, 0.55f), 0.7f, 0.8f);
            _fx.Ring(feet, 1.3f, Gold, 0.5f);
            if (Fastest) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < 4; i++) _pending.Add((now + 0.12f * i, feet + new Vector2(Random.Range(-0.3f, 0.3f), 0.6f + 0.7f * i), 0.5f));
        }

        void Update()
        {
            if (_pending.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].at > now) continue;
                _fx.Spark(_pending[i].where, Gold, _pending[i].size, 0.35f);
                _pending.RemoveAt(i);
            }
        }
    }
}
