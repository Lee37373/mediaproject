using System;
using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// Moves a transform along an A* path with terrain-dependent speed, lookahead
    /// smoothing, and automatic repath when the grid changes.
    /// </summary>
    public class PathFollower : MonoBehaviour
    {
        [SerializeField] float speed = 3f;

        readonly List<Vector2Int> _cells = new();
        readonly List<Vector2> _waypoints = new();
        int _index;
        Vector2 _destination;
        float _stopDistance;
        int _gridVersion;
        WorldService _world;

        public float Speed { get => speed; set => speed = Mathf.Max(0f, value); }
        public float SpeedMultiplier { get; set; } = 1f;
        /// <summary>Status-effect multiplier (buffs/slows), separate from behaviour multipliers.</summary>
        public float BuffMultiplier { get; set; } = 1f;
        public bool IsMoving { get; private set; }
        public Vector2 Destination => _destination;
        public Vector2 Velocity { get; private set; }
        public bool FacingLeft { get; private set; }
        /// <summary>Whether intact town gates open for this walker (P4-39): false for monsters.</summary>
        public bool PassesGates { get; set; } = true;
        /// <summary>Cells this walker takes to be shut though they are open: a broken gate its kind has not seen (monsters).</summary>
        public Func<int, int, bool> BelievedShut { get; set; }

        /// <summary>
        /// Cells this walker has never seen (P4-64, free exploration). Set it and the route is drawn
        /// from what is known — unseen ground is planned as open grass — and redrawn as soon as the
        /// walker sees that the plan runs into water, a mountain or a wall. Null (the default) plans
        /// with the whole map, as a walker that knows the country does.
        /// </summary>
        public Func<int, int, bool> UnknownToMe { get; set; }

        /// <summary>How many times terrain it discovered made this walker redraw its route.</summary>
        public int RouteCorrections { get; private set; }

        /// <summary>Seconds between checks of the plan against what has been seen since.</summary>
        const float RecheckSeconds = 0.25f;
        /// <summary>How far along the plan a correction looks (further ahead is still guesswork).</summary>
        const int RecheckWaypoints = 12;
        float _recheck;

        bool Passable(Vector2Int c) => World.Grid.IsWalkable(c, PassesGates) && (BelievedShut == null || !BelievedShut(c.x, c.y));

        public event Action Arrived;
        public event Action PathFailed;

        WorldService World => _world ??= Services.Get<WorldService>();

        /// <summary>
        /// How this walk weighs risky ground (P4-63): set by travelling states from the party's
        /// decision (<c>GuildMaster.Units.RouteChoice</c>) and kept for the repaths of this walk.
        /// Every other call leaves it at 0 and takes the shortest way, as fighting and looting do.
        /// </summary>
        public float RouteCaution { get; private set; }

        /// <summary>
        /// Walks to <paramref name="target"/>. <paramref name="caution"/> (P4-63) makes the route
        /// avoid cover (above 0) or seek it (below 0); the default 0 is the shortest path.
        /// </summary>
        static readonly Unity.Profiling.ProfilerMarker s_setDestinationMarker = new("Mover.SetDestination");

        public bool SetDestination(Vector2 target, float stopDistance = 0.05f, float caution = 0f)
        {
            _destination = target;
            _stopDistance = Mathf.Max(0.05f, stopDistance);
            RouteCaution = caution;
            _steerFrame = -10;
            bool built;
            using (s_setDestinationMarker.Auto()) built = BuildPath();
            if (!built)
            {
                IsMoving = false;
                PathFailed?.Invoke();
                return false;
            }
            IsMoving = true;
            return true;
        }

        public void Stop()
        {
            IsMoving = false;
            Velocity = Vector2.zero;
            _waypoints.Clear();
            _steerVelocity = Vector2.zero;
            _steerFrame = -10;
        }

        // ---------------- Local steering (P4-47) ----------------

        /// <summary>Speed change per second while steering, as a multiple of the top speed.</summary>
        const float SteerAcceleration = 5f;
        Vector2 _steerVelocity;
        int _steerFrame = -10;

        bool SteeredLately => Time.frameCount - _steerFrame <= 1;

        /// <summary>Moving under <see cref="Steer"/> (no path): walks on screen like <see cref="IsMoving"/>.</summary>
        public bool IsSteering => SteeredLately && _steerVelocity.sqrMagnitude > 0.01f;

        /// <summary>
        /// Moves by a velocity instead of along a path (close-quarters footwork, P4-47): drops any path
        /// and turns with limited acceleration towards <paramref name="direction"/> (length 0..1, zero
        /// brakes) × walking speed × <paramref name="speedFactor"/>. Never enters unwalkable cells.
        /// Call every frame while it should last.
        /// </summary>
        public void Steer(Vector2 direction, float speedFactor, float dt)
        {
            if (IsMoving) { IsMoving = false; _waypoints.Clear(); }
            if (!SteeredLately) _steerVelocity = Vector2.zero;
            _steerFrame = Time.frameCount;

            Vector2 pos = transform.position;
            var cell = GridMap.WorldToCell(pos);
            float cost = World.Grid.InBounds(cell) ? World.Grid.Cost(cell) : 1f;
            if (float.IsInfinity(cost)) cost = 1f;
            float top = speed * SpeedMultiplier * BuffMultiplier / cost * speedFactor;
            var desired = Vector2.ClampMagnitude(direction, 1f) * top;
            _steerVelocity = Vector2.MoveTowards(_steerVelocity, desired, top * SteerAcceleration * dt);

            var next = pos + _steerVelocity * dt;
            if (!Passable(GridMap.WorldToCell(next)))
            {
                var nx = pos + new Vector2(_steerVelocity.x * dt, 0f);
                var ny = pos + new Vector2(0f, _steerVelocity.y * dt);
                if (Passable(GridMap.WorldToCell(nx))) { next = nx; _steerVelocity.y = 0f; }
                else if (Passable(GridMap.WorldToCell(ny))) { next = ny; _steerVelocity.x = 0f; }
                else { next = pos; _steerVelocity = Vector2.zero; }
            }
            Velocity = _steerVelocity;
            if (Mathf.Abs(_steerVelocity.x) > 0.05f) FacingLeft = _steerVelocity.x < 0f;
            transform.position = new Vector3(next.x, next.y, transform.position.z);
        }

        /// <summary>Remaining path distance estimate (straight segments).</summary>
        public float RemainingDistance()
        {
            if (!IsMoving || _index >= _waypoints.Count) return 0f;
            float d = Vector2.Distance(transform.position, _waypoints[_index]);
            for (int i = _index; i < _waypoints.Count - 1; i++) d += Vector2.Distance(_waypoints[i], _waypoints[i + 1]);
            return d;
        }

        bool BuildPath()
        {
            _waypoints.Clear();
            _index = 0;
            Vector2 from = transform.position;
            if (!World.FindPath(from, _destination, _cells, PassesGates, BelievedShut, RouteCaution, UnknownToMe)) return false;

            for (int i = 1; i < _cells.Count; i++) _waypoints.Add(GridMap.CellToWorld(_cells[i]));
            // End exactly at the requested point if it is inside the last cell.
            var destCell = GridMap.WorldToCell(_destination);
            if (_cells.Count > 0 && _cells[^1] == destCell)
            {
                if (_waypoints.Count > 0) _waypoints[^1] = _destination;
                else _waypoints.Add(_destination);
            }
            _gridVersion = World.Grid.Version;
            return true;
        }

        /// <summary>
        /// True when a cell on the near part of the plan is one the walker has since seen, and it is
        /// not walkable after all: the route crossed ground nobody had looked at (P4-64).
        /// </summary>
        bool SeenTerrainBlocksThePlan()
        {
            int last = Mathf.Min(_waypoints.Count - 1, _index + RecheckWaypoints);
            for (int i = _index; i <= last; i++)
            {
                var c = GridMap.WorldToCell(_waypoints[i]);
                if (!UnknownToMe(c.x, c.y) && !Passable(c)) return true;
            }
            return false;
        }

        /// <summary>
        /// The map changed and a cell still ahead on the plan cannot be walked any more: a gate shut
        /// again, a building put up. Only the next waypoint used to be checked (P4-76), so a walker
        /// whose way was closed further on walked straight through the repaired gate or the new tower.
        /// Cells the walker has never seen are the plan's own guess (P4-64) and are left to it.
        /// </summary>
        bool PlanBlocked()
        {
            for (int i = _index; i < _waypoints.Count; i++)
            {
                var c = GridMap.WorldToCell(_waypoints[i]);
                if (UnknownToMe != null && UnknownToMe(c.x, c.y)) continue;
                if (!Passable(c)) return true;
            }
            return false;
        }

        void Update()
        {
            if (!IsMoving) return;

            if (_gridVersion != World.Grid.Version)
            {
                _gridVersion = World.Grid.Version;
                if (PlanBlocked())
                {
                    if (!BuildPath()) { Stop(); PathFailed?.Invoke(); return; }
                }
            }

            // Walking on a plan drawn over unseen ground (P4-64): the moment the walker sees that the
            // way ahead is water, rock or wall, it draws a new route from where it stands.
            if (UnknownToMe != null)
            {
                _recheck -= Time.deltaTime;
                if (_recheck <= 0f)
                {
                    _recheck = RecheckSeconds;
                    if (SeenTerrainBlocksThePlan())
                    {
                        RouteCorrections++;
                        if (!BuildPath()) { Stop(); PathFailed?.Invoke(); return; }
                    }
                }
            }

            Vector2 pos = transform.position;
            if (_index >= _waypoints.Count || Vector2.Distance(pos, _destination) <= _stopDistance)
            {
                Finish();
                return;
            }

            // Lookahead smoothing: skip intermediate waypoints with a clear straight line.
            for (int look = Mathf.Min(_waypoints.Count - 1, _index + 4); look > _index; look--)
            {
                if (World.HasClearLine(pos, _waypoints[look])) { _index = look; break; }
            }

            var cell = GridMap.WorldToCell(pos);
            float cost = World.Grid.InBounds(cell) ? World.Grid.Cost(cell) : 1f;
            if (float.IsInfinity(cost)) cost = 1f;
            float step = speed * SpeedMultiplier * BuffMultiplier / cost * Time.deltaTime;

            Vector2 target = _waypoints[_index];
            Vector2 next = Vector2.MoveTowards(pos, target, step);
            // Never into ground that cannot be walked (P4-80): a plan over unseen cells (P4-64) walked into
            // rock before the fog showed it (the fog looks every tenth of a real second, two or three
            // cells at ×8), and from inside the rock no path led out. Back to the middle of its own cell
            // instead — from there the plan's diagonal steps clip no corner — and look again at once.
            var nextCell = GridMap.WorldToCell(next);
            if (nextCell != cell && !Passable(nextCell) && Passable(cell))
            {
                next = Vector2.MoveTowards(pos, GridMap.CellToWorld(cell), step);
                _recheck = 0f;
            }
            Velocity = (next - pos) / Mathf.Max(Time.deltaTime, 1e-5f);
            if (Mathf.Abs(next.x - pos.x) > 0.0001f) FacingLeft = next.x < pos.x;
            transform.position = new Vector3(next.x, next.y, transform.position.z);

            if ((next - target).sqrMagnitude < 0.0004f)
            {
                _index++;
                if (_index >= _waypoints.Count) Finish();
            }
        }

        void Finish()
        {
            IsMoving = false;
            Velocity = Vector2.zero;
            Arrived?.Invoke();
        }

        void OnDisable() => Stop();
    }
}
