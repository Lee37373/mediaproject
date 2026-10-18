using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Villages
{
    /// <summary>A place for a village: the centre cell of its icon, its region, and whether it lies on the inner land.</summary>
    public struct VillageSite
    {
        public Vector2Int Cell;
        public Region Region;
        public bool Inner;
        public string Name;
    }

    /// <summary>
    /// Where the other villages stand (P4-84, user decision 2026-10-18): two in each outer region — one on the
    /// inner land (60~180 cells from the town), one beyond it — on open walkable ground, apart from each other
    /// and from every den. Pure (map data in, sites out), so it is covered by EditMode tests. The first
    /// villages are drawn with their own random stream, so adding them did not change the seeded worlds.
    /// </summary>
    public static class VillagePlacement
    {
        public static readonly Region[] OuterRegions = { Region.Desert, Region.Snow, Region.Swamp, Region.Wasteland };

        /// <summary>Random cells tried per site before giving up.</summary>
        const int Attempts = 4000;

        /// <summary>The random stream for the first villages: the map seed, apart from the game's own stream.</summary>
        public static GameRandom StreamFor(int mapSeed) => new(mapSeed ^ 0x51D7A6E);

        /// <summary>The first villages of a fresh world: <see cref="VillageRules.PerRegion"/> in each outer region.</summary>
        public static List<VillageSite> PlaceInitial(MapData map, GameRandom rng)
        {
            var sites = new List<VillageSite>();
            foreach (var region in OuterRegions)
            {
                if (TryPick(map, region, VillageRules.InnerMin, VillageRules.InnerMax, sites, rng, out var inner))
                {
                    inner.Inner = true;
                    sites.Add(Named(inner, sites, rng));
                }
                if (TryPick(map, region, VillageRules.OuterMin, float.MaxValue, sites, rng, out var outer))
                    sites.Add(Named(outer, sites, rng));
            }
            return sites;
        }

        /// <summary>
        /// A place for a new village (P4-84: one lost may be replaced at random): a region drawn by
        /// <paramref name="weightOf"/> (a cleansed one weighs more), then a spot anywhere on its land past the
        /// grassland, keeping the usual distances from <paramref name="standing"/> villages and the dens.
        /// </summary>
        public static bool TryPickNew(MapData map, IReadOnlyList<VillageSite> standing, IReadOnlyCollection<string> namesInUse,
                                      System.Func<Region, float> weightOf, GameRandom rng, out VillageSite site)
        {
            var regions = new List<Region>(OuterRegions);
            // Draw a region by weight; if it has no room, try the others in turn.
            while (regions.Count > 0)
            {
                float total = 0f;
                foreach (var r in regions) total += Mathf.Max(0f, weightOf(r));
                if (total <= 0f) break;
                float roll = rng.Value * total;
                int pick = regions.Count - 1;
                for (int i = 0; i < regions.Count; i++)
                {
                    roll -= Mathf.Max(0f, weightOf(regions[i]));
                    if (roll < 0f) { pick = i; break; }
                }
                var region = regions[pick];
                if (TryPick(map, region, VillageRules.InnerMin, float.MaxValue, standing, rng, out site))
                {
                    site.Inner = Vector2.Distance(site.Cell, map.TownCenter) <= VillageRules.InnerMax;
                    site.Name = PickName(region, namesInUse, rng);
                    return true;
                }
                regions.RemoveAt(pick);
            }
            site = default;
            return false;
        }

        /// <summary>A random spot in <paramref name="region"/> between the two distances from the town that <see cref="Fits"/>.</summary>
        static bool TryPick(MapData map, Region region, float minDist, float maxDist, IReadOnlyList<VillageSite> others,
                            GameRandom rng, out VillageSite site)
        {
            int m = VillageRules.EdgeMargin;
            for (int i = 0; i < Attempts; i++)
            {
                var c = new Vector2Int(rng.Range(m, map.Width - m), rng.Range(m, map.SurfaceHeight - m));
                if (map.RegionOf(c.x, c.y) != region) continue;
                float d = Vector2.Distance(c, map.TownCenter);
                if (d < minDist || d > maxDist) continue;
                if (!Fits(map, c, others)) continue;
                site = new VillageSite { Cell = c, Region = region };
                return true;
            }
            site = default;
            return false;
        }

        /// <summary>
        /// The icon and a ring of one cell round it are open walkable ground (grass, sand or road — no trees,
        /// water or rock, so nobody is walled in), inside the map, clear of the dens and the other villages.
        /// </summary>
        public static bool Fits(MapData map, Vector2Int centre, IReadOnlyList<VillageSite> others)
        {
            int half = VillageRules.Size / 2 + 1;
            for (int y = centre.y - half; y <= centre.y + half; y++)
            for (int x = centre.x - half; x <= centre.x + half; x++)
            {
                if (x < VillageRules.EdgeMargin || y < VillageRules.EdgeMargin
                    || x >= map.Width - VillageRules.EdgeMargin || y >= map.SurfaceHeight - VillageRules.EdgeMargin) return false;
                var t = map[x, y];
                if (t is not (TileType.Grass or TileType.Sand or TileType.Road)) return false;
            }
            float nest2 = VillageRules.NestClearance * VillageRules.NestClearance;
            foreach (var n in map.Nests)
                if ((n.Cell - centre).sqrMagnitude < nest2) return false;
            // And of the ruins' ways down (P4-85), as of the dens.
            foreach (var r in map.Ruins)
                if ((r.Entrance - centre).sqrMagnitude < nest2) return false;
            float apart2 = VillageRules.Spacing * VillageRules.Spacing;
            foreach (var o in others)
                if ((o.Cell - centre).sqrMagnitude < apart2) return false;
            return true;
        }

        /// <summary>The icon's cells round its centre.</summary>
        public static RectInt Footprint(Vector2Int centre)
            => new(centre.x - VillageRules.Size / 2, centre.y - VillageRules.Size / 2, VillageRules.Size, VillageRules.Size);

        // ---------------- Names ----------------

        static readonly Dictionary<Region, string[]> Names = new()
        {
            [Region.Desert] = new[] { "모래샘", "붉은바위", "낙타고개", "해마루", "마른우물", "황금언덕" },
            [Region.Snow] = new[] { "서리목", "얼음골", "흰여울", "눈꽃재", "겨울나루", "북녘솔" },
            [Region.Swamp] = new[] { "갈대늪", "안개못", "이끼다리", "버들섬", "개구리여울", "물안개골" },
            [Region.Wasteland] = new[] { "잿빛둔덕", "검은재", "부싯돌골", "마른가지", "돌무지", "바람벌" },
        };

        static VillageSite Named(VillageSite site, List<VillageSite> taken, GameRandom rng)
        {
            var used = new List<string>();
            foreach (var s in taken) used.Add(s.Name);
            site.Name = PickName(site.Region, used, rng);
            return site;
        }

        /// <summary>A name of the region not yet in use ("○○ 2" when all are).</summary>
        public static string PickName(Region region, IReadOnlyCollection<string> inUse, GameRandom rng)
        {
            if (!Names.TryGetValue(region, out var pool)) pool = new[] { "작은" };
            var free = new List<string>();
            foreach (var n in pool) if (!Contains(inUse, n)) free.Add(n);
            if (free.Count > 0) return free[rng.Range(0, free.Count)];
            string root = pool[rng.Range(0, pool.Length)];
            for (int k = 2; ; k++)
                if (!Contains(inUse, $"{root} {k}")) return $"{root} {k}";
        }

        static bool Contains(IReadOnlyCollection<string> list, string name)
        {
            foreach (var n in list) if (n == name) return true;
            return false;
        }
    }
}
