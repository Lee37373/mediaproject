using System;
using UnityEngine;
using BT = GuildMaster.Town.BuildingType;
using TrainingYardLayout = GuildMaster.Town.TrainingYardLayout;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// The town's buildings (P4-114, user request 2026-10-31: drawn again to stand beside the LAYERLAB characters and
    /// monsters — thick dark outlines, flat colours with one shaded side, rounded shapes, a soft shadow at the foot). Each
    /// fills its footprint: the front wall at the bottom with its door (over the cell people go in by), windows with
    /// shutters and flower boxes, a sign over the door or a gable; a roof of scalloped shingles above; and a few things of
    /// its trade standing by the door. Drawn <see cref="BuildingDetail"/> times finer than the other props. Inside: plank
    /// floors and furniture, seen when the outside fades.
    /// </summary>
    public static partial class CartoonArt
    {
        const int BuildingDetail = 2;

        static readonly Color ShadeTint = new(0.16f, 0.1f, 0.24f, 0.16f);   // the shaded side, laid over what is drawn there
        static readonly Color FootShadow = new(0f, 0f, 0f, 0.2f);
        static readonly Color Timber = Hex(0x8C5A33), DoorWood = Hex(0xA4693A), Stonework = Hex(0xC9C2B6), Iron = Hex(0x4A4E57);
        static readonly Color Gold = Hex(0xF2C94C), Leaves = Hex(0x5DBB44), Straw = Hex(0xE8C98A), Plank = Hex(0xC08A52);
        static readonly Color[] Blooms = { Hex(0xFF7BA6), Hex(0xFFD24A), Hex(0xFF5A5A), Hex(0xFFFFFF) };

        /// <summary>
        /// A building as it stands on its footprint. <paramref name="doorColumn"/>: the footprint column its door cell lies
        /// under, where the door is drawn (-1: the middle one, as the player's buildings have). <paramref name="level"/>: what it
        /// has been raised to (P4-115: the picture grows richer).
        /// </summary>
        public static Sprite Building(BT type, int tilesW, int tilesH, int doorColumn = -1, int level = 1)
        {
            if (doorColumn < 0 || doorColumn >= tilesW) doorColumn = tilesW / 2;
            level = Mathf.Clamp(level, 1, 3);
            string key = $"c_building:{type}:{tilesW}x{tilesH}:{doorColumn}:{level}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            int h = tilesH * PixelsPerUnit, room = type == BT.TrainingGround ? 0 : RoofRoom;
            var c = new Canvas(tilesW * PixelsPerUnit, h + room, BuildingDetail);
            float doorX = (doorColumn + 0.5f) * PixelsPerUnit;
            switch (type)
            {
                case BT.TrainingGround: TrainingYard(c, doorX, level); break;
                case BT.Watchtower: Watchtower(c, h); break;
                case BT.ArrowTower: ArrowTower(c, h, level); break;
                default: House(c, h, type, doorX, level); break;
            }
            // The footprint's middle stays on the building's place; the room above is for flags.
            return c.ToSprite(key, new Vector2(0.5f, h * 0.5f / c.H));
        }

        /// <summary>Room over the footprint for what rises above a roof: flags and pennants (logical pixels).</summary>
        const int RoofRoom = 16;

        // ---------------- Houses ----------------

        /// <summary>How one kind of house looks.</summary>
        struct Look
        {
            public Color Roof, Wall, Shutter, Glass, Awning;
            /// <summary>Stone walls (no timber, no flower boxes); a gable over the door; a row of windows upstairs.</summary>
            public bool Stone, Gable, Upstairs, Lanterns;
            /// <summary>The chimney's place across the width, 0 for none.</summary>
            public float Chimney;
        }

        static Look LookOf(BT type)
        {
            var k = new Look { Roof = Hex(0xB9854A), Wall = Hex(0xF6E8C8), Glass = Hex(0x9FD8F2) };
            switch (type)
            {
                case BT.TownHall: k.Roof = Hex(0xD9534F); k.Wall = Hex(0xF8EEDC); k.Gable = k.Upstairs = k.Lanterns = true; break;
                case BT.GuildHall: k.Roof = Hex(0x4A7BD0); k.Gable = k.Upstairs = k.Lanterns = true; k.Chimney = 0.24f; break;
                case BT.Blacksmith: k.Roof = Hex(0x5D6370); k.Wall = Hex(0xC9C2B6); k.Stone = true; k.Chimney = 0.74f; k.Glass = Hex(0xFFB24A); break;
                case BT.Apothecary: k.Roof = Hex(0x4DB06B); k.Awning = Hex(0x4DB06B); break;
                // The guild's facilities (P4-87).
                case BT.Armory: k.Roof = Hex(0x9B3B2E); k.Wall = Hex(0xD8D0C2); k.Stone = true; break;
                case BT.ArmorWorkshop: k.Roof = Hex(0x5B7A99); k.Chimney = 0.72f; break;
                case BT.DiningHall: k.Roof = Hex(0xE08A2E); k.Chimney = 0.72f; k.Glass = Hex(0xFFE3A3); k.Awning = Hex(0xD9534F); break;
                case BT.PoisonLab: k.Roof = Hex(0x6FA83A); k.Wall = Hex(0xE6E4C6); k.Glass = Hex(0xB8F27A); break;
                case BT.RuneForge: k.Roof = Hex(0x7B5BD6); k.Wall = Hex(0xBFB8C8); k.Stone = true; k.Glass = Hex(0xD6C8FF); break;
                case BT.FrostAltar: k.Roof = Hex(0x6FBBE0); k.Wall = Hex(0xEEF4FA); k.Stone = true; k.Glass = Hex(0xD6F2FF); break;
                case BT.Dormitory: k.Roof = Hex(0xA0643A); k.Upstairs = true; k.Chimney = 0.28f; break;
            }
            k.Shutter = Darker(k.Roof, 0.82f);
            return k;
        }

        /// <summary>
        /// A house on a footprint <paramref name="h"/> pixels deep. Raised (P4-115), it shows it: from level 2 dormer windows
        /// in the roof and a pennant over the ridge; at level 3 a gilt cresting on the ridge, a pennant at each end, dressed
        /// stone at the corners and a gilt rim to the sign.
        /// </summary>
        static void House(Canvas c, int h, BT type, float doorX, int level)
        {
            var k = LookOf(type);
            int w = c.W;
            bool big = w >= 5 * PixelsPerUnit;
            float eave = Mathf.Round(h * 0.5f), top = h - 4f;
            float x0 = 7f, x1 = w - 7f, wallTop = eave + 4f;
            // A tall wall with one floor (the smithy) gets a taller door and windows, so it is not bare above them.
            bool lofty = !big && !k.Upstairs && eave - 8f > 50f;
            float dw = big ? 14f : lofty ? 12f : 10.5f, dh = big ? 22f : lofty ? 20f : 15f;
            float ww = big ? 7.5f : lofty ? 6.5f : 4.8f, wh = big ? 14f : lofty ? 16f : 10f, wy = big ? 20f : lofty ? 22f : 17f;
            float upY = wy + wh + ww + 10f;
            // Upstairs: a floor, and more while a tall wall has room (P4-115: the guild hall people walk into stands three high).
            var floors = new System.Collections.Generic.List<float>();
            if (k.Upstairs)
                for (float y = upY; floors.Count == 0 || y + wh * 0.85f + ww < eave - 12f; y += wh * 0.85f + ww + 12f) floors.Add(y);

            c.Shape(Ellipse(w * 0.5f, 9f, w * 0.48f, 8f), FootShadow, FootShadow, 0f);

            // The front wall: plaster between timbers, or stone.
            var walls = RoundRect(x0, 8f, x1, wallTop, 4f);
            c.Shape(walls, k.Wall, Ink, 3f);
            Func<float, float, float> wallIn = (x, y) => walls(x, y) + 3f;
            if (k.Stone) Courses(c, wallIn, x0, x1, 12f, wallTop, Darker(k.Wall, 0.82f));
            else
            {
                c.Shape(RoundRect(x0 + 2f, 9f, x0 + 7f, wallTop - 1f, 1f), Timber, Ink, 1.5f);         // corner posts
                c.Shape(RoundRect(x1 - 7f, 9f, x1 - 2f, wallTop - 1f, 1f), Timber, Ink, 1.5f);
                foreach (float y in floors) c.Shape(RoundRect(x0 + 2f, y - 7f, x1 - 2f, y - 3.5f, 1f), Timber, Ink, 1.5f);   // the floors between
            }
            c.Shape(RoundRect(x0 - 2f, 4f, x1 + 2f, 13f, 3f), Stonework, Ink, 2.5f);                    // the plinth
            for (float x = x0 + 12f; x < x1 - 4f; x += 14f)
                c.Shape(RoundRect(x, 6f, x + 1.2f, 11f, 0.5f), Darker(Stonework, 0.8f), Darker(Stonework, 0.8f), 0f);

            // Door, windows either side of it (and upstairs), a lantern each side of a grand door.
            Door(c, doorX, dw, dh);
            float clear = dw + (k.Lanterns ? 15f : 4f);
            Windows(c, k, x0 + 6f, doorX - clear, wy, ww, wh);
            Windows(c, k, doorX + clear, x1 - 6f, wy, ww, wh);
            for (int f = 0; f < floors.Count; f++)
            {
                // Over the door hangs the sign or rises the gable; a floor higher up has windows right across.
                if (f > 0) { Windows(c, k, x0 + 6f, x1 - 6f, floors[f], ww, wh * 0.85f); continue; }
                float over = dw + 8f;
                Windows(c, k, x0 + 6f, doorX - over, floors[f], ww, wh * 0.85f);
                Windows(c, k, doorX + over, x1 - 6f, floors[f], ww, wh * 0.85f);
            }
            if (k.Lanterns)
                for (int s = -1; s <= 1; s += 2) Lantern(c, doorX + s * (dw + 8f), 8f + dh * 0.95f, s);
            if (level >= 3) Quoins(c, x0, x1, wallTop);

            // Light from the left: the wall's right fifth in shade, and a band under the eaves.
            c.ShapeClipped(RoundRect(x1 - (x1 - x0) * 0.2f, 0f, w, h, 0f), walls, ShadeTint);
            c.ShapeClipped(RoundRect(0f, eave - 8f, w, h, 0f), walls, ShadeTint);

            // The roof: a hip of scalloped shingles, darker at its edge and on its right.
            float inset = Mathf.Min(w * 0.16f, (top - eave) * 0.5f);
            var roof = Poly(3f, new Vector2(5f, eave - 1f), new Vector2(w - 5f, eave - 1f),
                            new Vector2(w - 5f - inset, top - 3f), new Vector2(5f + inset, top - 3f));
            c.Shape(roof, k.Roof, Ink, 3f);
            Func<float, float, float> roofIn = (x, y) => roof(x, y) + 3f;
            var seam = Darker(k.Roof, 0.78f);
            int row = 0;
            for (float y = eave + 2f; y < top + 4f; y += 8f, row++)
            {
                // Each row's lower edge: a crescent under each shingle (the row above overlaps this one's top).
                var shingle = CircleRow(row % 2 == 0 ? 0f : 5f, y, 10f, 5.6f);
                var above = CircleRow(row % 2 == 0 ? 0f : 5f, y + 1.8f, 10f, 5.6f);
                c.ShapeClipped((x, yy) => Mathf.Max(shingle(x, yy), -above(x, yy)), roofIn, seam);
            }
            c.ShapeClipped(RoundRect(w * 0.66f, 0f, w, h, 0f), roof, ShadeTint);
            c.ShapeClipped(RoundRect(0f, 0f, w, eave + 2.5f, 0f), roofIn, Darker(k.Roof, 0.68f));
            // A tall roof gets a broader gable (the big guild hall), the others keep theirs.
            float gableHalf = k.Gable ? Mathf.Min(dw + 22f + Mathf.Max(0f, top - eave - 80f) * 0.5f, w * 0.2f) : 0f;
            if (level >= 2) Dormers(c, k, type, w, eave, top, inset, doorX, gableHalf, big ? 2 : 1);
            c.Shape(RoundRect(4f + inset, top - 6.5f, w - 4f - inset, top, 2.5f), Darker(k.Roof, 0.7f), Ink, 2.5f);   // ridge
            if (level >= 3) Cresting(c, 4f + inset, w - 4f - inset, top);
            if (type == BT.FrostAltar) Snow(c, roofIn, w, eave, top, inset);
            if (k.Chimney > 0f) Chimney(c, w * k.Chimney, eave + (top - eave) * 0.45f, h - 3f, k.Stone ? k.Wall : Hex(0xC9785A));
            if (type == BT.TownHall) Turret(c, w * 0.27f, top, k.Wall, k.Roof);
            if (level >= 2) RidgePennants(c, type, k, w, top, inset, both: level >= 3);

            // Over the door: a gable with the town's clock or the guild's crest, or a sign hanging from the eaves.
            if (k.Gable) Gable(c, type, doorX, gableHalf, eave, top - 6f, k);
            else
            {
                float r = big ? 11f : 8.5f, sy = eave - 5f;
                for (int s = -1; s <= 1; s += 2)
                    c.Shape(Segment(doorX + s * r * 0.55f, sy, doorX + s * r * 0.55f, eave + 2f, 0.7f), Iron, Iron, 0f);
                if (level >= 2) c.Shape(Circle(doorX, sy, r + 1.8f), level >= 3 ? Gold : Hex(0xC9CED8), Ink, 1.4f);   // a rim, silver then gilt
                c.Shape(Circle(doorX, sy, r), Hex(0xF6E7C1), Ink, 2.2f);
                Emblem(c, type, doorX, sy, r / 13f);
            }

            Props(c, type, doorX, dw, x0, x1);
        }

        static void Courses(Canvas c, Func<float, float, float> inside, float x0, float x1, float y0, float y1, Color mortar)
        {
            int row = 0;
            for (float y = y0; y < y1; y += 9f, row++)
            {
                float yy = y;
                c.ShapeClipped(RoundRect(x0, yy, x1, yy + 1.3f, 0f), inside, mortar);
                for (float x = x0 + (row % 2 == 0 ? 8f : 16f); x < x1; x += 16f)
                {
                    float xx = x;
                    c.ShapeClipped(RoundRect(xx, yy, xx + 1.3f, yy + 9f, 0f), inside, mortar);
                }
            }
        }

        static void Door(Canvas c, float dx, float dw, float dh)
        {
            float y0 = 8f, arch = y0 + dh;
            c.Shape(Union(RoundRect(dx - dw - 3f, y0 - 1f, dx + dw + 3f, arch, 1f), Circle(dx, arch, dw + 3f)), Stonework, Ink, 2.5f);
            var door = Union(RoundRect(dx - dw, y0 - 1f, dx + dw, arch, 1f), Circle(dx, arch, dw));
            c.Shape(door, DoorWood, Ink, 2.5f);
            Func<float, float, float> inside = (x, y) => door(x, y) + 2.5f;
            for (int i = -1; i <= 1; i++)   // plank seams
                c.ShapeClipped(RoundRect(dx + i * dw * 0.42f - 0.7f, 0f, dx + i * dw * 0.42f + 0.7f, arch + dw, 0f), inside, Darker(DoorWood, 0.72f));
            c.ShapeClipped(RoundRect(dx - dw, y0 + 4f, dx + dw, y0 + 6.5f, 0f), inside, Iron);   // iron bands
            c.ShapeClipped(RoundRect(dx - dw, arch - 3f, dx + dw, arch - 0.5f, 0f), inside, Iron);
            c.Shape(Circle(dx + dw * 0.55f, y0 + dh * 0.55f, 1.8f), Gold, Ink, 1f);
            c.Shape(RoundRect(dx - dw - 6f, 1.5f, dx + dw + 6f, 8f, 2.5f), Hex(0xBDB6AA), Ink, 2f);    // the step
        }

        /// <summary>As many windows as fit between <paramref name="a"/> and <paramref name="b"/>, with shutters when there is room.</summary>
        static void Windows(Canvas c, in Look k, float a, float b, float y0, float ww, float wh)
        {
            float span = b - a;
            if (span < 2f * ww + 2f) return;
            int n = Mathf.Max(1, Mathf.FloorToInt((span + 8f) / (2f * ww + 18f)));
            bool shutters = n * (2f * ww + 10.8f) + (n - 1) * 8f <= span;
            for (int i = 0; i < n; i++) Window(c, k, a + span * (i + 0.5f) / n, y0, ww, wh, shutters);
        }

        static void Window(Canvas c, in Look k, float cx, float y0, float ww, float wh, bool shutters)
        {
            float yTop = y0 + wh;
            if (k.Awning.a > 0f) Awning(c, cx - ww - 6f, cx + ww + 6f, yTop + ww + 2f, k.Awning);
            if (shutters)
                for (int s = -1; s <= 1; s += 2)
                {
                    float sx = cx + s * (ww + 2.6f);
                    var sh = RoundRect(sx - 2.8f, y0, sx + 2.8f, yTop + ww * 0.6f, 1.2f);
                    c.Shape(sh, k.Shutter, Ink, 1.8f);
                    float mid = (y0 + yTop) * 0.5f;
                    c.ShapeClipped(RoundRect(sx - 2.8f, mid, sx + 2.8f, mid + 1.2f, 0f), (x, y) => sh(x, y) + 1.8f, Darker(k.Shutter, 0.72f));
                }
            var glass = Union(RoundRect(cx - ww, y0, cx + ww, yTop, 1f), Circle(cx, yTop, ww));
            c.Shape(glass, k.Glass, Ink, 2.5f);
            Func<float, float, float> inside = (x, y) => glass(x, y) + 2.5f;
            c.ShapeClipped(Segment(cx - ww * 0.9f, y0 + wh * 0.2f, cx + ww * 0.1f, yTop + ww * 0.9f, 1.6f), inside, new Color(1f, 1f, 1f, 0.55f));
            c.ShapeClipped(RoundRect(cx - 0.9f, 0f, cx + 0.9f, yTop + ww, 0f), inside, Timber);   // the bars
            c.ShapeClipped(RoundRect(cx - ww, y0 + wh * 0.55f - 0.9f, cx + ww, y0 + wh * 0.55f + 0.9f, 0f), inside, Timber);
            if (!k.Stone) FlowerBox(c, cx, y0, ww);
        }

        static void FlowerBox(Canvas c, float cx, float y0, float ww)
        {
            c.Shape(Ellipse(cx, y0 + 0.5f, ww + 2.5f, 3f), Leaves, Ink, 1.5f);
            int n = Mathf.Max(2, Mathf.RoundToInt(ww / 2.5f));
            for (int i = 0; i < n; i++)
                c.Shape(Circle(cx - ww + 2f * ww * (i + 0.5f) / n, y0 + 2f, 1.9f), Blooms[(i + (int)cx) % Blooms.Length], Ink, 1f);
            c.Shape(RoundRect(cx - ww - 3f, y0 - 5f, cx + ww + 3f, y0 - 0.5f, 1.5f), Timber, Ink, 1.8f);
        }

        /// <summary>A striped cloth awning with a scalloped edge.</summary>
        static void Awning(Canvas c, float a, float b, float y, Color col)
        {
            var scallops = CircleRow(a + 3f, y, 6f, 3f);
            var hem = RoundRect(a, y - 4f, b, y + 1f, 0f);
            var body = Union(RoundRect(a, y, b, y + 6f, 1.5f), (x, yy) => Mathf.Max(scallops(x, yy), hem(x, yy)));
            c.Shape(body, Hex(0xFFF6E6), Ink, 2f);
            Func<float, float, float> inside = (x, yy) => body(x, yy) + 2f;
            for (float x = a; x < b; x += 12f)
            {
                float xx = x;
                c.ShapeClipped(RoundRect(xx, y - 4f, xx + 6f, y + 7f, 0f), inside, col);
            }
        }

        static void Lantern(Canvas c, float x, float y, int side)
        {
            c.Shape(Circle(x, y, 7.5f), new Color(1f, 0.85f, 0.4f, 0.25f), new Color(1f, 0.85f, 0.4f, 0.25f), 0f);   // its glow
            c.Shape(Segment(x - side * 4.5f, y + 6.5f, x, y + 6.5f, 0.9f), Iron, Iron, 0f);
            c.Shape(RoundRect(x - 2.6f, y - 3.5f, x + 2.6f, y + 3.5f, 1.2f), Hex(0xFFD86B), Ink, 1.5f);
            c.Shape(RoundRect(x - 3.4f, y + 3f, x + 3.4f, y + 5.2f, 0.8f), Iron, Ink, 1f);
        }

        static void Chimney(Canvas c, float cx, float y0, float y1, Color brick)
        {
            var stack = RoundRect(cx - 6f, y0, cx + 6f, y1 - 4f, 1.5f);
            c.Shape(stack, brick, Ink, 2.5f);
            c.ShapeClipped(RoundRect(cx + 1.5f, 0f, cx + 8f, y1, 0f), stack, ShadeTint);
            c.Shape(RoundRect(cx - 8f, y1 - 7f, cx + 8f, y1, 1.5f), Darker(brick, 0.8f), Ink, 2.5f);
        }

        /// <summary>A gable over the door: its own roof boards, a gilt finial, and the town's clock or the guild's crest.</summary>
        static void Gable(Canvas c, BT type, float x, float gw, float eave, float apex, in Look k)
        {
            var gable = Poly(2f, new Vector2(x - gw, eave - 1f), new Vector2(x + gw, eave - 1f), new Vector2(x, apex));
            c.Shape(gable, k.Wall, Ink, 3f);
            c.ShapeClipped(RoundRect(x, 0f, x + gw + 4f, apex + 4f, 0f), gable, ShadeTint);
            for (int s = -1; s <= 1; s += 2)
                c.Shape(Segment(x + s * (gw + 5f), eave - 3f, x, apex + 5f, 3.6f), k.Roof, Ink, 2.5f);
            c.Shape(Circle(x, apex + 5f, 4f), Gold, Ink, 2f);
            float cy = eave + (apex - eave) * 0.38f, r = Mathf.Min(11f, gw * 0.32f);
            if (type == BT.TownHall)
            {
                c.Shape(Circle(x, cy, r), Hex(0xFFF6DA), Ink, 2.5f);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI * 0.5f;
                    c.Shape(Circle(x + Mathf.Cos(a) * r * 0.62f, cy + Mathf.Sin(a) * r * 0.62f, 1.1f), Ink, Ink, 0f);
                }
                c.Shape(Segment(x, cy, x, cy + r * 0.55f, 1.1f), Ink, Ink, 0f);
                c.Shape(Segment(x, cy, x + r * 0.4f, cy - r * 0.15f, 1.1f), Ink, Ink, 0f);
            }
            else
            {
                // The guild's crest: a blue shield with crossed swords.
                var shield = Poly(1.5f, new Vector2(x - r, cy + r), new Vector2(x + r, cy + r), new Vector2(x + r, cy - r * 0.1f),
                                  new Vector2(x, cy - r * 1.15f), new Vector2(x - r, cy - r * 0.1f));
                c.Shape(shield, Hex(0x3F6FC4), Ink, 2.2f);
                var steel = Hex(0xE3E7EE);
                c.Shape(Segment(x - r * 0.65f, cy - r * 0.6f, x + r * 0.65f, cy + r * 0.8f, 1.5f), steel, Ink, 1f);
                c.Shape(Segment(x + r * 0.65f, cy - r * 0.6f, x - r * 0.65f, cy + r * 0.8f, 1.5f), steel, Ink, 1f);
                c.Shape(Segment(x - r * 0.75f, cy - r * 0.25f, x - r * 0.3f, cy - r * 0.7f, 1.3f), Gold, Ink, 1f);
                c.Shape(Segment(x + r * 0.75f, cy - r * 0.25f, x + r * 0.3f, cy - r * 0.7f, 1.3f), Gold, Ink, 1f);
            }
        }

        /// <summary>The town hall's bell turret, on the roof's left.</summary>
        static void Turret(Canvas c, float x, float top, Color wall, Color roof)
        {
            var body = RoundRect(x - 10f, top - 34f, x + 10f, top - 13f, 2f);
            c.Shape(body, wall, Ink, 2.5f);
            c.Shape(Union(RoundRect(x - 5.5f, top - 31f, x + 5.5f, top - 23f, 0.5f), Circle(x, top - 23f, 5.5f)), Hex(0x3A2E2A), Ink, 1.5f);
            c.Shape(Union(Circle(x, top - 24f, 3.4f), RoundRect(x - 4.5f, top - 29f, x + 4.5f, top - 25f, 1.5f)), Gold, Ink, 1.2f);
            c.ShapeClipped(RoundRect(x + 4f, 0f, x + 12f, top, 0f), body, ShadeTint);
            c.Shape(Poly(1.5f, new Vector2(x - 13f, top - 15f), new Vector2(x + 13f, top - 15f), new Vector2(x, top + 2.5f)), roof, Ink, 2.5f);
        }

        /// <summary>Snow on the frost altar's roof: a cap along the ridge and drifts on the eaves, icicles below.</summary>
        static void Snow(Canvas c, Func<float, float, float> roofIn, int w, float eave, float top, float inset)
        {
            var white = Hex(0xFAFDFF);
            c.ShapeClipped(Union(RoundRect(0f, top - 12f, w, top + 4f, 0f), CircleRow(4f, top - 12f, 11f, 4.5f)), roofIn, white);
            c.ShapeClipped(Union(RoundRect(0f, eave - 6f, w, eave + 4f, 0f), CircleRow(2f, eave + 4f, 9f, 3.5f)), roofIn, white);
            for (float x = 12f; x < w - 10f; x += 13f)
                c.Shape(Poly(0.6f, new Vector2(x - 2.2f, eave - 3.5f), new Vector2(x + 2.2f, eave - 3.5f), new Vector2(x, eave - 10f)), Hex(0xC8ECFF), Ink, 1f);
        }

        // ---------------- What a raised building adds (P4-115) ----------------

        /// <summary>
        /// Little gabled windows in the roof (level 2 and up): as many as asked where the chimney, the gable and the town
        /// hall's turret leave room, spread across it and kept clear of its slanting ends.
        /// </summary>
        static void Dormers(Canvas c, in Look k, BT type, float w, float eave, float top, float inset, float doorX, float gableHalf, int count)
        {
            float y0 = eave + (top - eave) * 0.2f, yTop = y0 + 24f;
            float t = Mathf.Clamp01((yTop - eave + 1f) / (top - eave - 2f));
            float left = 5f + inset * t + 12f, right = w - 5f - inset * t - 12f;
            if (right < left) return;
            float chimney = k.Chimney > 0f ? w * k.Chimney : float.NaN;
            bool Free(float x) => !(Mathf.Abs(x - chimney) < 20f)
                                  && !(gableHalf > 0f && Mathf.Abs(x - doorX) < gableHalf + 14f)
                                  && !(type == BT.TownHall && Mathf.Abs(x - w * 0.27f) < 26f);
            Span<float> placed = stackalloc float[4];
            int n = 0;
            for (int i = 0; i < count && n < placed.Length; i++)
            {
                // The free place nearest this dormer's share of the roof, not crowding one already put in.
                float want = left + (right - left) * (i + 0.5f) / count, best = float.NaN;
                for (float x = left; x <= right; x += 2f)
                {
                    if (!Free(x)) continue;
                    bool crowded = false;
                    for (int j = 0; j < n; j++) crowded |= Mathf.Abs(x - placed[j]) < 28f;
                    if (crowded) continue;
                    if (float.IsNaN(best) || Mathf.Abs(x - want) < Mathf.Abs(best - want)) best = x;
                }
                if (float.IsNaN(best)) break;
                placed[n++] = best;
                Dormer(c, k, best, y0);
            }
        }

        static void Dormer(Canvas c, in Look k, float x, float y0)
        {
            var box = RoundRect(x - 8f, y0, x + 8f, y0 + 14f, 1.5f);
            c.Shape(box, k.Wall, Ink, 2.2f);
            var glass = Union(RoundRect(x - 4.5f, y0 + 2.5f, x + 4.5f, y0 + 8.5f, 0.8f), Circle(x, y0 + 8.5f, 4.5f));
            c.Shape(glass, k.Glass, Ink, 1.8f);
            c.ShapeClipped(RoundRect(x - 0.7f, 0f, x + 0.7f, y0 + 14f, 0f), (px, py) => glass(px, py) + 1.8f, Timber);
            c.ShapeClipped(RoundRect(x + 3f, 0f, x + 9f, y0 + 14f, 0f), box, ShadeTint);
            var roof = Poly(1.5f, new Vector2(x - 11.5f, y0 + 12f), new Vector2(x + 11.5f, y0 + 12f), new Vector2(x, y0 + 24f));
            c.Shape(roof, k.Roof, Ink, 2.5f);
            c.ShapeClipped(RoundRect(x, 0f, x + 14f, y0 + 26f, 0f), roof, ShadeTint);
        }

        /// <summary>A gilt cresting along the ridge (level 3): a rail of little knobs and a finial at each end.</summary>
        static void Cresting(Canvas c, float x0, float x1, float top)
        {
            c.Shape(RoundRect(x0 + 2f, top - 1.5f, x1 - 2f, top + 1.2f, 1f), Gold, Ink, 1f);
            for (float x = x0 + 9f; x < x1 - 6f; x += 9f) c.Shape(Circle(x, top + 2.6f, 1.6f), Gold, Ink, 0.9f);
            c.Shape(Circle(x0 + 2.5f, top + 2.5f, 3f), Gold, Ink, 1.3f);
            c.Shape(Circle(x1 - 2.5f, top + 2.5f, 3f), Gold, Ink, 1.3f);
        }

        /// <summary>
        /// A pennant on a pole over the ridge (level 2: at the end clear of the chimney and the turret; level 3: both ends) —
        /// the guild's blue on its own buildings, the town's red on the town's.
        /// </summary>
        static void RidgePennants(Canvas c, BT type, in Look k, float w, float top, float inset, bool both)
        {
            float left = 7f + inset, right = w - 7f - inset;
            float busy = type == BT.TownHall ? w * 0.27f : k.Chimney > 0f ? w * k.Chimney : -1f;
            var colour = type is BT.TownHall or BT.Blacksmith or BT.Apothecary ? Hex(0xD9534F) : Hex(0x3F6FC4);
            bool rightFirst = busy < 0f || Mathf.Abs(busy - right) >= Mathf.Abs(busy - left);
            Pennant(c, rightFirst ? right : left, top, colour);
            if (both) Pennant(c, rightFirst ? left : right, top, colour);
        }

        static void Pennant(Canvas c, float x, float top, Color colour)
        {
            c.Shape(Segment(x, top - 3f, x, top + 13f, 0.9f), Iron, Iron, 0f);
            c.Shape(Poly(0.6f, new Vector2(x + 0.8f, top + 13f), new Vector2(x + 0.8f, top + 6.5f), new Vector2(x + 12f, top + 9.5f)), colour, Ink, 1.3f);
            c.Shape(Circle(x, top + 13.5f, 1.6f), Gold, Ink, 0.9f);
        }

        /// <summary>Dressed stone at the wall's corners (level 3), long and short blocks in turn.</summary>
        static void Quoins(Canvas c, float x0, float x1, float wallTop)
        {
            int i = 0;
            for (float y = 13f; y < wallTop - 6f; y += 8f, i++)
            {
                float reach = i % 2 == 0 ? 10f : 6.5f;
                c.Shape(RoundRect(x0 + 0.5f, y, x0 + reach, y + 7f, 1.2f), Lighter(Stonework, 0.25f), Ink, 1.5f);
                c.Shape(RoundRect(x1 - reach, y, x1 - 0.5f, y + 7f, 1.2f), Stonework, Ink, 1.5f);
            }
        }

        // ---------------- Things by the door ----------------

        static void Props(Canvas c, BT type, float doorX, float dw, float x0, float x1)
        {
            float left = x0 + 6f, right = x1 - 6f;
            switch (type)
            {
                case BT.TownHall:
                case BT.Dormitory:
                case BT.Apothecary:
                    Pot(c, doorX - dw - 9f, 1f, 0);
                    Pot(c, doorX + dw + 9f, 1f, 1);
                    if (type == BT.Apothecary) Barrel(c, right, 1f);
                    break;
                case BT.GuildHall:
                    Barrel(c, left, 1f);
                    Crate(c, left + 6f, 1f, 10f);
                    Barrel(c, right, 1f);
                    break;
                case BT.Blacksmith:
                    Anvil(c, right - 4f, 1f);
                    Barrel(c, left, 1f);
                    for (int i = -1; i <= 1; i++)   // swords stood in the barrel
                        c.Shape(Segment(left + i * 2.5f, 12f, left + i * 3.5f, 22f, 0.9f), Hex(0xD9DEE6), Ink, 0.8f);
                    break;
                case BT.Armory:
                    Rack(c, right - 2f, 1f);
                    Crate(c, left - 5f, 1f, 10f);
                    break;
                case BT.ArmorWorkshop:
                    Crate(c, left - 5f, 1f, 10f);
                    c.Shape(Union(RoundRect(right - 6f, 4f, right + 6f, 14f, 2f), Circle(right, 4f, 6f)), Hex(0x8C9AAE), Ink, 2f);   // a shield leant on the wall
                    c.Shape(Segment(right, 0f, right, 13f, 1.1f), Gold, Gold, 0f);
                    break;
                case BT.DiningHall:
                    Barrel(c, right, 1f);
                    Barrel(c, right - 11f, 1f);
                    Crate(c, left - 5f, 1f, 10f);
                    break;
                case BT.PoisonLab:
                    Cauldron(c, right - 2f, 1f, Hex(0x8BE04A));
                    Pot(c, left, 1f, 2);
                    break;
                case BT.RuneForge:
                    RuneStone(c, right, 1f);
                    Crate(c, left - 5f, 1f, 10f);
                    break;
                case BT.FrostAltar:
                    Crystal(c, right, 1f);
                    Crystal(c, left, 1f);
                    break;
            }
        }

        static void Barrel(Canvas c, float cx, float y0)
        {
            var body = RoundRect(cx - 5.5f, y0, cx + 5.5f, y0 + 13f, 4f);
            c.Shape(body, Hex(0xB7783F), Ink, 2f);
            Func<float, float, float> inside = (x, y) => body(x, y) + 2f;
            c.ShapeClipped(RoundRect(cx - 6f, y0 + 3f, cx + 6f, y0 + 4.5f, 0f), inside, Iron);
            c.ShapeClipped(RoundRect(cx - 6f, y0 + 8.5f, cx + 6f, y0 + 10f, 0f), inside, Iron);
            c.ShapeClipped(RoundRect(cx + 1.5f, 0f, cx + 8f, y0 + 14f, 0f), body, ShadeTint);
        }

        static void Crate(Canvas c, float x, float y0, float s)
        {
            var box = RoundRect(x, y0, x + s, y0 + s, 1.5f);
            var wood = Hex(0xD19A5B);
            c.Shape(box, wood, Ink, 2f);
            Func<float, float, float> inside = (px, py) => box(px, py) + 2f;
            c.ShapeClipped(Segment(x + 2f, y0 + 2f, x + s - 2f, y0 + s - 2f, 1f), inside, Darker(wood, 0.75f));
            c.ShapeClipped(Segment(x + 2f, y0 + s - 2f, x + s - 2f, y0 + 2f, 1f), inside, Darker(wood, 0.75f));
        }

        static void Pot(Canvas c, float cx, float y0, int variant)
        {
            c.Shape(Circle(cx, y0 + 9.5f, 5.2f), Leaves, Ink, 1.5f);
            c.Shape(Circle(cx - 2f, y0 + 11f, 1.7f), Blooms[variant % Blooms.Length], Ink, 0.9f);
            c.Shape(Circle(cx + 2.2f, y0 + 9f, 1.7f), Blooms[(variant + 1) % Blooms.Length], Ink, 0.9f);
            c.Shape(RoundRect(cx - 4.2f, y0, cx + 4.2f, y0 + 6.5f, 1.5f), Hex(0xC9663A), Ink, 1.8f);
        }

        static void Anvil(Canvas c, float cx, float y0)
        {
            c.Shape(RoundRect(cx - 5f, y0, cx + 5f, y0 + 6.5f, 2f), Plank, Ink, 1.8f);   // the stump
            c.Shape(Union(RoundRect(cx - 7f, y0 + 9.5f, cx + 6f, y0 + 13f, 1.2f), RoundRect(cx - 2.5f, y0 + 5.5f, cx + 2.5f, y0 + 10f, 0.5f),
                          Circle(cx - 7f, y0 + 11.6f, 2f)), Hex(0x5A5F69), Ink, 1.8f);
        }

        static void Rack(Canvas c, float cx, float y0)
        {
            for (int i = -1; i <= 1; i++)   // spears
            {
                float sx = cx + i * 4f;
                c.Shape(Segment(sx, y0 + 1f, sx, y0 + 20f, 0.9f), Plank, Ink, 0.8f);
                c.Shape(Poly(0.5f, new Vector2(sx - 1.8f, y0 + 19f), new Vector2(sx + 1.8f, y0 + 19f), new Vector2(sx, y0 + 25f)), Hex(0xD9DEE6), Ink, 1f);
            }
            c.Shape(RoundRect(cx - 8f, y0 + 9f, cx + 8f, y0 + 11.5f, 1f), Timber, Ink, 1.5f);
            c.Shape(RoundRect(cx - 9f, y0, cx - 6f, y0 + 13f, 1f), Timber, Ink, 1.5f);
            c.Shape(RoundRect(cx + 6f, y0, cx + 9f, y0 + 13f, 1f), Timber, Ink, 1.5f);
        }

        static void Cauldron(Canvas c, float cx, float y0, Color brew)
        {
            c.Shape(Ellipse(cx, y0 + 6.5f, 8f, 6.5f), Hex(0x3D4048), Ink, 2f);
            c.Shape(Ellipse(cx, y0 + 11.5f, 7.2f, 2.4f), brew, Ink, 1.5f);
            c.Shape(Circle(cx - 2f, y0 + 15f, 1.8f), Lighter(brew, 0.4f), Ink, 0.9f);
            c.Shape(Circle(cx + 2.5f, y0 + 18f, 1.3f), Lighter(brew, 0.4f), Ink, 0.9f);
        }

        static void RuneStone(Canvas c, float cx, float y0)
        {
            var stone = RoundRect(cx - 5f, y0, cx + 5f, y0 + 17f, 3.5f);
            c.Shape(stone, Hex(0x8C88A0), Ink, 2f);
            Func<float, float, float> inside = (x, y) => stone(x, y) + 2f;
            var glow = Hex(0xC9B5FF);
            c.ShapeClipped(Segment(cx, y0 + 4f, cx, y0 + 13f, 0.9f), inside, glow);
            c.ShapeClipped(Segment(cx - 2.5f, y0 + 10f, cx + 2.5f, y0 + 7f, 0.9f), inside, glow);
            c.ShapeClipped(RoundRect(cx + 1.5f, 0f, cx + 6f, y0 + 18f, 0f), stone, ShadeTint);
        }

        static void Crystal(Canvas c, float cx, float y0)
        {
            var ice = Hex(0xBDE8FA);
            c.Shape(Poly(0.8f, new Vector2(cx - 6f, y0 + 1f), new Vector2(cx - 2f, y0 + 1f), new Vector2(cx - 5f, y0 + 11f)), ice, Ink, 1.5f);
            c.Shape(Poly(0.8f, new Vector2(cx + 2f, y0 + 1f), new Vector2(cx + 6f, y0 + 1f), new Vector2(cx + 5f, y0 + 10f)), ice, Ink, 1.5f);
            c.Shape(Poly(0.8f, new Vector2(cx - 3f, y0 + 1f), new Vector2(cx + 3f, y0 + 1f), new Vector2(cx, y0 + 16f)), Lighter(ice, 0.3f), Ink, 1.5f);
        }

        /// <summary>
        /// A round sign's picture of what is done inside (P4-87: the facilities; P4-114: the smith and the apothecary too),
        /// drawn <paramref name="k"/> times the size it was made at (a plaque of radius 13).
        /// </summary>
        static void Emblem(Canvas c, BT type, float cx, float cy, float k)
        {
            var steel = Hex(0xC9CED8);
            Func<float, float, float> S(float x0, float y0, float x1, float y1, float r) => Segment(cx + x0 * k, cy + y0 * k, cx + x1 * k, cy + y1 * k, r * k);
            Func<float, float, float> R(float x0, float y0, float x1, float y1, float r) => RoundRect(cx + x0 * k, cy + y0 * k, cx + x1 * k, cy + y1 * k, r * k);
            Func<float, float, float> O(float x, float y, float r) => Circle(cx + x * k, cy + y * k, r * k);
            switch (type)
            {
                case BT.Armory: // crossed swords
                    c.Shape(S(-7, -7, 7, 7, 1.8f), steel, Ink, 1.2f * k);
                    c.Shape(S(7, -7, -7, 7, 1.8f), steel, Ink, 1.2f * k);
                    c.Shape(S(-7, -3, -3, -7, 1.4f), Hex(0x8A5A30), Ink, k);
                    c.Shape(S(7, -3, 3, -7, 1.4f), Hex(0x8A5A30), Ink, k);
                    break;
                case BT.ArmorWorkshop: // shield
                    c.Shape(Union(R(-7, -2, 7, 8, 2), O(0, -2, 7)), Hex(0x5B7A99), Ink, 2f * k);
                    c.Shape(S(0, -7, 0, 6, 1.2f), Gold, Gold, 0f);
                    break;
                case BT.DiningHall: // bowl with steam
                    c.Shape(Union(Ellipse(cx, cy - 2 * k, 9 * k, 5 * k), R(-9, -2, 9, 0, 1)), Hex(0xE8C98A), Ink, 2f * k);
                    c.Shape(S(-3, 3, -3, 8, 1.1f), Hex(0xBFC6D0), Hex(0xBFC6D0), 0f);
                    c.Shape(S(3, 3, 3, 8, 1.1f), Hex(0xBFC6D0), Hex(0xBFC6D0), 0f);
                    break;
                case BT.PoisonLab: // flask with green brew
                case BT.Apothecary: // and a red one
                    c.Shape(Union(O(0, -3, 6.5f), R(-2.5f, 0, 2.5f, 8, 1)), type == BT.Apothecary ? Hex(0xE85A6B) : Hex(0x8BE04A), Ink, 2f * k);
                    c.Shape(O(-2, -2, 1.4f), Hex(0xF4FFE6), Hex(0xF4FFE6), 0f);
                    break;
                case BT.RuneForge: // rune ring
                    c.Shape(O(0, 0, 8), Hex(0xB8A2F5), Ink, 2f * k);
                    c.Shape(O(0, 0, 4), Hex(0xF6E7C1), Ink, 1.5f * k);
                    c.Shape(S(0, -8, 0, 8, 1), Hex(0x4A2E9A), Hex(0x4A2E9A), 0f);
                    break;
                case BT.FrostAltar: // snowflake
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI / 3f, dx = Mathf.Cos(a) * 8f, dy = Mathf.Sin(a) * 8f;
                        c.Shape(S(-dx, -dy, dx, dy, 1.4f), Hex(0x9ED8F5), Hex(0x2B6E9A), 0.8f * k);
                    }
                    break;
                case BT.Dormitory: // bed
                    c.Shape(R(-9, -5, 9, 1, 2), Hex(0xD9534F), Ink, 1.5f * k);
                    c.Shape(R(-9, -1, -3, 4, 1.5f), Hex(0xFFF6DA), Ink, 1.5f * k);
                    c.Shape(R(-10, -7, -8, 6, 0.8f), Hex(0x8A5A30), Ink, k);
                    break;
                case BT.Blacksmith: // anvil and hammer
                    c.Shape(Union(R(-8, 0, 7, 4, 1.2f), R(-3, -5, 3, 1, 0.5f), R(-6, -8, 6, -5, 1), O(-8, 2, 2)), Hex(0x5A5F69), Ink, 1.4f * k);
                    c.Shape(S(2, 6, 7, 10, 1.1f), Hex(0x8A5A30), Ink, k);
                    c.Shape(R(-1, 5, 5, 8.5f, 1), Hex(0x8C929C), Ink, 1.2f * k);
                    break;
            }
        }

        // ---------------- Yards and towers ----------------

        // The training ground (P4-115): drawn in layers, since people walk in it — as one picture over them it hid everyone
        // training. The sand, the side fences and the shadows lie under the people; the back and front fences and each
        // dummy, target, rack and banner stand among them, sorted by their foot (Town.Building). What stands where, by the
        // ground's level: Town.TrainingYardLayout.

        /// <summary>The whole yard in one picture (the build window's preview).</summary>
        static void TrainingYard(Canvas c, float gateX, int level)
        {
            YardGround(c, gateX, level);
            YardFence(c, gateX, front: false);
            foreach (var s in TrainingYardLayout.Of(level)) Station(c, s.Kind, s.At.x * c.W, s.At.y * c.H);
            YardFence(c, gateX, front: true);
        }

        /// <summary>The sand, the side fences and the shadows of what stands on it, laid under the people.</summary>
        public static Sprite TrainingYardGround(int tilesW, int tilesH, int gateColumn, int level)
        {
            string key = $"c_yard_ground:{tilesW}x{tilesH}:{gateColumn}:{level}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(tilesW * PixelsPerUnit, tilesH * PixelsPerUnit, BuildingDetail);
            YardGround(c, (gateColumn + 0.5f) * PixelsPerUnit, level);
            return c.ToSprite(key, new Vector2(0.5f, 0.5f));
        }

        /// <summary>The front fence (open at the gate) or the back one, over the whole footprint.</summary>
        public static Sprite TrainingYardFence(int tilesW, int tilesH, int gateColumn, bool front)
        {
            string key = $"c_yard_fence:{tilesW}x{tilesH}:{gateColumn}:{front}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(tilesW * PixelsPerUnit, tilesH * PixelsPerUnit, BuildingDetail);
            YardFence(c, (gateColumn + 0.5f) * PixelsPerUnit, front);
            return c.ToSprite(key, new Vector2(0.5f, 0.5f));
        }

        /// <summary>A dummy, target, rack or banner on its own, pivoted at its foot.</summary>
        public static Sprite TrainingStation(TrainingYardLayout.Kind kind)
        {
            string key = $"c_yard_station:{kind}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            // The picture's size and where its foot is in it (logical pixels).
            var (w, h, fx, fy) = kind switch
            {
                TrainingYardLayout.Kind.Dummy => (52, 60, 26f, 5f),
                TrainingYardLayout.Kind.Target => (32, 42, 16f, 3f),
                TrainingYardLayout.Kind.Rack => (54, 28, 27f, 2f),
                _ => (34, 66, 8f, 3f),
            };
            var c = new Canvas(w, h, BuildingDetail);
            Station(c, kind, fx, fy);
            return c.ToSprite(key, new Vector2(fx / w, fy / h));
        }

        const float YardSideRail0 = 8f, YardSideRail1 = 12.5f;

        static void YardGround(Canvas c, float gateX, int level)
        {
            int w = c.W, h = c.H;
            var sand = Hex(0xE3C48E);
            c.Shape(RoundRect(4f, 4f, w - 4f, h - 4f, 10f), sand, Hex(0xB89361), 3f);
            for (int i = 0; i < 26; i++)   // pebbles and scuffs
            {
                float px = 14f + (i * 37 % 97) / 97f * (w - 28f), py = 14f + (i * 61 % 89) / 89f * (h - 28f);
                c.Shape(Ellipse(px, py, 2.6f + i % 3, 1.6f), Darker(sand, 0.88f), Darker(sand, 0.88f), 0f);
            }
            // Worn ground where people stand to strike, and the things' shadows.
            foreach (var s in TrainingYardLayout.Of(level))
            {
                float x = s.At.x * w, y = s.At.y * h;
                if (s.Drills) c.Shape(Ellipse(x, y + 1f, 34f, 8f), Darker(sand, 0.93f), Darker(sand, 0.93f), 0f);
                var (rx, ry) = s.Kind switch
                {
                    TrainingYardLayout.Kind.Dummy => (12f, 4f),
                    TrainingYardLayout.Kind.Target => (11f, 3f),
                    TrainingYardLayout.Kind.Rack => (25f, 3.5f),
                    _ => (6f, 2f),
                };
                c.Shape(Ellipse(x, y + 1f, rx, ry), FootShadow, FootShadow, 0f);
            }
            // The side fences: low enough to lie under whoever walks by.
            c.Shape(Union(RoundRect(YardSideRail0, 9f, YardSideRail1, h - 9f, 1f), RoundRect(w - YardSideRail1, 9f, w - YardSideRail0, h - 9f, 1f)),
                    Plank, Ink, 1.5f);
            for (float y = 34f; y < h - 20f; y += 24f)
            {
                c.Shape(RoundRect(6.5f, y - 6f, 13.5f, y + 6f, 2f), Plank, Ink, 2f);
                c.Shape(RoundRect(w - 13.5f, y - 6f, w - 6.5f, y + 6f, 2f), Plank, Ink, 2f);
            }
        }

        static void YardFence(Canvas c, float gateX, bool front)
        {
            int w = c.W, h = c.H;
            if (front)
            {
                c.Shape(Union(RoundRect(8f, 9f, gateX - 14f, 12.5f, 1f), RoundRect(gateX + 14f, 9f, w - 8f, 12.5f, 1f)), Plank, Ink, 1.5f);
                for (float x = 10f; x <= w - 9f; x += 24f)
                    if (Mathf.Abs(x - gateX) >= 14f) c.Shape(RoundRect(x - 3.5f, 5f, x + 3.5f, 17f, 2f), Plank, Ink, 2f);
                c.Shape(RoundRect(gateX - 17f, 4f, gateX - 11f, 22f, 2f), Plank, Ink, 2f);   // gateposts
                c.Shape(RoundRect(gateX + 11f, 4f, gateX + 17f, 22f, 2f), Plank, Ink, 2f);
                return;
            }
            c.Shape(RoundRect(8f, h - 13f, w - 8f, h - 9.5f, 1f), Plank, Ink, 1.5f);
            for (float x = 10f; x <= w - 9f; x += 24f)
                c.Shape(RoundRect(x - 3.5f, h - 17f, x + 3.5f, h - 5f, 2f), Plank, Ink, 2f);
        }

        /// <summary>One thing in the yard with its foot at (<paramref name="x"/>, <paramref name="foot"/>).</summary>
        static void Station(Canvas c, TrainingYardLayout.Kind kind, float x, float foot)
        {
            switch (kind)
            {
                case TrainingYardLayout.Kind.Dummy: Dummy(c, x, foot + 22f); break;
                case TrainingYardLayout.Kind.Target: Target(c, x, foot + 20f); break;
                case TrainingYardLayout.Kind.Rack: SwordRack(c, x, foot + 6f); break;
                case TrainingYardLayout.Kind.Banner: YardBanner(c, x, foot); break;
            }
        }

        static void Dummy(Canvas c, float x, float y)
        {
            var straw = Hex(0xF2C14E);
            c.Shape(Segment(x, y - 22f, x, y + 4f, 2f), Timber, Ink, 1.8f);
            c.Shape(RoundRect(x - 17f, y + 6f, x + 17f, y + 10.5f, 2f), Timber, Ink, 1.8f);
            c.Shape(Circle(x - 17f, y + 8f, 3.5f), straw, Ink, 1.8f);   // straw sticking out of the arms
            c.Shape(Circle(x + 17f, y + 8f, 3.5f), straw, Ink, 1.8f);
            var body = Ellipse(x, y + 3f, 11f, 14f);
            c.Shape(body, straw, Ink, 2.8f);
            Func<float, float, float> inBody = (px, py) => body(px, py) + 2.8f;
            c.ShapeClipped(RoundRect(x - 12f, y - 2f, x + 12f, y + 2f, 0f), inBody, Hex(0xD9534F));   // a red sash
            c.ShapeClipped(RoundRect(x - 12f, y + 9f, x + 12f, y + 10.5f, 0f), inBody, Hex(0xB98A3A));
            c.ShapeClipped(RoundRect(x + 4f, y - 12f, x + 12f, y + 18f, 0f), body, ShadeTint);
            var head = Circle(x, y + 22f, 7.5f);
            c.Shape(head, straw, Ink, 2.8f);
            c.ShapeClipped(RoundRect(x + 3f, y + 14f, x + 9f, y + 30f, 0f), head, ShadeTint);
            c.Shape(Segment(x - 3f, y + 20.5f, x + 3f, y + 18.5f, 1f), Ink, Ink, 0f);   // a scowl painted on
            c.Shape(Circle(x - 2.5f, y + 24f, 1.2f), Ink, Ink, 0f);
            c.Shape(Circle(x + 2.5f, y + 24f, 1.2f), Ink, Ink, 0f);
        }

        static void Target(Canvas c, float tx, float ty)
        {
            c.Shape(Segment(tx - 6f, ty - 20f, tx - 2f, ty, 1.4f), Timber, Ink, 1.5f);
            c.Shape(Segment(tx + 6f, ty - 20f, tx + 2f, ty, 1.4f), Timber, Ink, 1.5f);
            c.Shape(Circle(tx, ty + 4f, 13f), Hex(0xFFF6E6), Ink, 2.5f);
            c.Shape(Circle(tx, ty + 4f, 9f), Hex(0xD9534F), Ink, 0f);
            c.Shape(Circle(tx, ty + 4f, 5.5f), Hex(0xFFF6E6), Ink, 0f);
            c.Shape(Circle(tx, ty + 4f, 2.5f), Hex(0xD9534F), Ink, 0f);
            c.Shape(Segment(tx + 2f, ty + 6f, tx + 10f, ty + 14f, 0.9f), Plank, Ink, 0.7f);   // an arrow in it
        }

        /// <summary>A rack of practice swords.</summary>
        static void SwordRack(Canvas c, float rx, float ry)
        {
            c.Shape(RoundRect(rx - 22f, ry + 6f, rx + 22f, ry + 9f, 1f), Timber, Ink, 1.5f);
            for (float x = rx - 16f; x <= rx + 16f; x += 8f)
                c.Shape(Segment(x, ry - 4f, x, ry + 16f, 1.1f), Hex(0xC9A57A), Ink, 1f);
            c.Shape(RoundRect(rx - 24f, ry - 6f, rx - 20f, ry + 12f, 1f), Timber, Ink, 1.5f);
            c.Shape(RoundRect(rx + 20f, ry - 6f, rx + 24f, ry + 12f, 1f), Timber, Ink, 1.5f);
        }

        /// <summary>The guild's banner on a pole (a level-3 yard): blue with a swallowtail and the crest's swords.</summary>
        static void YardBanner(Canvas c, float x, float foot)
        {
            float top = foot + 58f;
            c.Shape(Segment(x, foot, x, top, 1.7f), Timber, Ink, 1.4f);
            var cloth = Poly(1f, new Vector2(x + 1.5f, top - 4f), new Vector2(x + 24f, top - 4f), new Vector2(x + 24f, top - 30f),
                             new Vector2(x + 12.75f, top - 24f), new Vector2(x + 1.5f, top - 30f));
            c.Shape(cloth, Hex(0x3F6FC4), Ink, 2f);
            c.ShapeClipped(RoundRect(x + 16f, 0f, x + 30f, top + 4f, 0f), cloth, ShadeTint);
            float cx = x + 12.75f, cy = top - 14f;
            c.Shape(Segment(cx - 5f, cy - 5f, cx + 5f, cy + 5f, 1.2f), Hex(0xE3E7EE), Ink, 0.8f);
            c.Shape(Segment(cx + 5f, cy - 5f, cx - 5f, cy + 5f, 1.2f), Hex(0xE3E7EE), Ink, 0.8f);
            c.Shape(Circle(x, top + 1.5f, 3f), Gold, Ink, 1.4f);
        }

        /// <summary>A stone shaft with battlements, a lit lookout window and a pennant (P4-13).</summary>
        static void Watchtower(Canvas c, int h)
        {
            int w = c.W;
            var stone = Hex(0xB9BCC6);
            float shaftW = w * 0.34f, cx = w * 0.5f;
            c.Shape(Ellipse(cx, 6f, w * 0.42f, 5f), FootShadow, FootShadow, 0f);
            c.Shape(RoundRect(cx - shaftW * 0.72f, 2f, cx + shaftW * 0.72f, h * 0.22f, 4f), Darker(stone, 0.85f), Ink, 3f);
            var shaft = RoundRect(cx - shaftW, h * 0.16f, cx + shaftW, h * 0.74f, 4f);
            c.Shape(shaft, stone, Ink, 3f);
            Courses(c, (x, y) => shaft(x, y) + 3f, cx - shaftW, cx + shaftW, h * 0.2f, h * 0.72f, Darker(stone, 0.8f));
            c.Shape(Union(RoundRect(cx - 7f, h * 0.44f, cx + 7f, h * 0.56f, 1f), Circle(cx, h * 0.56f, 7f)), Hex(0xFFD86B), Ink, 2.5f);
            c.ShapeClipped(RoundRect(cx + shaftW * 0.35f, 0f, w, h, 0f), shaft, ShadeTint);
            var top = RoundRect(cx - shaftW * 1.25f, h * 0.70f, cx + shaftW * 1.25f, h * 0.82f, 3f);
            c.Shape(top, Lighter(stone, 0.12f), Ink, 3f);
            float merlonW = shaftW * 0.5f;
            for (int i = -1; i <= 1; i++)
            {
                float mx = cx + i * shaftW * 0.8f;
                c.Shape(RoundRect(mx - merlonW * 0.5f, h * 0.81f, mx + merlonW * 0.5f, h * 0.92f, 2f), Lighter(stone, 0.12f), Ink, 2.5f);
            }
            c.ShapeClipped(RoundRect(cx + shaftW * 0.45f, 0f, w, h, 0f), top, ShadeTint);
            c.Shape(RoundRect(cx - 1.5f, h * 0.88f, cx + 1.5f, h - 3f, 1f), Timber, Ink, 1.5f);
            c.Shape(Poly(1f, new Vector2(cx + 2f, h - 15f), new Vector2(cx + 2f, h - 4f), new Vector2(cx + 19f, h - 9.5f)), Hex(0xD9534F), Ink, 2f);
        }

        /// <summary>
        /// A timber tower on four legs with a railed platform, a peaked roof and an arrow slit (P4-113). Raised (P4-115): round
        /// shields hung on the platform at level 2, and at level 3 iron bands on the legs and a pennant over the roof.
        /// </summary>
        static void ArrowTower(Canvas c, int h, int level)
        {
            int w = c.W;
            var timber = Hex(0xB07A44);
            float cx = w * 0.5f, legW = w * 0.06f, spread = w * 0.3f;
            c.Shape(Ellipse(cx, 6f, w * 0.42f, 5f), FootShadow, FootShadow, 0f);
            c.Shape(RoundRect(cx - spread - legW, 2f, cx - spread + legW, h * 0.56f, 2f), Darker(timber, 0.85f), Ink, 2.5f);
            c.Shape(RoundRect(cx + spread - legW, 2f, cx + spread + legW, h * 0.56f, 2f), Darker(timber, 0.7f), Ink, 2.5f);
            c.Shape(Segment(cx - spread, h * 0.1f, cx + spread, h * 0.4f, 1.6f), Darker(timber, 0.75f), Ink, 1.5f);
            c.Shape(Segment(cx + spread, h * 0.1f, cx - spread, h * 0.4f, 1.6f), Darker(timber, 0.75f), Ink, 1.5f);
            var box = RoundRect(cx - spread * 1.25f, h * 0.48f, cx + spread * 1.25f, h * 0.74f, 3f);
            c.Shape(box, timber, Ink, 3f);
            for (float x = cx - spread * 1.25f + 8f; x < cx + spread * 1.25f - 4f; x += 8f)
            {
                float xx = x;
                c.ShapeClipped(RoundRect(xx, h * 0.48f, xx + 1.3f, h * 0.74f, 0.5f), (px, py) => box(px, py) + 3f, Darker(timber, 0.8f));
            }
            c.Shape(RoundRect(cx - 2.5f, h * 0.54f, cx + 2.5f, h * 0.68f, 1.5f), Hex(0x2B2420), Ink, 1.5f);
            c.ShapeClipped(RoundRect(cx + spread * 0.5f, 0f, w, h, 0f), box, ShadeTint);
            if (level >= 2)
                for (int s = -1; s <= 1; s += 2)
                {
                    float sx = cx + s * spread * 0.75f, sy = h * 0.61f;
                    c.Shape(Circle(sx, sy, 5.2f), s < 0 ? Hex(0x3F6FC4) : Hex(0xD9534F), Ink, 1.6f);
                    c.Shape(Circle(sx, sy, 1.8f), Hex(0xC9CED8), Ink, 1f);
                }
            if (level >= 3)
                for (int s = -1; s <= 1; s += 2)
                    foreach (float by in new[] { h * 0.14f, h * 0.36f })
                        c.Shape(RoundRect(cx + s * spread - legW - 1f, by, cx + s * spread + legW + 1f, by + 2.6f, 0.8f), Iron, Ink, 1f);
            var roofCol = Hex(0x9B3B2E);
            var roof = Poly(2f, new Vector2(cx - spread * 1.45f, h * 0.74f), new Vector2(cx + spread * 1.45f, h * 0.74f), new Vector2(cx, h - 4f));
            c.Shape(roof, roofCol, Ink, 2.5f);
            c.ShapeClipped(RoundRect(cx, 0f, w, h, 0f), roof, ShadeTint);
            if (level >= 3) Pennant(c, cx, h - 5f, Hex(0x3F6FC4));
            c.Shape(Circle(cx, h - 4f, 2.5f), Gold, Ink, 1.5f);
        }

        // ---------------- Inside ----------------

        /// <summary>
        /// What the building looks like inside (seen when the outside fades out): plank floor inside
        /// thick walls, plus a few pieces of furniture per building type. Null for open yards.
        /// </summary>
        public static Sprite BuildingInterior(BT type, int tilesW, int tilesH)
        {
            // The guild hall is walked into (P4-115): its own inside, Town.GuildHallInterior.
            if (type is BT.TrainingGround or BT.Watchtower or BT.ArrowTower or BT.GuildHall) return null;
            string key = $"c_interior:{type}:{tilesW}x{tilesH}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            int w = tilesW * PixelsPerUnit, h = tilesH * PixelsPerUnit;
            var c = new Canvas(w, h);
            var wallBand = Hex(0xC9B48E);
            var floor = Hex(0xD9A866);
            // Outer walls, then the plank floor with board lines.
            var outer = RoundRect(2, 2, w - 2, h - 2, 6f);
            c.Shape(outer, wallBand, Ink, 3.5f);
            var room = RoundRect(12, 12, w - 12, h - 14, 3f);
            c.Shape(room, floor, Darker(wallBand, 0.7f), 2f);
            for (float y = 22; y < h - 16; y += 12)
            {
                float yy = y;
                c.ShapeClipped(RoundRect(14, yy, w - 14, yy + 1.5f, 0.5f), (x, y2) => room(x, y2) + 2f, Darker(floor, 0.85f));
            }
            // Doorway gap in the front wall.
            c.Shape(RoundRect(w * 0.5f - 11, 0, w * 0.5f + 11, 14, 2f), floor, floor, 0f);

            float cx = w * 0.5f;
            switch (type)
            {
                case BT.TownHall:
                    c.Shape(RoundRect(cx - 12, 14, cx + 12, h - 30, 3f), Hex(0xC0392B), Darker(Hex(0xC0392B), 0.6f), 2f);   // carpet
                    c.Shape(RoundRect(cx - 40, h - 44, cx + 40, h - 28, 4f), Hex(0x8A5A30), Ink, 2.5f);                    // long table
                    for (int i = -1; i <= 1; i++) c.Shape(Circle(cx + i * 26, h - 52, 5f), Hex(0xB07A44), Ink, 2f);       // chairs
                    break;
                case BT.GuildHall:
                    c.Shape(RoundRect(cx - 30, h - 30, cx + 30, h - 16, 2f), Hex(0xB98A55), Ink, 2.5f);                   // quest board
                    for (int i = 0; i < 4; i++) c.Shape(RoundRect(cx - 26 + i * 14, h - 27, cx - 16 + i * 14, h - 19, 1f), Hex(0xFFF6DA), Ink, 1f);
                    c.Shape(RoundRect(18, 30, 58, 42, 3f), Hex(0x8A5A30), Ink, 2.5f);                                      // counter
                    c.Shape(Circle(w - 40, 48, 11f), Hex(0xA56B3A), Ink, 2.5f);                                            // round table
                    c.Shape(Circle(w - 58, 48, 4.5f), Hex(0xB07A44), Ink, 2f);
                    c.Shape(Circle(w - 22, 48, 4.5f), Hex(0xB07A44), Ink, 2f);
                    break;
                case BT.Blacksmith:
                    c.Shape(RoundRect(16, h - 42, 44, h - 18, 4f), Hex(0x6B6F7A), Ink, 2.5f);                              // forge
                    c.Shape(Ellipse(30, h - 30, 8, 6), Hex(0xFF9F3A), Hex(0xFF6B2A), 2f);                                  // fire
                    c.Shape(Union(RoundRect(w - 52, 36, w - 20, 44, 2f), RoundRect(w - 42, 24, w - 30, 38, 1f)), Hex(0x4A4E57), Ink, 2f);  // anvil
                    break;
                case BT.Apothecary:
                    c.Shape(RoundRect(14, h - 32, w - 14, h - 20, 2f), Hex(0x8A5A30), Ink, 2f);                            // shelf
                    var bottles = new[] { Hex(0xE74C3C), Hex(0x3498DB), Hex(0x2ECC71), Hex(0xE74C3C), Hex(0x9B59B6) };
                    for (int i = 0; i < bottles.Length && 20 + i * 12 < w - 20; i++) c.Shape(Circle(22 + i * 12, h - 26, 4f), bottles[i], Ink, 1.5f);
                    c.Shape(RoundRect(cx - 18, 26, cx + 18, 36, 2f), Hex(0xA56B3A), Ink, 2f);                              // counter
                    break;
                // The guild's facilities (P4-87).
                case BT.Armory:
                    c.Shape(RoundRect(16, h - 30, w - 16, h - 22, 2f), Hex(0x8A5A30), Ink, 2f);                            // weapon rack
                    for (float x = 22; x < w - 18; x += 10) c.Shape(Segment(x, h - 44, x, h - 20, 1.6f), Hex(0xC9CED8), Ink, 1f);
                    break;
                case BT.ArmorWorkshop:
                    for (int i = -1; i <= 1; i += 2)
                        c.Shape(Union(Circle(cx + i * 18, h - 30, 6f), RoundRect(cx + i * 18 - 7, h - 50, cx + i * 18 + 7, h - 34, 3f)), Hex(0x8C9AAE), Ink, 2f);  // armour stands
                    break;
                case BT.DiningHall:
                    c.Shape(RoundRect(cx - 26, 30, cx + 26, 42, 3f), Hex(0xA56B3A), Ink, 2f);                              // long table
                    c.Shape(RoundRect(18, h - 34, 44, h - 20, 3f), Hex(0x6B6F7A), Ink, 2f);                                // stove
                    break;
                case BT.PoisonLab:
                    c.Shape(RoundRect(14, h - 32, w - 14, h - 20, 2f), Hex(0x8A5A30), Ink, 2f);                            // shelf
                    for (int i = 0; 22 + i * 12 < w - 20; i++) c.Shape(Circle(22 + i * 12, h - 26, 4f), Hex(0x8BE04A), Ink, 1.5f);
                    c.Shape(Circle(cx, 34, 8f), Hex(0x4A4E57), Ink, 2f);                                                    // cauldron
                    break;
                case BT.RuneForge:
                    c.Shape(Circle(cx, h * 0.5f, 16f), Hex(0xB8A2F5), Hex(0x4A2E9A), 2f);                                  // rune circle
                    c.Shape(Circle(cx, h * 0.5f, 9f), floor, Hex(0x4A2E9A), 1.5f);
                    break;
                case BT.FrostAltar:
                    c.Shape(RoundRect(cx - 14, h * 0.5f - 6, cx + 14, h * 0.5f + 8, 3f), Hex(0x9ED8F5), Hex(0x2B6E9A), 2f);  // altar
                    break;
                case BT.Dormitory:
                    for (float x = 20; x < w - 24; x += 22)
                    {
                        c.Shape(RoundRect(x, h - 46, x + 16, h - 18, 3f), Hex(0xD9534F), Ink, 2f);                         // beds
                        c.Shape(RoundRect(x + 2, h - 26, x + 14, h - 20, 2f), Hex(0xFFF6DA), Ink, 1f);
                    }
                    break;
            }
            return c.ToSprite(key, new Vector2(0.5f, 0.5f));
        }
    }
}
