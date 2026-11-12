using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Monsters;
using GuildMaster.Units;
using GuildMaster.Visuals;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>A race for kills between two rivals was decided (P4-131).</summary>
    public struct RivalRaceEvent
    {
        public Adventurer Winner, Loser;
        public int WinnerKills, LoserKills;
    }

    /// <summary>
    /// The members' feelings for each other in a fight (P4-131, F-12; the rules in <see cref="BondRules"/>): a friend fighting
    /// beside a member raises its attack and a foe in its way lowers it (<see cref="Unit.BondAttack"/>), with a line and an
    /// effect as it starts; foes who fought side by side quarrel once the fight is over; rivals race for kills in a shared
    /// fight — the one behind holds on longer (<see cref="IsBehind"/>), the race's end leaves a thought and a tally
    /// (<see cref="AdventurerData.rivalry"/>); a mentor near a kill makes its pupil's EXP count more (<see cref="Mentored"/>).
    /// Its lines and chances come from its own dice, so the shared stream the rest of the game draws from is left alone.
    /// </summary>
    public class BondService : MonoBehaviour
    {
        /// <summary>Tests: every line and quarrel that may come does (no chance, no cooldown).</summary>
        internal static bool Certain;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Certain = false;

        const float TickSeconds = 0.25f;
        /// <summary>A member still counts as in the fight this long after its last target (seconds): between two blows, or kills.</summary>
        const float FightMemory = 2.5f;
        /// <summary>Foes quarrel once both have been out of the fight this long (seconds).</summary>
        const float QuietSeconds = 3f;
        const float AnswerDelay = 2.2f;

        sealed class Contest
        {
            public Adventurer A, B;
            public int KillsA, KillsB;
            public float Last;
            public bool ABehindSaid, BBehindSaid;
            public int Lead => System.Math.Sign(KillsA - KillsB);
        }

        GuildRoster _roster;
        GameClock _clock;
        GameRandom _rng;
        float _timer;
        readonly List<Adventurer> _fighting = new();
        readonly Dictionary<Adventurer, float> _lastFought = new();
        readonly Dictionary<Adventurer, (Adventurer friend, Adventurer foe)> _beside = new();
        readonly Dictionary<Adventurer, float> _lastLine = new();
        readonly Dictionary<Adventurer, float> _lastMentorLine = new();
        readonly Dictionary<string, (Adventurer a, Adventurer b)> _foesInFight = new();
        readonly Dictionary<string, float> _lastQuarrel = new();
        readonly Dictionary<string, Contest> _contests = new();
        readonly HashSet<Adventurer> _behind = new();
        readonly List<(float due, Adventurer who, string line)> _later = new();
        readonly List<string> _done = new();

        float Hours => _clock != null ? _clock.TotalHours : Time.time / 60f;

        public void Init(GuildRoster roster, GameClock clock, int seed)
        {
            _roster = roster;
            _clock = clock;
            _rng = new GameRandom(seed ^ 0x5131);
            EventBus.Subscribe<UnitDiedEvent>(OnDied);
        }

        void OnDestroy() => EventBus.Unsubscribe<UnitDiedEvent>(OnDied);

        static bool Member(Adventurer a) => a != null && a.IsAlive && a.IsGuildMember && !a.IsVisitor && a.Data != null;

        static string Key(Adventurer a, Adventurer b)
            => string.CompareOrdinal(a.Data.id, b.Data.id) <= 0 ? a.Data.id + "|" + b.Data.id : b.Data.id + "|" + a.Data.id;

        bool InFight(Adventurer a) => _lastFought.TryGetValue(a, out float t) && Time.time - t < FightMemory;

        /// <summary>Behind its rival in a race right now: it holds on longer before pulling back (<see cref="BondRules.RetreatWhenBehind"/>).</summary>
        public bool IsBehind(Adventurer a) => a != null && _behind.Contains(a);

        // ---------------- Friends and foes, each moment ----------------

        void Update()
        {
            SayWhatIsDue();
            _timer -= Time.deltaTime;
            if (_timer > 0f || _roster == null) return;
            _timer = TickSeconds;
            float now = Time.time;

            _fighting.Clear();
            foreach (var a in _roster.Members)
            {
                if (!Member(a))
                {
                    if (a != null) a.BondAttack = 0f;
                    continue;
                }
                // Against monsters only: a comrade gone berserk or a fistfight is no fight to stand side by side in.
                if (a.Engage.Target is Monster) _lastFought[a] = now;
                if (InFight(a)) _fighting.Add(a);
                else
                {
                    a.BondAttack = 0f;
                    _beside.Remove(a);
                }
            }

            foreach (var a in _fighting)
            {
                Adventurer friend = null, foe = null;
                foreach (var o in _fighting)
                {
                    if (o == a || (o.Position - a.Position).sqrMagnitude > BondRules.Range * BondRules.Range) continue;
                    int ab = Relationships.Get(a, o), ba = Relationships.Get(o, a);
                    if (friend == null && BondRules.Friends(ab, ba)) friend = o;
                    if (foe == null && BondRules.Foes(ab, ba)) foe = o;
                }
                a.BondAttack = BondRules.AttackShare(friend != null, foe != null);
                _beside.TryGetValue(a, out var was);
                if (friend != null && was.friend == null) FriendBeside(a, friend);
                if (foe != null && was.foe == null) FoeInTheWay(a, foe);
                _beside[a] = (friend, foe);
                if (foe != null) _foesInFight[Key(a, foe)] = (a, foe);
            }

            AfterTheFight(now);
            Races(now);
        }

        void FriendBeside(Adventurer a, Adventurer friend)
        {
            // The effect each time; the words now and then.
            if (!CasualVfx.PlayIfSeen("Buff1_Yellow", a.Position, 0.6f) && Services.TryGet<FxService>(out var fx))
                fx.Spark(a.Position + new Vector2(0f, 0.6f), new Color(1f, 0.85f, 0.35f), 0.5f, 0.35f);
            if (MayTalk(a)) a.Say(BondRules.FriendLine(friend.Data.name, _rng.Range(0, 100)), false);
        }

        void FoeInTheWay(Adventurer a, Adventurer foe)
        {
            if (MayTalk(a)) a.Say(BondRules.FoeLine(foe.Data.name, _rng.Range(0, 100)), false);
        }

        /// <summary>Now and then, and not again for a while (game hours).</summary>
        bool MayTalk(Adventurer a)
        {
            float h = Hours;
            if (Certain) return true;
            if (_lastLine.TryGetValue(a, out float last) && h - last < BondRules.LineCooldownHours) return false;
            if (!_rng.Chance(BondRules.LineChance)) return false;
            _lastLine[a] = h;
            return true;
        }

        /// <summary>Foes who fought side by side have words once both are out of it.</summary>
        void AfterTheFight(float now)
        {
            _done.Clear();
            foreach (var kv in _foesInFight)
            {
                var (a, b) = kv.Value;
                if (!Member(a) || !Member(b)) { _done.Add(kv.Key); continue; }
                if (QuietFor(a, now) < QuietSeconds || QuietFor(b, now) < QuietSeconds) continue;
                _done.Add(kv.Key);
                float h = Hours;
                if (!Certain && _lastQuarrel.TryGetValue(kv.Key, out float last) && h - last < BondRules.QuarrelCooldownHours) continue;
                if (!Certain && !_rng.Chance(BondRules.QuarrelChance)) continue;
                _lastQuarrel[kv.Key] = h;
                var (first, answer) = BondRules.Quarrel(b.Data.name, _rng.Range(0, 100));
                a.Say(first, false);
                _later.Add((now + AnswerDelay, b, answer));
            }
            foreach (var k in _done) _foesInFight.Remove(k);
        }

        float QuietFor(Adventurer a, float now) => _lastFought.TryGetValue(a, out float t) ? now - t : float.MaxValue;

        void SayWhatIsDue()
        {
            for (int i = _later.Count - 1; i >= 0; i--)
            {
                if (Time.time < _later[i].due) continue;
                var (_, who, line) = _later[i];
                _later.RemoveAt(i);
                if (Member(who)) who.Say(line, false);
            }
        }

        // ---------------- Rivals: a race for kills ----------------

        void OnDied(UnitDiedEvent e)
        {
            if (e.Victim is not Monster || e.Killer is not Adventurer k || !Member(k) || _roster == null) return;
            foreach (var o in _roster.Members)
            {
                if (o == k || !Member(o) || !InFight(o) || (o.Position - k.Position).sqrMagnitude > BondRules.RivalRange * BondRules.RivalRange) continue;
                if (!BondRules.Rivals(Relationships.Get(k, o), Relationships.Get(o, k), k.Data.level, o.Data.level)) continue;
                string key = Key(k, o);
                if (!_contests.TryGetValue(key, out var c))
                    _contests[key] = c = string.CompareOrdinal(k.Data.id, o.Data.id) <= 0 ? new Contest { A = k, B = o } : new Contest { A = o, B = k };
                int leadBefore = c.Lead;
                if (c.A == k) c.KillsA++;
                else c.KillsB++;
                c.Last = Time.time;
                int lead = c.Lead;
                int mine = c.A == k ? 1 : -1;
                // Taking the lead: it says so; the one now behind will not have it (once a race each).
                if (lead == mine && leadBefore != mine) k.Say(BondRules.AheadLine(o.Data.name, _rng.Range(0, 100)), false);
                if (lead != 0)
                {
                    var behind = lead > 0 ? c.B : c.A;
                    var ahead = lead > 0 ? c.A : c.B;
                    bool said = behind == c.A ? c.ABehindSaid : c.BBehindSaid;
                    if (!said)
                    {
                        if (behind == c.A) c.ABehindSaid = true;
                        else c.BBehindSaid = true;
                        _later.Add((Time.time + AnswerDelay, behind, BondRules.BehindLine(ahead.Data.name, _rng.Range(0, 100))));
                    }
                }
            }
        }

        void Races(float now)
        {
            _behind.Clear();
            _done.Clear();
            foreach (var kv in _contests)
            {
                var c = kv.Value;
                if (!Member(c.A) || !Member(c.B)) { _done.Add(kv.Key); continue; }
                bool apart = (c.A.Position - c.B.Position).sqrMagnitude > 4f * BondRules.RivalRange * BondRules.RivalRange;
                if (now - c.Last > BondRules.ContestIdleSeconds || apart)
                {
                    _done.Add(kv.Key);
                    Decide(c);
                    continue;
                }
                if (c.Lead > 0) _behind.Add(c.B);
                else if (c.Lead < 0) _behind.Add(c.A);
            }
            foreach (var k in _done) _contests.Remove(k);
        }

        void Decide(Contest c)
        {
            int result = BondRules.ContestResult(c.KillsA, c.KillsB);
            if (result == 0) return;
            var (winner, loser) = result > 0 ? (c.A, c.B) : (c.B, c.A);
            int wk = result > 0 ? c.KillsA : c.KillsB, lk = result > 0 ? c.KillsB : c.KillsA;
            Tally(winner, loser, won: true);
            Tally(loser, winner, won: false);
            ThoughtRecorder.Give(winner, ThoughtRules.RivalBeaten, loser.Data.id, loser.Data.name);
            ThoughtRecorder.Give(loser, ThoughtRules.RivalLostTo, winner.Data.id, winner.Data.name);
            winner.Say(BondRules.WonLine(loser.Data.name), false);
            _later.Add((Time.time + AnswerDelay, loser, BondRules.LostLine));
            EventBus.Publish(new RivalRaceEvent { Winner = winner, Loser = loser, WinnerKills = wk, LoserKills = lk });
        }

        static void Tally(Adventurer a, Adventurer other, bool won)
        {
            var list = a.Data.rivalry;
            var r = list.Find(x => x.id == other.Data.id);
            if (r == null) list.Add(r = new RivalRecord { id = other.Data.id });
            if (won) r.won++;
            else r.lost++;
        }

        /// <summary>Its record against a rival (zeros if they never raced).</summary>
        public static (int won, int lost) Record(Adventurer a, Adventurer other)
        {
            var r = a?.Data?.rivalry.Find(x => x.id == other?.Data?.id);
            return r == null ? (0, 0) : (r.won, r.lost);
        }

        // ---------------- Mentors ----------------

        /// <summary>
        /// The EXP a member gets from a kill, with a mentor of its near (within <see cref="Adventurer.ExpShareRadius"/>): ×
        /// <see cref="BondRules.MentorExp"/>, a "learnt from" thought, and now and then a word from the mentor.
        /// </summary>
        public int Mentored(Adventurer pupil, int exp)
        {
            var mentor = MentorNear(pupil);
            if (mentor == null || exp <= 0) return exp;
            ThoughtRecorder.Give(pupil, ThoughtRules.Mentored, mentor.Data.id, mentor.Data.name);
            float h = Hours;
            if (!_lastMentorLine.TryGetValue(mentor, out float last) || h - last >= BondRules.MentorLineCooldownHours)
            {
                _lastMentorLine[mentor] = h;
                mentor.Say(BondRules.MentorLine(pupil.Data.name, _rng.Range(0, 100)), false);
            }
            return Mathf.RoundToInt(exp * BondRules.MentorExp);
        }

        public Adventurer MentorNear(Adventurer pupil)
        {
            if (!Member(pupil) || _roster == null) return null;
            const float r2 = Adventurer.ExpShareRadius * Adventurer.ExpShareRadius;
            foreach (var o in _roster.Members)
            {
                if (o == pupil || !Member(o) || (o.Position - pupil.Position).sqrMagnitude > r2) continue;
                if (BondRules.Mentors(o.Data.level, pupil.Data.level, Relationships.Get(o, pupil), Relationships.Get(pupil, o))) return o;
            }
            return null;
        }

        // ---------------- For the detail window ----------------

        /// <summary>Who is what to this member among the guild, by kind.</summary>
        public static List<(Adventurer other, List<BondKind> kinds)> BondsOf(Adventurer a, IEnumerable<Adventurer> members)
        {
            var list = new List<(Adventurer, List<BondKind>)>();
            if (a?.Data == null) return list;
            foreach (var o in members)
            {
                if (o == a || o?.Data == null || !o.IsAlive || o.IsVisitor) continue;
                var kinds = BondRules.Kinds(Relationships.Get(a, o), Relationships.Get(o, a), a.Data.level, o.Data.level);
                if (kinds.Count > 0) list.Add((o, kinds));
            }
            return list;
        }
    }
}
