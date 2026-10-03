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
        /// <summary>
        /// Farthest from the leader a follower picks a fight or a pickup (P4-38, user report
        /// 2026-09-21: "after a fight they wander off alone"). Whatever attacks it is still fought.
        /// </summary>
        public const float Leash = 10f;
        /// <summary>The leader stops and waits while a member is farther than this.</summary>
        public const float RegroupDistance = 7f;
        const float CallInterval = 0.3f;
        const float RepathSeconds = 0.4f;

        class PartyState
        {
            public Vector2 Heading = Vector2.up;
            public Unit Called;
            public TargetMarker Marker;
        }

        /// <summary>Keyed by what holds the party together: a Quest, or a HuntBand (P4-37).</summary>
        readonly Dictionary<object, PartyState> _parties = new();
        readonly List<Adventurer> _members = new();
        readonly List<object> _stale = new();
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

        /// <summary>
        /// Out on the quest (travelling to it or working its area), hunting, or on the way home
        /// (P4-71: a party walks home together too, not on a lure run), so part of the formation.
        /// </summary>
        static bool IsOut(Adventurer a)
            => a != null && a.IsAlive && a.IsGuildMember
               && (a.Fsm.Current == a.TravelState || a.Fsm.Current == a.HuntState || a.Fsm.Current == a.FreeHuntState
                   || a.Fsm.Current == a.ReturnState && !a.ReturnState.Luring);

        /// <summary>
        /// What holds this adventurer's party together: its quest, the hunting band it went out
        /// with (P4-37, also while it walks back from a party retreat, P4-71), or on the way home its
        /// standing party (P4-50). Null when it is on its own.
        /// </summary>
        static object GroupOf(Adventurer a)
        {
            if (IsPartyQuest(a.CurrentQuest)) return a.CurrentQuest;
            var s = a.Fsm.Current;
            if (s != a.FreeHuntState && s != a.ReturnState) return null;
            if (Services.TryGet<HuntBandService>(out var bands) && bands.BandOf(a) is { } band && band.Members.Count >= 2) return band;
            if (s == a.ReturnState && Services.TryGet<GuildParties>(out var parties)) return parties.PartyOf(a);
            return null;
        }

        /// <summary>Members of the adventurer's party who are out, leader first (joining order).</summary>
        public List<Adventurer> Party(Adventurer a, List<Adventurer> into)
        {
            into.Clear();
            var group = GroupOf(a);
            switch (group)
            {
                case Quest q:
                    foreach (var t in q.Assignees)
                        if (t is Adventurer m && m.CurrentQuest == q && IsOut(m)) into.Add(m);
                    break;
                case HuntBand band:
                    foreach (var m in band.Members) if (IsOut(m) && GroupOf(m) == group) into.Add(m);
                    break;
                case Party party:
                    foreach (var m in party.Members) if (IsOut(m) && GroupOf(m) == group) into.Add(m);
                    break;
            }
            if (into.Count < 2) into.Clear();
            return into;
        }

        public Adventurer LeaderOf(Adventurer a) => Party(a, _members).Count > 0 ? _members[0] : null;

        /// <summary>The target the adventurer's party leader called (for members who follow calls), or null.</summary>
        public Unit CalledTargetFor(Adventurer a)
        {
            var group = GroupOf(a);
            if (a.Tier < CombatTier.Skilled || group == null || !_parties.TryGetValue(group, out var p)) return null;
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
            // Fallen far behind (a long chase, a detour): head straight for the leader instead of
            // walking on alone (P4-38 — this used to hand the follower back to its own roaming).
            var slot = Vector2.Distance(a.Position, leader.Position) > FollowRange ? leader.Position : SlotFor(a, leader, _members);
            float dist = Vector2.Distance(a.Position, slot);
            var leaderVelocity = leader.Mover.IsMoving ? leader.Mover.Velocity : Vector2.zero;
            // Compare walking speeds before terrain: both cross the same ground, and the leader's
            // velocity on a road would read faster than the follower's plain speed.
            float leaderSpeed = leaderVelocity.sqrMagnitude < 0.01f ? 0f
                : leader.Mover.Speed * leader.Mover.SpeedMultiplier * leader.Mover.BuffMultiplier;
            float behind = leaderVelocity.sqrMagnitude < 0.01f ? dist : Vector2.Dot(slot - a.Position, leaderVelocity.normalized);
            a.Mover.SpeedMultiplier = FollowerPace(dist, behind, leaderSpeed, a.Mover.Speed * a.Mover.BuffMultiplier);
            // A walking leader's slot keeps moving: aim where it will be by the next repath, or a
            // quicker follower gets there first, stops, and waits — stop-and-go (user report 2026-09-22).
            if (dist <= StragglerDistance) slot += leaderVelocity * (RepathSeconds * 2f);
            repathTimer -= dt;
            if (dist <= 0.6f && leaderVelocity.sqrMagnitude < 0.01f) return true;
            // Stopped short of a moving slot: repath almost at once (but not every frame, in case
            // the path keeps failing).
            if (repathTimer > (a.Mover.IsMoving ? 0f : RepathSeconds * 0.75f)) return true;
            repathTimer = RepathSeconds;
            if (!a.Mover.IsMoving || Vector2.Distance(a.Mover.Destination, slot) > 0.5f) a.Mover.SetDestination(slot, 0.3f);
            return true;
        }

        /// <summary>
        /// Speed multiplier for a follower: sprint when straggling; otherwise match the leader's
        /// walking speed, a little faster while behind the slot and slower while ahead of it
        /// (<paramref name="behindSlot"/> is measured along the leader's heading, negative when
        /// ahead), so a quick member keeps step instead of rushing ahead and stopping.
        /// </summary>
        public static float FollowerPace(float distToSlot, float behindSlot, float leaderSpeed, float ownSpeed)
        {
            if (distToSlot > StragglerDistance) return 1.3f;
            if (leaderSpeed < 0.1f || ownSpeed < 0.1f) return 1f;
            return Mathf.Clamp((leaderSpeed + 0.6f * behindSlot) / ownSpeed, 0.3f, 1.3f);
        }

        /// <summary>
        /// Slows the leader down while followers lag behind (or are slower), and stops it outright
        /// while one is farther than <see cref="RegroupDistance"/> — still fighting, picking up loot,
        /// catching up — so the party moves on together (P4-38). Returns true while the leader
        /// waits: the caller then skips its own movement. Always false for non-leaders.
        /// </summary>
        public bool PaceLeader(Adventurer a)
        {
            if (Party(a, _members).Count == 0 || _members[0] != a) return false;
            float slowest = float.MaxValue;
            bool straggler = false;
            Adventurer farthest = null;
            float farthestDist = RegroupDistance;
            for (int i = 1; i < _members.Count; i++)
            {
                var m = _members[i];
                slowest = Mathf.Min(slowest, m.Stats.MoveSpeed);
                if (Vector2.Distance(m.Position, SlotFor(m, a, _members)) > StragglerDistance) straggler = true;
                float d = Vector2.Distance(m.Position, a.Position);
                if (d > farthestDist) { farthestDist = d; farthest = m; }
            }
            if (farthest != null)
            {
                // A member still fighting out there gets help, not an audience: the leader walks
                // over and the formation brings the rest along. Otherwise it simply waits.
                if (farthest.Engage.Target != null)
                {
                    a.Mover.SpeedMultiplier = 1f;
                    if (!a.Mover.IsMoving || Vector2.Distance(a.Mover.Destination, farthest.Position) > 2f)
                        a.Mover.SetDestination(farthest.Position, 2f);
                }
                else if (a.Mover.IsMoving) a.Mover.Stop();
                return true;
            }
            float pace = Mathf.Clamp(slowest / Mathf.Max(0.1f, a.Stats.MoveSpeed), 0.6f, 1f);
            a.Mover.SpeedMultiplier = straggler ? 0.5f : pace;
            return false;
        }

        /// <summary>
        /// Where a follower's leash is tied: its leader's position, or null for a leader or someone
        /// out on its own (no leash). See <see cref="Leash"/>.
        /// </summary>
        public Vector2? LeashCentre(Adventurer a)
        {
            if (Party(a, _members).Count == 0 || _members[0] == a) return null;
            return _members[0].Position;
        }

        /// <summary>Whether a point is within the leash of <paramref name="centre"/> (always, with no leash).</summary>
        public static bool InLeash(Vector2? centre, Vector2 point)
            => centre == null || Vector2.Distance(centre.Value, point) <= Leash;

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
            var group = GroupOf(a);
            var heading = group != null ? StateOf(group).Heading : Vector2.up;
            var slot = leader.Position + SlotOffset(heading, role, index, Mathf.Max(1, count));
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

        PartyState StateOf(object group)
        {
            if (!_parties.TryGetValue(group, out var s)) _parties[group] = s = new PartyState();
            return s;
        }

        static bool StillAParty(object group) => group switch
        {
            Quest q => IsPartyQuest(q),
            HuntBand b => b.Members.Count >= 2,
            Party p => p.Members.Count >= 2,
            _ => false,
        };

        void Update()
        {
            _callTimer -= Time.deltaTime;
            if (_callTimer > 0f) return;
            _callTimer = CallInterval;

            foreach (var group in _parties.Keys) if (!StillAParty(group)) _stale.Add(group);
            foreach (var group in _stale) { Forget(_parties[group]); _parties.Remove(group); }
            _stale.Clear();

            if (Services.TryGet<QuestBoard>(out var board))
                foreach (var q in board.Quests)
                    if (IsPartyQuest(q)) Track(q, LeaderOfQuest(q));
            if (Services.TryGet<HuntBandService>(out var bands))
                foreach (var band in bands.Bands)
                    if (band.Members.Count >= 2) Track(band, LeaderOfBand(band));
            if (Services.TryGet<GuildParties>(out var standing))
                foreach (var p in standing.Parties)
                    if (p.Members.Count >= 2) Track(p, LeaderOfParty(p));
        }

        static Adventurer LeaderOfQuest(Quest q)
        {
            foreach (var t in q.Assignees)
                if (t is Adventurer m && m.CurrentQuest == q && IsOut(m)) return m;
            return null;
        }

        static Adventurer LeaderOfBand(HuntBand band)
        {
            foreach (var m in band.Members) if (IsOut(m)) return m;
            return null;
        }

        /// <summary>A standing party walking home together (P4-71): its first member on the way.</summary>
        static Adventurer LeaderOfParty(Party p)
        {
            foreach (var m in p.Members) if (IsOut(m) && GroupOf(m) == p) return m;
            return null;
        }

        /// <summary>Keeps the party's heading current and refreshes the leader's call.</summary>
        void Track(object group, Adventurer leader)
        {
            if (leader == null) return;
            var state = StateOf(group);
            if (leader.Mover.IsMoving && leader.Mover.Velocity.sqrMagnitude > 0.01f) state.Heading = leader.Mover.Velocity.normalized;
            UpdateCall(group, state, leader);
        }

        /// <summary>Skilled+ leaders call their own target; lower tiers and quiet moments call nothing.</summary>
        void UpdateCall(object group, PartyState state, Adventurer leader)
        {
            var target = leader.Tier >= CombatTier.Skilled ? leader.Engage.Target : null;
            if (target != null && !target.IsAlive) target = null;
            if (target == state.Called) return;
            ReleaseMarker(state);
            state.Called = target;
            if (target == null) return;
            state.Marker = TargetMarker.Show(_pool, target);
            leader.Say(CallLines[GameRandom.Shared.Range(0, CallLines.Length)]);
            EventBus.Publish(new PartyTargetCalledEvent { Quest = group as Quest, Leader = leader, Target = target });
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
