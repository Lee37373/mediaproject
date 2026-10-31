using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>
    /// What stands in the training yard at each of the ground's levels (P4-115): where the picture puts its straw dummies,
    /// targets and rack, and where trainees take their places to strike (<see cref="Adventurers.TrainState"/>). Places are
    /// fractions of the footprint, at the thing's foot. Level 1 does not teach yet (<see cref="UpgradeService.Trains"/>): one
    /// dummy; level 2 two and a target; level 3 three, two targets and the guild's banner.
    /// </summary>
    public static class TrainingYardLayout
    {
        public enum Kind { Dummy, Target, Rack, Banner }

        public readonly struct Station
        {
            public readonly Kind Kind;
            public readonly Vector2 At;
            public Station(Kind kind, float x, float y) { Kind = kind; At = new Vector2(x, y); }
            /// <summary>Something to strike at (the rack and the banner are not).</summary>
            public bool Drills => Kind is Kind.Dummy or Kind.Target;
        }

        static readonly Station[][] s_levels =
        {
            new[] { new Station(Kind.Rack, 0.5f, 0.76f), new Station(Kind.Dummy, 0.32f, 0.31f) },
            new[] { new Station(Kind.Rack, 0.5f, 0.76f), new Station(Kind.Dummy, 0.22f, 0.31f), new Station(Kind.Dummy, 0.42f, 0.31f),
                    new Station(Kind.Target, 0.76f, 0.37f) },
            new[] { new Station(Kind.Rack, 0.48f, 0.78f), new Station(Kind.Banner, 0.15f, 0.72f), new Station(Kind.Dummy, 0.16f, 0.3f),
                    new Station(Kind.Dummy, 0.38f, 0.3f), new Station(Kind.Dummy, 0.6f, 0.3f), new Station(Kind.Target, 0.84f, 0.3f),
                    new Station(Kind.Target, 0.84f, 0.58f) },
        };

        public static IReadOnlyList<Station> Of(int level) => s_levels[Mathf.Clamp(level, 1, s_levels.Length) - 1];

        /// <summary>Where a station's foot stands on the map.</summary>
        public static Vector2 WorldOf(RectInt footprint, Station s)
            => new(footprint.xMin + s.At.x * footprint.width, footprint.yMin + s.At.y * footprint.height);

        /// <summary>How far to either side of a dummy or target a trainee stands (cells).</summary>
        public const float Beside = 0.85f;
    }
}
