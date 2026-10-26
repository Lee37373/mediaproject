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
               && by.Fsm.Current == by.PlaceVisitState && by.PlaceVisitState.Place == p;

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
