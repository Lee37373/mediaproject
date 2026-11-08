using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Places
{
    /// <summary>A member finished at a place (P4-105b): soaked, prayed, or gathered <see cref="Gathered"/> of its yield.</summary>
    public struct PlaceVisitedEvent
    {
        public Adventurer Member;
        public PlaceSite Place;
        public int Gathered;
    }

    /// <summary>
    /// Members at the places worth going to (P4-105b, user decision 2026-10-26): which of the known places a member would go to
    /// now (safe enough for it, close enough to town, a gathering spot with something on it and nobody else on the way there),
    /// what a stay does for it when it is over — a soak in a hot spring eases stress and fatigue and heals, a prayer at a
    /// shrine blesses it with more health for two days, an ore vein or a herb patch fills its pack and is picked clean for a
    /// day — and the end of a blessing. The going there is <see cref="PlaceVisitState"/>, the choosing the think tree's
    /// places source.
    /// </summary>
    public class PlaceVisits : MonoBehaviour
    {
        PlaceService _places;
        GameClock _clock;
        GuildRoster _roster;
        WorldService _world;
        /// <summary>A gathering spot's one gatherer (not saved: the walk there takes it again when it resumes).</summary>
        readonly Dictionary<int, Adventurer> _claims = new();

        public void Init(PlaceService places, GameClock clock, GuildRoster roster, WorldService world)
        {
            _places = places;
            _clock = clock;
            _roster = roster;
            _world = world;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnDestroy() => EventBus.Unsubscribe<HourPassedEvent>(OnHour);

        float Now => _clock != null ? _clock.TotalHours : 0f;

        // ---------------- Choosing ----------------

        /// <summary>Soaked lately: the warmth of the last soak still on its mind.</summary>
        public static bool SoakedLately(Adventurer a)
        {
            foreach (var t in a.Data.thoughts) if (t.id == ThoughtRules.HotSpring.Id) return true;
            return false;
        }

        /// <summary>
        /// The nearest known place of this kind it would go to now, or null: within its reach of the town (a newcomer's own
        /// ground, P4-99), nothing born there more than <see cref="PlaceRules.LevelMargin"/> levels above it, and a gathering
        /// spot with something on it that nobody else is on the way to.
        /// </summary>
        public PlaceSite Best(Adventurer a, PlaceKind kind)
        {
            if (_places == null || a == null || a.Data == null) return null;
            var town = _world.TownCenterWorld;
            float power = AdventurerStats.Power(a.Data);
            float reach = a.IsNewcomer ? a.NewcomerReach : PlaceRules.MaxDistanceFromTown;
            var eco = Services.TryGet<EcosystemManager>(out var e) ? e : null;
            PlaceSite best = null;
            float bestD = float.MaxValue;
            foreach (var p in _places.Known(kind))
            {
                if (!_places.IsReady(p) || ClaimedByAnother(p, a)) continue;
                float fromTown = Vector2.Distance(p.WorldPosition, town);
                int born = eco != null ? eco.BirthLevelAt(p.WorldPosition) : 1;
                if (!PlaceRules.SafeFor(born, power, fromTown, reach)) continue;
                float d = Vector2.Distance(a.Position, p.WorldPosition);
                if (d >= bestD) continue;
                bestD = d;
                best = p;
            }
            return best;
        }

        // ---------------- Gathering spots: one at a time ----------------

        bool ClaimedByAnother(PlaceSite p, Adventurer a)
            => PlaceRules.IsGathering(p.Kind) && _claims.TryGetValue(p.Index, out var by) && by != a && by != null && by.IsAlive
               && (by.Fsm.Current == by.PlaceVisitState && by.PlaceVisitState.Place == p
                   // On a gathering errand at it (P4-133): the spot stays its own through a fight or a night's camp.
                   || by.CurrentQuest is { Type: Quests.QuestType.Gather, IsActive: true } && by.HuntState.GatherSpot == p);

        /// <summary>
        /// Where a gathering errand goes next (P4-133): the nearest known spot of the kind with something on it that nobody
        /// else is on (<paramref name="ready"/>), else — all picked clean — the known one that grows back soonest, to wait by.
        /// Null when the guild knows none.
        /// </summary>
        public PlaceSite ErrandSpot(Adventurer a, PlaceKind kind, out bool ready)
        {
            ready = false;
            if (_places == null || a == null) return null;
            PlaceSite best = null, soonest = null;
            float bestD = float.MaxValue, soonestH = float.MaxValue;
            foreach (var p in _places.Known(kind))
            {
                if (ClaimedByAnother(p, a)) continue;
                float d = Vector2.Distance(a.Position, p.WorldPosition);
                if (_places.IsReady(p))
                {
                    if (d < bestD) { bestD = d; best = p; }
                }
                else
                {
                    float h = _places.HoursUntilReady(p) + d * 0.01f;
                    if (h < soonestH) { soonestH = h; soonest = p; }
                }
            }
            ready = best != null;
            return best ?? soonest;
        }

        /// <summary>
        /// A gathering errand's dig (P4-133): what the spot gives (<see cref="PlaceRules.GatherCount"/>), the spot spent as by
        /// any gatherer — the caller hands the yield to the errand (the rest to the member's pack). 0 if picked clean meanwhile.
        /// </summary>
        public int Harvest(Adventurer a, PlaceSite p)
        {
            if (a == null || p == null || !a.IsAlive || !PlaceRules.IsGathering(p.Kind) || !_places.IsReady(p)) return 0;
            int got = PlaceRules.GatherCount(p.Kind, GameRandom.Shared.Value);
            _places.Spend(p, PlaceRules.RegrowHours);
            Release(p, a);
            EventBus.Publish(new PlaceVisitedEvent { Member = a, Place = p, Gathered = got });
            return got;
        }

        /// <summary>Takes a gathering spot for this member (anything else is shared); false when someone else has it.</summary>
        public bool Claim(PlaceSite p, Adventurer a)
        {
            if (p == null) return false;
            if (!PlaceRules.IsGathering(p.Kind)) return true;
            if (ClaimedByAnother(p, a)) return false;
            _claims[p.Index] = a;
            return true;
        }

        public void Release(PlaceSite p, Adventurer a)
        {
            if (p != null && _claims.TryGetValue(p.Index, out var by) && by == a) _claims.Remove(p.Index);
        }

        // ---------------- A stay over ----------------

        /// <summary>What the stay did for the member; returns how much it gathered (0 for a soak or a prayer).</summary>
        public int Finish(Adventurer a, PlaceSite p)
        {
            if (a == null || p == null || !a.IsAlive) return 0;
            var d = a.Data;
            int got = 0;
            switch (p.Kind)
            {
                case PlaceKind.HotSpring:
                    d.stress = Mathf.Max(0f, d.stress - PlaceRules.SpringStressRelief);
                    d.fatigue = Mathf.Max(0f, d.fatigue - PlaceRules.SpringFatigueRelief);
                    a.Heal(a.Stats.MaxHp * PlaceRules.SpringHeal);
                    ThoughtRecorder.Give(a, ThoughtRules.HotSpring);
                    break;
                case PlaceKind.Shrine:
                    d.blessedUntil = Now + PlaceRules.BlessingHours;
                    d.shrineReadyAt = Now + PlaceRules.ShrineCooldownHours;
                    a.RecalculateStats();
                    ThoughtRecorder.Give(a, ThoughtRules.Blessed);
                    break;
                case PlaceKind.OreVein:
                case PlaceKind.HerbPatch:
                    if (!_places.IsReady(p)) break;
                    got = PlaceRules.GatherCount(p.Kind, GameRandom.Shared.Value);
                    d.AddLoot(PlaceRules.GatherItem(p.Kind), got);
                    _places.Spend(p, PlaceRules.RegrowHours);
                    break;
            }
            Release(p, a);
            EventBus.Publish(new PlaceVisitedEvent { Member = a, Place = p, Gathered = got });
            return got;
        }

        // ---------------- On the way (P4-105c) ----------------

        /// <summary>The nearest known abandoned camp within <see cref="PlaceRules.OldCampReach"/> of a band about to make camp, or null.</summary>
        public PlaceSite OldCampNear(Vector2 at)
        {
            if (_places == null) return null;
            PlaceSite best = null;
            float bestD = PlaceRules.OldCampReach;
            foreach (var p in _places.Known(PlaceKind.OldCamp))
            {
                float d = Vector2.Distance(p.WorldPosition, at);
                if (d > bestD) continue;
                bestD = d;
                best = p;
            }
            return best;
        }

        /// <summary>
        /// The known ruined watchtower nobody has climbed yet that a scouting band goes for first, or null: within its reach of
        /// the town, the nearest to it, away from <paramref name="avoid"/>.
        /// </summary>
        public PlaceSite TowerToClimb(Vector2 from, float reach, Vector2? avoid = null, float avoidRadius = 0f)
        {
            if (_places == null) return null;
            var town = _world.TownCenterWorld;
            PlaceSite best = null;
            float bestD = float.MaxValue;
            foreach (var p in _places.Known(PlaceKind.WatchRuin))
            {
                if (_places.IsClimbed(p) || Vector2.Distance(p.WorldPosition, town) > Mathf.Min(reach, PlaceRules.MaxDistanceFromTown)) continue;
                if (avoid != null && Vector2.Distance(p.WorldPosition, avoid.Value) < avoidRadius) continue;
                float d = Vector2.Distance(p.WorldPosition, from);
                if (d >= bestD) continue;
                bestD = d;
                best = p;
            }
            return best;
        }

        /// <summary>An unclimbed watchtower this close to where a scout stands, or null.</summary>
        public PlaceSite TowerAt(Vector2 at, float within = 3f)
        {
            if (_places == null) return null;
            foreach (var p in _places.Known(PlaceKind.WatchRuin))
                if (!_places.IsClimbed(p) && Vector2.Distance(p.WorldPosition, at) <= within) return p;
            return null;
        }

        /// <summary>
        /// Up the tower: the land <see cref="PlaceRules.TowerSightRadius"/> round is seen at once (only the first time), and
        /// <paramref name="who"/> and those with it are glad of the view.
        /// </summary>
        public void Climb(Adventurer who, PlaceSite tower, IEnumerable<Adventurer> with = null)
        {
            if (who == null || tower == null || _places.IsClimbed(tower)) return;
            _places.MarkClimbed(tower);
            if (Services.TryGet<Fog.FogOfWar>(out var fog)) fog.Reveal(tower.WorldPosition, PlaceRules.TowerSightRadius);
            ThoughtRecorder.Give(who, ThoughtRules.Lookout);
            if (with != null)
                foreach (var m in with)
                    if (m != null && m != who && m.IsAlive && m.IsGuildMember) ThoughtRecorder.Give(m, ThoughtRules.Lookout);
            who.Say("저 멀리까지 다 보인다!");
            EventBus.Publish(new PlaceVisitedEvent { Member = who, Place = tower });
        }

        /// <summary>A blessing run out: its health goes back to what it was.</summary>
        void OnHour(HourPassedEvent e)
        {
            if (_roster == null) return;
            foreach (var a in _roster.Members)
            {
                if (a == null || a.Data == null || a.Data.blessedUntil <= 0f || Now < a.Data.blessedUntil) continue;
                a.Data.blessedUntil = 0f;
                if (a.IsAlive) a.RecalculateStats();
            }
        }
    }
}
