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
    /// <summary>A pack left its den: driven out by <see cref="DrivenBy"/>, or fleeing a disaster (<see cref="Cause"/>, P4-72; DrivenBy null).</summary>
    public struct NestAbandonedEvent { public Nest From; public Nest To; public MonsterDefinition Species; public MonsterDefinition DrivenBy; public string Cause; public int Count; public bool Observed; }
    /// <summary>Intruding monsters from <see cref="IntruderHome"/> were outmatched by a nest's residents and turn back.</summary>
    public struct IntrudersRoutedEvent { public Nest Territory; public Nest IntruderHome; public int Count; public bool Observed; }

    /// <summary>
    /// Territorial clashes between packs (P4-18; any other nest, same species included). Fighting itself comes from Monster.IsHostileTo
    /// (residents attack intruders in Nest territory). This service handles lopsided encounters:
    /// when one side is <see cref="RoutRatio"/>x stronger, the weaker one does not fight to the death —
    /// outmatched residents abandon the nest and resettle elsewhere, outmatched intruders turn back
    /// (raids give up). The guild is never routed by this rule.
    /// </summary>
    public class TerritoryService : MonoBehaviour, ISaveable
    {
        public const float RoutRatio = 2.5f;
        const float CheckInterval = 0.5f;
        const float NestCooldown = 8f;
        /// <summary>Resettle at least this far from the threat.</summary>
        const float MinResettleDistance = 10f;
        /// <summary>How long after a hit a monster still counts as fighting (see <see cref="InCombat"/>).</summary>
        const float CombatGrace = 3f;

        EcosystemManager _eco;
        float _timer;
        readonly Dictionary<Nest, float> _cooldown = new();
        /// <summary>Intruders per pack: keyed by their nest, or by species for nest-less strays.</summary>
        readonly Dictionary<object, List<Monster>> _groups = new();
        readonly List<Unit> _inside = new();

        public void Init(EcosystemManager eco) => _eco = eco;

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SaveData
        {
            /// <summary>Nests (map order) still cooling down after a rout, and the seconds left.</summary>
            public List<int> nests = new();
            public List<float> until = new();
        }

        public string SaveKey => "territory";

        public string Capture()
        {
            var s = new SaveData();
            for (int i = 0; i < _eco.Nests.Count; i++)
            {
                if (!_cooldown.TryGetValue(_eco.Nests[i], out var until) || until <= Time.time) continue;
                s.nests.Add(i);
                s.until.Add(SaveTime.Rel(until));
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _cooldown.Clear();
            for (int i = 0; i < s.nests.Count && i < s.until.Count; i++)
                if (s.nests[i] >= 0 && s.nests[i] < _eco.Nests.Count) _cooldown[_eco.Nests[s.nests[i]]] = SaveTime.Abs(s.until[i]);
        }

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

        /// <summary>
        /// Same, but a fight that has already started is never called off. The rule decides whether
        /// a pack *walks into* a territory; once blades are out it is settled by fighting. Without
        /// this a lone monster the player lured in was sent home within half a second, so luring a
        /// pursuer into another pack never produced a brawl (user report 2026-09-20).
        /// </summary>
        public static int Outcome(float residents, float intruders, bool alreadyFighting)
            => alreadyFighting ? 0 : Outcome(residents, intruders);

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

        /// <summary>Already swinging: has a live target, or was hit in the last few seconds.</summary>
        static bool InCombat(Unit u) => u != null && u.IsAlive && u.IsInFight(CombatGrace);

        /// <summary>True while either side is already in a fight (see the Outcome overload).</summary>
        static bool AnyFighting(List<Monster> a, List<Monster> b)
        {
            foreach (var m in a) if (InCombat(m)) return true;
            foreach (var m in b) if (InCombat(m)) return true;
            return false;
        }

        void CheckNest(Nest nest)
        {
            foreach (var list in _groups.Values) list.Clear();
            bool any = false;
            float radius = nest.TerritoryRadius;
            if (radius <= 0f) return;
            // Only the units inside the territory (P5-2: every nest used to scan the whole map).
            UnitRegistry.Collect(nest.WorldPosition, radius, _inside);
            foreach (var u in _inside)
            {
                if (u is not Monster m || m.Home == nest) continue;
                object pack = m.Home != null ? m.Home : m.Roaming != null ? m.Roaming : m.Def;
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
                switch (Outcome(residentStrength, intruderStrength, AnyFighting(residents, intruders)))
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

        /// <summary>
        /// Outmatched residents leave — the whole pack, never a part of it (P4-58, user decision
        /// 2026-09-22: a pack is one) — for the nearest safe empty nest of their habitat. With none,
        /// the pack takes to the road (<see cref="RoamingPackService"/>); it never moves in with
        /// another pack, of its own kind or not.
        /// </summary>
        /// <summary>
        /// A natural disaster is upon the den (P4-72): the whole pack leaves, like one driven out by
        /// a stronger pack — to an empty den of its habitat away from it, or onto the road.
        /// </summary>
        public void FleeDisaster(Nest nest, Vector2 threat, string cause)
        {
            if (nest == null || nest.IsEmpty) return;
            var residents = nest.Members.Where(m => m != null && m.IsAlive).ToList();
            Abandon(nest, residents, null, threat, null, cause);
        }

        void Abandon(Nest nest, List<Monster> residents, MonsterDefinition drivenBy, Vector2 threat, Nest drivenFrom, string cause = null)
        {
            if (residents.Count == 0) return;
            Services.TryGet<MigrationService>(out var migration);
            if (migration != null && migration.IsMigrating(nest)) return; // the pack is already on its way elsewhere
            var pack = nest.Members.Where(m => m != null && m.IsAlive).ToList();
            var dest = FindNewHome(nest, nest.Species, threat, drivenFrom, migration);
            bool observed = Observed(nest.WorldPosition);
            var species = nest.Species;
            if (dest != null)
            {
                if (dest.Species != species) dest.ChangeSpecies(species);
                foreach (var m in pack) nest.TransferMember(m, dest);
                Debug.Log($"[Territory] {nest.DisplayName}: {pack.Count} {species.displayName} driven out by {drivenBy?.displayName ?? cause} → {dest.DisplayName}");
            }
            else if (Services.TryGet<RoamingPackService>(out var roaming))
            {
                roaming.SendOnTheRoad(nest, threat);
                Debug.Log($"[Territory] {nest.DisplayName}: {pack.Count} {species.displayName} driven out by {drivenBy?.displayName ?? cause} → roaming");
            }
            else
            {
                foreach (var r in residents) r.FleeFrom(threat);
                return;
            }
            EventBus.Publish(new NestAbandonedEvent
            {
                From = nest, To = dest, Species = species, DrivenBy = drivenBy, Cause = cause, Count = pack.Count, Observed = observed,
            });
        }

        /// <summary>
        /// Nearest empty nest of the species' habitat at a safe distance, that no migrating band is
        /// heading for (preferring its biomes); null when there is none.
        /// </summary>
        Nest FindNewHome(Nest from, MonsterDefinition species, Vector2 threat, Nest avoid, MigrationService migration)
        {
            bool Safe(Nest n) => n != from && n != avoid && Vector2.Distance(n.WorldPosition, threat) >= MinResettleDistance;
            return _eco.Nests.Where(n => n.IsEmpty && n.Suits(species) && Safe(n) && (migration == null || !migration.IsTarget(n)))
                .OrderBy(n => species.biomes.Contains(n.Biome) ? 0 : 1)
                .ThenBy(n => Vector2.Distance(n.WorldPosition, from.WorldPosition))
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
            Debug.Log($"[Territory] {nest.DisplayName}: {intruders.Count} {intruders[0].Def.displayName} turned back by {residents.Count} residents");
            bool observed = Observed(nest.WorldPosition);
            foreach (var home in raidHomes)
                EventBus.Publish(new IntrudersRoutedEvent { Territory = nest, IntruderHome = home, Count = intruders.Count, Observed = observed });
        }
    }
}
