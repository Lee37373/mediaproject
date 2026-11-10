using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Villages
{
    /// <summary>A village took a blow (real, whether or not the guild sees it).</summary>
    public struct VillageDamagedEvent { public Village Village; public float Amount; public Unit Attacker; }
    /// <summary>A village fell (real). It is gone for good (P4-84); the guild may learn of it only later.</summary>
    public struct VillageFellEvent { public Village Village; }

    /// <summary>One of a village's recruitment candidates (P4-84): who they are and when they came.</summary>
    [System.Serializable]
    public class VillageCandidate
    {
        public AdventurerData data;
        public float arrived;
    }

    /// <summary>
    /// Another village (P4-84): an icon of a building's size that nobody enters, with durability that raiders
    /// wear down and that mends when left alone. What the guild knows of it (<see cref="Known"/>, the state it
    /// was last seen in) is kept apart from the real state, and the icon shows what the guild saw (information
    /// hiding, as the fog ghosts). Its candidates are data until the guild master comes to meet them
    /// (<see cref="VillageService"/>).
    /// </summary>
    public class Village : MonoBehaviour
    {
        public int Id { get; private set; }
        public string Name { get; private set; }
        public string DisplayName => $"{Name} 마을";
        public Vector2Int Cell { get; private set; }
        public RectInt Footprint { get; private set; }
        public Region Region { get; private set; }
        /// <summary>On the inner land (within 180 cells of the town).</summary>
        public bool Inner { get; private set; }
        public Vector2 WorldPosition => GridMap.CellToWorld(Cell);

        /// <summary>"Long ago" for the hour fields (saves cannot hold infinity).</summary>
        public const float Never = -1e6f;

        public float MaxHp { get; private set; } = VillageRules.MaxHp;
        public float Hp { get; private set; } = VillageRules.MaxHp;
        public float HpRatio => MaxHp > 0f ? Hp / MaxHp : 0f;
        public bool IsDestroyed => Hp <= 0f;
        public float LastHitHour { get; internal set; } = Never;
        public float FellAtHour { get; internal set; } = -1f;
        /// <summary>When a passing pack last turned on it (<see cref="VillageRules.PassingCooldownHours"/>).</summary>
        public float LastPassingRaidHour { get; internal set; } = Never;

        // ---- What the guild knows ----
        /// <summary>The guild has seen it (it shows on the map from then on).</summary>
        public bool Known { get; internal set; }
        /// <summary>The guild has seen it in ruins.</summary>
        public bool SeenRuined { get; internal set; }
        /// <summary>Durability share when the guild last saw it.</summary>
        public float SeenHpRatio { get; internal set; } = 1f;
        public float SeenAtHour { get; internal set; } = -1f;

        // ---- Candidates ----
        public readonly List<VillageCandidate> Candidates = new();
        /// <summary>The candidates were drawn (the first time the guild master came, or someone had to flee).</summary>
        public bool CandidatesDrawn { get; internal set; }
        public float NextRotationHour { get; internal set; }
        /// <summary>Its candidates already fled from this siege; set again once it has mended above the line.</summary>
        public bool RefugeesSent { get; internal set; }

        SpriteRenderer _renderer;
        float _flash;

        /// <summary>Saves and quests point at a village by this key.</summary>
        public static string SaveKeyOf(int id) => "village:" + id;

        public void Init(int id, VillageSite site, Transform parent)
        {
            Id = id;
            Name = site.Name;
            Cell = site.Cell;
            Footprint = VillagePlacement.Footprint(site.Cell);
            Region = site.Region;
            Inner = site.Inner;
            name = $"Village {id} {Name}";
            transform.SetParent(parent, false);
            transform.position = new Vector3(Footprint.center.x, Footprint.center.y, 0f);
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(Footprint.yMin * 8f) + 4;
            // Hidden until the guild has seen it, like a den (it is in the fog anyway).
            _renderer.enabled = false;
            RefreshLook(false);
        }

        /// <summary>A raider's blow (or a debug one).</summary>
        public void Damage(float amount, Unit attacker, float nowHour)
        {
            if (IsDestroyed || amount <= 0f) return;
            Hp = Mathf.Max(0f, Hp - amount);
            LastHitHour = nowHour;
            _flash = 0.15f;
            EventBus.Publish(new VillageDamagedEvent { Village = this, Amount = amount, Attacker = attacker });
            if (!IsDestroyed) return;
            FellAtHour = nowHour;
            Candidates.Clear();
            EventBus.Publish(new VillageFellEvent { Village = this });
        }

        /// <summary>Mends by the hour (the rule is <see cref="VillageRules.Mend"/>).</summary>
        internal void Mend(float nowHour, float hours)
        {
            Hp = VillageRules.Mend(Hp, MaxHp, nowHour - LastHitHour, hours);
            if (HpRatio >= VillageRules.RefugeeBelow * 2f) RefugeesSent = false;
        }

        /// <summary>Puts a saved state back quietly (no events).</summary>
        internal void RestoreState(float hp, float lastHit, float fellAt, float lastPassing)
        {
            Hp = Mathf.Clamp(hp, 0f, MaxHp);
            LastHitHour = lastHit;
            FellAtHour = fellAt;
            LastPassingRaidHour = lastPassing;
        }

        /// <summary>
        /// What the icon shows: the real state while the guild sees it, else what it last saw — a village the
        /// guild did not see fall still stands on its map.
        /// </summary>
        internal void RefreshLook(bool visibleNow)
        {
            if (_renderer == null) return;
            _renderer.enabled = Known;
            bool ruined = visibleNow ? IsDestroyed : SeenRuined;
            _renderer.sprite = CartoonArt.Village(Region, ruined);
            float hp = visibleNow ? HpRatio : SeenHpRatio;
            var tint = ruined ? Color.white : Color.Lerp(new Color(0.6f, 0.45f, 0.4f), Color.white, Mathf.Clamp01(hp));
            if (_flash > 0f && visibleNow) tint = Color.Lerp(tint, new Color(1f, 0.45f, 0.4f), Session.GameSettings.CalmFlashes ? 0.2f : 0.6f); // P6-3: calmer flashes
            _renderer.color = tint;
        }

        void Update()
        {
            if (_flash <= 0f) return;
            _flash -= Time.deltaTime;
            if (_flash <= 0f && Services.TryGet<VillageService>(out var svc)) RefreshLook(svc.IsVisible(this));
        }

        /// <summary>The cells round the icon a raider or a visitor stands on, nearest to <paramref name="from"/> first.</summary>
        public Vector2 ClosestPoint(Vector2 from)
        {
            var r = Footprint;
            return new Vector2(Mathf.Clamp(from.x, r.xMin, r.xMax), Mathf.Clamp(from.y, r.yMin, r.yMax));
        }

        /// <summary>Where people come out: the walkable cell in front of the icon (south side).</summary>
        public Vector2 Doorstep => new(Footprint.center.x, Footprint.yMin - 0.6f);
    }
}
