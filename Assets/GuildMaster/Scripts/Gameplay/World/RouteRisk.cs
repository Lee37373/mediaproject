using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// How risky a cell is to walk through (P4-63, user decision 2026-09-23). An ambush needs cover,
    /// so the risk of a cell is exactly how much shorter the view is there: the forest (a third,
    /// <see cref="ForestSight.InForestFactor"/>) and the regions that shorten sight — swamp mist,
    /// blowing snow, ash (<see cref="TerrainRegions.SightMultiplier"/>). Open grassland is 0.
    ///
    /// A path weighs this by the walker's caution (<c>GuildMaster.Units.RouteChoice</c>), which is a
    /// party decision: 0 walks the shortest way (in a hurry), a positive value goes round, and a
    /// negative one (a pack that hunts from cover) takes the woods for the shortcut they are.
    /// The layer is terrain only, so it holds no hidden information about monsters.
    /// </summary>
    public class RouteRisk
    {
        readonly MapData _map;
        float[] _risk;
        /// <summary>Forest cells burnt down (P4-72): open ground until they grow back.</summary>
        bool[] _cleared;

        public RouteRisk(MapData map) => _map = map;

        /// <summary>Risk of one cell, 0 (open) to almost 1 (forest in the mist): the view lost there.</summary>
        public static float Of(MapData map, int x, int y, bool cleared = false)
        {
            if (map == null || !map.InBounds(x, y)) return 0f;
            int i = map.Index(x, y);
            float sight = TerrainRegions.SightMultiplier(map.Regions[i]);
            if (map.Tiles[i] == TileType.Forest && !cleared) sight *= ForestSight.InForestFactor;
            return Mathf.Clamp01(1f - sight);
        }

        /// <summary>Baked on first use, then a plain lookup (a burnt forest updates its cells, <see cref="SetForestCleared"/>).</summary>
        public float At(int x, int y)
        {
            if (!_map.InBounds(x, y)) return 0f;
            if (_risk == null) Bake();
            return _risk[_map.Index(x, y)];
        }

        void Bake()
        {
            var risk = new float[_map.Width * _map.Height];
            for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++)
                risk[_map.Index(x, y)] = Of(_map, x, y, Cleared(_map.Index(x, y)));
            _risk = risk;
        }

        bool Cleared(int i) => _cleared != null && _cleared[i];

        /// <summary>The tiles here changed (P4-86: the town walls moved out and the woods round them were cleared).</summary>
        public void Refresh(RectInt cells)
        {
            for (int y = Mathf.Max(0, cells.yMin); y < Mathf.Min(_map.Height, cells.yMax); y++)
            for (int x = Mathf.Max(0, cells.xMin); x < Mathf.Min(_map.Width, cells.xMax); x++)
            {
                int i = _map.Index(x, y);
                if (_cleared != null && _map.Tiles[i] != TileType.Forest) _cleared[i] = false;
                if (_risk != null) _risk[i] = Of(_map, x, y, Cleared(i));
            }
        }

        /// <summary>A forest cell burnt down, or grown back (P4-72).</summary>
        public void SetForestCleared(int x, int y, bool cleared)
        {
            if (!_map.InBounds(x, y)) return;
            int i = _map.Index(x, y);
            _cleared ??= new bool[_map.Width * _map.Height];
            _cleared[i] = cleared;
            if (_risk != null) _risk[i] = Of(_map, x, y, cleared);
        }

        /// <summary>
        /// What one cell costs a walker of this nerve, as a multiple of its walking cost. Caution
        /// above 0 makes cover expensive; below 0 it is a discount, and only in the forest itself —
        /// a goblin band takes the woods, not every foggy swamp.
        /// </summary>
        public float Multiplier(int x, int y, float caution)
        {
            if (Mathf.Approximately(caution, 0f)) return 1f;
            if (caution < 0f && (!_map.InBounds(x, y) || _map.Tiles[_map.Index(x, y)] != TileType.Forest || Cleared(_map.Index(x, y)))) return 1f;
            return Mathf.Max(0.05f, 1f + caution * At(x, y));
        }

        /// <summary>Smallest multiplier this caution can produce: keeps the A* heuristic admissible.</summary>
        public static float LowestMultiplier(float caution) => caution >= 0f ? 1f : Mathf.Max(0.05f, 1f + caution);
    }
}
