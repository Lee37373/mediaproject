using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Quests;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    public enum PartyRole { Front, Back }

    /// <summary>A party leader called a target for focus fire (P4-27).</summary>
    public struct PartyTargetCalledEvent { public Quest Quest; public Adventurer Leader; public Unit Target; }

    /// <summary>
    /// Party roles and formation (P4-27). A party is the guild members working the same (non-escort)
    /// quest; its leader is the first of them who is out on it (travelling or hunting).
    /// - Roles: warriors hold the front line, the others the back line.
    /// - Moving: followers keep a slot around the leader (front-liners ahead, back-liners behind) and
    ///   the leader waits for stragglers. Fights break the formation.
    /// - Fighting: back-liners fall back behind a front-liner while reloading; skilled+ leaders call a
    ///   target that skilled+ members focus on (marked above it).
    /// </summary>
    public class PartyTactics : MonoBehaviour
    {
        /// <summary>Slot distance ahead of / behind the leader, and spacing between neighbours.</summary>
        public const float SlotDepth = 1.5f;
        public const float SlotSpacing = 1.2f;
        const float FollowRange = 25f;
        const float StragglerDistance = 5f;
        const float CallInterval = 0.3f;
        const float RepathSeconds = 0.4f;

        class PartyState
        {
            public Vector2 Heading = Vector2.up;
            public Unit Called;
            public TargetMarker Marker;
        }

        readonly Dictionary<Quest, PartyState> _parties = new();
        readonly List<Adventurer> _members = new();
        readonly List<Quest> _stale = new();
        PoolService _pool;
        float _callTimer;

        static readonly string[] CallLines = { "저 녀석부터 치자!", "저놈을 먼저 노려!", "한 놈씩 처리하자!" };

        public void Init(PoolService pool)
        {
            _pool = pool;
            TargetMarker.RegisterPool(pool);
        }

        // ---------------- Pure rules (tested) ----------------

        public static PartyRole RoleOf(AdventurerClass cls) => cls == AdventurerClass.Warrior ? PartyRole.Front : PartyRole.Back;

        /// <summary>
        /// Offset of a follower's slot from the leader, in world space: front-liners <see cref="SlotDepth"/>
        /// ahead along the heading, back-liners behind, spread sideways by index within their role.
        /// </summary>
        public static Vector2 SlotOffset(Vector2 heading, PartyRole role, int index, int countInRole)
        {
            var forward = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector2.up;
            var right = new Vector2(forward.y, -forward.x);
            float side = (index - (countInRole - 1) * 0.5f) * SlotSpacing;
            return forward * (role == PartyRole.Front ? SlotDepth : -SlotDepth) + right * side;
        }

        /// <summary>
        /// How far from the enemy a back-liner should stand to be behind a front-liner that is
        /// <paramref name="frontDistance"/> from it: 1.5 behind, within 90% of its range. Null when the
        /// front-liner is too far forward for that (the back-liner then fights as usual).
        /// </summary>
        public static float? BackLineDistance(float frontDistance, float attackRange)
        {
            float want = frontDistance + 1.5f;
            float max = attackRange * 0.9f;
            if (max < frontDistance + 0.75f) return null;
            return Mathf.Min(want, max);
        }

        // ---------------- Party queries ----------------

        /// <summary>Quests that give formations: several members, not an escort (that has its own).</summary>
        static bool IsPartyQuest(Quest q) => q != null && q.IsActive && q.Type != QuestType.Escort && q.Assignees.Count >= 2;

        /// <summary>Out on the quest (travelling to it or working its area), so part of the formation.</summary>
        static bool IsOut(Adventurer a)
            => a != null && a.IsAlive && a.IsGuildMember && (a.Fsm.Current == a.TravelState || a.Fsm.Current == a.HuntState);

        /// <summary>Members of the adventurer's party out on the quest, leader first (acceptance order).</summary>
        public List<Adventurer> Party(Adventurer a, List<Adventurer> into)
        {
            into.Clear();
            var q = a.CurrentQuest;
            if (!IsPartyQuest(q)) return into;
            foreach (var t in q.Assignees)
                if (t is Adventurer m && m.CurrentQuest == q && IsOut(m)) into.Add(m);
            if (into.Count < 2) into.Clear();
            return into;
        }

        public Adventurer LeaderOf(Adventurer a) => Party(a, _members).Count > 0 ? _members[0] : null;

        /// <summary>The target the adventurer's party leader called (for members who follow calls), or null.</summary>
        public Unit CalledTargetFor(Adventurer a)
        {
            if (a.Tier < CombatTier.Skilled || a.CurrentQuest == null || !_parties.TryGetValue(a.CurrentQuest, out var p)) return null;
            return p.Called != null && p.Called.IsAlive ? p.Called : null;
        }

        // ---------------- Moving in formation ----------------

        /// <summary>
        /// Moves a follower to its slot around the leader. Returns false when the adventurer is the
        /// leader or not in a formation (then it moves on its own).
        /// </summary>
        public bool Follow(Adventurer a, ref float repathTimer, float dt, out Adventurer leader)
        {
            leader = null;
            if (Party(a, _members).Count == 0 || _members[0] == a) return false;
            leader = _members[0];
            if (Vector2.Distance(a.Position, leader.Position) > FollowRange) return false;

            var slot = SlotFor(a, leader, _members);
            float dist = Vector2.Distance(a.Position, slot);
            a.Mover.SpeedMultiplier = dist > StragglerDistance ? 1.3f : 1f;
            repathTimer -= dt;
            if (dist <= 0.6f || repathTimer > 0f) return true;
            repathTimer = RepathSeconds;
            if (!a.Mover.IsMoving || Vector2.Distance(a.Mover.Destination, slot) > 0.5f) a.Mover.SetDestination(slot, 0.3f);
            return true;
        }

        /// <summary>Slows the leader down while followers lag behind (or are slower). Resets for non-leaders.</summary>
        public void PaceLeader(Adventurer a)
        {
            if (Party(a, _members).Count == 0 || _members[0] != a) return;
            float slowest = float.MaxValue;
            bool straggler = false;
            for (int i = 1; i < _members.Count; i++)
            {
                var m = _members[i];
                slowest = Mathf.Min(slowest, m.Stats.MoveSpeed);
                if (Vector2.Distance(m.Position, SlotFor(m, a, _members)) > StragglerDistance) straggler = true;
            }
            float pace = Mathf.Clamp(slowest / Mathf.Max(0.1f, a.Stats.MoveSpeed), 0.6f, 1f);
            a.Mover.SpeedMultiplier = straggler ? 0.5f : pace;
        }

        Vector2 SlotFor(Adventurer a, Adventurer leader, List<Adventurer> members)
        {
            var role = RoleOf(a.Data.cls);
            int index = 0, count = 0;
            for (int i = 1; i < members.Count; i++)
            {
                if (RoleOf(members[i].Data.cls) != role) continue;
                if (members[i] == a) index = count;
                count++;
            }
            var state = StateOf(a.CurrentQuest);
            var slot = leader.Position + SlotOffset(state.Heading, role, index, Mathf.Max(1, count));
            return Services.Get<WorldService>().Grid.IsWalkable(GridMap.WorldToCell(slot)) ? slot : leader.Position;
        }

        // ---------------- Back line ----------------

        /// <summary>
        /// Where a back-liner should stand against <paramref name="target"/>: straight behind the party
        /// front-liner closest to it. Null when it already stands behind the front line, has no
        /// front-liner near the enemy, or the spot is unusable.
        /// </summary>
        public Vector2? BackLineSpot(Adventurer a, Unit target)
        {
            if (target == null || RoleOf(a.Data.cls) != PartyRole.Back || Party(a, _members).Count == 0) return null;
            Adventurer front = null;
            float frontDist = float.MaxValue;
            foreach (var m in _members)
            {
                if (m == a || RoleOf(m.Data.cls) != PartyRole.Front) continue;
                float d = Vector2.Distance(m.Position, target.Position);
                if (d < frontDist) { frontDist = d; front = m; }
            }
            if (front == null || frontDist > 6f) return null;
            if (Vector2.Distance(a.Position, target.Position) >= frontDist + 1f) return null; // already behind
            var want = BackLineDistance(frontDist, a.Stats.AttackRange);
            if (!want.HasValue) return null;
            var dir = (front.Position - target.Position).normalized;
            if (dir == Vector2.zero) return null;
            var spot = target.Position + dir * want.Value;
            var world = Services.Get<WorldService>();
            if (!world.Grid.IsWalkable(GridMap.WorldToCell(spot)) || !world.HasLineOfSight(spot, target.Position, 2)) return null;
            return spot;
        }

        // ---------------- Leader calls (focus fire) ----------------

        PartyState StateOf(Quest q)
        {
            if (!_parties.TryGetValue(q, out var s)) _parties[q] = s = new PartyState();
            return s;
        }

        void Update()
        {
            _callTimer -= Time.deltaTime;
            if (_callTimer > 0f) return;
            _callTimer = CallInterval;

            foreach (var q in _parties.Keys) if (!IsPartyQuest(q)) _stale.Add(q);
            foreach (var q in _stale) { Forget(_parties[q]); _parties.Remove(q); }
            _stale.Clear();

            if (!Services.TryGet<QuestBoard>(out var board)) return;
            foreach (var q in board.Quests)
            {
                if (!IsPartyQuest(q)) continue;
                Adventurer leader = null;
                foreach (var t in q.Assignees)
                    if (t is Adventurer m && m.CurrentQuest == q && IsOut(m)) { leader = m; break; }
                if (leader == null) continue;
                var state = StateOf(q);
                if (leader.Mover.IsMoving && leader.Mover.Velocity.sqrMagnitude > 0.01f) state.Heading = leader.Mover.Velocity.normalized;
                UpdateCall(q, state, leader);
            }
        }

        /// <summary>Skilled+ leaders call their own target; lower tiers and quiet moments call nothing.</summary>
        void UpdateCall(Quest q, PartyState state, Adventurer leader)
        {
            var target = leader.Tier >= CombatTier.Skilled ? leader.Engage.Target : null;
            if (target != null && !target.IsAlive) target = null;
            if (target == state.Called) return;
            ReleaseMarker(state);
            state.Called = target;
            if (target == null) return;
            state.Marker = TargetMarker.Show(_pool, target);
            leader.Say(CallLines[GameRandom.Shared.Range(0, CallLines.Length)]);
            EventBus.Publish(new PartyTargetCalledEvent { Quest = q, Leader = leader, Target = target });
        }

        static void Forget(PartyState s)
        {
            ReleaseMarker(s);
            s.Called = null;
        }

        /// <summary>
        /// Returns the party's marker to the pool — only if it still marks the called unit: a marker
        /// whose unit died already released itself and may now mark another party's target.
        /// </summary>
        static void ReleaseMarker(PartyState s)
        {
            if (s.Marker != null && s.Called != null && s.Marker.Unit == s.Called) s.Marker.Release();
            s.Marker = null;
        }
    }
}
