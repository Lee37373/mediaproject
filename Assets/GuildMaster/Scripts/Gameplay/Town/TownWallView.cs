using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>What a wall cell shows (P4-115): a stretch with its battlements on the outer side, a corner tower, or a gate's tower.</summary>
    public enum WallPiece { AlongNorth, AlongSouth, AlongWest, AlongEast, Corner, GateTower }

    /// <summary>
    /// The town's walls standing up (P4-115, user request 2026-10-31: they were a flat strip of grey bricks). Each wall
    /// cell of the tile map gets its own picture (<see cref="CartoonArt.Wall"/>), sorted among the people by the
    /// cell's foot like everything that stands, so the wall hides whoever is just behind it. Only the pictures: the tiles,
    /// the walking and the gates are as they were. When the walls move out (P4-86) they are put up again.
    /// </summary>
    public class TownWallView : MonoBehaviour
    {
        WorldService _world;
        readonly List<(Vector2Int cell, WallPiece piece, SpriteRenderer sr)> _pieces = new();

        public IEnumerable<(Vector2Int cell, WallPiece piece)> Pieces
        {
            get { foreach (var p in _pieces) yield return (p.cell, p.piece); }
        }

        public void Init(WorldService world)
        {
            _world = world;
            EventBus.Subscribe<TownGroundChangedEvent>(OnTownGround);
            Raise();
        }

        void OnDestroy() => EventBus.Unsubscribe<TownGroundChangedEvent>(OnTownGround);

        void OnTownGround(TownGroundChangedEvent e) => Raise();

        void Raise()
        {
            foreach (var p in _pieces)
                if (p.sr != null) Destroy(p.sr.gameObject);
            _pieces.Clear();
            var map = _world.Map;
            var town = map.TownRect;
            for (int y = town.yMin; y < town.yMax; y++)
            for (int x = town.xMin; x < town.xMax; x++)
            {
                if (map[x, y] != TileType.Wall) continue;
                var piece = PieceAt(map, x, y);
                var go = new GameObject("Wall_" + piece);
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(x + 0.5f, y, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = CartoonArt.Wall(piece, Variant(x, y));
                // Towers stand over the stretches beside them in the same row.
                bool tower = piece is WallPiece.Corner or WallPiece.GateTower;
                sr.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(y * 8f) + (tower ? 2 : 1);
                _pieces.Add((new Vector2Int(x, y), piece, sr));
            }
        }

        /// <summary>
        /// The piece for a wall cell: beside a gate, its tower; where the ring turns, a corner tower; otherwise a stretch,
        /// its battlements on the side away from the town.
        /// </summary>
        public static WallPiece PieceAt(MapData map, int x, int y)
        {
            bool Gate(int cx, int cy) => map.InBounds(cx, cy) && map[cx, cy] == TileType.Gate;
            bool Ring(int cx, int cy) => map.InBounds(cx, cy) && map[cx, cy] is TileType.Wall or TileType.Gate;
            if (Gate(x + 1, y) || Gate(x - 1, y) || Gate(x, y + 1) || Gate(x, y - 1)) return WallPiece.GateTower;
            bool across = Ring(x + 1, y) || Ring(x - 1, y), along = Ring(x, y + 1) || Ring(x, y - 1);
            if (across && along) return WallPiece.Corner;
            var c = map.TownCenter;
            if (across) return y > c.y ? WallPiece.AlongNorth : WallPiece.AlongSouth;
            return x < c.x ? WallPiece.AlongWest : WallPiece.AlongEast;
        }

        /// <summary>A stretch's wear, fixed by the cell: mostly plain.</summary>
        static int Variant(int x, int y)
        {
            int h = (x * 73856093) ^ (y * 19349663);
            h = (h ^ (h >> 13)) & 0x7fffffff;
            return h % 7 == 0 ? 1 : h % 11 == 0 ? 2 : 0;
        }
    }
}
