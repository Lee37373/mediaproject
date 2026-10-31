using System.Collections.Generic;
using System.Text;
using GuildMaster.Core;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Flares
{
    /// <summary>A member shot a flare (P4-94): it bursts at <see cref="At"/> after <see cref="FlareRules.FlightSeconds"/>.</summary>
    public struct FlareFiredEvent { public Unit By; public Vector2 At; }

    /// <summary>
    /// The flares in the sky (P4-94): shoots them (pooled <see cref="SkyFlare"/>s) and answers whether a spot stands in the
    /// light of a burning one — <see cref="Unit.CanSee(Unit)"/> asks for every guild eye, so the burning ones are a static
    /// list that is empty (and costs nothing) most of the time. Not saved: a flare lives half a minute.
    /// </summary>
    public class FlareService : MonoBehaviour
    {
        public const string PoolKey = "flares.flare";

        static readonly List<SkyFlare> s_burning = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_burning.Clear();

        PoolService _pool;

        /// <summary>Flares shot since the game started (debug, tests).</summary>
        public int Fired { get; private set; }
        public static IReadOnlyList<SkyFlare> Burning => s_burning;

        public void Init(PoolService pool)
        {
            _pool = pool;
            pool.Register(PoolKey, SkyFlare.Create, 2);
        }

        /// <summary>Shoots one from <paramref name="by"/> (or from the spot itself) to burst over <paramref name="at"/>.</summary>
        public SkyFlare Fire(Unit by, Vector2 at)
        {
            var from = by != null ? by.Position : at;
            var flare = _pool.Spawn<SkyFlare>(PoolKey, at);
            flare.Launch(from, at);
            Fired++;
            EventBus.Publish(new FlareFiredEvent { By = by, At = at });
            return flare;
        }

        internal static void Lit(SkyFlare f)
        {
            if (!s_burning.Contains(f)) s_burning.Add(f);
        }

        internal static void Out(SkyFlare f) => s_burning.Remove(f);

        /// <summary>The point stands in the light of a burning flare.</summary>
        public static bool IsLit(Vector2 point)
        {
            for (int i = 0; i < s_burning.Count; i++)
                if (FlareRules.Lights(s_burning[i].Centre, point)) return true;
            return false;
        }

        /// <summary>A burning flare's centre is within <paramref name="within"/> of the point.</summary>
        public static bool Covers(Vector2 point, float within)
        {
            for (int i = 0; i < s_burning.Count; i++)
                if ((s_burning[i].Centre - point).sqrMagnitude <= within * within) return true;
            return false;
        }

        /// <summary>A guild eye at <paramref name="viewer"/> sees <paramref name="target"/> by a flare's light.</summary>
        public static bool LightsFor(Vector2 viewer, Vector2 target)
            => s_burning.Count > 0 && (target - viewer).sqrMagnitude <= FlareRules.SeeLitWithin * FlareRules.SeeLitWithin && IsLit(target);

        /// <summary>Debug: what is burning, and what the members carry.</summary>
        public string Describe()
        {
            var sb = new StringBuilder($"flares fired {Fired}, burning {s_burning.Count}");
            foreach (var f in s_burning) sb.Append($"\n  at ({f.Centre.x:0.0}, {f.Centre.y:0.0}) {f.SecondsLeft:0.0}s left");
            if (Services.TryGet<Adventurers.GuildRoster>(out var roster))
            {
                sb.Append("\n  carried:");
                foreach (var a in roster.Members) if (a.IsAlive) sb.Append($" {a.Data.name} {a.Data.flares}");
            }
            if (Services.TryGet<Economy.GuildTreasury>(out var t)) sb.Append($"\n  in stock {t.GetStock(FlareRules.ItemId)}");
            return sb.ToString();
        }
    }
}
