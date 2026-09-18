using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Recruitment
{
    public struct VisitorArrivedEvent { public Adventurer Visitor; }

    /// <summary>
    /// Spawns recruitable visitors at a town gate every 12–20 game hours. They stay on the plaza
    /// for 12 hours (longer while talking) and then leave (see VisitorStates).
    /// </summary>
    public class VisitorService : MonoBehaviour
    {
        public const float StayHours = 12f;
        const float MinIntervalHours = 12f;
        const float MaxIntervalHours = 20f;
        const int MaxConcurrent = 2;

        readonly List<Adventurer> _visitors = new();
        GameClock _clock;
        GuildRoster _roster;
        WorldService _world;
        float _nextVisitHour;

        public IReadOnlyList<Adventurer> Visitors => _visitors;

        public void Init(GameClock clock, GuildRoster roster, WorldService world)
        {
            _clock = clock;
            _roster = roster;
            _world = world;
            // First visitor comes early so the feature is discoverable.
            _nextVisitHour = clock.TotalHours + GameRandom.Shared.Range(3f, 6f);
            EventBus.Subscribe<VisitorLeftTownEvent>(OnLeft);
            EventBus.Subscribe<AdventurerJoinedEvent>(OnJoined);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<VisitorLeftTownEvent>(OnLeft);
            EventBus.Unsubscribe<AdventurerJoinedEvent>(OnJoined);
        }

        void OnLeft(VisitorLeftTownEvent e) => _visitors.Remove(e.Visitor);
        void OnJoined(AdventurerJoinedEvent e) => _visitors.Remove(e.Adventurer);

        void Update()
        {
            if (_clock == null || _clock.TotalHours < _nextVisitHour) return;
            _nextVisitHour = _clock.TotalHours + GameRandom.Shared.Range(MinIntervalHours, MaxIntervalHours);
            _visitors.RemoveAll(v => v == null);
            if (_roster.IsFull || _visitors.Count >= MaxConcurrent) return;
            SpawnVisitor();
        }

        /// <summary>Spawns a visitor at a random gate now (also used by debug commands).</summary>
        public Adventurer SpawnVisitor()
        {
            var rng = GameRandom.Shared;
            int level = Mathf.Max(1, 1 + (_clock.Day - 1) / 4 + rng.Range(-1, 2));
            var data = AdventurerGenerator.Generate(rng, level);
            var gates = _world.Map.Gates;
            var cell = gates.Count > 0 ? gates[rng.Range(0, gates.Count)] : _world.Map.TownCenter;
            var visitor = Adventurer.Create(data, GridMap.CellToWorld(cell), transform);
            visitor.BecomeVisitor(_clock.TotalHours + StayHours);
            _visitors.Add(visitor);
            EventBus.Publish(new VisitorArrivedEvent { Visitor = visitor });
            return visitor;
        }
    }
}
