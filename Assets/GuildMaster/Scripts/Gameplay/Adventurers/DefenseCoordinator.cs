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
                if (!CanDefend(a) || Vector2.Distance(a.Position, center) > _radius) continue;
                a.ChangeState(a.DefendState);
            }
        }

        bool CanDefend(Adventurer a)
        {
            if (a == null || !a.IsAlive || a.IsVisitor) return false;
            var s = a.Fsm.Current;
            if (s == a.DefendState || s == a.DeadState || s == a.RestState) return false;
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
                else a.ChangeState(a.IdleState);
            }
        }
    }
}
