using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// Combat juice (P4-8), driven by events: hit debris coloured by the victim, bigger bursts and a
    /// short hit-stop on critical hits, death bursts, level-up sparkles, and camera shake scaled to
    /// how hard a hit landed. Only for what the guild sees and what is on screen.
    /// </summary>
    public class CombatFeedback : MonoBehaviour
    {
        const float HitStopSeconds = 0.05f;
        const float HitStopCooldown = 0.3f;
        /// <summary>A single hit taking at least this share of max HP counts as a big hit (shake).</summary>
        const float BigHitShare = 0.25f;

        static readonly Color GuildBlood = new(0.9f, 0.22f, 0.2f);
        static readonly Color CritFlash = new(1f, 0.95f, 0.7f);
        static readonly Color LevelGold = new(1f, 0.85f, 0.3f);

        HitParticles _particles;
        FxService _fx;
        float _nextHitStop;

        public void Init(HitParticles particles, FxService fx)
        {
            _particles = particles;
            _fx = fx;
            EventBus.Subscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Subscribe<UnitDiedEvent>(OnDied);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Unsubscribe<UnitDiedEvent>(OnDied);
            EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);
        }

        // ---------------- Events ----------------

        void OnDamaged(UnitDamagedEvent e)
        {
            var victim = e.Target;
            // A poison step is no blow: no burst (P4-87).
            if (victim == null || e.Amount <= 0f || e.OverTime || !OnScreen(victim.Position, out float closeness)) return;
            var at = victim.Position + new Vector2(0f, 0.35f);
            float share = victim.Stats.MaxHp > 0f ? e.Amount / victim.Stats.MaxHp : 0f;
            var color = ColorOf(victim);

            int count = Mathf.RoundToInt(Mathf.Lerp(4f, 14f, Mathf.Clamp01(share * 2.5f))) + (e.Critical ? 8 : 0);
            _particles.Burst(at, new BurstSpec
            {
                Color = color, Count = count, SpeedMin = 1.2f, SpeedMax = e.Critical ? 4.2f : 3f,
                SizeMin = 0.14f, SizeMax = e.Critical ? 0.42f : 0.3f, Lifetime = 0.45f, Gravity = 0.6f, Radius = 0.1f,
            });

            if (e.Critical)
            {
                _fx.Spark(at, CritFlash, 1.1f, 0.18f);
                bool guildInvolved = victim.Faction == Faction.Guild || (e.Source != null && e.Source.Faction == Faction.Guild);
                if (guildInvolved && Time.unscaledTime >= _nextHitStop && Services.TryGet<GameClock>(out var clock))
                {
                    _nextHitStop = Time.unscaledTime + HitStopCooldown;
                    clock.HitStop(HitStopSeconds);
                }
            }

            // Shake only for hits that really hurt, weaker the farther from the screen centre.
            if (share >= BigHitShare) Shake(Mathf.Lerp(0.12f, 0.35f, Mathf.InverseLerp(BigHitShare, 0.7f, share)) * closeness);
            else if (e.Critical && victim.Faction == Faction.Guild) Shake(0.08f * closeness);
        }

        void OnDied(UnitDiedEvent e)
        {
            var victim = e.Victim;
            if (victim == null || !OnScreen(victim.Position, out float closeness)) return;
            var at = victim.Position + new Vector2(0f, 0.3f);
            var color = ColorOf(victim);
            bool big = victim is Monster m && m.Def != null && m.Danger == DangerLevel.High;

            _particles.Burst(at, new BurstSpec
            {
                Color = color, Count = big ? 34 : 20, SpeedMin = 1.5f, SpeedMax = big ? 5f : 3.8f,
                SizeMin = 0.16f, SizeMax = big ? 0.55f : 0.4f, Lifetime = 0.7f, Gravity = 0.8f, Radius = 0.2f,
            });
            _fx.Ring(victim.Position, big ? 1.6f : 1f, new Color(color.r, color.g, color.b, 0.8f), 0.4f);

            if (victim.Faction == Faction.Guild)
            {
                // A fallen member: pale light drifting up.
                _particles.Burst(at, new BurstSpec
                {
                    Color = new Color(0.9f, 0.95f, 1f, 0.9f), Count = 14, SpeedMin = 0.2f, SpeedMax = 0.6f,
                    SizeMin = 0.16f, SizeMax = 0.32f, Lifetime = 1.6f, Gravity = -0.25f, Radius = 0.25f,
                });
                Shake(0.25f * closeness);
            }
            else if (big) Shake(0.3f * closeness);
        }

        void OnLevelUp(AdventurerLevelUpEvent e)
        {
            var a = e.Adventurer;
            if (a == null || !OnScreen(a.Position, out _)) return;
            _fx.Ring(a.Position, 1.3f, LevelGold, 0.6f);
            _fx.Spark(a.Position + new Vector2(0f, 0.4f), LevelGold, 1.4f, 0.35f);
            _particles.Burst(a.Position + new Vector2(0f, 0.1f), new BurstSpec
            {
                Color = LevelGold, Count = 26, SpeedMin = 0.3f, SpeedMax = 1.2f,
                SizeMin = 0.14f, SizeMax = 0.3f, Lifetime = 1.3f, Gravity = -0.45f, Radius = 0.35f,
            });
        }

        // ---------------- Helpers ----------------

        static Color ColorOf(Unit u) => u is Monster m && m.Def != null ? m.Def.bodyColor : GuildBlood;

        /// <summary>
        /// True when the guild sees the point and it is inside the camera view (small margin).
        /// <paramref name="closeness"/> is 1 at the view centre, 0.4 at its edge (shake falloff).
        /// </summary>
        static bool OnScreen(Vector2 p, out float closeness)
        {
            closeness = 0f;
            if (Services.TryGet<FogOfWar>(out var fog) && !fog.IsVisible(p)) return false;
            if (!Services.TryGet<CameraController>(out var camCtl) || camCtl.Camera == null) return true;
            var cam = camCtl.Camera;
            float hh = cam.orthographicSize + 1f, hw = hh * cam.aspect + 1f;
            var d = p - (Vector2)cam.transform.position;
            if (Mathf.Abs(d.x) > hw || Mathf.Abs(d.y) > hh) return false;
            float edge = Mathf.Max(Mathf.Abs(d.x) / hw, Mathf.Abs(d.y) / hh);
            closeness = Mathf.Lerp(1f, 0.4f, edge);
            return true;
        }

        static void Shake(float trauma)
        {
            if (trauma > 0f && Services.TryGet<CameraController>(out var cam)) cam.Shake(trauma);
        }
    }
}
