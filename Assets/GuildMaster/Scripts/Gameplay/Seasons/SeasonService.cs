using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Disasters;
using GuildMaster.Storytelling;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Seasons
{
    /// <summary>The town says a new season comes tomorrow (P4-111b).</summary>
    public struct SeasonComingEvent { public Season Season; public int Day; }
    /// <summary>A new season came today (P4-111b).</summary>
    public struct SeasonChangedEvent { public Season Season; public int Day; public int Year; }
    /// <summary>The harvest festival is today (P4-111b).</summary>
    public struct FestivalEvent { public int Day; }

    /// <summary>
    /// The seasons at work (P4-111b, H11; rules in <see cref="SeasonRules"/>): the season comes from the day, so nothing is
    /// saved. It tells of the next one the day before and of each as it comes, gives the festival and a cold camp their
    /// thoughts, and hands the others their factor — breeding, disasters, raids, the merchant, meals, walking, a night by
    /// the fire, the storyteller's draw. Off (<see cref="Enabled"/>), every factor is 1 and nothing is told.
    /// </summary>
    public class SeasonService : MonoBehaviour
    {
        /// <summary>Tests: off keeps the world as it was before P4-111 (spring's breeding would change every test's dens).</summary>
        public static bool Enabled = true;

        /// <summary>The walking factor, kept as a number for the walkers' every frame (set each hour).</summary>
        public static float Walk { get; private set; } = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Enabled = true;
            Walk = 1f;
        }

        GameClock _clock;
        GuildRoster _roster;
        WorldService _world;

        /// <summary>The season of the day (spring before there is a clock).</summary>
        public static Season Now => Services.TryGet<GameClock>(out var c) ? SeasonRules.Of(c.Day) : Season.Spring;

        static bool On => Enabled && Services.TryGet<SeasonService>(out _);

        public static float Breed => On ? SeasonRules.BreedFactor(Now) : 1f;
        public static float Disaster => On ? SeasonRules.DisasterFactor(Now) : 1f;
        public static float Raid => On ? SeasonRules.RaidFactor(Now) : 1f;
        public static float MerchantPrice => On ? SeasonRules.MerchantPriceFactor(Now) : 1f;
        public static float Meal => On ? SeasonRules.MealFactor(Now) : 1f;
        public static float CampRest => On ? SeasonRules.CampRestFactor(Now) : 1f;
        public static float MerchantInterval(float usual) => On ? SeasonRules.MerchantInterval(Now, usual) : usual;
        public static float BeatWeight(StoryBeat b) => On ? SeasonRules.BeatWeight(Now, b) : 1f;
        public static float DisasterKindWeight(DisasterKind k) => On ? SeasonRules.DisasterKindWeight(Now, k) : 1f;

        /// <summary>Stress eases this much faster in town today (the harvest festival).</summary>
        public static float FestivalRelief
            => On && Services.TryGet<GameClock>(out var c) && SeasonRules.IsFestival(c.Day) ? SeasonRules.FestivalStressRelief : 1f;

        /// <summary>"봄 3일" for the clock on the HUD; empty when the seasons are off.</summary>
        public static string Label => On && Services.TryGet<GameClock>(out var c) ? $"{SeasonRules.Name(SeasonRules.Of(c.Day))} {SeasonRules.DayOf(c.Day)}일" : "";

        /// <summary>The light's tint now: the season's, turning from the last one's over the first hours of its first day.</summary>
        public static Color Tint(GameClock clock)
        {
            if (!On || clock == null) return Color.white;
            var s = SeasonRules.Of(clock.Day);
            var tint = SeasonRules.Tint(s);
            if (SeasonRules.Begins(clock.Day) && clock.HourOfDay < SeasonRules.TintTurnHours)
                tint = Color.Lerp(SeasonRules.Tint((Season)(((int)s + 3) % 4)), tint, clock.HourOfDay / SeasonRules.TintTurnHours);
            return tint;
        }

        public void Init(GameClock clock, GuildRoster roster, WorldService world)
        {
            _clock = clock;
            _roster = roster;
            _world = world;
            Walk = Enabled ? SeasonRules.WalkFactor(SeasonRules.Of(clock.Day)) : 1f;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
            EventBus.Subscribe<CampPitchedEvent>(OnCamp);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
            EventBus.Unsubscribe<CampPitchedEvent>(OnCamp);
            Walk = 1f;
        }

        void OnHour(HourPassedEvent e)
        {
            Walk = Enabled ? SeasonRules.WalkFactor(SeasonRules.Of(e.Day)) : 1f;
            if (!Enabled) return;
            if (e.Hour == 0)
            {
                if (SeasonRules.Begins(e.Day))
                    EventBus.Publish(new SeasonChangedEvent { Season = SeasonRules.Of(e.Day), Day = e.Day, Year = SeasonRules.Year(e.Day) });
                if (SeasonRules.Eve(e.Day)) EventBus.Publish(new SeasonComingEvent { Season = SeasonRules.Of(e.Day + 1), Day = e.Day + 1 });
                if (SeasonRules.IsFestival(e.Day)) EventBus.Publish(new FestivalEvent { Day = e.Day });
            }
            if (SeasonRules.IsFestival(e.Day) && SeasonRules.FestivalHour(e.Hour)) Feast();
        }

        /// <summary>Those in town at the festival have a good time of it (once each: the thought does not stack).</summary>
        void Feast()
        {
            foreach (var a in _roster.Members)
                if (a != null && a.IsAlive && a.IsGuildMember && _world.Grid.IsInTown(a.Position)) ThoughtRecorder.Give(a, ThoughtRules.Festival);
        }

        /// <summary>A winter night out: the camp's members shiver by the fire.</summary>
        void OnCamp(CampPitchedEvent e)
        {
            if (!Enabled || SeasonRules.Of(_clock.Day) != Season.Winter || e.Camp == null) return;
            foreach (var a in e.Camp.Members) ThoughtRecorder.Give(a, ThoughtRules.ColdCamp);
        }
    }
}
