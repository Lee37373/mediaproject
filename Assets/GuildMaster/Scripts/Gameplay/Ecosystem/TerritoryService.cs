using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Fog;
using GuildMaster.Monsters;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Ecosystem
{
    /// <summary>Residents were outmatched and moved to another nest. Observed = the guild saw the old nest then.</summary>
    public struct NestAbandonedEvent { public Nest From; public Nest To; public MonsterDefinition Species; public MonsterDefinition DrivenBy; public int Count; public bool Observed; }
    /// <summary>Intruding monsters from <see cref="IntruderHome"/> were outmatched by a nest's residents and turn back.</summary>
    public struct IntrudersRoutedEvent { public Nest Territory; public Nest IntruderHome; public int Count; public bool Observed; }

    /// <summary>
    /// Territorial clashes between packs (P4-18; any other nest, same species included). Fighting itself comes from Monster.IsHostileTo
    /// (residents attack intruders in Nest territory). This service handles lopsided encounters:
    /// when one side is <see cref="RoutRatio"/>x stronger, the weaker one does not fight to the death —
    /// outmatched residents abandon the nest and resettle elsewhere, outmatched intruders turn back
    /// (raids give up). The guild is never routed by this rule.
    /// </summary>
    public class TerritoryService : MonoBehaviour
    {
        public const float RoutRatio = 2.5f;
        const float CheckInterval = 0.5f;
        const float NestCooldown = 8f;
        /// <summary>Resettle at least this far from the threat.</summary>
        const float MinResettleDistance = 10f;

        EcosystemManager _eco;
        float _timer;
        readonly Dictionary<Nest, float> _cooldown = new();
        /// <summary>Intruders per pack: keyed by their nest, or by species for nest-less strays.</summary>
        readonly Dictionary<object, List<Monster>> _groups = new();

        public void Init(EcosystemManager eco) => _eco = eco;

        /// <summary>Fighting weight of a unit: current HP × attack.</summary>
        public static float Strength(Unit u) => u == null || !u.IsAlive ? 0f : u.Hp * Mathf.Max(1f, u.Stats.Attack);

        /// <summary>-1 residents rout, +1 intruders rout, 0 they fight (pure, tested).</summary>
        public static int Outcome(float residents, float intruders)
        {
            if (intruders <= 0f) return 0;
            if (intruders >= residents * RoutRatio) return -1;
            if (residents >= intruders * RoutRatio) return 1;
            return 0;
        }

        void Update()
        {
            if (_eco == null) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = CheckInterval;
            foreach (var nest in _eco.Nests.ToList())
            {
                if (nest.IsEmpty) continue;
                if (_cooldown.TryGetValue(nest, out var until) && Time.time < until) continue;
                CheckNest(nest);
            }
        }

        void CheckNest(Nest nest)
        {
            foreach (var list in _groups.Values) list.Clear();
            bool any = false;
            foreach (var u in UnitRegistry.All)
            {
                if (u is not Monster m || !m.IsAlive || m.Home == nest || !nest.InTerritory(m.Position)) continue;
                object pack = m.Home != null ? m.Home : m.Def;
                if (!_groups.TryGetValue(pack, out var list)) _groups[pack] = list = new List<Monster>();
                list.Add(m);
                any = true;
            }
            if (!any) return;

            var residents = nest.Members.Where(r => r.IsAlive && r.IsDefendingTerritory).ToList();
            float residentStrength = residents.Sum(Strength);
            foreach (var kv in _groups)
            {
                var species = kv.Value.Count > 0 ? kv.Value[0].Def : null;
                var intruders = kv.Value;
                if (intruders.Count == 0) continue;
                float intruderStrength = intruders.Sum(Strength);
                switch (Outcome(residentStrength, intruderStrength))
                {
                    case -1:
                        Abandon(nest, residents, species, Centroid(intruders), intruders[0].Home);
                        _cooldown[nest] = Time.time + NestCooldown;
                        return;
                    case 1:
                        RouteIntruders(nest, intruders, residents);
                        _cooldown[nest] = Time.time + NestCooldown;
                        return;
                }
            }
        }

        static Vector2 Centroid(List<Monster> units)
        {
            var sum = Vector2.zero;
            foreach (var u in units) sum += u.Position;
            return sum / Mathf.Max(1, units.Count);
        }

        static bool Observed(Vector2 p) => Services.TryGet<FogOfWar>(out var fog) && fog.IsVisible(p);

        /// <summary>Outmatched residents move to the best other nest; if there is none they just run.</summary>
        void Abandon(Nest nest, List<Monster> residents, MonsterDefinition drivenBy, Vector2 threat, Nest drivenFrom)
        {
            if (residents.Count == 0) return;
            var dest = FindNewHome(nest, nest.Species, residents.Count, threat, drivenFrom);
            bool observed = Observed(nest.WorldPosition);
            if (dest == null)
            {
                foreach (var r in residents) r.FleeFrom(threat);
                return;
            }
            if (dest.IsEmpty && dest.Species != nest.Species) dest.ChangeSpecies(nest.Species);
            foreach (var r in residents) nest.TransferMember(r, dest);
            Debug.Log($"[Territory] {nest.DisplayName}: {residents.Count} {nest.Species.displayName} driven out by {drivenBy.displayName} → {dest.DisplayName}");
            EventBus.Publish(new NestAbandonedEvent
            {
                From = nest, To = dest, Species = nest.Species, DrivenBy = drivenBy, Count = residents.Count, Observed = observed,
            });
        }

        /// <summary>
        /// Nearest empty nest site at a safe distance (preferring the species' biomes), else the
        /// same-species nest with the most free room.
        /// </summary>
        Nest FindNewHome(Nest from, MonsterDefinition species, int movers, Vector2 threat, Nest avoid)
        {
            // Never into the nest of the pack that drove them out (same-species rivals).
            bool Safe(Nest n) => n != from && n != avoid && Vector2.Distance(n.WorldPosition, threat) >= MinResettleDistance;
            var empty = _eco.Nests.Where(n => n.IsEmpty && Safe(n))
                .OrderBy(n => species.biomes.Contains(n.Biome) ? 0 : 1)
                .ThenBy(n => Vector2.Distance(n.WorldPosition, from.WorldPosition))
                .FirstOrDefault();
            if (empty != null) return empty;
            return _eco.Nests.Where(n => n.Species == species && Safe(n) && n.Capacity - n.Population >= Mathf.Min(movers, 2))
                .OrderBy(n => Vector2.Distance(n.WorldPosition, from.WorldPosition))
                .FirstOrDefault();
        }

        /// <summary>Outmatched intruders turn back: raids give up (WaveManager), others run from the residents.</summary>
        static void RouteIntruders(Nest nest, List<Monster> intruders, List<Monster> residents)
        {
            var strongest = residents.OrderByDescending(Strength).FirstOrDefault();
            var from = strongest != null ? strongest.Position : nest.WorldPosition;
            var raidHomes = new HashSet<Nest>();
            foreach (var m in intruders)
            {
                if (m.DefaultStateOverride != null && m.Home != null) raidHomes.Add(m.Home);
                else m.FleeFrom(from);
            }
            bool observed = Observed(nest.WorldPosition);
            foreach (var home in raidHomes)
                EventBus.Publish(new IntrudersRoutedEvent { Territory = nest, IntruderHome = home, Count = intruders.Count, Observed = observed });
        }
    }
}
