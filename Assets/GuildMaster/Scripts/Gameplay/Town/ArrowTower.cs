using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Units;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>An arrow tower brought a monster down (P4-113).</summary>
    public struct ArrowTowerKillEvent { public Building Tower; public Unit Victim; }

    /// <summary>
    /// An arrow tower (P4-113, H10): a building outside the walls that shoots the nearest threatening monster (<see cref="IsThreat"/>)
    /// the guild can see within its range (<see cref="DefenceRules"/>, by its level), over its own roof and through a few trees, while it stands. It
    /// also gives the guild sight round it, over walls (it stands high). Nobody mans it (user decision): a raid marching on
    /// the nearest building comes at it first, and a fallen tower shoots no more until it is stood up again.
    /// </summary>
    [RequireComponent(typeof(Building))]
    public class ArrowTower : MonoBehaviour
    {
        /// <summary>Seconds between looks for a target when there was none.</summary>
        const float LookSeconds = 0.25f;

        Building _building;
        VisionSource _vision;
        float _cooldown;

        public Building Building => _building;

        public void Init(Building building)
        {
            _building = building;
            _vision = GetComponent<VisionSource>();
            if (_vision == null)
            {
                _vision = gameObject.AddComponent<VisionSource>();
                _vision.SeesOverWalls = true;
            }
            FitToLevel();
            EventBus.Subscribe<BuildingLevelChangedEvent>(OnLevel);
        }

        void OnDestroy() => EventBus.Unsubscribe<BuildingLevelChangedEvent>(OnLevel);

        void OnLevel(BuildingLevelChangedEvent e)
        {
            if (e.Building == _building) FitToLevel();
        }

        void FitToLevel()
        {
            if (_vision != null) _vision.SightRadius = DefenceRules.TowerSight(_building.Level);
        }

        void Update()
        {
            if (_building == null || _building.IsDestroyed) return;
            _cooldown -= Time.deltaTime;
            if (_cooldown > 0f) return;
            var target = PickTarget();
            if (target == null)
            {
                _cooldown = LookSeconds;
                return;
            }
            Shoot(target);
            _cooldown = DefenceRules.TowerCooldown(_building.Level);
        }

        /// <summary>Where a shot at this point leaves the tower: just outside its walls on that side.</summary>
        Vector2 Muzzle(Vector2 toward)
        {
            var edge = _building.ClosestPoint(toward);
            var away = toward - edge;
            return away.sqrMagnitude < 1e-4f ? edge : edge + away.normalized * 0.6f;
        }

        /// <summary>The nearest monster the guild can see, in range and in the open from the tower.</summary>
        public Unit PickTarget()
        {
            if (!Services.TryGet<WorldService>(out var world)) return null;
            Services.TryGet<FogOfWar>(out var fog);
            var from = _building.Center;
            float reach = DefenceRules.TowerRange(_building.Level) + _building.Footprint.width * 0.5f;
            return UnitRegistry.FindNearest(from, reach, u => u != null && u.IsAlive && u.Faction == Faction.Monster && IsThreat(u)
                                                             && (fog == null || fog.IsVisible(u.Position))
                                                             && world.HasLineOfSight(Muzzle(u.Position), u.Position, 3));
        }

        /// <summary>
        /// What a tower shoots: anything out of its den — raiders, a horde, a pack on the move or with no home, the dragon — and
        /// a den's own only while it fights the guild. A den minding its own business by the walls is left to the guild's
        /// quests (the first tower by the south gate emptied the slime den the first quest was for).
        /// </summary>
        public static bool IsThreat(Unit u)
        {
            if (u is not Monsters.Monster m) return true;
            if (!m.IsDefendingTerritory) return true;
            var t = m.Engage.Target;
            return t != null && t.IsAlive && (t.Faction == Faction.Guild || t.ExposedToMonsters);
        }

        void Shoot(Unit target)
        {
            if (!Services.TryGet<ProjectileService>(out var projectiles)) return;
            float raw = DefenceRules.TowerDamage(_building.Level);
            var spec = ProjectileSpec.Of(Visuals.ShotLook.TowerArrow);
            spec.TreeBlockChance = DefenceRules.TowerTreeBlock;
            spec.Launcher = _building.Footprint;
            var tower = _building;
            spec.OnImpact = (_, hit) => Hit(tower, hit, raw);
            projectiles.LaunchFrom(_building.Center + new Vector2(0f, 0.8f), target, spec);
        }

        /// <summary>An arrow lands: the target's defence takes its share, as against any blow; nobody gets the kill.</summary>
        static void Hit(Building tower, Unit hit, float raw)
        {
            if (hit == null || !hit.IsAlive) return;
            hit.ReceiveDamage(Combat.Mitigate(raw, hit.EffectiveDefense), null, false);
            if (!hit.IsAlive) EventBus.Publish(new ArrowTowerKillEvent { Tower = tower, Victim = hit });
        }
    }
}
