using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.World
{
    public enum TileType : byte
    {
        Grass,
        Forest,
        Sand,
        Water,
        Mountain,
        Road,
        TownFloor,
        Wall,
        Gate,
    }

    public enum Biome : byte { Plains, Forest, Mountain, Shore }

    public struct NestSite
    {
        public Vector2Int Cell;
        public Biome Biome;
        /// <summary>Terrain region of the site (P4-58: species live by region).</summary>
        public Region Region;
        /// <summary>Walk distance from the town centre in cells; species are placed by it (P5-1).</summary>
        public int DistanceFromTown;
    }

    /// <summary>Pure data result of map generation (no Unity objects).</summary>
    public class MapData
    {
        public readonly int Width;
        public readonly int Height;
        public readonly TileType[] Tiles;
        public RectInt TownRect;
        public Vector2Int TownCenter;
        public readonly List<Vector2Int> Gates = new();
        public readonly List<NestSite> Nests = new();
        public readonly List<Vector2Int> EdgeSpawnCells = new();

        /// <summary>Forest cells that contain a tree (walkable, but give partial cover against projectiles).</summary>
        public readonly bool[] Trees;

        /// <summary>Terrain region of each cell (P4-55); all grassland unless the generator lays them out.</summary>
        public readonly Region[] Regions;
        public RegionLayout RegionLayout;

        public Region RegionOf(int x, int y) => InBounds(x, y) ? Regions[y * Width + x] : Region.Grassland;

        public MapData(int width, int height)
        {
            Width = width;
            Height = height;
            Tiles = new TileType[width * height];
            Trees = new bool[width * height];
            Regions = new Region[width * height];
        }

        public bool HasTree(int x, int y) => InBounds(x, y) && Trees[y * Width + x];

        /// <summary>Terrain that always stops projectiles and line of sight.</summary>
        public static bool IsHardCover(TileType t) => t == TileType.Mountain || t == TileType.Wall;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public bool InBounds(Vector2Int c) => InBounds(c.x, c.y);
        public int Index(int x, int y) => y * Width + x;

        public TileType this[int x, int y]
        {
            get => Tiles[y * Width + x];
            set => Tiles[y * Width + x] = value;
        }

        public TileType this[Vector2Int c]
        {
            get => Tiles[c.y * Width + c.x];
            set => Tiles[c.y * Width + c.x] = value;
        }

        public static bool IsWalkable(TileType t) => t != TileType.Water && t != TileType.Mountain && t != TileType.Wall;

        public static float MoveCost(TileType t) => t switch
        {
            TileType.Road => 0.7f,
            TileType.TownFloor => 0.8f,
            TileType.Gate => 0.8f,
            TileType.Grass => 1f,
            TileType.Sand => 1.25f,
            TileType.Forest => 1.6f,
            _ => float.PositiveInfinity,
        };
    }
}
