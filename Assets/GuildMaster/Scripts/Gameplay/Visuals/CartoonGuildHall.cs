using System;
using UnityEngine;
using GuildHallLayout = GuildMaster.Town.GuildHallLayout;

namespace GuildMaster.Visuals
{
    /// <summary>The guild hall's furniture, each its own picture sorted among the people by its foot (P4-115).</summary>
    public enum HallFurniture { RoundTable, Counter, Barrels, Plant, ArmorStand }

    /// <summary>
    /// The guild hall cut open (P4-115, user decisions 2026-10-31: the guild's life is seen inside it): with the roof and the
    /// front taken away, a plank floor under the people with the low side walls' tops, the back wall seen from inside (the
    /// fire, the quest board, the shelf behind the counter, windows), a low front wall with the door posts, and the furniture.
    /// Raised, it fills: a rug, banners and sconces at level 2; at level 3 the guild's crest on the rug, a gilt frame to the
    /// board, a trophy over the fire and the light of a chandelier. Laid out by <see cref="GuildHallLayout"/>.
    /// </summary>
    public static partial class CartoonArt
    {
        /// <summary>How tall the back wall stands inside, and the low walls left of the sides and the front (logical pixels).</summary>
        const float HallBackWallH = 56f, HallLowWallH = 14f, HallWallThick = 13f;

        static readonly Color Plaster = Hex(0xF3E3C3), Wainscot = Hex(0xB98552), FloorPlank = Hex(0xD9A866), HallRoof = Hex(0x4A7BD0);

        /// <summary>The floor over the whole footprint, with the side walls' tops, the hearth, the step, stools and rugs.</summary>
        public static Sprite HallFloor(int level)
        {
            level = Mathf.Clamp(level, 1, 3);
            string key = $"c_hall_floor:{level}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            const int P = PixelsPerUnit;
            int w = GuildHallLayout.Width * P, h = GuildHallLayout.Height * P;
            float back = (GuildHallLayout.Height - 1) * P;   // the back wall's foot
            var c = new Canvas(w, h, BuildingDetail);
            var room = RoundRect(HallWallThick - 2f, 0f, w - HallWallThick + 2f, back + 8f, 0f);
            c.Shape(room, FloorPlank, FloorPlank, 0f);
            var seam = Darker(FloorPlank, 0.84f);
            int row = 0;
            for (float y = 10f; y < back + 8f; y += 10f, row++)   // boards, their ends staggered
            {
                float yy = y;
                c.ShapeClipped(RoundRect(0f, yy, w, yy + 1.1f, 0f), room, seam);
                for (float x = HallWallThick + (row % 2 == 0 ? 20f : 44f); x < w - HallWallThick; x += 48f)
                {
                    float xx = x;
                    c.ShapeClipped(RoundRect(xx, yy - 10f, xx + 1.1f, yy, 0f), room, seam);
                }
            }
            // Light from the windows and the door, and a chandelier's at level 3.
            c.ShapeClipped(Ellipse(w * 0.5f, P * 1.2f, P * 1.4f, P * 0.9f), room, new Color(1f, 0.95f, 0.75f, 0.18f));
            if (level >= 3) c.ShapeClipped(Ellipse(w * 0.5f, h * 0.45f, P * 2.6f, P * 1.6f), room, new Color(1f, 0.9f, 0.55f, 0.16f));

            // Rugs: plain at level 2, the guild's crest woven in at level 3 — along the aisle, door to board.
            if (level >= 2)
            {
                float cx = (GuildHallLayout.Entrance.x + 0.5f) * P;
                var rug = RoundRect(cx - 26f, P * 1.15f, cx + 26f, P * 5.6f, 6f);
                var rugColour = level >= 3 ? Hex(0x3F6FC4) : Hex(0xB5443B);
                c.Shape(rug, rugColour, Ink, 2f);
                c.Shape(RoundRect(cx - 21f, P * 1.15f + 5f, cx + 21f, P * 5.6f - 5f, 4f), rugColour, Gold, 1.8f);   // a border
                if (level >= 3)
                {
                    float my = P * 3.4f;
                    var steel = Hex(0xE3E7EE);
                    c.Shape(Poly(1.5f, new Vector2(cx - 12f, my + 12f), new Vector2(cx + 12f, my + 12f), new Vector2(cx + 12f, my - 1f),
                                 new Vector2(cx, my - 14f), new Vector2(cx - 12f, my - 1f)), Hex(0x2E5AA8), Gold, 2f);
                    c.Shape(Segment(cx - 7f, my - 6f, cx + 7f, my + 8f, 1.6f), steel, Ink, 1f);
                    c.Shape(Segment(cx + 7f, my - 6f, cx - 7f, my + 8f, 1.6f), steel, Ink, 1f);
                }
            }

            // Hearth stones before the fire, the step at the door.
            float fx = GuildHallLayout.FireX * P;
            c.Shape(Union(RoundRect(fx - 30f, back - 12f, fx + 30f, back + 6f, 4f)), Hex(0xB9B4AA), Ink, 2f);
            for (float x = fx - 20f; x < fx + 28f; x += 12f) c.Shape(RoundRect(x, back - 12f, x + 1.1f, back, 0f), Darker(Hex(0xB9B4AA), 0.8f), Darker(Hex(0xB9B4AA), 0.8f), 0f);
            float ex = GuildHallLayout.Entrance.x * P;
            c.Shape(RoundRect(ex + 1f, 0f, ex + P - 1f, 9f, 2f), Hex(0xBDB6AA), Ink, 2f);

            // Stools at the seats (under whoever stands at them).
            foreach (var s in GuildHallLayout.Spots)
            {
                if (s.Use is not (Town.HallUse.Seat or Town.HallUse.Table or Town.HallUse.Counter)) continue;
                float sx = (s.Cell.x + 0.5f) * P, sy = (s.Cell.y + 0.35f) * P;
                c.Shape(Ellipse(sx, sy - 3f, 7f, 3f), FootShadow, FootShadow, 0f);
                c.Shape(Ellipse(sx, sy, 7f, 4.5f), Hex(0xA56B3A), Ink, 1.6f);
            }

            // The side walls, cut low: their tops run back along each side.
            foreach (bool left in new[] { true, false })
            {
                float x0 = left ? 0f : w - HallWallThick, x1 = x0 + HallWallThick;
                var top = RoundRect(x0, HallLowWallH, x1, back + HallLowWallH + 6f, 0f);
                c.Shape(top, Wainscot, Ink, 2f);
                c.ShapeClipped(RoundRect(x0 + 3.5f, 0f, x1 - 3.5f, h, 0f), (x, y) => top(x, y) + 2f, Darker(Wainscot, 0.82f));
                c.Shape(RoundRect(left ? x1 - 1.5f : x0, HallLowWallH, left ? x1 + 2.5f : x0 + 1.5f, back + 6f, 0f), new Color(0f, 0f, 0f, 0.12f), new Color(0f, 0f, 0f, 0.12f), 0f);
            }
            return c.ToSprite(key, new Vector2(0.5f, 0.5f));
        }

        /// <summary>The back wall seen from inside, pivoted at its foot: plaster over panelling, the fire, the board, the shelf.</summary>
        public static Sprite HallBackWall(int level)
        {
            level = Mathf.Clamp(level, 1, 3);
            string key = $"c_hall_back:{level}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            const int P = PixelsPerUnit;
            int w = GuildHallLayout.Width * P;
            float H = HallBackWallH;
            var c = new Canvas(w, (int)H + 16, BuildingDetail);

            var wall = RoundRect(-6f, 0f, w + 6f, H, 0f);
            c.Shape(wall, Plaster, Ink, 2.5f);
            Func<float, float, float> onWall = (x, y) => wall(x, y) + 2.5f;
            c.ShapeClipped(RoundRect(-6f, 0f, w + 6f, 17f, 0f), onWall, Wainscot);                       // panelling
            c.ShapeClipped(RoundRect(-6f, 16f, w + 6f, 18.5f, 0f), onWall, Darker(Wainscot, 0.7f));
            for (float x = 16f; x < w; x += 22f) c.ShapeClipped(RoundRect(x, 0f, x + 1.2f, 16f, 0f), onWall, Darker(Wainscot, 0.8f));
            foreach (float x in new[] { HallWallThick, w * 0.37f, w * 0.63f, w - HallWallThick })   // timber posts and a beam
                c.ShapeClipped(RoundRect(x - 3f, 18f, x + 3f, H, 0f), onWall, Timber);
            c.ShapeClipped(RoundRect(-6f, H - 9f, w + 6f, H - 4f, 0f), onWall, Timber);
            c.ShapeClipped(RoundRect(-6f, 0f, w + 6f, 6f, 0f), onWall, new Color(0f, 0f, 0f, 0.1f));   // its foot in shadow

            // A window between the fire and the board.
            var k = LookOf(Town.BuildingType.GuildHall);
            Window(c, k, GuildHallLayout.FireX * P + 40f, 24f, 5f, 12f, false);

            // The fire: a stone chimney breast with an arched hearth, flames, a mantel.
            float fx = GuildHallLayout.FireX * P;
            var breast = RoundRect(fx - 24f, 0f, fx + 24f, H - 4f, 2f);
            c.Shape(breast, Hex(0xB9B4AA), Ink, 2.5f);
            Courses(c, (x, y) => breast(x, y) + 2.5f, fx - 24f, fx + 24f, 4f, H - 4f, Darker(Hex(0xB9B4AA), 0.8f));
            c.ShapeClipped(RoundRect(fx + 10f, 0f, fx + 26f, H, 0f), breast, ShadeTint);
            var hearth = Union(RoundRect(fx - 14f, 0f, fx + 14f, 16f, 1f), Circle(fx, 16f, 14f));
            c.Shape(hearth, Hex(0x2B2420), Ink, 2.5f);
            c.Shape(Circle(fx, 13f, 11f), new Color(1f, 0.6f, 0.2f, 0.35f), new Color(1f, 0.6f, 0.2f, 0.35f), 0f);
            c.Shape(Poly(1.5f, new Vector2(fx - 9f, 2f), new Vector2(fx + 9f, 2f), new Vector2(fx + 2f, 20f), new Vector2(fx - 3f, 12f)), Hex(0xFF9F3A), Hex(0xE8682A), 1.5f);
            c.Shape(Poly(1f, new Vector2(fx - 4f, 2f), new Vector2(fx + 5f, 2f), new Vector2(fx + 1f, 12f)), Hex(0xFFE08A), Hex(0xFFE08A), 0f);
            c.Shape(RoundRect(fx - 9f, 1f, fx + 9f, 4f, 1f), Hex(0x6B4224), Ink, 1f);   // logs
            c.Shape(RoundRect(fx - 27f, 33f, fx + 27f, 37.5f, 1.5f), Timber, Ink, 2f);   // the mantel
            c.Shape(RoundRect(fx - 18f, 37.5f, fx - 15f, 43f, 0.8f), Hex(0xFFF6DA), Ink, 1f);
            c.Shape(RoundRect(fx + 15f, 37.5f, fx + 18f, 43f, 0.8f), Hex(0xFFF6DA), Ink, 1f);
            if (level >= 3) Trophy(c, fx, 46f);

            // The quest board: cork in a frame, notes pinned up (gilt frame and a star at level 3).
            float bx = GuildHallLayout.BoardX * P;
            var board = RoundRect(bx - 28f, 15f, bx + 28f, 47f, 2f);
            c.Shape(board, Hex(0xC79A5E), level >= 3 ? Gold : Ink, level >= 3 ? 3.5f : 3f);
            var notes = new[] { (-20f, 34f), (-8f, 38f), (5f, 33f), (18f, 37f), (-15f, 21f), (0f, 20f), (14f, 22f) };
            for (int i = 0; i < notes.Length; i++)
            {
                var (nx, ny) = notes[i];
                float x = bx + nx, y = ny;
                c.Shape(RoundRect(x - 5f, y - 5f, x + 5f, y + 5f, 0.6f), Hex(0xFFF6DA), Ink, 1f);
                c.Shape(RoundRect(x - 3f, y + 0.5f, x + 3f, y + 1.3f, 0f), Hex(0x9A8F80), Hex(0x9A8F80), 0f);
                c.Shape(RoundRect(x - 3f, y - 2f, x + 2f, y - 1.2f, 0f), Hex(0x9A8F80), Hex(0x9A8F80), 0f);
                c.Shape(Circle(x, y + 4f, 1.3f), i % 3 == 0 ? Hex(0xD9534F) : Hex(0x3F6FC4), Ink, 0.6f);
            }
            if (level >= 3) c.Shape(Poly(0.8f, new Vector2(bx - 6f, 47f), new Vector2(bx + 6f, 47f), new Vector2(bx, 55f)), Gold, Ink, 1.3f);

            // The shelf behind the counter, bottles and mugs on it.
            float s0 = 6f * P + 4f, s1 = 8f * P - 2f;
            foreach (float sy in new[] { 22f, 38f })
            {
                c.Shape(RoundRect(s0, sy, s1, sy + 3f, 1f), Timber, Ink, 1.6f);
                var bottles = new[] { Hex(0x7FB24A), Hex(0xD9534F), Hex(0x6FBBE0), Hex(0xE8C98A), Hex(0x7B5BD6) };
                int n = 0;
                for (float x = s0 + 6f; x < s1 - 4f; x += 8.5f, n++)
                {
                    if ((n + (int)sy) % 4 == 3) { c.Shape(RoundRect(x - 3f, sy + 3f, x + 3f, sy + 9f, 1f), Hex(0xC9A57A), Ink, 1.2f); continue; }   // a mug
                    c.Shape(Union(RoundRect(x - 2.6f, sy + 3f, x + 2.6f, sy + 10f, 1.2f), RoundRect(x - 1f, sy + 9f, x + 1f, sy + 13f, 0.5f)),
                            bottles[(n + (int)sy) % bottles.Length], Ink, 1.1f);
                }
            }

            // Banners either side of the board and sconces at the ends (level 2 and up).
            if (level >= 2)
            {
                foreach (float x in new[] { bx - 40f, bx + 40f })
                {
                    var cloth = Poly(0.8f, new Vector2(x - 6f, 50f), new Vector2(x + 6f, 50f), new Vector2(x + 6f, 26f), new Vector2(x, 30f), new Vector2(x - 6f, 26f));
                    c.Shape(cloth, Hex(0x3F6FC4), Ink, 1.6f);
                    c.Shape(Circle(x, 41f, 2.6f), Gold, Ink, 0.9f);
                    c.Shape(RoundRect(x - 8f, 49.5f, x + 8f, 52f, 1f), Gold, Ink, 1f);
                }
                foreach (float x in new[] { HallWallThick + 14f, w - HallWallThick - 14f })
                {
                    c.Shape(Circle(x, 36f, 9f), new Color(1f, 0.85f, 0.4f, 0.22f), new Color(1f, 0.85f, 0.4f, 0.22f), 0f);
                    c.Shape(RoundRect(x - 2.5f, 31f, x + 2.5f, 34f, 1f), Iron, Ink, 1f);
                    c.Shape(RoundRect(x - 1.4f, 34f, x + 1.4f, 39f, 0.6f), Hex(0xFFF6DA), Ink, 0.8f);
                    c.Shape(Circle(x, 40.5f, 1.5f), Hex(0xFFC94A), Hex(0xFFC94A), 0f);
                }
            }

            // The cut: the roof timbers and the blue tiles' edge where the hall is opened up.
            c.Shape(RoundRect(-6f, H - 1f, w + 6f, H + 6f, 0f), Darker(Timber, 0.8f), Ink, 2f);
            var tiles = RoundRect(-6f, H + 5f, w + 6f, H + 12f, 0f);
            c.Shape(tiles, HallRoof, Ink, 2f);
            c.ShapeClipped(CircleRow(0f, H + 6f, 10f, 5.6f), (x, y) => tiles(x, y) + 2f, Darker(HallRoof, 0.78f));
            return c.ToSprite(key, new Vector2(0.5f, 0f));
        }

        /// <summary>A horned beast's head on a shield-shaped board, over the fire (level 3).</summary>
        static void Trophy(Canvas c, float x, float y)
        {
            c.Shape(Poly(1.5f, new Vector2(x - 8f, y + 8f), new Vector2(x + 8f, y + 8f), new Vector2(x + 8f, y - 1f), new Vector2(x, y - 6f), new Vector2(x - 8f, y - 1f)),
                    Timber, Ink, 1.6f);
            var bone = Hex(0xEDE3CF);
            c.Shape(Segment(x - 4f, y + 5f, x - 11f, y + 10f, 1.6f), bone, Ink, 1f);   // horns
            c.Shape(Segment(x + 4f, y + 5f, x + 11f, y + 10f, 1.6f), bone, Ink, 1f);
            c.Shape(Ellipse(x, y + 2f, 5.5f, 5f), Hex(0x7A5A3A), Ink, 1.4f);
            c.Shape(Circle(x - 2f, y + 3f, 1f), Ink, Ink, 0f);
            c.Shape(Circle(x + 2f, y + 3f, 1f), Ink, Ink, 0f);
        }

        /// <summary>The front wall cut low, pivoted at its foot, open at the entrance between two door posts.</summary>
        public static Sprite HallFrontWall(int level)
        {
            level = Mathf.Clamp(level, 1, 3);
            string key = $"c_hall_front:{level}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            const int P = PixelsPerUnit;
            int w = GuildHallLayout.Width * P;
            float ex0 = GuildHallLayout.Entrance.x * P, ex1 = ex0 + P;
            var c = new Canvas(w, 46, BuildingDetail);
            c.Shape(Ellipse(w * 0.5f, 2f, w * 0.5f, 3f), FootShadow, FootShadow, 0f);
            foreach (var (a, b) in new[] { (0f, ex0 - 2f), (ex1 + 2f, (float)w) })
            {
                var face = RoundRect(a, 0f, b, HallLowWallH, 1f);
                c.Shape(face, Plaster, Ink, 2.5f);
                c.ShapeClipped(RoundRect(a, 0f, b, 4f, 0f), (x, y) => face(x, y) + 2.5f, Stonework);
                var cut = RoundRect(a, HallLowWallH - 1f, b, HallLowWallH + HallWallThick, 1.5f);   // its cut top
                c.Shape(cut, Wainscot, Ink, 2f);
                c.ShapeClipped(RoundRect(a, HallLowWallH + 4f, b, HallLowWallH + HallWallThick - 4f, 0f), (x, y) => cut(x, y) + 2f, Darker(Wainscot, 0.82f));
            }
            foreach (float x in new[] { ex0 - 3f, ex1 + 3f })   // door posts
            {
                var post = RoundRect(x - 3.5f, 0f, x + 3.5f, 38f, 1.5f);
                c.Shape(post, Timber, Ink, 2f);
                if (level >= 2)
                {
                    c.Shape(Circle(x, 41f, 4.5f), new Color(1f, 0.85f, 0.4f, 0.3f), new Color(1f, 0.85f, 0.4f, 0.3f), 0f);
                    c.Shape(RoundRect(x - 2.4f, 38f, x + 2.4f, 44f, 1f), Hex(0xFFD86B), Ink, 1.2f);
                }
            }
            return c.ToSprite(key, new Vector2(0.5f, 0f));
        }

        /// <summary>A piece of the hall's furniture, pivoted at its foot.</summary>
        public static Sprite HallPiece(HallFurniture kind)
        {
            string key = $"c_hall_piece:{kind}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var (w, h, fx, fy) = kind switch
            {
                HallFurniture.RoundTable => (44, 36, 22f, 4f),
                HallFurniture.Counter => (70, 40, 35f, 3f),
                HallFurniture.Barrels => (34, 36, 17f, 2f),
                HallFurniture.Plant => (24, 34, 12f, 2f),
                _ => (28, 50, 14f, 2f),
            };
            var c = new Canvas(w, h, BuildingDetail);
            switch (kind)
            {
                case HallFurniture.RoundTable:
                    c.Shape(Ellipse(fx, fy, 12f, 3.5f), FootShadow, FootShadow, 0f);
                    c.Shape(RoundRect(fx - 3f, fy, fx + 3f, fy + 14f, 1f), Darker(Plank, 0.8f), Ink, 1.6f);
                    c.Shape(RoundRect(fx - 9f, fy - 1f, fx + 9f, fy + 3f, 1.5f), Darker(Plank, 0.8f), Ink, 1.6f);
                    c.Shape(Ellipse(fx, fy + 15.5f, 19f, 9f), Darker(Hex(0xA56B3A), 0.72f), Ink, 2.2f);   // the edge below the top
                    var top = Ellipse(fx, fy + 18f, 19f, 9.5f);
                    c.Shape(top, Hex(0xA56B3A), Ink, 2.2f);
                    c.ShapeClipped(Ellipse(fx - 4f, fy + 20f, 10f, 4f), top, new Color(1f, 1f, 1f, 0.15f));
                    c.Shape(RoundRect(fx - 9f, fy + 18f, fx - 4f, fy + 25f, 1.2f), Hex(0xC9A57A), Ink, 1.2f);   // mugs
                    c.Shape(RoundRect(fx + 5f, fy + 16f, fx + 10f, fy + 23f, 1.2f), Hex(0xC9A57A), Ink, 1.2f);
                    c.Shape(Ellipse(fx - 6.5f, fy + 25f, 2.5f, 1f), Hex(0xFFF6DA), Hex(0xFFF6DA), 0f);
                    c.Shape(Ellipse(fx + 1f, fy + 21f, 3.2f, 1.6f), Hex(0xE8C98A), Ink, 0.9f);   // a plate
                    break;
                case HallFurniture.Counter:
                    c.Shape(Ellipse(fx, fy, 33f, 3f), FootShadow, FootShadow, 0f);
                    var front = RoundRect(3f, fy, w - 3f, fy + 22f, 2f);
                    c.Shape(front, Hex(0x8C5A33), Ink, 2.5f);
                    for (float x = 12f; x < w - 6f; x += 9f) c.ShapeClipped(RoundRect(x, 0f, x + 1.2f, h, 0f), (px, py) => front(px, py) + 2.5f, Darker(Hex(0x8C5A33), 0.78f));
                    c.ShapeClipped(RoundRect(w * 0.7f, 0f, w, h, 0f), front, ShadeTint);
                    c.Shape(RoundRect(1f, fy + 20f, w - 1f, fy + 28f, 2f), Hex(0xC08A52), Ink, 2.2f);   // the top
                    c.Shape(RoundRect(10f, fy + 27f, 16f, fy + 34f, 1.2f), Hex(0xC9A57A), Ink, 1.2f);   // a mug
                    c.Shape(RoundRect(22f, fy + 27f, 34f, fy + 30f, 0.8f), Hex(0xFFF6DA), Ink, 1f);     // the ledger
                    var keg = RoundRect(w - 22f, fy + 27f, w - 8f, fy + 36f, 3f);
                    c.Shape(keg, Hex(0xB7783F), Ink, 1.6f);
                    c.ShapeClipped(RoundRect(w - 23f, fy + 30.5f, w - 7f, fy + 32f, 0f), keg, Iron);
                    c.Shape(RoundRect(w - 26f, fy + 29f, w - 22f, fy + 31f, 0.6f), Gold, Ink, 0.8f);   // the tap
                    break;
                case HallFurniture.Barrels:
                    Barrel(c, fx - 6f, fy);
                    Barrel(c, fx + 6f, fy);
                    Barrel(c, fx, fy + 12f);
                    break;
                case HallFurniture.Plant:
                    Pot(c, fx, fy, 1);
                    c.Shape(Circle(fx, fy + 16f, 6.5f), Leaves, Ink, 1.5f);
                    c.Shape(Circle(fx - 5f, fy + 12f, 4.5f), Hex(0x4FAE3C), Ink, 1.3f);
                    c.Shape(Circle(fx + 5f, fy + 13f, 4.5f), Hex(0x6BC04A), Ink, 1.3f);
                    break;
                case HallFurniture.ArmorStand:
                    c.Shape(Ellipse(fx, fy, 9f, 2.5f), FootShadow, FootShadow, 0f);
                    c.Shape(RoundRect(fx - 7f, fy, fx + 7f, fy + 3f, 1f), Timber, Ink, 1.4f);
                    c.Shape(RoundRect(fx - 1.2f, fy + 2f, fx + 1.2f, fy + 18f, 0.5f), Timber, Ink, 1f);
                    var steel = Hex(0xC9CED8);
                    var chest = Union(RoundRect(fx - 8f, fy + 16f, fx + 8f, fy + 34f, 3f), Circle(fx - 9f, fy + 31f, 3.5f), Circle(fx + 9f, fy + 31f, 3.5f));
                    c.Shape(chest, steel, Ink, 2f);
                    c.ShapeClipped(RoundRect(fx + 3f, 0f, fx + 14f, h, 0f), chest, ShadeTint);
                    c.Shape(RoundRect(fx - 1f, fy + 18f, fx + 1f, fy + 32f, 0.5f), Gold, Gold, 0f);
                    var helm = Union(Circle(fx, fy + 40f, 6f), RoundRect(fx - 6f, fy + 34f, fx + 6f, fy + 40f, 1f));
                    c.Shape(helm, steel, Ink, 2f);
                    c.Shape(RoundRect(fx - 4f, fy + 37f, fx + 4f, fy + 38.5f, 0.3f), Ink, Ink, 0f);
                    c.Shape(Segment(fx, fy + 46f, fx + 3f, fy + 48f, 1.2f), Hex(0xD9534F), Ink, 0.8f);   // a plume
                    break;
            }
            return c.ToSprite(key, new Vector2(fx / w, fy / h));
        }
    }
}
