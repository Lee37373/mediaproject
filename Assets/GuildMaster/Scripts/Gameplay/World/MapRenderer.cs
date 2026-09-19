using System.Collections.Generic;
using System.Threading.Tasks;
using GuildMaster.Visuals;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace GuildMaster.World
{
    /// <summary>
    /// Draws the map in the cartoon style of the character art. The ground is painted once into
    /// 16×16-cell chunk textures (curved, outlined terrain edges; see <see cref="CartoonGroundPainter"/>);
    /// trees and rocks sit on a decoration Tilemap. Gameplay keeps using the tile grid.
    /// </summary>
    public class MapRenderer : MonoBehaviour
    {
        public const int GroundOrder = 0;
        public const int DecorOrder = 1;
        const int Variants = 4;
        const int ChunkCells = 16;
        const int GroundPixelsPerCell = 32;

        Tilemap _decor;
        readonly List<Tile> _trees = new();
        readonly List<Tile> _rocks = new();

        public void Build(MapData map)
        {
            if (!TryGetComponent<Grid>(out var grid)) grid = gameObject.AddComponent<Grid>();
            grid.cellSize = Vector3.one;
            BuildGround(map);
            _decor = CreateTilemap("Decor", DecorOrder);

            for (int v = 0; v < Variants; v++)
            {
                _trees.Add(MakeTile(CartoonArt.Tree(v)));
                _rocks.Add(MakeTile(CartoonArt.Rock(v)));
            }

            var decorPositions = new List<Vector3Int>();
            var decorTiles = new List<TileBase>();
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                int i = map.Index(x, y);
                int hash = Hash(x, y);
                if (map.Trees[i])
                {
                    decorPositions.Add(new Vector3Int(x, y, 0));
                    decorTiles.Add(_trees[(hash / 7) % Variants]);
                }
                else if (map.Tiles[i] == TileType.Mountain && hash % 100 < 30)
                {
                    decorPositions.Add(new Vector3Int(x, y, 0));
                    decorTiles.Add(_rocks[(hash / 7) % Variants]);
                }
            }
            _decor.SetTiles(decorPositions.ToArray(), decorTiles.ToArray());
        }

        /// <summary>Paints all ground chunks (in parallel), then uploads them as sprites.</summary>
        void BuildGround(MapData map)
        {
            var root = new GameObject("Ground").transform;
            root.SetParent(transform, false);
            var painter = new CartoonGroundPainter(map, GroundPixelsPerCell);
            int cw = (map.Width + ChunkCells - 1) / ChunkCells, ch = (map.Height + ChunkCells - 1) / ChunkCells;
            var buffers = new Color32[cw * ch][];
            var sizes = new Vector2Int[cw * ch];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Parallel.For(0, cw * ch, i =>
            {
                int tx = i % cw * ChunkCells, ty = i / cw * ChunkCells;
                int tw = Mathf.Min(ChunkCells, map.Width - tx), th = Mathf.Min(ChunkCells, map.Height - ty);
                var px = new Color32[tw * GroundPixelsPerCell * th * GroundPixelsPerCell];
                painter.Paint(tx, ty, tw, th, px);
                buffers[i] = px;
                sizes[i] = new Vector2Int(tw, th);
            });
            for (int i = 0; i < buffers.Length; i++)
            {
                int tx = i % cw * ChunkCells, ty = i / cw * ChunkCells;
                int pw = sizes[i].x * GroundPixelsPerCell, ph = sizes[i].y * GroundPixelsPerCell;
                var tex = new Texture2D(pw, ph, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = $"GroundChunk_{tx}_{ty}",
                };
                tex.SetPixels32(buffers[i]);
                tex.Apply(false, true); // free the CPU copy
                var go = new GameObject(tex.name);
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3(tx, ty, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Sprite.Create(tex, new Rect(0, 0, pw, ph), Vector2.zero, GroundPixelsPerCell, 0, SpriteMeshType.FullRect);
                sr.sortingOrder = GroundOrder;
            }
            Debug.Log($"[Map] Ground painted: {buffers.Length} chunks in {sw.ElapsedMilliseconds} ms");
        }

        public void ClearDecor(RectInt area)
        {
            for (int y = area.yMin; y < area.yMax; y++)
            for (int x = area.xMin; x < area.xMax; x++)
                _decor.SetTile(new Vector3Int(x, y, 0), null);
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
