using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// Runtime navigation grid: terrain walkability/cost plus dynamic blockers (buildings).
    /// One tile = one world unit; cell (x,y) spans [x,x+1) x [y,y+1).
    /// </summary>
    public class GridMap
    {
        public readonly MapData Data;
        readonly bool[] _blocked;
        readonly bool[] _gateClosed;
        readonly HazardKind[] _hazard;

        public int Width => Data.Width;
        public int Height => Data.Height;

        /// <summary>Incremented whenever walkability changes, so cached paths can be revalidated.</summary>
        public int Version { get; private set; }

        public GridMap(MapData data)
        {
            Data = data;
            _blocked = new bool[data.Width * data.Height];
            _gateClosed = new bool[data.Width * data.Height];
            _hazard = new HazardKind[data.Width * data.Height];
        }

        public bool InBounds(Vector2Int c) => Data.InBounds(c);

        public bool IsWalkable(Vector2Int c) => IsWalkable(c.x, c.y);

        public bool IsWalkable(int x, int y)
        {
            if (!Data.InBounds(x, y)) return false;
            int i = y * Width + x;
            return !_blocked[i] && MapData.IsWalkable(Data.Tiles[i]);
        }

        /// <summary>
        /// Walkability for a unit that may or may not pass the town gates (P4-39): an intact gate
        /// opens for the guild and its guests, and stays shut for monsters until they break it.
        /// </summary>
        public bool IsWalkable(int x, int y, bool passesGates)
            => IsWalkable(x, y) && (passesGates || !_gateClosed[y * Width + x]);

        public bool IsWalkable(Vector2Int c, bool passesGates) => IsWalkable(c.x, c.y, passesGates);

        /// <summary>Any gate cell, standing or broken.</summary>
        public bool IsGate(int x, int y) => Data.InBounds(x, y) && Data.Tiles[y * Width + x] == TileType.Gate;

        /// <summary>A cell of a gate that is still standing (shut to monsters).</summary>
        public bool IsClosedGate(Vector2Int c) => Data.InBounds(c) && _gateClosed[c.y * Width + c.x];

        /// <summary>Every town gate is standing: nothing that cannot pass gates gets in or out.</summary>
        public bool TownSealed { get; set; }

        public void SetGateClosed(System.Collections.Generic.IEnumerable<Vector2Int> cells, bool closed)
        {
            foreach (var c in cells)
                if (Data.InBounds(c)) _gateClosed[c.y * Width + c.x] = closed;
            Version++;
        }

        /// <summary>Walking cost: the terrain, slowed further in harsh regions off the roads (P4-55).</summary>
        public float Cost(int x, int y)
        {
            int i = y * Width + x;
            var t = Data.Tiles[i];
            float cost = MapData.MoveCost(t) * Hazards.CostFactor(_hazard[i]);
            if (t is TileType.Road or TileType.TownFloor or TileType.Gate) return cost;
            cost *= TerrainRegions.MoveMultiplier(Data.Regions[i]);
            // The deep woods' undergrowth (P4-103), while they stand.
            return Data.Deep[i] && !Hazards.ClearsForest(_hazard[i]) && t == TileType.Forest ? cost * DeepWoodRules.MoveFactor : cost;
        }

        /// <summary>Deep woods standing here (P4-103: not burnt down).</summary>
        public bool IsDeep(int x, int y) => Data.IsDeep(x, y) && IsForest(x, y);
        public bool IsDeep(Vector2Int c) => IsDeep(c.x, c.y);

        /// <summary>Mountains, walls and buildings fully block projectiles / sight lines.</summary>
        public bool IsHardCover(int x, int y)
        {
            if (!Data.InBounds(x, y)) return true;
            int i = y * Width + x;
            return _blocked[i] || MapData.IsHardCover(Data.Tiles[i]);
        }

        public bool HasTree(int x, int y) => Data.HasTree(x, y) && !Hazards.ClearsForest(_hazard[y * Width + x]);

        /// <summary>Forest floor (P4-48: blocks the view from outside, shortens it inside).</summary>
        public bool IsForest(int x, int y)
            => Data.InBounds(x, y) && Data.Tiles[y * Width + x] == TileType.Forest && !Hazards.ClearsForest(_hazard[y * Width + x]);
        public bool IsForest(Vector2Int c) => IsForest(c.x, c.y);

        public float Cost(Vector2Int c) => Cost(c.x, c.y);

        // ---------------- Hazards (P4-72) ----------------

        public HazardKind HazardAt(int x, int y) => Data.InBounds(x, y) ? _hazard[y * Width + x] : HazardKind.None;
        public HazardKind HazardAt(Vector2Int c) => HazardAt(c.x, c.y);

        /// <summary>Lays a hazard on a cell (or clears it with <see cref="HazardKind.None"/>). Walkers re-check their paths when a wall of snow or a crack comes or goes.</summary>
        public void SetHazard(Vector2Int c, HazardKind kind)
        {
            if (!Data.InBounds(c)) return;
            int i = c.y * Width + c.x;
            var old = _hazard[i];
            if (old == kind) return;
            _hazard[i] = kind;
            if (Hazards.Blocks(old) != Hazards.Blocks(kind)) Version++;
        }

        /// <summary>The tiles themselves changed (P4-86: the walls moved out): walkers re-check their paths.</summary>
        public void TerrainChanged() => Version++;

        public void SetBlocked(RectInt area, bool blocked)
        {
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
                if (Data.InBounds(x, y)) _blocked[y * Width + x] = blocked;
            Version++;
        }

        public static Vector2Int WorldToCell(Vector2 world) => new(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));

        public static Vector2 CellToWorld(Vector2Int cell) => new(cell.x + 0.5f, cell.y + 0.5f);

        public bool IsInTown(Vector2Int c) => Data.TownRect.Contains(c);

        public bool IsInTown(Vector2 world) => IsInTown(WorldToCell(world));

        /// <summary>Spiral search for the nearest walkable cell (used when a target is inside an obstacle).</summary>
        public bool TryFindNearestWalkable(Vector2Int origin, int maxRadius, out Vector2Int result)
        {
            if (IsWalkable(origin)) { result = origin; return true; }
            for (int r = 1; r <= maxRadius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    var c = new Vector2Int(origin.x + dx, origin.y + dy);
                    if (IsWalkable(c)) { result = c; return true; }
                }
            }
            result = origin;
            return false;
        }

        /// <summary>Random walkable cell within radius (for wandering).</summary>
        public bool TryRandomWalkableNear(Vector2Int origin, int radius, Core.GameRandom rng, out Vector2Int result, int attempts = 12)
        {
            for (int i = 0; i < attempts; i++)
            {
                var c = new Vector2Int(origin.x + rng.Range(-radius, radius + 1), origin.y + rng.Range(-radius, radius + 1));
                if (IsWalkable(c)) { result = c; return true; }
            }
            result = origin;
            return false;
        }
    }
}
