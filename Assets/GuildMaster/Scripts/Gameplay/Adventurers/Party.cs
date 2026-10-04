using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>A party was formed, grew, or lost a member (UI, log).</summary>
    public struct PartyChangedEvent { public Party Party; public Adventurer Member; public bool Joined; public string Reason; }

    /// <summary>What one member did on the current outing (the split weighs it).</summary>
    public class OutingRecord
    {
        public float Damage;
        /// <summary>Damage soaked from the enemy: holding the front line counts too (user request 2026-09-22).</summary>
        public float Taken;
        public float Healing;
        public int Kills;
        public int Deposited;
        public float Weight => Damage + Taken + Healing + Kills * 10f;
    }

    /// <summary>
    /// A standing party (P4-50, user decision 2026-09-22): formed in town by a leader, it goes out
    /// together (a quest or a free hunt), pools the gold it brings back, and at the guild the leader
    /// splits it (<see cref="PartySettlement"/>). It stays together over several outings; a member
    /// unhappy with the split, or on bad terms with another, leaves, and a party that does not go out
    /// again within <see cref="GuildParties.DisbandIdleHours"/> of a split breaks up (P4-71). Members are in joining order,
    /// the first leads; when the leader falls the next one takes over.
    /// </summary>
    public class Party
    {
        static int s_next;

        public int Id { get; internal set; } = ++s_next;

        /// <summary>The next id (saved, P5-4).</summary>
        internal static int NextId { get => s_next; set => s_next = value; }
        public readonly List<Adventurer> Members = new();
        public Adventurer Leader => Members.Count > 0 ? Members[0] : null;
        public string Name => Leader != null ? $"{Leader.Data.name}의 파티" : "파티";

        // ---- The current outing ----
        public bool OnOuting { get; internal set; }
        /// <summary>Who set out this time (a member left in town is not owed a share).</summary>
        public readonly Dictionary<Adventurer, OutingRecord> Out = new();
        /// <summary>Gold brought back: quest rewards and loot sold at the guild.</summary>
        public int Pool { get; internal set; }
        /// <summary>Members back at the guild, waiting for the split.</summary>
        public readonly HashSet<Adventurer> Arrived = new();
        /// <summary>Game hour each came back (the wait by the guild is limited, the share is not).</summary>
        public readonly Dictionary<Adventurer, float> ArrivedAt = new();
        public float FirstArrivalHour { get; internal set; } = -1f;
        public bool Settling { get; internal set; }
        /// <summary>Game hour the last outing was settled; the party breaks up if it does not go out again soon (P4-71).</summary>
        public float SettledAtHour { get; internal set; } = -1f;

        public bool Has(Adventurer a) => Members.Contains(a);
        public bool WentOut(Adventurer a) => OnOuting && Out.ContainsKey(a);
    }

    /// <summary>
    /// Keeps the standing parties (P4-50): forming and merging them, the outing bookkeeping
    /// (contribution from combat events, the gold pool, who is back) and starting the split once
    /// everyone who went out is back at the guild.
    /// </summary>
    public class GuildParties : MonoBehaviour, ISaveable, ISaveLink
    {
        /// <summary>Game hours a member back waits by the guild for the rest; after that it goes about town, still owed its share.</summary>
        public const float WaitForRestHours = 2f;
        /// <summary>Game hours after the first one is back before the split goes ahead without the missing.</summary>
        public const float GiveUpOnMissingHours = 12f;
        /// <summary>
        /// Game hours after a split before a party that has not gone out together again breaks up
        /// (P4-71, user decision 2026-10-03: "it never breaks up once back in town").
        /// </summary>
        public const float DisbandIdleHours = 6f;

        readonly List<Party> _parties = new();
        readonly Dictionary<Adventurer, Party> _of = new();
        GameClock _clock;
        PartySettlement _settlement;

        public IReadOnlyList<Party> Parties => _parties;

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SavedRecord
        {
            public string member;
            public float damage;
            public float taken;
            public float healing;
            public int kills;
            public int deposited;
        }

        [System.Serializable]
        class SavedParty
        {
            public int id;
            public List<string> members = new();
            public bool on_outing;
            public List<SavedRecord> out_records = new();
            public int pool;
            public List<string> arrived = new();
            public List<float> arrived_at = new();
            public float first_arrival;
            public float settled_at;
        }

        [System.Serializable]
        class SaveData
        {
            public int next_id;
            public List<SavedParty> parties = new();
        }

        SaveData _restoring;

        public string SaveKey => "parties";

        /// <summary>A split under way is not saved: it starts again once loaded (the leader asks anew).</summary>
        public string Capture()
        {
            var s = new SaveData { next_id = Party.NextId };
            foreach (var p in _parties)
            {
                var sp = new SavedParty
                {
                    id = p.Id, on_outing = p.OnOuting, pool = p.Pool, first_arrival = p.FirstArrivalHour, settled_at = p.SettledAtHour,
                };
                foreach (var m in p.Members) sp.members.Add(m.Data.id);
                foreach (var kv in p.Out)
                {
                    if (kv.Key == null) continue;
                    sp.out_records.Add(new SavedRecord
                    {
                        member = kv.Key.Data.id, damage = kv.Value.Damage, taken = kv.Value.Taken, healing = kv.Value.Healing,
                        kills = kv.Value.Kills, deposited = kv.Value.Deposited,
                    });
                }
                sp.out_records.Sort((a, b) => string.CompareOrdinal(a.member, b.member));
                var arrived = new List<Adventurer>(p.Arrived);
                arrived.Sort((a, b) => string.CompareOrdinal(a.Data.id, b.Data.id));
                foreach (var a in arrived)
                {
                    sp.arrived.Add(a.Data.id);
                    sp.arrived_at.Add(p.ArrivedAt.TryGetValue(a, out var at) ? at : 0f);
                }
                s.parties.Add(sp);
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
        }

        /// <summary>Parties are their members: built once the members are back.</summary>
        public void Link(SaveContext ctx)
        {
            foreach (var sp in _restoring.parties)
            {
                var p = new Party
                {
                    Id = sp.id, OnOuting = sp.on_outing, Pool = sp.pool, FirstArrivalHour = sp.first_arrival, SettledAtHour = sp.settled_at,
                };
                foreach (var id in sp.members)
                {
                    if (ctx.Find<Adventurer>(id) is not { } a) continue;
                    p.Members.Add(a);
                    _of[a] = p;
                }
                foreach (var r in sp.out_records)
                    if (ctx.Find<Adventurer>(r.member) is { } a)
                        p.Out[a] = new OutingRecord { Damage = r.damage, Taken = r.taken, Healing = r.healing, Kills = r.kills, Deposited = r.deposited };
                for (int i = 0; i < sp.arrived.Count && i < sp.arrived_at.Count; i++)
                {
                    if (ctx.Find<Adventurer>(sp.arrived[i]) is not { } a) continue;
                    p.Arrived.Add(a);
                    p.ArrivedAt[a] = sp.arrived_at[i];
                }
                _parties.Add(p);
            }
            // Building them counted ids up again: the saved counter goes on from where it was.
            Party.NextId = _restoring.next_id;
            _restoring = null;
        }

        public void Init(GameClock clock, PartySettlement settlement)
        {
            _clock = clock;
            _settlement = settlement;
        }

        void OnEnable()
        {
            EventBus.Subscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Subscribe<UnitHealedEvent>(OnHealed);
            EventBus.Subscribe<UnitDiedEvent>(OnDied);
            EventBus.Subscribe<PartyFormedEvent>(OnQuestPartyFormed);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Unsubscribe<UnitHealedEvent>(OnHealed);
            EventBus.Unsubscribe<UnitDiedEvent>(OnDied);
            EventBus.Unsubscribe<PartyFormedEvent>(OnQuestPartyFormed);
        }

        /// <summary>A quest party leaves town: it becomes (or stays) a standing party on an outing.</summary>
        void OnQuestPartyFormed(PartyFormedEvent e)
        {
            var goers = new List<Adventurer> { e.Leader };
            foreach (var m in e.Members) if (m != e.Leader) goers.Add(m);
            SetOut(goers);
        }

        public Party PartyOf(Adventurer a) => a != null && _of.TryGetValue(a, out var p) ? p : null;

        /// <summary>In a party and not its leader: waits for the leader instead of deciding alone.</summary>
        public bool FollowsALeader(Adventurer a) => PartyOf(a) is { } p && p.Leader != a;

        /// <summary>
        /// These are setting out together: the party of the first of them that has one grows by the
        /// rest (a party stays one party), otherwise a new party forms in this order. Then the
        /// outing starts. Fewer than two: nothing to form.
        /// </summary>
        public Party SetOut(IReadOnlyList<Adventurer> goers)
        {
            if (goers == null || goers.Count < 2) return null;
            Party party = null;
            foreach (var a in goers) if ((party = PartyOf(a)) != null) break;
            if (party == null)
            {
                party = new Party();
                _parties.Add(party);
            }
            foreach (var a in goers)
            {
                if (a == null || !a.IsAlive || party.Has(a)) continue;
                var old = PartyOf(a);
                if (old != null) Remove(old, a, "다른 파티로 옮김");
                party.Members.Add(a);
                _of[a] = party;
                EventBus.Publish(new PartyChangedEvent { Party = party, Member = a, Joined = true });
            }
            if (party.Members.Count < 2) { Disband(party, null); return null; }

            if (party.OnOuting && !party.Settling) return party; // already out: late joiners share the pool
            party.OnOuting = true;
            party.Settling = false;
            party.Pool = 0;
            party.Out.Clear();
            party.Arrived.Clear();
            party.ArrivedAt.Clear();
            party.FirstArrivalHour = -1f;
            foreach (var a in goers) if (a != null && a.IsAlive) party.Out[a] = new OutingRecord();
            return party;
        }

        /// <summary>Gold a member brings back into the party's pool (quest pay, loot sold).</summary>
        public bool Deposit(Adventurer a, int gold)
        {
            var p = PartyOf(a);
            if (p == null || !p.WentOut(a) || p.Settling || gold <= 0) return false;
            p.Pool += gold;
            p.Out[a].Deposited += gold;
            return true;
        }

        /// <summary>A member who went out is back at the guild; true while it should wait for the split.</summary>
        public bool Arrive(Adventurer a)
        {
            var p = PartyOf(a);
            if (p == null || !p.WentOut(a)) return false;
            if (p.Arrived.Count == 0) p.FirstArrivalHour = Hour;
            if (p.Arrived.Add(a)) p.ArrivedAt[a] = Hour;
            TrySettle(p);
            return p.OnOuting;
        }

        /// <summary>Still waiting for the split (or for the others to come back).</summary>
        public bool IsWaiting(Adventurer a)
            => PartyOf(a) is { } p && p.WentOut(a) && p.ArrivedAt.TryGetValue(a, out var at) && Hour - at < WaitForRestHours;

        /// <summary>Its party is still out (or settling): no new outing is decided until it is over.</summary>
        public bool OutingUnfinished(Adventurer a) => PartyOf(a) is { OnOuting: true };

        float Hour => _clock != null ? _clock.TotalHours : Time.time / 10f;

        void Update()
        {
            for (int i = _parties.Count - 1; i >= 0; i--)
            {
                var p = _parties[i];
                p.Members.RemoveAll(m => m == null || !m.IsGuildMember);
                if (p.Members.Count < 2) { Disband(p, "인원 부족"); continue; }
                if (!p.OnOuting && p.SettledAtHour >= 0f && Hour - p.SettledAtHour >= DisbandIdleHours) { Disband(p, "마을에서 해산"); continue; }
                if (p.OnOuting && !p.Settling && p.Arrived.Count > 0) TrySettle(p);
            }
        }

        void TrySettle(Party p)
        {
            if (!p.OnOuting || p.Settling || p.Arrived.Count == 0) return;
            bool allBack = true;
            foreach (var kv in p.Out)
                if (kv.Key != null && kv.Key.IsAlive && kv.Key.IsGuildMember && !p.Arrived.Contains(kv.Key)) { allBack = false; break; }
            if (!allBack && Hour - p.FirstArrivalHour < GiveUpOnMissingHours) return;
            p.Settling = true;
            if (_settlement != null) _settlement.Begin(p, this);
            else Finish(p);
        }

        /// <summary>The split is paid: the outing is over, the unhappy leave (see <see cref="PartySettlement"/>).</summary>
        internal void Finish(Party p)
        {
            p.OnOuting = false;
            p.SettledAtHour = Hour;
            p.Settling = false;
            p.Out.Clear();
            p.Arrived.Clear();
            p.ArrivedAt.Clear();
            p.Pool = 0;
            if (p.Members.Count < 2) Disband(p, "인원 부족");
        }

        /// <summary>A member leaves (unhappy, fallen, moved on). A party of one is no party.</summary>
        public void Remove(Party p, Adventurer a, string reason)
        {
            if (p == null || !p.Members.Remove(a)) return;
            _of.Remove(a);
            EventBus.Publish(new PartyChangedEvent { Party = p, Member = a, Joined = false, Reason = reason });
            // The one left behind did not move or die: its party simply ran out (P4-73: it was logged with the leaver's reason).
            if (p.Members.Count < 2 && !p.Settling) Disband(p, "인원 부족");
        }

        void Disband(Party p, string reason)
        {
            foreach (var m in p.Members)
            {
                _of.Remove(m);
                EventBus.Publish(new PartyChangedEvent { Party = p, Member = m, Joined = false, Reason = reason ?? "파티 해산" });
            }
            p.Members.Clear();
            p.OnOuting = false;
            _parties.Remove(p);
        }

        // ---------------- Contribution on the outing ----------------

        OutingRecord RecordOf(Unit u)
            => u is Adventurer a && PartyOf(a) is { } p && p.OnOuting && !p.Settling && p.Out.TryGetValue(a, out var r) ? r : null;

        void OnDamaged(UnitDamagedEvent e)
        {
            // Blows dealt to the enemy, and blows taken from it (not friendly fire).
            if (e.Target is not Adventurer && RecordOf(e.Source) is { } dealt) dealt.Damage += e.Amount;
            if (e.Source is not Adventurer && RecordOf(e.Target) is { } taken) taken.Taken += e.Amount;
        }

        void OnHealed(UnitHealedEvent e)
        {
            if (RecordOf(e.Healer) is { } r) r.Healing += e.Amount;
        }

        void OnDied(UnitDiedEvent e)
        {
            if (RecordOf(e.Killer) is { } r) r.Kills++;
            if (e.Victim is Adventurer dead && PartyOf(dead) is { } p)
            {
                p.Out.Remove(dead);
                p.Arrived.Remove(dead);
                p.ArrivedAt.Remove(dead);
                // The leader falls: the next member takes over (user decision); Remove keeps the order.
                Remove(p, dead, "사망");
            }
        }
    }
}
