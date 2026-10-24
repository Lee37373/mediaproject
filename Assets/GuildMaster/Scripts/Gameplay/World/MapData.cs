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
        /// <summary>Nothing: the dark between the surface and the underground (P4-85). Not walkable, blocks sight.</summary>
        Void,
        /// <summary>A ruin's underground floor (P4-85).</summary>
        RuinFloor,
        /// <summary>A ruin's underground wall (P4-85): not walkable, blocks sight.</summary>
        RuinWall,
    }

    /// <summary>
    /// A ruin (P4-85): a way down on the surface and the dungeon it leads to, in the underground band above the map
    /// (<see cref="Underground"/>). The two portal cells are linked: a route that crosses between them steps from one
    /// to the other (<see cref="WorldService.FindPath"/>, <see cref="PathFollower"/>).
    /// </summary>
    public class RuinSite
    {
        public int Index;
        /// <summary>The way down: a walkable surface cell.</summary>
        public Vector2Int Entrance;
        /// <summary>The way up: a walkable cell in the first room.</summary>
        public Vector2Int Inside;
        /// <summary>The dungeon's cells (walls included) in the underground band.</summary>
        public RectInt Area;
        /// <summary>The middle of the last room, where the treasure is.</summary>
        public Vector2Int Chest;
        /// <summary>Its rooms from the first (the way up) to the treasure room.</summary>
        public readonly List<RectInt> Rooms = new();
        /// <summary>The surface region round the entrance (the dungeon takes its look and its kinds).</summary>
        public Region Region;
        /// <summary>How many ruins of its region came before it (names it among them).</summary>
        public int RegionOrdinal;
    }

    public enum Biome : byte { Plains, Forest, Mountain, Shore }

    /// <summary>A way across: a trail cleared through a wood belt (P4-103; a cut through rock before), a bridge, a ford.</summary>
    public enum PassKind : byte { Trail, Bridge, Ford }

    /// <summary>
    /// A way across the ridge or river between two regions (P4-101, <see cref="RegionBarriers"/>), or one cut to land
    /// the barriers and landforms had shut in (<see cref="A"/> == <see cref="B"/>).
    /// </summary>
    public struct PassSite
    {
        public Vector2Int Cell;
        public PassKind Kind;
        public Region A, B;
        /// <summary>Unit vector across the barrier, from <see cref="A"/>'s side to <see cref="B"/>'s.</summary>
        public Vector2 Across;
        /// <summary>One of the gates' roads runs through it.</summary>
        public bool OnRoad;
        /// <summary>The den placed to hold it (index into <see cref="MapData.Nests"/>), or -1.</summary>
        public int GuardNest;
    }

    public struct NestSite
    {
        public Vector2Int Cell;
        public Biome Biome;
        /// <summary>Terrain region of the site (P4-58: species live by region).</summary>
        public Region Region;
        /// <summary>Walk distance from the town centre in cells; species are placed by it (P5-1).</summary>
        public int DistanceFromTown;
        /// <summary>The zone it lies in (P4-102, index into <see cref="MapData.Zones"/>): dens of a zone keep to one kind.</summary>
        public int Zone;
    }

    /// <summary>What sets a zone apart (P4-102).</summary>
    public enum ZoneKind : byte
    {
        Plain,
        /// <summary>A pocket of danger: its monsters are born stronger than the distance says.</summary>
        Pocket,
        /// <summary>A haven: born weaker, a village's site.</summary>
        Haven,
    }

    /// <summary>The lie of a zone's land (P4-102): what its ground is mostly made of.</summary>
    public enum ZoneCharacter : byte
    {
        Mixed,
        /// <summary>Open ground: few lakes, rocks or woods.</summary>
        Open,
        /// <summary>Lakes and pools (oases, frozen lakes, bog pools, lava lakes).</summary>
        Lakes,
        /// <summary>Thick woods.</summary>
        Woods,
        /// <summary>Rocky highland.</summary>
        Rocky,
    }

    /// <summary>
    /// One zone of the map (P4-102, <see cref="ZoneMap"/>): a named stretch of land in one region, with its own danger.
    /// Zone 0 is the town's own fields.
    /// </summary>
    public class ZoneSite
    {
        public int Index;
        public string Name;
        public Region Region;
        public ZoneKind Kind;
        public ZoneCharacter Character;
        /// <summary>The middle of its cells.</summary>
        public Vector2 Centre;
        /// <summary>Open walkable ground near its middle: where its roads lead and its dens gather round.</summary>
        public Vector2Int Heart;
        public int Cells;
        /// <summary>Added to the depth its monsters are born at (0..2, <see cref="Ecosystem.EcosystemManager.Depth"/>).</summary>
        public float DangerShift;
        /// <summary>The region's landmark (a volcano, mesas, a frozen lake, a great mire) lies in it.</summary>
        public bool Landmark;
        /// <summary>A road leads to its heart.</summary>
        public bool OnRoad;
        public readonly List<int> Neighbours = new();
    }

    /// <summary>Pure data result of map generation (no Unity objects).</summary>
    public class MapData
    {
        public readonly int Width;
        /// <summary>All the rows: the surface and, above it, the underground band (P4-85) when there is one.</summary>
        public readonly int Height;
        /// <summary>
        /// The surface's rows (P4-85): what the map is to anything that means the land — edges, random spots, the minimap,
        /// the camera's bounds. Equal to <see cref="Height"/> on a map without an underground.
        /// </summary>
        public readonly int SurfaceHeight;
        public readonly TileType[] Tiles;
        /// <summary>The ruins and their dungeons (P4-85, <see cref="Underground"/>); none on a plain generated map.</summary>
        public readonly List<RuinSite> Ruins = new();
        public RectInt TownRect;
        public Vector2Int TownCenter;
        /// <summary>Cells from the centre to the walls now (P4-86: the walls move out a step at a time, <see cref="TownWalls"/>).</summary>
        public int TownHalfSize;
        /// <summary>The walls' half size when the map was made.</summary>
        public int BaseTownHalfSize;
        /// <summary>Gate centre cells, always north, south, west, east (saves and the gate objects go by this order).</summary>
        public readonly List<Vector2Int> Gates = new();
        public readonly List<NestSite> Nests = new();
        public readonly List<Vector2Int> EdgeSpawnCells = new();
        /// <summary>The ways across the region barriers (P4-101); none on a map made without landforms.</summary>
        public readonly List<PassSite> Passes = new();

        /// <summary>Forest cells that contain a tree (walkable, but give partial cover against projectiles).</summary>
        public readonly bool[] Trees;

        /// <summary>Terrain region of each cell (P4-55); all grassland unless the generator lays them out.</summary>
        public readonly Region[] Regions;

        public Region RegionOf(int x, int y) => InBounds(x, y) ? Regions[y * Width + x] : Region.Grassland;

        /// <summary>The zones (P4-102, <see cref="ZoneMap"/>); none on a hand-made map.</summary>
        public readonly List<ZoneSite> Zones = new();
        /// <summary>Zone of each cell (index into <see cref="Zones"/>), -1 where there is none (the underground, a hand-made map).</summary>
        public readonly short[] ZoneIds;
        /// <summary>
        /// The deep woods (P4-103, user request 2026-10-24: "not rock between the lands, a very dangerous forest"): forest
        /// cells of the belts on the region borders — slow, dark, with miasma, traps and dens in them (<see cref="DeepWoods"/>).
        /// </summary>
        public readonly bool[] Deep;

        public bool IsDeep(int x, int y) => InBounds(x, y) && Deep[y * Width + x];

        /// <summary>Spots kept for villages (P4-102): open ground at road ends in the havens and the quieter zones.</summary>
        public readonly List<Vector2Int> SettlementSites = new();

        /// <summary>The zone at a cell, or null.</summary>
        public ZoneSite ZoneAt(int x, int y)
        {
            if (!InBounds(x, y)) return null;
            int z = ZoneIds[y * Width + x];
            return z >= 0 && z < Zones.Count ? Zones[z] : null;
        }

        public MapData(int width, int height) : this(width, height, height) { }

        public MapData(int width, int height, int surfaceHeight)
        {
            Width = width;
            Height = height;
            SurfaceHeight = Mathf.Clamp(surfaceHeight, 1, height);
            Tiles = new TileType[width * height];
            Trees = new bool[width * height];
            Regions = new Region[width * height];
            Deep = new bool[width * height];
            ZoneIds = new short[width * height];
            System.Array.Fill(ZoneIds, (short)-1);
        }

        /// <summary>Above the surface: in the underground band (P4-85).</summary>
        public bool IsUnderground(Vector2Int c) => c.y >= SurfaceHeight;

        /// <summary>The ruin whose dungeon holds this cell, or null (on the surface, or in the dark between).</summary>
        public RuinSite RuinAt(Vector2Int c)
        {
            if (c.y < SurfaceHeight) return null;
            foreach (var r in Ruins) if (r.Area.Contains(c)) return r;
            return null;
        }

        public bool HasTree(int x, int y) => InBounds(x, y) && Trees[y * Width + x];

        /// <summary>Terrain that always stops projectiles and line of sight.</summary>
        public static bool IsHardCover(TileType t) => t is TileType.Mountain or TileType.Wall or TileType.Void or TileType.RuinWall;

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

        public static bool IsWalkable(TileType t)
            => t != TileType.Water && t != TileType.Mountain && t != TileType.Wall && t != TileType.Void && t != TileType.RuinWall;

        public static float MoveCost(TileType t) => t switch
        {
            TileType.RuinFloor => 1f,
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
