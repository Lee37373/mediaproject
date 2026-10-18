using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Quests;
using GuildMaster.Town;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Calls guild members to arms once a raid has been sighted (P3-6): everyone fit to fight within the defend
    /// radius of the town — escorts included (user decision) — switches to <see cref="DefendState"/>;
    /// members coming back into range mid-raid join too. When the raid ends they resume their quest,
    /// rest if badly hurt, or go back to idling.
    /// </summary>
    public class DefenseCoordinator : MonoBehaviour
    {
        const float CheckInterval = 1f;
        const float MinHpToDefend = 0.5f;

        WaveManager _waves;
        GuildRoster _roster;
        WorldService _world;
        float _radius;
        float _timer;

        public void Init(WaveManager waves, GuildRoster roster, WorldService world, float radius)
        {
            _waves = waves;
            _roster = roster;
            _world = world;
            _radius = radius;
            EventBus.Subscribe<RaidSightedEvent>(OnSighted);
            EventBus.Subscribe<RaidEndedEvent>(OnEnded);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<RaidSightedEvent>(OnSighted);
            EventBus.Unsubscribe<RaidEndedEvent>(OnEnded);
        }

        void OnSighted(RaidSightedEvent e) => CallToArms();

        void Update()
        {
            if (_waves == null || !_waves.RaidSighted) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = CheckInterval;
            CallToArms();
        }

        void CallToArms()
        {
            if (!_waves.RaidSighted) return;
            var center = _world.TownCenterWorld;
            foreach (var a in _roster.Members)
            {
                // Down in a ruin (P4-85), as far as its way down: the dungeon's coordinates are no measure of the walk.
                if (!CanDefend(a) || Vector2.Distance(_world.OnTheSurface(a.Position), center) > _radius) continue;
                a.ChangeState(a.DefendState);
            }
            AssignFronts();
        }

        /// <summary>
        /// Shares the defenders out over the raids' fronts by raid size, nearest first, at least one
        /// each while there are enough (user report 2026-09-22: with two raids at once only the
        /// latest one was met).
        /// </summary>
        void AssignFronts()
        {
            var fronts = _waves.Fronts;
            if (fronts.Count == 0) return;
            var defenders = new List<Adventurer>();
            foreach (var a in _roster.Members)
                if (a != null && a.IsAlive && a.Fsm.Current == a.DefendState) defenders.Add(a);
            if (defenders.Count == 0) return;

            int total = fronts.Sum(f => Mathf.Max(1, f.Strength));
            var quota = new int[fronts.Count];
            for (int i = 0; i < fronts.Count; i++)
                quota[i] = Mathf.Max(1, Mathf.RoundToInt(defenders.Count * Mathf.Max(1, fronts[i].Strength) / (float)total));

            var pairs = new List<(Adventurer a, int f, float d)>();
            foreach (var a in defenders)
                for (int i = 0; i < fronts.Count; i++)
                    pairs.Add((a, i, Vector2.Distance(a.Position, fronts[i].Point)));
            var assigned = new HashSet<Adventurer>();
            foreach (var (a, f, _) in pairs.OrderBy(p => p.d))
            {
                if (assigned.Contains(a) || quota[f] <= 0) continue;
                quota[f]--;
                assigned.Add(a);
                a.DefendState.Front = fronts[f].Point;
            }
            // Rounding left someone over: the nearest front.
            foreach (var a in defenders)
                if (!assigned.Contains(a))
                    a.DefendState.Front = fronts.OrderBy(f => Vector2.Distance(a.Position, f.Point)).First().Point;
        }

        bool CanDefend(Adventurer a)
        {
            if (a == null || !a.IsAlive || a.IsVisitor) return false;
            var s = a.Fsm.Current;
            if (s == a.DefendState || s == a.DeadState) return false;
            // Resting out of weariness in town is no reason to sit out a raid on the town (user
            // report 2026-09-22: only one member fought while the others slept it off).
            if (s == a.RestState && !_world.Grid.IsInTown(a.Position)) return false;
            if (s == a.ReturnState && a.ReturnState.Retreating) return false; // badly hurt, heading home
            return a.HpRatio >= MinHpToDefend;
        }

        void OnEnded(RaidEndedEvent e)
        {
            // Another raid the guild knows about is still going on: keep defending.
            if (_waves.RaidSighted) return;
            foreach (var a in _roster.Members)
            {
                if (a == null || !a.IsAlive || a.Fsm.Current != a.DefendState) continue;
                a.Engage.Clear();
                var q = a.CurrentQuest;
                if (a.HpRatio < MinHpToDefend) a.ChangeState(a.RestState);
                else if (q != null && q.Status == QuestStatus.Completed) a.ChangeState(a.VisitState);
                else if (q != null && q.IsActive && q.IsAssigned(a)) a.ChangeState(a.TravelState);
                // Called in from the field (P4-76): report at the guild first — sell the loot, join
                // the party's split. Straight to idling, an outing never counted them back.
                else if (a.Data.LootCount > 0 || BackFromAnOuting(a)) a.ChangeState(a.VisitState);
                else a.ChangeState(a.IdleState);
            }
        }

        static bool BackFromAnOuting(Adventurer a)
            => Services.TryGet<GuildParties>(out var parties) && parties.PartyOf(a) is { } p && p.WentOut(a) && !p.Arrived.Contains(a);
    }
}
