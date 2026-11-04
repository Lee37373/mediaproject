using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Units;
using UnityEngine;
using UnityEngine.Rendering;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// Combat and skill effects from the Cartoon Casual VFX Pack (2026-10-30: "애니메이션 효과를 새 에셋으로"): hits,
    /// critical hits, skills, heals, level-ups and a fallen person's smoke. The pack's prefabs are particle systems with
    /// Built-in shaders and looping on, so each copy is made one-shot and its materials are redrawn with
    /// Resources/Shaders/CasualParticle (the 2D renderer draws neither the Built-in nor URP's own particle shaders) at
    /// runtime: no change to the pack, nothing to convert by hand. When the pack is not imported nothing happens and
    /// <see cref="CombatFeedback"/> keeps its own particles.
    /// </summary>
    public class CasualVfx : MonoBehaviour
    {
        /// <summary>Above characters (2000 ± y) and the older effects.</summary>
        public const int SortingOrder = 2970;
        /// <summary>Fits the pack's effects (about 0.7 units at scale 1) to the one-unit characters.</summary>
        const float BaseScale = 1.2f;
        const float MaxSeconds = 2.2f;

        /// <summary>True when the pack is imported and the service runs: <see cref="CombatFeedback"/> leaves hits to it.</summary>
        public static bool Active { get; private set; }

        readonly Dictionary<string, Queue<GameObject>> _pool = new();
        readonly Dictionary<Material, Material> _converted = new();
        readonly List<(GameObject go, string key, float until)> _live = new();
        Shader _particleShader;

        public void Init()
        {
            _particleShader = Resources.Load<Shader>("Shaders/CasualParticle");
            Active = ArtLibrary.HasVfx && _particleShader != null && _particleShader.isSupported;
            if (!Active) return;
            EventBus.Subscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Subscribe<UnitHealedEvent>(OnHealed);
            EventBus.Subscribe<SkillUsedEvent>(OnSkill);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Subscribe<UnitDiedEvent>(OnDied);
        }

        void OnDestroy()
        {
            Active = false;
            EventBus.Unsubscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Unsubscribe<UnitHealedEvent>(OnHealed);
            EventBus.Unsubscribe<SkillUsedEvent>(OnSkill);
            EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Unsubscribe<UnitDiedEvent>(OnDied);
            foreach (var m in _converted.Values) if (m != null) Destroy(m);
        }

        // ---------------- Events ----------------

        void OnDamaged(UnitDamagedEvent e)
        {
            var victim = e.Target;
            if (victim == null || e.Amount <= 0f || !Seen(victim.Position)) return;
            var at = victim.Position + new Vector2(0f, 0.35f);
            // A monster struck by the guild flashes yellow (purple for a mage); a person struck flashes red.
            bool personHit = victim is not Monster;
            string key = e.Critical ? (personHit ? "Hit5_Red" : "Hit5_Yellow")
                       : personHit ? "Hit1_Red"
                       : e.Source is Adventurer a && a.Data != null && a.Data.cls == AdventurerClass.Mage ? "Hit1_Purple" : "Hit1_Yellow";
            Play(key, at, e.Critical ? 1.2f : 0.8f);
        }

        void OnHealed(UnitHealedEvent e)
        {
            if (e.Target == null || e.Amount <= 0f || !Seen(e.Target.Position)) return;
            Play("Heal1", e.Target.Position, 0.9f);
        }

        void OnSkill(SkillUsedEvent e)
        {
            var user = e.User;
            if (user == null || !Seen(user.Position)) return;
            user.Visual?.PlaySkill();
            // A circle in the skill's colour at the user's feet.
            Play("Buff1_" + ColorName(e.Color), user.Position, 1f);
        }

        void OnLevelUp(AdventurerLevelUpEvent e)
        {
            if (e.Adventurer == null || !Seen(e.Adventurer.Position)) return;
            Play("Levelup_Yellow", e.Adventurer.Position, 1.1f);
        }

        void OnDied(UnitDiedEvent e)
        {
            // People only; monsters keep their own death bursts (user: "몬스터들은 바꾸지 말고").
            if (e.Victim == null || e.Victim is Monster || !Seen(e.Victim.Position)) return;
            Play("Smoke", e.Victim.Position + new Vector2(0f, 0.2f), 0.9f);
        }

        /// <summary>The pack's colour variant nearest to a skill's colour.</summary>
        public static string ColorName(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s < 0.25f) return "White";
            float deg = h * 360f;
            return deg < 25f || deg >= 330f ? "Red"
                 : deg < 70f ? "Yellow"
                 : deg < 170f ? "Green"
                 : deg < 255f ? "Blue"
                 : "Purple";
        }

        static bool Seen(Vector2 p)
        {
            if (Services.TryGet<FogOfWar>(out var fog) && !fog.IsVisible(p)) return false;
            if (!Services.TryGet<CameraController>(out var camCtl) || camCtl.Camera == null) return true;
            var cam = camCtl.Camera;
            float hh = cam.orthographicSize + 1.5f, hw = hh * cam.aspect + 1.5f;
            var d = p - (Vector2)cam.transform.position;
            return Mathf.Abs(d.x) <= hw && Mathf.Abs(d.y) <= hh;
        }

        // ---------------- Spawning ----------------

        /// <summary>
        /// A skill's own effect (P4-126), played where the guild can see and the camera looks; false when it was not played
        /// (no pack, unseen, or no such effect) so the caller can draw its plain stand-in instead.
        /// </summary>
        public static bool PlayIfSeen(string key, Vector2 at, float scale = 1f)
        {
            if (!Active || !Services.TryGet<CasualVfx>(out var vfx)) return false;
            if (!Seen(at)) return true; // nothing to draw, and no stand-in either
            return vfx.Play(key, at, scale);
        }

        /// <summary>Plays an effect once at <paramref name="at"/>; returns false when the pack has no such effect.</summary>
        public bool Play(string key, Vector2 at, float scale = 1f)
        {
            if (!Active) return false;
            var go = Take(key);
            if (go == null) return false;
            go.transform.position = new Vector3(at.x, at.y, 0f);
            go.transform.localScale = Vector3.one * (BaseScale * scale);
            go.SetActive(true);
            float seconds = 0f;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                var main = ps.main;
                seconds = Mathf.Max(seconds, main.duration + main.startLifetime.constantMax + main.startDelay.constantMax);
            }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(false);
            _live.Add((go, key, Time.time + Mathf.Min(MaxSeconds, Mathf.Max(0.3f, seconds))));
            return true;
        }

        void Update()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var (go, key, until) = _live[i];
                if (go == null) { _live.RemoveAt(i); continue; }
                if (Time.time < until) continue;
                _live.RemoveAt(i);
                go.SetActive(false);
                _pool[key].Enqueue(go);
            }
        }

        GameObject Take(string key)
        {
            if (!_pool.TryGetValue(key, out var q)) _pool[key] = q = new Queue<GameObject>();
            if (q.Count > 0) return q.Dequeue();
            var prefab = ArtLibrary.Vfx(key);
            if (prefab == null) return null;
            var go = Instantiate(prefab, transform);
            go.name = key;
            go.SetActive(false);
            Prepare(go);
            return go;
        }

        /// <summary>One-shot, drawn over the characters, and with materials the URP renderer can draw.</summary>
        void Prepare(GameObject go)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = false;
                main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            foreach (var r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                r.sortingOrder = SortingOrder + r.sortingOrder;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Convert(mats[i]);
                r.sharedMaterials = mats;
                if (r.trailMaterial != null) r.trailMaterial = Convert(r.trailMaterial);
            }
        }

        /// <summary>A copy of a pack material on <c>GuildMaster/CasualParticle</c> (same formula as the Built-in particle
        /// shaders; "_add" materials drawn so they still show on light ground, the rest alpha blended).</summary>
        Material Convert(Material src)
        {
            if (src == null || _particleShader == null) return src;
            if (_converted.TryGetValue(src, out var done)) return done;

            string shader = src.shader != null ? src.shader.name : "";
            bool additive = shader.Contains("Additive") || src.name.EndsWith("_add");
            var m = new Material(_particleShader) { name = src.name + " (2D)" };
            if (src.HasProperty("_MainTex"))
            {
                m.mainTexture = src.GetTexture("_MainTex");
                m.mainTextureScale = src.mainTextureScale;
                m.mainTextureOffset = src.mainTextureOffset;
            }
            m.SetColor("_TintColor", src.HasProperty("_TintColor") ? src.GetColor("_TintColor")
                                   : src.HasProperty("_Color") ? src.GetColor("_Color") * 0.5f
                                   : new Color(0.5f, 0.5f, 0.5f, 0.5f));
            m.SetFloat("_Additive", additive ? 1f : 0f);
            m.SetFloat("_SrcBlend", (float)(additive ? BlendMode.One : BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.renderQueue = (int)RenderQueue.Transparent;
            _converted[src] = m;
            return m;
        }
    }
}
