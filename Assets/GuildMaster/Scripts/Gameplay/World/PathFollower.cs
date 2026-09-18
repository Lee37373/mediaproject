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

        public event Action Arrived;
        public event Action PathFailed;

        WorldService World => _world ??= Services.Get<WorldService>();

        public bool SetDestination(Vector2 target, float stopDistance = 0.05f)
        {
            _destination = target;
            _stopDistance = Mathf.Max(0.05f, stopDistance);
            if (!BuildPath())
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
            if (!World.FindPath(from, _destination, _cells)) return false;

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

        void Update()
        {
            if (!IsMoving) return;

            if (_gridVersion != World.Grid.Version)
            {
                _gridVersion = World.Grid.Version;
                if (_index < _waypoints.Count && !World.Grid.IsWalkable(GridMap.WorldToCell(_waypoints[_index])))
                {
                    if (!BuildPath()) { Stop(); PathFailed?.Invoke(); return; }
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
