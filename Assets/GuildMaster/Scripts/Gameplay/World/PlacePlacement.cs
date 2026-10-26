using System;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// Lays the places worth going to (P4-105) over a finished map, from their own stream of the seed and without touching the
    /// ground — the same seed gives the same places, and the map's other features do not move (no new save version). Each kind
    /// has its ground: hot springs by water, ore veins against rock, herb patches at a forest's edge or in the swamp, abandoned
    /// camps by a road, shrines and ruined watchtowers on open ground; all on walkable ground clear of the town (and the room it
    /// may grow into), the roads, the deep woods, the dens, the ruins' ways down, the passes and one another. Some of the gathering
    /// spots lie close to town, the rest further out.
    /// </summary>
    public static class PlacePlacement
    {
        const int Salt = 0x7A3E;
        /// <summary>Tries per place before a kind gives up (a map may have room for fewer).</summary>
        const int TriesPerPlace = 900;
        /// <summary>Kept clear: the town and the room its walls may grow into (P4-86), the dens, the ruins' ways down, the passes, other places.</summary>
        const int TownMargin = 16, NestClearance = 10, RuinClearance = 10, PassClearance = 6, PlaceSpacing = 20;

        /// <summary>What one kind wants: how many, how far from the town, how many of them close in, how far apart.</summary>
        public readonly struct Spec
        {
            public readonly PlaceKind Kind;
            public readonly int Count, NearCount;
            public readonly float MinDistance, MaxDistance, NearDistance, Spacing;

            public Spec(PlaceKind kind, int count, float min, float max, float spacing, int nearCount = 0, float nearDistance = 0f)
            {
                Kind = kind;
                Count = count;
                MinDistance = min;
                MaxDistance = max;
                Spacing = spacing;
                NearCount = nearCount;
                NearDistance = nearDistance;
            }
        }

        public static readonly Spec[] Specs =
        {
            new(PlaceKind.HotSpring, 4, 60f, 260f, 60f),
            new(PlaceKind.Shrine, 3, 70f, 280f, 120f),
            new(PlaceKind.OreVein, 8, 35f, 300f, 30f, 2, 80f),
            new(PlaceKind.HerbPatch, 10, 25f, 300f, 30f, 3, 70f),
            new(PlaceKind.OldCamp, 5, 60f, 260f, 60f),
            new(PlaceKind.WatchRuin, 5, 70f, 300f, 90f),
        };

        public static void Lay(MapData map, int seed)
        {
            map.Places.Clear();
            var rng = new System.Random(seed ^ Salt);
            var town = map.TownCenter;
            foreach (var spec in Specs)
            {
                // Strict ground first; a map short of it (little rock since the ridges became woods, P4-103) gets a second,
                // looser look so every kind still turns up.
                int placed = 0;
                foreach (bool loose in new[] { false, true })
                    for (int t = 0; t < TriesPerPlace * spec.Count && placed < spec.Count; t++)
                    {
                        bool near = placed < spec.NearCount;
                        float min = spec.MinDistance, max = near ? Mathf.Max(min + 1f, spec.NearDistance) : spec.MaxDistance;
                        double angle = rng.NextDouble() * Math.PI * 2.0;
                        double d = min + rng.NextDouble() * (max - min);
                        var cell = new Vector2Int(town.x + (int)Math.Round(Math.Cos(angle) * d), town.y + (int)Math.Round(Math.Sin(angle) * d));
                        if (!Fits(map, cell, spec, loose)) continue;
                        map.Places.Add(new PlaceSite { Index = map.Places.Count, Kind = spec.Kind, Cell = cell });
                        placed++;
                    }
            }
        }

        static bool Fits(MapData map, Vector2Int c, in Spec spec, bool loose)
        {
            if (c.x < 2 || c.y < 2 || c.x >= map.Width - 2 || c.y >= map.SurfaceHeight - 2) return false;
            int i = map.Index(c.x, c.y);
            var t = map.Tiles[i];
            if (t != TileType.Grass && t != TileType.Sand || map.Deep[i]) return false;
            var town = map.TownRect;
            if (c.x >= town.xMin - TownMargin && c.x < town.xMax + TownMargin && c.y >= town.yMin - TownMargin && c.y < town.yMax + TownMargin) return false;
            // Off the road itself (a camp wants one close by, not under it).
            if (Near(map, c, 1, j => map.Tiles[j] == TileType.Road)) return false;
            foreach (var n in map.Nests) if ((n.Cell - c).sqrMagnitude < NestClearance * NestClearance) return false;
            foreach (var r in map.Ruins) if ((r.Entrance - c).sqrMagnitude < RuinClearance * RuinClearance) return false;
            foreach (var p in map.Passes) if ((p.Cell - c).sqrMagnitude < PassClearance * PassClearance) return false;
            foreach (var p in map.Places)
            {
                float apart = p.Kind == spec.Kind ? spec.Spacing : PlaceSpacing;
                if ((p.Cell - c).sqrMagnitude < apart * apart) return false;
            }
            int reach = loose ? 2 : 1;
            return spec.Kind switch
            {
                PlaceKind.HotSpring => Near(map, c, 4 * reach, j => map.Tiles[j] == TileType.Water),
                PlaceKind.OreVein => Near(map, c, loose ? 8 : 3, j => map.Tiles[j] == TileType.Mountain)
                                     || loose && map.Regions[i] is Region.Desert or Region.Wasteland,
                PlaceKind.HerbPatch => map.Regions[i] == Region.Swamp || Near(map, c, 3 * reach, j => map.Tiles[j] == TileType.Forest && !map.Deep[j]),
                PlaceKind.OldCamp => Near(map, c, 6 * reach, j => map.Tiles[j] == TileType.Road),
                // Open ground: nothing in the way of a shrine or a lookout.
                _ => !Near(map, c, 2, j => map.Tiles[j] is TileType.Forest or TileType.Mountain or TileType.Water),
            };
        }

        /// <summary>Any cell within <paramref name="r"/> (square) of <paramref name="c"/> on the surface that matches.</summary>
        static bool Near(MapData map, Vector2Int c, int r, Func<int, bool> match)
        {
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = c.x + dx, y = c.y + dy;
                if (x < 0 || y < 0 || x >= map.Width || y >= map.SurfaceHeight) continue;
                if (match(map.Index(x, y))) return true;
            }
            return false;
        }
    }
}
