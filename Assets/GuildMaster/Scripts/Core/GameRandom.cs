using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Core
{
    /// <summary>Seedable RNG wrapper so map generation and spawning are reproducible.</summary>
    public class GameRandom
    {
        readonly System.Random _rng;

        public GameRandom(int seed) { _rng = new System.Random(seed); }

        public int Range(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);
        public float Range(float min, float max) => min + (float)_rng.NextDouble() * (max - min);
        public float Value => (float)_rng.NextDouble();
        public bool Chance(float probability) => _rng.NextDouble() < probability;

        public T Pick<T>(IReadOnlyList<T> list) => list[_rng.Next(list.Count)];

        public Vector2 InsideUnitCircle()
        {
            float a = Range(0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Value);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>Global gameplay RNG (seeded by the session).</summary>
        public static GameRandom Shared { get; set; } = new GameRandom(12345);
    }
}
