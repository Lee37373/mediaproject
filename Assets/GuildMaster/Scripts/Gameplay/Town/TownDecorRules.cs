using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>The things that make the town look lived in (P4-114), by kind: pictures only (<see cref="TownDecor"/>).</summary>
    public enum TownProp : byte { None, Lamp, Bush, FlowerBed, Tree, Barrels, Crates, Bench }

    /// <summary>
    /// Where the town's decorations stand (P4-114, pure): a lamp post beside the streets every <see cref="LampEvery"/> cells
    /// from the plaza, on alternate sides; and on the grass away from the streets, the plaza and the walls, now and then a
    /// bush, a flower bed, a tree, barrels, crates or a bench — by a fixed hash of the cell, so a town always looks the same.
    /// </summary>
    public static class TownDecorRules
    {
        public const int LampEvery = 4;
        /// <summary>Cells kept clear along the walls, and the plaza's radius kept clear (squared).</summary>
        public const int WallGap = 2, PlazaClear2 = 16;

        /// <summary>Chances per thousand cells of open grass, in order.</summary>
        static readonly (TownProp prop, int perMille)[] OnGrass =
        {
            (TownProp.Bush, 40), (TownProp.FlowerBed, 30), (TownProp.Tree, 16), (TownProp.Barrels, 10), (TownProp.Crates, 10), (TownProp.Bench, 8),
        };

        public static TownProp At(MapData map, int x, int y)
        {
            var town = map.TownRect;
            if (!map.InBounds(x, y) || map[x, y] != TileType.TownFloor) return TownProp.None;
            if (x < town.xMin + WallGap || x >= town.xMax - WallGap || y < town.yMin + WallGap || y >= town.yMax - WallGap) return TownProp.None;
            int dx = x - map.TownCenter.x, dy = y - map.TownCenter.y;
            if (Lamp(dx, dy) || Lamp(dy, dx)) return TownProp.Lamp;
            if (Mathf.Abs(dx) <= 1 || Mathf.Abs(dy) <= 1 || dx * dx + dy * dy <= PlazaClear2) return TownProp.None;
            int roll = (int)(Hash(x, y) % 1000u);
            foreach (var (prop, perMille) in OnGrass)
            {
                if (roll < perMille) return prop;
                roll -= perMille;
            }
            return TownProp.None;
        }

        /// <summary>Beside the street that runs along <paramref name="along"/>: every few cells from the plaza, sides taking turns.</summary>
        static bool Lamp(int along, int across)
            => Mathf.Abs(across) == 1 && along != 0 && along % LampEvery == 0 && across == (((along / LampEvery) & 1) == 0 ? 1 : -1);

        /// <summary>Which of a kind's four looks a cell gets.</summary>
        public static int Variant(int x, int y) => (int)(Hash(x + 17, y - 5) % 4u);

        static uint Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return h;
            }
        }
    }
}
