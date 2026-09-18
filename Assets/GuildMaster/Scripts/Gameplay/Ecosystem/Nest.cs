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
        public string DisplayName => $"{Species.displayName} 둥지 #{NestId.Substring(2)}";

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
            _renderer.sprite = PixelArt.Nest(species);
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
