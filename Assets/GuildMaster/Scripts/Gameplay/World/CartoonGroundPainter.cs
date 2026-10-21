using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// Paints the ground in the cartoon style of the character art (flat colours, dark outlines,
    /// rounded shapes). Natural terrain (forest floor, sand, water, road, mountain) is drawn from
    /// smoothed per-tile fields so edges curve instead of stair-stepping; town floor, walls and
    /// gates stay grid-aligned. Pure CPU code: fills Color32 blocks, safe to run in parallel.
    /// Gameplay still uses the tile grid; only the picture changes.
    /// </summary>
    public class CartoonGroundPainter
    {
        public readonly int PixelsPerTile;

        readonly MapData _map;
        readonly int _w, _h;
        // Per-layer indicator per tile (0/1): smoothed at paint time.
        readonly byte[] _forest, _sand, _water, _road, _mountain, _nearBorder;

        // ---- Palette ----
        static readonly Color32 Grass = C(0x7C, 0xCF, 0x4F), GrassLight = C(0x8E, 0xDA, 0x5C);
        static readonly Color32 ForestFloor = C(0x5C, 0xAF, 0x3E), ForestEdge = C(0x4E, 0x9A, 0x35);
        static readonly Color32 Sand = C(0xF2, 0xD8, 0x8F), SandEdge = C(0xD9, 0xB8, 0x6A);
        static readonly Color32 Water = C(0x4B, 0xA9, 0xE9), WaterShallow = C(0x7F, 0xCB, 0xF5), WaterEdge = C(0x2C, 0x5C, 0x93), Foam = C(0xE8, 0xF7, 0xFF);
        static readonly Color32 Road = C(0xDE, 0xB4, 0x72), RoadEdge = C(0xA8, 0x7A, 0x45), RoadDot = C(0xCB, 0x9F, 0x5E);
        static readonly Color32 Rock = C(0x9A, 0x99, 0xA6), RockDark = C(0x80, 0x7F, 0x8C), RockEdge = C(0x45, 0x44, 0x4F), RockLight = C(0xB6, 0xB5, 0xC1);
        static readonly Color32 Cobble = C(0xD6, 0xCE, 0xC0), CobbleAlt = C(0xCA, 0xC1, 0xB2), Grout = C(0xA9, 0x9F, 0x90);
        static readonly Color32 WallStone = C(0xA5, 0xAB, 0xB4), WallTop = C(0xC4, 0xC9, 0xD0), WallLine = C(0x42, 0x47, 0x50);
        static readonly Color32 Wood = C(0xB5, 0x7C, 0x45), WoodDark = C(0x8A, 0x5A, 0x30), WoodLine = C(0x4A, 0x30, 0x1C);

        static Color32 C(int r, int g, int b) => new((byte)r, (byte)g, (byte)b, 255);

        // ---- Regions (P4-55): the same shapes in each region's colours ----

        public readonly struct Palette
        {
            public readonly Color32 Grass, GrassLight, ForestFloor, ForestEdge, Sand, SandEdge, Water, WaterShallow, WaterEdge, Foam;

            public Palette(Color32 grass, Color32 grassLight, Color32 forestFloor, Color32 forestEdge, Color32 sand, Color32 sandEdge,
                Color32 water, Color32 waterShallow, Color32 waterEdge, Color32 foam)
            {
                Grass = grass; GrassLight = grassLight; ForestFloor = forestFloor; ForestEdge = forestEdge; Sand = sand; SandEdge = sandEdge;
                Water = water; WaterShallow = waterShallow; WaterEdge = waterEdge; Foam = foam;
            }
        }

        /// <summary>Indexed by <see cref="Region"/>. "Sand" rings water: beach, dune, snow bank, mud, ash.</summary>
        static readonly Palette[] Palettes =
        {
            // Grassland
            new(Grass, GrassLight, ForestFloor, ForestEdge, Sand, SandEdge, Water, WaterShallow, WaterEdge, Foam),
            // Desert: sandy ground, dry scrub, blue oases
            new(C(0xE8, 0xC9, 0x86), C(0xF0, 0xD6, 0x98), C(0xC9, 0xA8, 0x62), C(0xA8, 0x88, 0x48), C(0xF5, 0xE0, 0xA8), C(0xD4, 0xB2, 0x70),
                Water, WaterShallow, WaterEdge, Foam),
            // Snow: white ground, frosted woods, ice lakes
            new(C(0xEE, 0xF4, 0xFA), C(0xFF, 0xFF, 0xFF), C(0xC8, 0xD8, 0xE6), C(0xA4, 0xB8, 0xCC), C(0xDD, 0xE6, 0xEF), C(0xB8, 0xC6, 0xD4),
                C(0xA8, 0xD8, 0xF0), C(0xC8, 0xEA, 0xFA), C(0x6A, 0x9A, 0xC0), C(0xFF, 0xFF, 0xFF)),
            // Swamp: dark olive ground, murky pools, mud banks
            new(C(0x5E, 0x6B, 0x3A), C(0x6B, 0x78, 0x42), C(0x45, 0x52, 0x2C), C(0x36, 0x42, 0x22), C(0x6E, 0x5A, 0x3A), C(0x55, 0x45, 0x2C),
                C(0x4A, 0x5E, 0x3A), C(0x5C, 0x70, 0x48), C(0x2C, 0x38, 0x22), C(0x7A, 0x8C, 0x5A)),
            // Wasteland: reddish ash, charred scrub, lava
            new(C(0x8A, 0x6E, 0x62), C(0x9A, 0x7C, 0x6E), C(0x6A, 0x55, 0x4C), C(0x55, 0x42, 0x3A), C(0x9E, 0x96, 0x90), C(0x7E, 0x76, 0x70),
                C(0xF0, 0x6A, 0x20), C(0xFF, 0xA0, 0x30), C(0x5A, 0x1E, 0x10), C(0xFF, 0xE0, 0x60)),
        };

        /// <summary>Colours used for a region (minimap, UI).</summary>
        public static Palette PaletteOf(Region r) => Palettes[(int)r];

        /// <summary>
        /// Ground region under a pixel: the cell's own, except on open ground near a border — there
        /// the frayed border (the regions bite into each other), as long as it names a region of a
        /// neighbouring cell. Features (lakes, woods, rocks) keep their cells' region, so the border
        /// runs round them (user decision 2026-09-22).
        /// </summary>
        Region RegionAt(float u, float v, int cx, int cy)
        {
            var own = _map.Regions[cy * _w + cx];
            if (_nearBorder[cy * _w + cx] == 0) return own;
            // Under a feature the ground only shows at its wobbly rim: that ground belongs to the
            // open land around it (else the feature's own region showed in cell-shaped notches).
            if (!IsOpen(cx, cy)) return OpenNeighbourRegion(cx, cy, own);
            var frayed = TerrainRegions.AtFrayed(_map.RegionLayout, u, v);
            if (frayed == own) return own;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = Mathf.Clamp(cx + dx, 0, _w - 1), y = Mathf.Clamp(cy + dy, 0, _h - 1);
                if (_map.Regions[y * _w + x] == frayed) return frayed;
            }
            return own;
        }

        bool IsOpen(int x, int y) => _forest[y * _w + x] == 0 && _sand[y * _w + x] == 0 && _mountain[y * _w + x] == 0;

        Region OpenNeighbourRegion(int cx, int cy, Region fallback)
        {
            for (int ring = 0; ring < 2; ring++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                bool corner = dx != 0 && dy != 0, side = (dx == 0) != (dy == 0);
                if (ring == 0 ? !side : !corner) continue;
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= _w || y >= _h || !IsOpen(x, y)) continue;
                return _map.Regions[y * _w + x];
            }
            return fallback;
        }

        /// <summary>Region of the nearest cell of a feature layer around (cx, cy): the whole feature is one region.</summary>
        Region LayerRegion(byte[] layer, int cx, int cy, Region fallback)
        {
            if (layer[cy * _w + cx] != 0) return _map.Regions[cy * _w + cx];
            // Sides first, then corners.
            for (int ring = 0; ring < 2; ring++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                bool corner = dx != 0 && dy != 0, side = (dx == 0) != (dy == 0);
                if (ring == 0 ? !side : !corner) continue;
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= _w || y >= _h || layer[y * _w + x] == 0) continue;
                return _map.Regions[y * _w + x];
            }
            return fallback;
        }

        /// <summary>1 where another region lies within the fraying distance (+1 cell).</summary>
        byte[] NearBorder()
        {
            int reach = Mathf.CeilToInt(TerrainRegions.Fringe) + 1;
            var near = new byte[_w * _h];
            for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                var r = _map.Regions[y * _w + x];
                for (int dy = -reach; dy <= reach && near[y * _w + x] == 0; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= _w || ny >= _h || _map.Regions[ny * _w + nx] == r) continue;
                    near[y * _w + x] = 1;
                    break;
                }
            }
            return near;
        }

        public CartoonGroundPainter(MapData map, int pixelsPerTile)
        {
            _map = map;
            _w = map.Width;
            _h = map.Height;
            PixelsPerTile = pixelsPerTile;
            int n = _w * _h;
            _forest = new byte[n]; _sand = new byte[n]; _water = new byte[n]; _road = new byte[n]; _mountain = new byte[n];
            for (int i = 0; i < n; i++) ReadTile(i);
            _nearBorder = NearBorder();
        }

        void ReadTile(int i)
        {
            var t = _map.Tiles[i];
            _forest[i] = (byte)(t == TileType.Forest ? 1 : 0);
            // Beaches: sand also rings every body of water.
            _sand[i] = (byte)(t == TileType.Sand || t == TileType.Water ? 1 : 0);
            _water[i] = (byte)(t == TileType.Water ? 1 : 0);
            _road[i] = (byte)(t == TileType.Road ? 1 : 0);
            _mountain[i] = (byte)(t == TileType.Mountain ? 1 : 0);
        }

        /// <summary>The tiles in this block changed (P4-86: the town walls moved out); paint from them from now on.</summary>
        public void Refresh(RectInt cells)
        {
            for (int y = Mathf.Max(0, cells.yMin); y < Mathf.Min(_h, cells.yMax); y++)
            for (int x = Mathf.Max(0, cells.xMin); x < Mathf.Min(_w, cells.xMax); x++)
                ReadTile(y * _w + x);
        }

        /// <summary>Fills a block of tiles [tx0, tx0+tilesW) × [ty0, ty0+tilesH) into pixels (row-major, bottom row first).</summary>
        public void Paint(int tx0, int ty0, int tilesW, int tilesH, Color32[] pixels) => Paint(tx0, ty0, tilesW, tilesH, pixels, PixelsPerTile);

        /// <summary>The same at another resolution (P4-81: the whole map's low-detail backdrop); shapes are in cells, so they match.</summary>
        public void Paint(int tx0, int ty0, int tilesW, int tilesH, Color32[] pixels, int ppt)
        {
            int pw = tilesW * ppt, ph = tilesH * ppt;
            for (int py = 0; py < ph; py++)
            {
                float v = ty0 + (py + 0.5f) / ppt;
                for (int px = 0; px < pw; px++)
                {
                    float u = tx0 + (px + 0.5f) / ppt;
                    pixels[py * pw + px] = CornersDiffer(u, v) ? Supersample(u, v, 0.25f / ppt) : Shade(u, v);
                }
            }
        }

        /// <summary>True near any terrain boundary (the four surrounding cell centres differ).</summary>
        bool CornersDiffer(float u, float v)
        {
            int x0 = Mathf.FloorToInt(u - 0.5f), y0 = Mathf.FloorToInt(v - 0.5f);
            var a = TypeAt(x0, y0);
            return TypeAt(x0 + 1, y0) != a || TypeAt(x0, y0 + 1) != a || TypeAt(x0 + 1, y0 + 1) != a;
        }

        TileType TypeAt(int x, int y) => _map[Mathf.Clamp(x, 0, _w - 1), Mathf.Clamp(y, 0, _h - 1)];

        /// <summary>2×2 supersampling for anti-aliased edges (only used on boundary pixels).</summary>
        Color32 Supersample(float u, float v, float d)
        {
            var a = Shade(u - d, v - d); var b = Shade(u + d, v - d); var c = Shade(u - d, v + d); var e = Shade(u + d, v + d);
            return new Color32((byte)((a.r + b.r + c.r + e.r) >> 2), (byte)((a.g + b.g + c.g + e.g) >> 2), (byte)((a.b + b.b + c.b + e.b) >> 2), 255);
        }

        Color32 Shade(float u, float v)
        {
            int cx = Mathf.Clamp((int)u, 0, _w - 1), cy = Mathf.Clamp((int)v, 0, _h - 1);
            var type = _map[cx, cy];
            float lx = u - cx, ly = v - cy;
            switch (type)
            {
                case TileType.Wall: return WallPixel(lx, ly, cx, cy);
                case TileType.Gate: return GatePixel(lx, ly, cx, cy);
                // The underground (P4-85): the dark between, and the ruins' halls.
                case TileType.Void: return VoidColor;
                case TileType.RuinFloor: return RuinFloorPixel(u, v);
                case TileType.RuinWall: return RuinWallPixel(lx, ly, cx, cy);
            }

            bool town = _map.TownRect.Contains(new Vector2Int(cx, cy));
            var region = town ? Region.Grassland : RegionAt(u, v, cx, cy);
            var pal = Palettes[(int)region];
            Color32 c = town ? CobblePixel(u, v) : GrassPixel(u, v, pal);
            if (town)
            {
                // Inside the walls only the plaza roads are overlaid.
                Overlay(ref c, _road, u, v, Road, RoadEdge, 0.06f, 0.35f);
                return c;
            }

            // Each feature in its own region's colours (a lake is all water or all lava).
            var forestPal = Palettes[(int)LayerRegion(_forest, cx, cy, region)];
            var wetPal = Palettes[(int)LayerRegion(_sand, cx, cy, region)];
            Overlay(ref c, _forest, u, v, forestPal.ForestFloor, forestPal.ForestEdge, 0.05f, 0.5f);
            Overlay(ref c, _sand, u, v, wetPal.Sand, wetPal.SandEdge, 0.05f, 0.45f);
            WaterOverlay(ref c, u, v, wetPal);
            if (!BridgeOverlay(ref c, u, v, cx, cy)) Overlay(ref c, _road, u, v, Road, RoadEdge, 0.07f, 0.3f);
            MountainOverlay(ref c, u, v);
            return c;
        }

        // ---------------- Fields ----------------

        /// <summary>Bilinear blend of a tile indicator at cell centres; <paramref name="mixed"/> is true near a boundary.</summary>
        float Field(byte[] layer, float u, float v, out bool mixed)
        {
            float gx = u - 0.5f, gy = v - 0.5f;
            int x0 = Mathf.FloorToInt(gx), y0 = Mathf.FloorToInt(gy);
            float tx = gx - x0, ty = gy - y0;
            int a = At(layer, x0, y0), b = At(layer, x0 + 1, y0), c = At(layer, x0, y0 + 1), d = At(layer, x0 + 1, y0 + 1);
            mixed = !(a == b && b == c && c == d);
            if (!mixed) return a;
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        int At(byte[] layer, int x, int y)
        {
            x = Mathf.Clamp(x, 0, _w - 1);
            y = Mathf.Clamp(y, 0, _h - 1);
            return layer[y * _w + x];
        }

        /// <summary>Field with organic wobble at the boundary (noise only where it matters).</summary>
        float Wobbly(byte[] layer, float u, float v, float wobble, out bool mixed)
        {
            float f = Field(layer, u, v, out mixed);
            if (mixed) f += (ValueNoise(u * 1.7f, v * 1.7f) - 0.5f) * wobble;
            return f;
        }

        void Overlay(ref Color32 c, byte[] layer, float u, float v, Color32 fill, Color32 edge, float outline, float wobble)
        {
            float f = Wobbly(layer, u, v, wobble, out _);
            if (f < 0.5f) return;
            c = f < 0.5f + outline ? edge : fill;
            if (layer == _road && f >= 0.5f + outline && Speck(u * 5f, v * 5f, 0.08f, 0.3f, 0.22f)) c = RoadDot;
        }

        /// <summary>
        /// Sparse round specks: the plane is split into unit cells, a fraction <paramref name="chance"/>
        /// of them holds one ellipse (radii rx, ry in cell units) at its centre.
        /// </summary>
        static bool Speck(float x, float y, float chance, float rx, float ry)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            if (Hash01(ix, iy) >= chance) return false;
            float dx = (x - ix - 0.5f) / rx, dy = (y - iy - 0.5f) / ry;
            return dx * dx + dy * dy < 1f;
        }

        void WaterOverlay(ref Color32 c, float u, float v, in Palette pal)
        {
            float f = Wobbly(_water, u, v, 0.45f, out _);
            if (f < 0.5f) return;
            if (f < 0.56f) { c = pal.WaterEdge; return; }   // dark outline
            if (f < 0.64f) { c = pal.Foam; return; }        // foam line
            if (f < 0.80f) { c = pal.WaterShallow; return; } // shallows
            c = pal.Water;
            // Sparkles on open water.
            if (Speck(u * 1.5f, v * 3f, 0.05f, 0.42f, 0.14f)) c = pal.Foam;   // short horizontal glints
        }

        void MountainOverlay(ref Color32 c, float u, float v)
        {
            float f = Wobbly(_mountain, u, v, 0.55f, out _);
            if (f < 0.5f) return;
            if (f < 0.58f) { c = RockEdge; return; }
            float n = ValueNoise(u * 0.9f, v * 0.9f);
            c = n > 0.62f ? RockLight : n < 0.35f ? RockDark : Rock;
        }

        Color32 GrassPixel(float u, float v, in Palette pal)
        {
            // Large soft patches of lighter grass, with crisp cartoon edges.
            float n = ValueNoise(u * 0.18f, v * 0.18f);
            var c = n > 0.62f ? pal.GrassLight : pal.Grass;
            // Sparse little tufts: two tiny blades side by side.
            if (Speck(u * 4f, v * 4f, 0.035f, 0.1f, 0.26f) || Speck(u * 4f + 0.22f, v * 4f + 0.05f, 0.035f, 0.1f, 0.2f)) c = pal.ForestEdge;
            return c;
        }

        // ---------------- Town (grid-aligned) ----------------

        Color32 CobblePixel(float u, float v)
        {
            // Half-tile stones, every other row offset by half a stone.
            float su = u * 2f, sv = v * 2f;
            int row = Mathf.FloorToInt(sv);
            if ((row & 1) == 1) su += 0.5f;
            int col = Mathf.FloorToInt(su);
            float lx = su - col, ly = sv - row;
            if (lx < 0.07f || ly < 0.07f) return Grout;
            return Hash01(col, row) < 0.3f ? CobbleAlt : Cobble;
        }

        Color32 WallPixel(float lx, float ly, int cx, int cy)
        {
            // Stone blocks with dark mortar lines and a light top edge.
            if (ly > 0.84f) return WallTop;
            float bx = lx * 2f + ((Mathf.FloorToInt(ly * 2f) & 1) == 1 ? 0.5f : 0f);
            float fx = bx - Mathf.Floor(bx), fy = ly * 2f - Mathf.Floor(ly * 2f);
            if (fx < 0.08f || fy < 0.08f) return WallLine;
            return Hash01(cx * 2 + Mathf.FloorToInt(bx), cy * 2 + Mathf.FloorToInt(ly * 2f)) < 0.35f ? WallTop : WallStone;
        }

        // ---------------- Ruins underground (P4-85) ----------------

        static readonly Color32 VoidColor = new(6, 5, 9, 255);
        static readonly Color32 RuinSlab = new(0x5C, 0x56, 0x52, 255);
        static readonly Color32 RuinSlabAlt = new(0x52, 0x4C, 0x49, 255);
        static readonly Color32 RuinGrout = new(0x34, 0x30, 0x2E, 255);
        static readonly Color32 RuinMoss = new(0x4E, 0x5E, 0x3E, 255);
        static readonly Color32 RuinBlock = new(0x3A, 0x36, 0x3F, 255);
        static readonly Color32 RuinBlockTop = new(0x55, 0x50, 0x5C, 255);
        static readonly Color32 RuinLine = new(0x22, 0x1F, 0x26, 255);

        /// <summary>Big worn flagstones, a few mossy.</summary>
        Color32 RuinFloorPixel(float u, float v)
        {
            int col = Mathf.FloorToInt(u), row = Mathf.FloorToInt(v);
            float lx = u - col, ly = v - row;
            if (lx < 0.06f || ly < 0.06f) return RuinGrout;
            float h = Hash01(col, row);
            if (h < 0.12f && Speck(u * 3f, v * 3f, 0.5f, 0.35f, 0.3f)) return RuinMoss;
            return h < 0.45f ? RuinSlabAlt : RuinSlab;
        }

        /// <summary>Dark stone blocks with a lighter top edge where the floor is in front (below).</summary>
        Color32 RuinWallPixel(float lx, float ly, int cx, int cy)
        {
            bool front = _map.InBounds(cx, cy - 1) && _map[cx, cy - 1] == TileType.RuinFloor;
            if (front && ly < 0.18f) return RuinBlockTop;
            float bx = lx * 2f + ((Mathf.FloorToInt(ly * 2f) & 1) == 1 ? 0.5f : 0f);
            float fx = bx - Mathf.Floor(bx), fy = ly * 2f - Mathf.Floor(ly * 2f);
            if (fx < 0.07f || fy < 0.07f) return RuinLine;
            return RuinBlock;
        }

        /// <summary>
        /// A road with water either side is a wooden bridge (P4-101: the bridges over the rivers, and the gates' roads
        /// where they cross a river or a lake): planks across the way, dark rails along its sides, straight edges.
        /// </summary>
        bool BridgeOverlay(ref Color32 c, float u, float v, int cx, int cy)
        {
            if (_road[cy * _w + cx] == 0) return false;
            bool northward = WaterBeside(cx, cy, 1, 0) && WaterBeside(cx, cy, -1, 0);
            if (!northward && !(WaterBeside(cx, cy, 0, 1) && WaterBeside(cx, cy, 0, -1))) return false;
            float f = Field(_road, u, v, out _);
            if (f < 0.5f) return true; // the water beside the deck
            if (f < 0.62f) { c = WoodLine; return true; }
            float along = (northward ? v : u) * 3f;
            float plank = along - Mathf.Floor(along);
            c = plank < 0.12f ? WoodLine : (Mathf.FloorToInt(along) & 1) == 0 ? Wood : WoodDark;
            return true;
        }

        /// <summary>Water within two cells this way, with only road between.</summary>
        bool WaterBeside(int x, int y, int dx, int dy)
        {
            for (int k = 1; k <= 2; k++)
            {
                if (At(_water, x + dx * k, y + dy * k) != 0) return true;
                if (At(_road, x + dx * k, y + dy * k) == 0) return false;
            }
            return false;
        }

        Color32 GatePixel(float lx, float ly, int cx, int cy)
        {
            // Wooden planks running across the passage.
            bool horizontal = _map.InBounds(cx - 1, cy) && _map[cx - 1, cy] == TileType.Gate
                              || _map.InBounds(cx + 1, cy) && _map[cx + 1, cy] == TileType.Gate;
            float p = horizontal ? lx : ly;
            float q = horizontal ? ly : lx;
            if (q < 0.06f || q > 0.94f) return WoodLine;
            float plank = p * 4f - Mathf.Floor(p * 4f);
            if (plank < 0.1f) return WoodDark;
            return Wood;
        }

        // ---------------- Noise ----------------

        static float Hash01(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        static float ValueNoise(float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Hash01(x0, y0), b = Hash01(x0 + 1, y0), c = Hash01(x0, y0 + 1), d = Hash01(x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }
    }
}
