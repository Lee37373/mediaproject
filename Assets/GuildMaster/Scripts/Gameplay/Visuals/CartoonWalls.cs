using System;
using UnityEngine;
using WP = GuildMaster.Town.WallPiece;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// The town's walls standing up (P4-115, user request 2026-10-31: they were a flat strip of grey bricks), one picture
    /// a wall cell, pivoted at the cell's foot: a stretch of stone wall with its walk on top and battlements on the outer
    /// side, a round tower with a cone roof at each corner, a square tower hung with the guild's banner either side of each
    /// gate. Stretches join without a seam (their patterns repeat every half cell); drawn as finely as the buildings.
    /// </summary>
    public static partial class CartoonArt
    {
        /// <summary>How tall the wall's face stands, and its battlements over the walk (logical pixels, 32 a cell).</summary>
        const float WallFace = 26f, Merlon = 9f;

        static readonly Color StoneFace = Hex(0xBDB6A8), StoneTop = Hex(0xDAD4C7), Moss = Hex(0x7FAF4A), TowerRoof = Hex(0xC0504D);

        /// <summary>The picture for a wall cell; <paramref name="variant"/> (0–2) adds a little moss or a cracked stone to a stretch.</summary>
        public static Sprite Wall(WP piece, int variant)
        {
            variant = piece is WP.Corner or WP.GateTower ? 0 : Mathf.Abs(variant) % 3;
            string key = $"c_wall:{piece}:{variant}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            Canvas c;
            switch (piece)
            {
                case WP.Corner:
                    c = new Canvas(52, 100, BuildingDetail);
                    CornerTower(c);
                    break;
                case WP.GateTower:
                    c = new Canvas(40, 72, BuildingDetail);
                    GateTower(c);
                    break;
                case WP.AlongNorth:
                case WP.AlongSouth:
                    c = new Canvas(PixelsPerUnit, 72, BuildingDetail);
                    WallAlong(c, outerFront: piece == WP.AlongSouth, variant);
                    break;
                default:
                    c = new Canvas(PixelsPerUnit, 72, BuildingDetail);
                    WallUpright(c, outerLeft: piece == WP.AlongWest, variant);
                    break;
            }
            return c.ToSprite(key, new Vector2(0.5f, 0f));
        }

        /// <summary>A stretch running east–west: the face, the walk seen from above, battlements on the front or the back.</summary>
        static void WallAlong(Canvas c, bool outerFront, int variant)
        {
            float w = c.W, H = WallFace, D = PixelsPerUnit;
            var mortar = Darker(StoneFace, 0.8f);
            // Wider than the cell, so the outline runs only along the top and bottom and the stretches join.
            var walk = RoundRect(-8f, H, w + 8f, H + D, 0f);
            c.Shape(walk, StoneTop, Ink, 2.5f);
            Func<float, float, float> onWalk = (x, y) => walk(x, y) + 2.5f;
            var joint = Darker(StoneTop, 0.86f);
            c.ShapeClipped(RoundRect(-8f, H + D * 0.5f - 0.6f, w + 8f, H + D * 0.5f + 0.6f, 0f), onWalk, joint);
            for (float x = 4f; x < w; x += 16f)
            {
                float xx = x;
                c.ShapeClipped(RoundRect(xx, H, xx + 1.2f, H + D * 0.5f, 0f), onWalk, joint);
                c.ShapeClipped(RoundRect(xx + 8f, H + D * 0.5f, xx + 9.2f, H + D, 0f), onWalk, joint);
            }
            var face = RoundRect(-8f, 0f, w + 8f, H, 0f);
            c.Shape(face, StoneFace, Ink, 2.5f);
            Func<float, float, float> onFace = (x, y) => face(x, y) + 2.5f;
            Courses(c, onFace, -8f, w + 8f, 3f, H, mortar);
            c.ShapeClipped(RoundRect(-8f, H - 5f, w + 8f, H, 0f), face, ShadeTint);   // under the walk's lip
            StretchWear(c, onFace, variant, w * 0.5f);
            for (float x = 3f; x < w; x += 16f)
                Battlement(c, x, x + 10f, outerFront ? H - 1f : H + D - 7f);
        }

        /// <summary>
        /// A stretch running north–south: seen mostly from above, its walk with a parapet and battlements along the outer side;
        /// its face shows only where the stretch ends (the cell below draws over the rest).
        /// </summary>
        static void WallUpright(Canvas c, bool outerLeft, int variant)
        {
            float w = c.W, H = WallFace, D = PixelsPerUnit;
            var mortar = Darker(StoneFace, 0.8f);
            var face = RoundRect(0f, -10f, w, H, 0f);
            c.Shape(face, StoneFace, Ink, 2.5f);
            Func<float, float, float> onFace = (x, y) => face(x, y) + 2.5f;
            Courses(c, onFace, 0f, w, 3f, H, mortar);
            StretchWear(c, onFace, variant, w * 0.5f);
            // The walk: no outline at its ends (the stretch goes on), its sides drawn as lines.
            var walk = RoundRect(0f, H, w, H + D, 0f);
            c.Shape(walk, StoneTop, StoneTop, 0f);
            var joint = Darker(StoneTop, 0.86f);
            c.ShapeClipped(RoundRect(0f, H + D * 0.5f - 0.6f, w, H + D * 0.5f + 0.6f, 0f), walk, joint);
            c.ShapeClipped(RoundRect(w * 0.6f, H, w * 0.6f + 1.2f, H + D * 0.5f, 0f), walk, joint);
            c.ShapeClipped(RoundRect(w * 0.35f, H + D * 0.5f, w * 0.35f + 1.2f, H + D, 0f), walk, joint);
            float p0 = outerLeft ? 0f : w - 9f, p1 = p0 + 9f;
            c.Shape(RoundRect(p0, H, p1, H + D, 0f), StoneFace, StoneFace, 0f);   // the parapet's top
            c.Shape(RoundRect(outerLeft ? p1 - 1.2f : p0, H, outerLeft ? p1 : p0 + 1.2f, H + D, 0f), Ink, Ink, 0f);
            c.Shape(RoundRect(0f, H, 2.5f, H + D, 0f), Ink, Ink, 0f);
            c.Shape(RoundRect(w - 2.5f, H, w, H + D, 0f), Ink, Ink, 0f);
            if (variant == 1) c.ShapeClipped(Ellipse(outerLeft ? w * 0.7f : w * 0.3f, H + D * 0.7f, 5f, 3f), walk, Moss);
            for (float y = H + 2f; y < H + D; y += 16f) Battlement(c, p0 - 0.5f, p1 + 0.5f, y);
        }

        /// <summary>A merlon: its face rising from <paramref name="baseY"/>, its top seen above it.</summary>
        static void Battlement(Canvas c, float x0, float x1, float baseY)
        {
            var block = RoundRect(x0, baseY, x1, baseY + Merlon, 1f);
            c.Shape(block, StoneFace, Ink, 2f);
            c.ShapeClipped(RoundRect(x0 + (x1 - x0) * 0.62f, baseY, x1 + 2f, baseY + Merlon, 0f), block, ShadeTint);
            c.Shape(RoundRect(x0, baseY + Merlon - 1.5f, x1, baseY + Merlon + 4.5f, 1f), StoneTop, Ink, 2f);
        }

        /// <summary>A little moss at the foot (variant 1) or a cracked stone (variant 2), so the stretches are not all alike.</summary>
        static void StretchWear(Canvas c, Func<float, float, float> onFace, int variant, float x)
        {
            if (variant == 1)
            {
                c.ShapeClipped(Ellipse(x - 4f, 2f, 9f, 4.5f), onFace, Moss);
                c.ShapeClipped(Circle(x + 6f, 3f, 2.5f), onFace, Moss);
            }
            else if (variant == 2)
            {
                var crack = Darker(StoneFace, 0.62f);
                c.ShapeClipped(Segment(x - 3f, 18f, x + 1f, 13f, 0.7f), onFace, crack);
                c.ShapeClipped(Segment(x + 1f, 13f, x - 1f, 8f, 0.7f), onFace, crack);
            }
        }

        /// <summary>A round corner tower: a stone drum with an arrow slit, a cone roof of shingles, a pennant on top.</summary>
        static void CornerTower(Canvas c)
        {
            float w = c.W, cx = w * 0.5f;
            var mortar = Darker(StoneFace, 0.8f);
            var drum = RoundRect(5f, 1f, w - 5f, 48f, 9f);
            c.Shape(drum, StoneFace, Ink, 3f);
            Func<float, float, float> inDrum = (x, y) => drum(x, y) + 3f;
            Courses(c, inDrum, 5f, w - 5f, 6f, 46f, mortar);
            c.ShapeClipped(RoundRect(8f, 0f, 13f, 48f, 0f), inDrum, new Color(1f, 1f, 1f, 0.18f));   // the lit side of the drum
            c.ShapeClipped(RoundRect(cx + 8f, 0f, w, 48f, 0f), drum, ShadeTint);
            c.Shape(Union(RoundRect(cx - 2.5f, 22f, cx + 2.5f, 34f, 0.5f), Circle(cx, 34f, 2.5f)), Hex(0x2B2420), Ink, 1.5f);   // arrow slit
            // The roof: an eave band, then the cone.
            var cone = Poly(3f, new Vector2(1f, 50f), new Vector2(w - 1f, 50f), new Vector2(cx, 91f));
            c.Shape(cone, TowerRoof, Ink, 3f);
            Func<float, float, float> inCone = (x, y) => cone(x, y) + 3f;
            var seam = Darker(TowerRoof, 0.78f);
            int row = 0;
            for (float y = 51f; y < 88f; y += 7f, row++)
            {
                var shingle = CircleRow(row % 2 == 0 ? 0f : 4f, y, 8f, 4.6f);
                var above = CircleRow(row % 2 == 0 ? 0f : 4f, y + 1.6f, 8f, 4.6f);
                c.ShapeClipped((x, yy) => Mathf.Max(shingle(x, yy), -above(x, yy)), inCone, seam);
            }
            c.ShapeClipped(RoundRect(cx, 0f, w, 100f, 0f), cone, ShadeTint);
            c.ShapeClipped(RoundRect(0f, 0f, w, 54f, 0f), inCone, Darker(TowerRoof, 0.7f));
            c.Shape(Segment(cx, 90f, cx, 98f, 0.9f), Iron, Iron, 0f);
            c.Shape(Poly(0.6f, new Vector2(cx + 0.8f, 98f), new Vector2(cx + 0.8f, 93f), new Vector2(cx + 11f, 95.5f)), Hex(0x3F6FC4), Ink, 1.3f);
            c.Shape(Circle(cx, 91f, 2.4f), Gold, Ink, 1.2f);
        }

        /// <summary>A square tower beside a gate: its walk and battlements on top, the guild's banner hung on its face.</summary>
        static void GateTower(Canvas c)
        {
            float w = c.W;
            var mortar = Darker(StoneFace, 0.8f);
            var body = RoundRect(3f, 1f, w - 3f, 46f, 3f);
            c.Shape(body, StoneFace, Ink, 3f);
            Func<float, float, float> inBody = (x, y) => body(x, y) + 3f;
            Courses(c, inBody, 3f, w - 3f, 5f, 46f, mortar);
            c.ShapeClipped(RoundRect(w * 0.74f, 0f, w, 50f, 0f), body, ShadeTint);
            c.Shape(RoundRect(1f, 44f, w - 1f, 60f, 2f), StoneTop, Ink, 2.5f);   // the top, seen from above
            for (int i = 0; i < 3; i++) Battlement(c, 1.5f + i * 13.5f, 10.5f + i * 13.5f, 54f);   // at the back
            for (int i = 0; i < 3; i++) Battlement(c, 1.5f + i * 13.5f, 10.5f + i * 13.5f, 43f);   // at the front
            // The guild's banner: blue with a swallowtail and the crest's crossed swords, on a gilt rod.
            float cx = w * 0.5f;
            var cloth = Poly(1f, new Vector2(cx - 7f, 37f), new Vector2(cx + 7f, 37f), new Vector2(cx + 7f, 13f),
                             new Vector2(cx, 17.5f), new Vector2(cx - 7f, 13f));
            c.Shape(cloth, Hex(0x3F6FC4), Ink, 2f);
            c.ShapeClipped(RoundRect(cx + 2.5f, 0f, w, 50f, 0f), cloth, ShadeTint);
            var steel = Hex(0xE3E7EE);
            c.Shape(Segment(cx - 3.8f, 22f, cx + 3.8f, 31f, 1.1f), steel, Ink, 0.8f);
            c.Shape(Segment(cx + 3.8f, 22f, cx - 3.8f, 31f, 1.1f), steel, Ink, 0.8f);
            c.Shape(RoundRect(cx - 9.5f, 36f, cx + 9.5f, 39.5f, 1.5f), Gold, Ink, 1.3f);
        }
    }
}
