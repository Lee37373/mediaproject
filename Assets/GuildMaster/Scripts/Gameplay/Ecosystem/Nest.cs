using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Ecosystem
{
    /// <summary>A monster den: owns its members and is the spawn point for its species.</summary>
    public class Nest : MonoBehaviour
    {
        readonly List<Monster> _members = new();
        SpriteRenderer _renderer;
        float _revealTimer;

        public string NestId { get; private set; }
        public MonsterDefinition Species { get; private set; }
        public Vector2Int Cell { get; private set; }
        public Biome Biome { get; private set; }
        public int Capacity { get; private set; }
        public float GrowthAccumulator { get; set; }
        public bool Discovered { get; private set; }

        public IReadOnlyList<Monster> Members => _members;
        public int Population => _members.Count;
        public bool IsEmpty => _members.Count == 0;
        public Vector2 WorldPosition => GridMap.CellToWorld(Cell);
        /// <summary>Real name (simulation/debug). Guild-facing text uses GuildIntel.NameOf (last seen species).</summary>
        public string DisplayName => NameFor(Species);

        public string NameFor(MonsterDefinition species) => $"{(species ?? Species).displayName} 둥지 #{NestId.Substring(2)}";

        /// <summary>Draws the nest as the given species' den (GuildIntel calls this when it sees the nest).</summary>
        public void ShowAs(MonsterDefinition species)
        {
            if (species != null && _renderer != null) _renderer.sprite = CartoonArt.Nest(species);
        }

        /// <summary>Cold, dim tint for a nest drawn from memory rather than sight (P4-23).</summary>
        static readonly Color RememberedTint = new(0.66f, 0.70f, 0.80f, 0.9f);

        /// <summary>Out of sight the nest is only what the guild last saw, so it is shown dimmed.</summary>
        public void SetRemembered(bool remembered)
        {
            if (_renderer != null) _renderer.color = remembered ? RememberedTint : Color.white;
        }

        /// <summary>
        /// Territory radius: the species' base scaled by how full the nest is (0.6x nearly empty,
        /// 1.4x full); 0 when nobody lives here. Real simulation value — guild-facing UI must not show it.
        /// </summary>
        public float TerritoryRadius => TerritoryRadiusFor(Species.territoryRadius, Population, Capacity);

        public static float TerritoryRadiusFor(float baseRadius, int population, int capacity)
            => population <= 0 ? 0f : baseRadius * (0.6f + 0.8f * Mathf.Clamp01(population / (float)Mathf.Max(1, capacity)));

        public bool InTerritory(Vector2 p)
        {
            float r = TerritoryRadius;
            return r > 0f && (p - WorldPosition).sqrMagnitude <= r * r;
        }

        /// <summary>
        /// Someone the residents drive out: a guild unit or any monster from another pack inside the
        /// territory — other species, and same-species monsters from other nests or with no nest
        /// (user request: packs are rivals even within a species). Visitors are left alone.
        /// </summary>
        public bool IsIntruder(Unit u)
        {
            if (u == null || !u.IsAlive || !InTerritory(u.Position)) return false;
            if (u is Monster m) return m.Home != this;
            return u.Faction == Faction.Guild;
        }

        /// <summary>Moves a living member to another nest (migration). Population per species is unchanged.</summary>
        internal void TransferMember(Monster m, Nest to)
        {
            if (to == null || to == this || !_members.Remove(m)) return;
            to._members.Add(m);
            m.ChangeHome(to);
        }

        /// <summary>An empty nest taken over by migrants of another species.</summary>
        internal void ChangeSpecies(MonsterDefinition species)
        {
            if (!IsEmpty || species == null || species == Species) return;
            Species = species;
            Capacity = species.nestCapacity;
            GrowthAccumulator = 0f;
            name = $"Nest {NestId} ({species.id})";
            // The map keeps the old den art until the guild sees the new owners (GuildIntel).
        }

        EcosystemManager _eco;

        public void Init(EcosystemManager eco, string id, MonsterDefinition species, NestSite site)
        {
            _eco = eco;
            NestId = id;
            Species = species;
            Cell = site.Cell;
            Biome = site.Biome;
            Capacity = species.nestCapacity;
            name = $"Nest {id} ({species.id})";
            transform.position = WorldPosition;

            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = CartoonArt.Nest(species);
            _renderer.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(transform.position.y * 8f) - 2;
            _renderer.enabled = false;
        }

        public Monster SpawnMember()
        {
            var pool = Services.Get<PoolService>();
            var world = Services.Get<WorldService>();
            var rng = GameRandom.Shared;
            world.Grid.TryRandomWalkableNear(Cell, 2, rng, out var cell);
            var pos = GridMap.CellToWorld(cell) + rng.InsideUnitCircle() * 0.3f;
            var monster = pool.Spawn<Monster>(Monster.PoolKey, pos);
            monster.Setup(Species, this);
            _members.Add(monster);
            return monster;
        }

        internal void OnMemberDied(Monster m, Unit killer)
        {
            if (!_members.Remove(m)) return;
            _eco.NotifyMemberDied(this, m, killer);
        }

        void OnDrawGizmos()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying || !UnitGizmos.ShowAll || Species == null) return;
            UnityEditor.Handles.color = new Color(Species.bodyColor.r, Species.bodyColor.g, Species.bodyColor.b, 0.8f);
            UnityEditor.Handles.DrawWireDisc(WorldPosition, Vector3.forward, TerritoryRadius, 2f);
            UnityEditor.Handles.Label(WorldPosition + new Vector2(0f, TerritoryRadius), $"{DisplayName} {Population}/{Capacity} 영역 {TerritoryRadius:0.0}");
#endif
        }

        void Update()
        {
            if (Discovered) return;
            _revealTimer -= Time.unscaledDeltaTime;
            if (_revealTimer > 0f) return;
            _revealTimer = 0.5f;
            if (Services.TryGet<FogOfWar>(out var fog) && fog.IsExploredRaw(Cell))
            {
                Discovered = true;
                _renderer.enabled = true;
                EventBus.Publish(new NestDiscoveredEvent { Nest = this });
            }
        }
    }
}
