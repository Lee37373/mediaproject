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
        /// <summary>Terrain region of the site (P4-58: species live by region).</summary>
        public Region Region { get; private set; }
        /// <summary>
        /// Whether a species can live here (P4-58) — and may move in: a region lord's lair is its own, and cleansed
        /// land takes nobody new (P4-83). Every move into a site (migration, newcomers, roaming packs, flight
        /// from a disaster) asks this.
        /// </summary>
        public bool Suits(MonsterDefinition species) => species != null && !IsLair && !Sealed && species.LivesAt(Region, Biome);

        /// <summary>The lair of a region lord (P4-83): its guards breed here while it lives; nobody leaves or moves in.</summary>
        public bool IsLair { get; private set; }
        /// <summary>The lord this lair was dealt (it may be dead now).</summary>
        public MonsterDefinition Lord { get; private set; }
        /// <summary>Cleansed land (P4-83): nothing is born here or moves in any more.</summary>
        public bool Sealed { get; internal set; }

        /// <summary>
        /// The town's walls moved out over this site (P4-86): it is the town's ground now — sealed, its den gone
        /// from the map and from what the guild knows (<see cref="Discovered"/> false).
        /// </summary>
        public bool Retired { get; private set; }

        /// <summary>See <see cref="Retired"/>; whoever still lives here has to be gone first.</summary>
        internal void Retire()
        {
            Retired = true;
            Sealed = true;
            Discovered = false;
            if (_renderer != null) _renderer.enabled = false;
        }

        internal void MakeLair(MonsterDefinition lord)
        {
            IsLair = true;
            Lord = lord;
            // A lair keeps the guard count the lords were tuned against (2026-11-02: other dens grew ×1.5).
            Capacity = MonsterAbundance.Unscaled(Capacity);
            name = $"Nest {NestId} (lair of {lord.id})";
        }

        /// <summary>The lord, alive and at home.</summary>
        public Monster LivingLord
        {
            get
            {
                if (!IsLair) return null;
                foreach (var m in _members) if (m != null && m.IsAlive && m.IsRegionLord) return m;
                return null;
            }
        }
        public int Capacity { get; private set; }
        public float GrowthAccumulator { get; set; }
        /// <summary>Breeding factor of this site by its distance from the town (<see cref="EcosystemManager.GrowthFactor"/>).</summary>
        public float GrowthFactor { get; internal set; } = 1f;
        /// <summary>Level its young are born at, by the site's distance from the town (<see cref="Monsters.MonsterLevel.BirthLevel"/>).</summary>
        public int BirthLevel { get; internal set; } = 1;
        /// <summary>
        /// Town gates (centre cells) this pack has seen broken (user report 2026-09-22): what the
        /// members see is shared among them, nothing more — a breach nobody saw is thought shut.
        /// </summary>
        public HashSet<Vector2Int> KnownOpenGates { get; } = new();
        /// <summary>Game hours the nest has been empty (a band may move in, see <see cref="MigrationService"/>).</summary>
        public int EmptyHours { get; set; }
        public bool Discovered { get; private set; }

        public ListView<Monster> Members => new(_members);
        internal List<Monster> MemberList => _members;
        public int Population => _members.Count;
        public bool IsEmpty => _members.Count == 0;
        public Vector2 WorldPosition => GridMap.CellToWorld(Cell);
        /// <summary>Real name (simulation/debug). Guild-facing text uses GuildIntel.NameOf (last seen species).</summary>
        public string DisplayName => NameFor(Species);

        /// <summary>Name for a given owner; a null species means the guild has not seen who lives here.</summary>
        public string NameFor(MonsterDefinition species) => $"{(species != null ? species.displayName : "정체불명")} 둥지 #{NestId.Substring(2)}";

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

        /// <summary>A member leaves with its whole pack for no nest (P4-58 roaming); population unchanged.</summary>
        internal bool ReleaseMember(Monster m) => _members.Remove(m);

        /// <summary>A roaming pack's member settles here (P4-58); population unchanged.</summary>
        internal void AdoptMember(Monster m)
        {
            if (m == null || _members.Contains(m)) return;
            _members.Add(m);
            m.ChangeHome(this);
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
            EmptyHours = 0;
            name = $"Nest {NestId} ({species.id})";
            // The map keeps the old den art until the guild sees the new owners (GuildIntel).
        }

        /// <summary>Save (P5-4): the owners it has now (they may have changed since the map was dealt), quietly.</summary>
        internal void RestoreSpecies(MonsterDefinition species)
        {
            if (species == null || species == Species) return;
            Species = species;
            Capacity = species.nestCapacity;
            name = $"Nest {NestId} ({species.id})";
        }

        /// <summary>Save (P5-4): found before; shown on the map at once, no discovery event.</summary>
        internal void RestoreDiscovered(bool discovered)
        {
            Discovered = discovered && !Retired;
            if (_renderer != null) _renderer.enabled = Discovered;
        }

        /// <summary>Save (P5-4): a saved member back in its place in the pack's order (the first one alive leads).</summary>
        internal void RestoreMember(Monster m) => _members.Add(m);

        EcosystemManager _eco;

        public void Init(EcosystemManager eco, string id, MonsterDefinition species, NestSite site)
        {
            _eco = eco;
            NestId = id;
            Species = species;
            Cell = site.Cell;
            Biome = site.Biome;
            Region = site.Region;
            Capacity = species.nestCapacity;
            name = $"Nest {id} ({species.id})";
            transform.position = WorldPosition;

            _renderer = gameObject.AddComponent<SpriteRenderer>();
            // Plain earth until the guild sees who lives here (GuildIntel calls ShowAs then, P4-76):
            // a den tinted by its owners' colour told them apart before anyone had seen them.
            _renderer.sprite = CartoonArt.Nest(null);
            _renderer.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(transform.position.y * 8f) - 2;
            _renderer.enabled = false;
        }

        /// <summary>
        /// A new member at the nest, or at <paramref name="at"/> (newcomers walking in from the map edge); <paramref name="boss"/> = the pack's boss (P4-57).
        /// <paramref name="kind"/> is another kind than the nest's own: a lair's lord (P4-83).
        /// </summary>
        public Monster SpawnMember(Vector2? at = null, bool boss = false, MonsterDefinition kind = null)
        {
            var pool = Services.Get<PoolService>();
            var world = Services.Get<WorldService>();
            var rng = GameRandom.Shared;
            world.Grid.TryRandomWalkableNear(at.HasValue ? GridMap.WorldToCell(at.Value) : Cell, 2, rng, out var cell);
            var pos = GridMap.CellToWorld(cell) + rng.InsideUnitCircle() * 0.3f;
            var monster = pool.Spawn<Monster>(Monster.PoolKey, pos);
            monster.Setup(kind ?? Species, this, boss);
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
            if (Discovered || Retired) return;
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
