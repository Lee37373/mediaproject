using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using GuildMaster.Core;
using GuildMaster.Visuals;
using Unity.Collections;
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
        MapData _map;
        readonly List<Tile> _trees = new();
        readonly List<Tile> _deepTrees = new();
        static readonly Color DeepTint = new(0.55f, 0.62f, 0.58f);
        readonly List<Tile> _rocks = new();
        /// <summary>Textures, sprites and tiles made here: runtime assets outlive the scene unless destroyed
        /// (PlayMode tests boot the game 40-odd times in one session; each boot leaked ~256 MB of ground).</summary>
        readonly List<Object> _owned = new();

        public void Build(MapData map)
        {
            _map = map;
            if (!TryGetComponent<Grid>(out var grid)) grid = gameObject.AddComponent<Grid>();
            grid.cellSize = Vector3.one;
            if (Session.GameSettings.PaintGround) BuildGround(map);
            _decor = CreateTilemap("Decor", DecorOrder);

            // One set per region (P4-55: dry scrub, frosted trees, dark swamp trees, charred stumps; rocks likewise).
            int regions = System.Enum.GetValues(typeof(Region)).Length;
            for (int r = 0; r < regions; r++)
            {
                for (int v = 0; v < Variants; v++)
                {
                    _trees.Add(MakeTile(CartoonArt.Tree(v, (Region)r)));
                    // The deep woods' trees (P4-103): the same, in the canopy's gloom.
                    var deep = MakeTile(CartoonArt.Tree(v, (Region)r));
                    deep.color = DeepTint;
                    _deepTrees.Add(deep);
                    _rocks.Add(MakeTile(CartoonArt.Rock(v, (Region)r)));
                }
            }

            var decorPositions = new List<Vector3Int>();
            var decorTiles = new List<TileBase>();
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var tile = DecorAt(x, y);
                if (tile == null) continue;
                decorPositions.Add(new Vector3Int(x, y, 0));
                decorTiles.Add(tile);
            }
            _decor.SetTiles(decorPositions.ToArray(), decorTiles.ToArray());
        }

        /// <summary>The tree or rock standing on a cell, or null.</summary>
        Tile DecorAt(int x, int y)
        {
            int i = _map.Index(x, y);
            int hash = Hash(x, y);
            if (_map.Trees[i]) return (_map.Deep[i] ? _deepTrees : _trees)[(int)_map.Regions[i] * Variants + (hash / 7) % Variants];
            if (_map.Tiles[i] == TileType.Mountain && hash % 100 < 30) return _rocks[(int)_map.Regions[i] * Variants + (hash / 7) % Variants];
            return null;
        }

        /// <summary>
        /// The tiles under these cells changed (P4-86: the walls moved out): their trees and rocks are set again and
        /// the ground chunks already painted there are painted again in the background, the old picture staying until
        /// the new one is ready (painting them on the spot froze the game for half a second); the ones not asked for
        /// yet will be painted from the new tiles when the fog lifts. The low-detail backdrop is left: painted chunks
        /// always cover it where anyone has looked.
        /// </summary>
        public void Repaint(RectInt cells)
        {
            if (_map == null) return;
            int xMin = Mathf.Max(0, cells.xMin), yMin = Mathf.Max(0, cells.yMin);
            int xMax = Mathf.Min(_map.Width, cells.xMax), yMax = Mathf.Min(_map.Height, cells.yMax);
            if (_decor != null)
                for (int y = yMin; y < yMax; y++)
                for (int x = xMin; x < xMax; x++)
                    _decor.SetTile(new Vector3Int(x, y, 0), DecorAt(x, y));
            if (_chunkState == null || xMax <= xMin || yMax <= yMin) return;

            _painter.Refresh(new RectInt(xMin, yMin, xMax - xMin, yMax - yMin));
            // Edges are smoothed from the cells round them: a chunk next to the change can change too.
            int x0 = Mathf.Max(0, (xMin - 2) / ChunkCells), x1 = Mathf.Min(_cw - 1, (xMax + 1) / ChunkCells);
            int y0 = Mathf.Max(0, (yMin - 2) / ChunkCells), y1 = Mathf.Min(_ch - 1, (yMax + 1) / ChunkCells);
            int again = 0;
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * _cw + x;
                // A worker painting the old tiles: its picture is thrown away when it comes back.
                _chunkGeneration[i]++;
                if (_chunkState[i] is not (ChunkState.Done or ChunkState.Painting)) continue;
                _chunkState[i] = ChunkState.Queued;
                _queue.Add(i);
                again++;
            }
            if (again > 0) Debug.Log($"[Map] Ground: {again} chunks to paint again");
        }

        // ---------------- Ground (P4-81) ----------------
        // At 32 px a cell the 512 map's ground is 1 GB of RGBA32 and about 36 s of painting. Now the
        // whole map is painted once at boot at BackdropPixelsPerCell (the backdrop, under everything),
        // and full chunks only where the fog lifts — and one chunk round it, ahead of the eye
        // (RequestGround, fed by FogRevealedEvent in GameBootstrap) — on worker threads, nearest the
        // camera first, compressed to BC1 (128 KB a chunk). Fog that never lifted is opaque, so the
        // ground under it is never missed.

        const int BackdropPixelsPerCell = 2;
        const int ChunkPixels = ChunkCells * GroundPixelsPerCell;
        /// <summary>Painted chunks turned into textures per frame (each ~0.1 ms).</summary>
        const int UploadsPerFrame = 6;

        enum ChunkState : byte { None, Queued, Painting, Done }

        struct PaintedChunk
        {
            public int Chunk;
            /// <summary>The chunk's <see cref="_chunkGeneration"/> when its painting began.</summary>
            public int Generation;
            public byte[] Data;
            public string Error;
        }

        CartoonGroundPainter _painter;
        Transform _groundRoot;
        ChunkState[] _chunkState;
        /// <summary>Bumped when a chunk's tiles change (P4-86): a picture begun before is out of date.</summary>
        int[] _chunkGeneration;
        /// <summary>Each painted chunk's sprite, to swap when it is painted again.</summary>
        SpriteRenderer[] _chunkRenderers;
        int _cw, _ch;
        int _workers;
        int _inFlight;
        readonly List<int> _queue = new();
        readonly ConcurrentQueue<PaintedChunk> _painted = new();
        readonly ConcurrentBag<Color32[]> _pixelPool = new();
        readonly ConcurrentBag<byte[]> _dataPool = new();
        Camera _camera;

        /// <summary>Ground chunks painted / in the map / asked for but not painted yet (debug, tests).</summary>
        public int GroundChunksPainted { get; private set; }
        public int GroundChunksTotal => _chunkState?.Length ?? 0;
        public int GroundChunksWaiting => _queue.Count + _inFlight;

        void BuildGround(MapData map)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _groundRoot = new GameObject("Ground").transform;
            _groundRoot.SetParent(transform, false);
            _painter = new CartoonGroundPainter(map, GroundPixelsPerCell);
            _cw = (map.Width + ChunkCells - 1) / ChunkCells;
            _ch = (map.Height + ChunkCells - 1) / ChunkCells;
            _chunkState = new ChunkState[_cw * _ch];
            _chunkGeneration = new int[_cw * _ch];
            _chunkRenderers = new SpriteRenderer[_cw * _ch];
            // Half the cores at most: the game keeps the main and render threads.
            _workers = Mathf.Clamp(SystemInfo.processorCount / 2, 1, 4);
            BuildBackdrop(map);
            Debug.Log($"[Map] Ground backdrop painted in {sw.ElapsedMilliseconds} ms ({_cw * _ch} chunks to paint where the fog lifts)");
        }

        /// <summary>The whole map at low detail, under the chunks: what shows before a chunk is painted.</summary>
        void BuildBackdrop(MapData map)
        {
            const int ppc = BackdropPixelsPerCell;
            int w = map.Width * ppc, h = map.Height * ppc;
            var pixels = new Color32[w * h];
            Parallel.For(0, _ch, row =>
            {
                int ty = row * ChunkCells, th = Mathf.Min(ChunkCells, map.Height - ty);
                var strip = new Color32[w * th * ppc];
                _painter.Paint(0, ty, map.Width, th, strip, ppc);
                System.Array.Copy(strip, 0, pixels, ty * ppc * w, strip.Length);
            });
            var tex = MakeGroundTexture(w, h, "GroundBackdrop", pixels, null);
            var go = new GameObject(tex.name);
            go.transform.SetParent(_groundRoot, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.zero, ppc, 0, SpriteMeshType.FullRect);
            _owned.Add(sr.sprite);
            sr.sortingOrder = GroundOrder - 1;
        }

        /// <summary>
        /// Asks for the ground under these cells, and one chunk round them so it is ready before anyone
        /// sees that far. Painted in the background; see <see cref="PaintRequestedNow"/>.
        /// </summary>
        public void RequestGround(RectInt cells)
        {
            if (_chunkState == null || cells.width <= 0 || cells.height <= 0) return;
            int x0 = Mathf.Max(0, cells.xMin / ChunkCells - 1), x1 = Mathf.Min(_cw - 1, (cells.xMax - 1) / ChunkCells + 1);
            int y0 = Mathf.Max(0, cells.yMin / ChunkCells - 1), y1 = Mathf.Min(_ch - 1, (cells.yMax - 1) / ChunkCells + 1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * _cw + x;
                if (_chunkState[i] != ChunkState.None) continue;
                _chunkState[i] = ChunkState.Queued;
                _queue.Add(i);
            }
        }

        /// <summary>Paints every chunk asked for so far, now and in parallel (boot: the town is there on the first frame).</summary>
        public void PaintRequestedNow()
        {
            if (_chunkState == null || _queue.Count == 0) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var chunks = _queue.ToArray();
            _queue.Clear();
            var data = new byte[chunks.Length][];
            Parallel.For(0, chunks.Length, k => data[k] = PaintChunk(chunks[k]));
            for (int k = 0; k < chunks.Length; k++) Finish(new PaintedChunk { Chunk = chunks[k], Data = data[k], Generation = _chunkGeneration[chunks[k]] });
            Debug.Log($"[Map] Ground: {chunks.Length} chunks painted at boot in {sw.ElapsedMilliseconds} ms");
        }

        void Update()
        {
            if (_chunkState == null) return;
            for (int n = 0; n < UploadsPerFrame && _painted.TryDequeue(out var done); n++)
            {
                _inFlight--;
                Finish(done);
            }
            // Handed out one per free worker, so the order follows the camera up to the last moment.
            while (_inFlight < _workers && _queue.Count > 0)
            {
                int k = NextQueued();
                int chunk = _queue[k];
                _queue[k] = _queue[^1];
                _queue.RemoveAt(_queue.Count - 1);
                _chunkState[chunk] = ChunkState.Painting;
                _inFlight++;
                int generation = _chunkGeneration[chunk];
                Task.Run(() =>
                {
                    var result = new PaintedChunk { Chunk = chunk, Generation = generation };
                    try { result.Data = PaintChunk(chunk); }
                    catch (System.Exception e) { result.Error = e.ToString(); }
                    _painted.Enqueue(result);
                });
            }
        }

        /// <summary>The queued chunk nearest the camera.</summary>
        int NextQueued()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return 0;
            Vector2 eye = _camera.transform.position;
            int best = 0;
            float bestD = float.MaxValue;
            for (int k = 0; k < _queue.Count; k++)
            {
                int i = _queue[k];
                var centre = new Vector2((i % _cw + 0.5f) * ChunkCells, (i / _cw + 0.5f) * ChunkCells);
                float d = (centre - eye).sqrMagnitude;
                if (d < bestD) { bestD = d; best = k; }
            }
            return best;
        }

        void ChunkRect(int i, out int tx, out int ty, out int tw, out int th)
        {
            tx = i % _cw * ChunkCells;
            ty = i / _cw * ChunkCells;
            tw = Mathf.Min(ChunkCells, _map.Width - tx);
            th = Mathf.Min(ChunkCells, _map.Height - ty);
        }

        /// <summary>Paints and compresses one chunk. Any thread: touches only the painter and pooled arrays.</summary>
        byte[] PaintChunk(int i)
        {
            ChunkRect(i, out int tx, out int ty, out int tw, out int th);
            if (!_pixelPool.TryTake(out var pixels)) pixels = new Color32[ChunkPixels * ChunkPixels];
            if (!_dataPool.TryTake(out var data)) data = new byte[Bc1Encoder.EncodedSize(ChunkPixels, ChunkPixels)];
            _painter.Paint(tx, ty, tw, th, pixels);
            Bc1Encoder.Encode(pixels, tw * GroundPixelsPerCell, th * GroundPixelsPerCell, data);
            _pixelPool.Add(pixels);
            return data;
        }

        /// <summary>Main thread: a painted chunk becomes a sprite.</summary>
        void Finish(PaintedChunk p)
        {
            if (this == null) return;
            if (p.Generation != _chunkGeneration[p.Chunk])
            {
                // Painted from tiles that have changed since: the chunk is queued to be painted again.
                if (p.Data != null) _dataPool.Add(p.Data);
                return;
            }
            _chunkState[p.Chunk] = ChunkState.Done;
            if (p.Error != null)
            {
                Debug.LogError($"[Map] Ground chunk {p.Chunk} could not be painted: {p.Error}");
                return;
            }
            ChunkRect(p.Chunk, out int tx, out int ty, out int tw, out int th);
            int pw = tw * GroundPixelsPerCell, ph = th * GroundPixelsPerCell;
            var tex = MakeGroundTexture(pw, ph, $"GroundChunk_{tx}_{ty}", null, p.Data);
            _dataPool.Add(p.Data);
            var sr = _chunkRenderers[p.Chunk];
            if (sr == null)
            {
                var go = new GameObject(tex.name);
                go.transform.SetParent(_groundRoot, false);
                go.transform.position = new Vector3(tx, ty, 0f);
                sr = _chunkRenderers[p.Chunk] = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = GroundOrder;
                GroundChunksPainted++;
            }
            else if (sr.sprite != null)
            {
                var old = sr.sprite;
                _owned.Remove(old.texture);
                _owned.Remove(old);
                Destroy(old.texture);
                Destroy(old);
            }
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, pw, ph), Vector2.zero, GroundPixelsPerCell, 0, SpriteMeshType.FullRect);
            _owned.Add(sr.sprite);
        }

        /// <summary>
        /// A BC1 texture from pixels (compressed here) or from BC1 data already made; plain RGBA32 when
        /// a side is not a multiple of 4. The CPU copy is freed.
        /// </summary>
        Texture2D MakeGroundTexture(int w, int h, string name, Color32[] pixels, byte[] bc1)
        {
            bool compress = w % 4 == 0 && h % 4 == 0;
            var tex = new Texture2D(w, h, compress ? TextureFormat.DXT1 : TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };
            if (!compress) tex.SetPixels32(pixels);
            else
            {
                if (bc1 == null)
                {
                    bc1 = new byte[Bc1Encoder.EncodedSize(w, h)];
                    Bc1Encoder.Encode(pixels, w, h, bc1);
                }
                NativeArray<byte>.Copy(bc1, tex.GetRawTextureData<byte>(), Bc1Encoder.EncodedSize(w, h));
            }
            tex.Apply(false, true);
            _owned.Add(tex);
            return tex;
        }

        void OnDestroy()
        {
            // Workers still painting finish into a queue nobody reads.
            _chunkState = null;
            foreach (var o in _owned) if (o != null) Destroy(o);
            _owned.Clear();
        }

        /// <summary>
        /// A forest cell burnt down (P4-72): its tree becomes a charred stump (the wasteland's), and
        /// grows back when the forest does.
        /// </summary>
        public void SetTreeBurnt(Vector2Int cell, bool burnt)
        {
            if (_decor == null || _map == null || !_map.HasTree(cell.x, cell.y)) return;
            int i = _map.Index(cell.x, cell.y);
            int variant = (Hash(cell.x, cell.y) / 7) % Variants;
            var region = burnt ? (int)Region.Wasteland : (int)_map.Regions[i];
            _decor.SetTile(new Vector3Int(cell.x, cell.y, 0), (!burnt && _map.Deep[i] ? _deepTrees : _trees)[region * Variants + variant]);
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

        Tile MakeTile(Sprite sprite)
        {
            var t = ScriptableObject.CreateInstance<Tile>();
            _owned.Add(t);
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
