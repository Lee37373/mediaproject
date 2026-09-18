using System.Collections.Generic;
using GuildMaster.Visuals;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace GuildMaster.World
{
    /// <summary>Builds ground and decoration Tilemaps from MapData using runtime-created tiles.</summary>
    public class MapRenderer : MonoBehaviour
    {
        public const int GroundOrder = 0;
        public const int DecorOrder = 1;
        const int Variants = 4;

        Tilemap _ground;
        Tilemap _decor;
        readonly Dictionary<(TileType, int), Tile> _groundTiles = new();
        readonly List<Tile> _trees = new();
        readonly List<Tile> _rocks = new();

        public void Build(MapData map)
        {
            if (!TryGetComponent<Grid>(out var grid)) grid = gameObject.AddComponent<Grid>();
            grid.cellSize = Vector3.one;
            _ground = CreateTilemap("Ground", GroundOrder);
            _decor = CreateTilemap("Decor", DecorOrder);

            for (int v = 0; v < Variants; v++)
            {
                _trees.Add(MakeTile(PixelArt.Tree(v)));
                _rocks.Add(MakeTile(PixelArt.Rock(v)));
            }

            int n = map.Width * map.Height;
            var positions = new Vector3Int[n];
            var groundTiles = new TileBase[n];
            var decorPositions = new List<Vector3Int>();
            var decorTiles = new List<TileBase>();

            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                int i = map.Index(x, y);
                var type = map.Tiles[i];
                int hash = Hash(x, y);
                positions[i] = new Vector3Int(x, y, 0);
                groundTiles[i] = GroundTile(type, hash % Variants);

                if (map.Trees[i])
                {
                    decorPositions.Add(positions[i]);
                    decorTiles.Add(_trees[(hash / 7) % Variants]);
                }
                else if (type == TileType.Mountain && hash % 100 < 45)
                {
                    decorPositions.Add(positions[i]);
                    decorTiles.Add(_rocks[(hash / 7) % Variants]);
                }
            }
            _ground.SetTiles(positions, groundTiles);
            _decor.SetTiles(decorPositions.ToArray(), decorTiles.ToArray());
        }

        /// <summary>Refresh a single ground tile (e.g. when terrain changes).</summary>
        public void SetGround(Vector2Int cell, TileType type)
        {
            _ground.SetTile((Vector3Int)cell, GroundTile(type, Hash(cell.x, cell.y) % Variants));
        }

        public void ClearDecor(RectInt area)
        {
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
                _decor.SetTile(new Vector3Int(x, y, 0), null);
        }

        Tile GroundTile(TileType type, int variant)
        {
            if (_groundTiles.TryGetValue((type, variant), out var t)) return t;
            t = MakeTile(PixelArt.TerrainTile(type, variant));
            _groundTiles[(type, variant)] = t;
            return t;
        }

        Tilemap CreateTilemap(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var tm = go.AddComponent<Tilemap>();
            var r = go.AddComponent<TilemapRenderer>();
            r.sortingOrder = order;
            return tm;
        }

        static Tile MakeTile(Sprite sprite)
        {
            var t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = sprite;
            t.colliderType = Tile.ColliderType.None;
            return t;
        }

        static int Hash(int x, int y)
        {
            unchecked
            {
                int h = x * 73856093 ^ y * 19349663;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                return h & 0x7fffffff;
            }
        }
    }
}
