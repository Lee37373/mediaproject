using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>The places worth going to (P4-105), in the same cartoon style as the trees, rocks and buildings.</summary>
    public static partial class CartoonArt
    {
        /// <summary>A place's look: a gathering spot shows whether it has grown back, a watchtower whether it has been climbed.</summary>
        public static Sprite Place(PlaceKind kind, bool spent = false) => kind switch
        {
            PlaceKind.HotSpring => HotSpring(),
            PlaceKind.Shrine => Shrine(),
            PlaceKind.OreVein => OreVein(spent),
            PlaceKind.HerbPatch => HerbPatch(spent),
            PlaceKind.OldCamp => OldCamp(),
            _ => WatchRuin(spent),
        };

        static void Shadow(Canvas c, float cx, float rx, float ry = 4f)
            => c.Shape(Ellipse(cx, 6, rx, ry), new Color(0f, 0f, 0f, 0.22f), new Color(0f, 0f, 0f, 0.22f), 0f);

        /// <summary>A steaming pool in a ring of stones.</summary>
        static Sprite HotSpring()
        {
            const string key = "c_place_spring";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(72, 52);
            var ink = Hex(0x2E3A40);
            var stone = Hex(0x9AA0A0);
            Shadow(c, 36, 32, 6f);
            c.Shape(Ellipse(36, 18, 32, 13f), stone, ink, 2f);
            c.Shape(Ellipse(36, 19, 26, 9.5f), Hex(0x4FC6C8), Hex(0x2C8C96), 1.5f);
            c.ShapeClipped(Ellipse(30, 22, 12, 3f), Ellipse(36, 19, 26, 9.5f), Hex(0x9FE8E6));
            // Stones round the rim.
            foreach (var (x, y, r) in new[] { (8f, 17f, 5f), (19f, 8f, 5.5f), (36f, 6f, 5f), (53f, 8f, 5.5f), (64f, 17f, 5f), (52f, 28f, 4.5f), (20f, 28f, 4.5f) })
                c.Shape(Circle(x, y, r), Lighter(stone, 0.08f), ink, 1.5f);
            // Steam.
            var steam = new Color(1f, 1f, 1f, 0.55f);
            foreach (var (x, y, r) in new[] { (28f, 33f, 5f), (33f, 40f, 4f), (40f, 34f, 5.5f), (46f, 43f, 4f), (36f, 47f, 3f) })
                c.Shape(Circle(x, y, r), steam, steam, 0f);
            return c.ToSprite(key, new Vector2(0.5f, 0.15f));
        }

        /// <summary>A small stone shrine: a stepped base, two pillars, a roof and a glow inside.</summary>
        static Sprite Shrine()
        {
            const string key = "c_place_shrine";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(56, 72);
            var ink = Hex(0x33302C);
            var stone = Hex(0xC9C1B1);
            Shadow(c, 28, 24);
            c.Shape(RoundRect(4, 4, 52, 12, 2f), Darker(stone, 0.85f), ink, 2f);
            c.Shape(RoundRect(8, 11, 48, 18, 2f), Darker(stone, 0.92f), ink, 2f);
            c.Shape(Ellipse(28, 34, 9, 11f), new Color(1f, 0.92f, 0.55f, 0.85f), new Color(1f, 0.85f, 0.4f, 0.85f), 1f);
            c.Shape(RoundRect(10, 17, 17, 52, 1.5f), stone, ink, 2f);
            c.Shape(RoundRect(39, 17, 46, 52, 1.5f), stone, ink, 2f);
            // A roof of three steps, wider at the bottom.
            c.Shape(RoundRect(4, 51, 52, 57, 2f), Hex(0x8C4A3C), ink, 2f);
            c.Shape(RoundRect(10, 56, 46, 62, 2f), Hex(0x9E5646), ink, 2f);
            c.Shape(RoundRect(18, 61, 38, 67, 2f), Hex(0xAE6250), ink, 2f);
            c.Shape(Circle(28, 69, 2.5f), Hex(0xF2D16B), ink, 1f);
            return c.ToSprite(key, new Vector2(0.5f, 0.08f));
        }

        /// <summary>A rock outcrop with ore glinting in it; picked clean, just the rock.</summary>
        static Sprite OreVein(bool spent)
        {
            string key = "c_place_ore:" + spent;
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(60, 46);
            var ink = Hex(0x2F2C2A);
            var rock = Hex(0x7E7A74);
            Shadow(c, 30, 27);
            c.Shape(Ellipse(22, 20, 17, 14f), rock, ink, 2f);
            c.Shape(Ellipse(40, 17, 15, 11f), Darker(rock, 0.9f), ink, 2f);
            c.Shape(Ellipse(31, 30, 12, 10f), Lighter(rock, 0.08f), ink, 2f);
            if (!spent)
            {
                var ore = Hex(0xC8743A);
                foreach (var (x, y, r) in new[] { (18f, 22f, 3f), (27f, 15f, 2.5f), (36f, 31f, 3f), (44f, 19f, 2.5f), (24f, 32f, 2f) })
                {
                    c.Shape(Circle(x, y, r), ore, Darker(ore, 0.6f), 1f);
                    c.Shape(Circle(x - 0.8f, y + 0.8f, r * 0.35f), Hex(0xFFE2B0), Hex(0xFFE2B0), 0f);
                }
            }
            return c.ToSprite(key, new Vector2(0.5f, 0.15f));
        }

        /// <summary>Clumps of herbs in flower; picked, low green stems.</summary>
        static Sprite HerbPatch(bool picked)
        {
            string key = "c_place_herb:" + picked;
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(60, 40);
            var ink = Hex(0x24391E);
            Shadow(c, 30, 26, 3.5f);
            float h = picked ? 0.45f : 1f;
            var leaf = picked ? Hex(0x7C9A52) : Hex(0x58A83C);
            foreach (var (x, r) in new[] { (12f, 8f), (24f, 10f), (37f, 9f), (49f, 8f) })
                c.Shape(Ellipse(x, 6 + r * h, r, r * h), leaf, ink, 1.5f);
            if (!picked)
                foreach (var (x, y, col) in new[] { (10f, 20f, 0xF4F0FF), (22f, 24f, 0xB58CE8), (27f, 18f, 0xF4F0FF), (37f, 23f, 0xB58CE8), (47f, 19f, 0xF4F0FF), (51f, 13f, 0xB58CE8) })
                {
                    c.Shape(Circle(x, y, 2.6f), Hex(col), Darker(Hex(col), 0.7f), 0.8f);
                    c.Shape(Circle(x, y, 0.9f), Hex(0xF2D16B), Hex(0xF2D16B), 0f);
                }
            return c.ToSprite(key, new Vector2(0.5f, 0.15f));
        }

        /// <summary>A sagging old tent and a ring of blackened stones.</summary>
        static Sprite OldCamp()
        {
            const string key = "c_place_camp";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(68, 52);
            var ink = Hex(0x2E2822);
            var cloth = Hex(0x9C8463);
            Shadow(c, 30, 28);
            // The tent: stepped cloth, a pole, a torn flap.
            c.Shape(RoundRect(6, 6, 42, 16, 2f), Darker(cloth, 0.85f), ink, 2f);
            c.Shape(RoundRect(10, 15, 38, 25, 2f), cloth, ink, 2f);
            c.Shape(RoundRect(15, 24, 33, 34, 2f), Lighter(cloth, 0.08f), ink, 2f);
            c.Shape(RoundRect(20, 33, 28, 40, 2f), Lighter(cloth, 0.14f), ink, 2f);
            c.Shape(Segment(24, 6, 24, 45, 1.2f), Hex(0x5E4632), ink, 0.6f);
            c.Shape(RoundRect(20, 6, 28, 18, 1f), Hex(0x2A221C), ink, 1f);
            // The fire ring, cold.
            foreach (var (x, y) in new[] { (50f, 9f), (57f, 12f), (62f, 9f), (57f, 6f) })
                c.Shape(Circle(x, y, 3.2f), Hex(0x6E6862), ink, 1.2f);
            c.Shape(Ellipse(56, 9, 4f, 2.2f), Hex(0x2B2522), Hex(0x2B2522), 0f);
            return c.ToSprite(key, new Vector2(0.45f, 0.15f));
        }

        /// <summary>A ruined stone watchtower; climbed, a little flag flies from it.</summary>
        static Sprite WatchRuin(bool climbed)
        {
            string key = "c_place_tower:" + climbed;
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            var c = new Canvas(48, 100);
            var ink = Hex(0x2C2926);
            var stone = Hex(0xA49C8F);
            Shadow(c, 24, 18);
            c.Shape(RoundRect(10, 4, 38, 78, 2f), stone, ink, 2f);
            // Courses of stone and a dark window.
            for (int y = 16; y < 76; y += 12)
                c.ShapeClipped(RoundRect(10, y, 38, y + 1.5f, 0f), RoundRect(10, 4, 38, 78, 2f), Darker(stone, 0.8f));
            c.Shape(RoundRect(20, 46, 28, 58, 3f), Hex(0x1E1A17), ink, 1f);
            c.Shape(RoundRect(19, 4, 29, 18, 4f), Hex(0x2A231E), ink, 1.5f);
            // Battlements, one broken off.
            c.Shape(RoundRect(8, 76, 40, 84, 1.5f), Lighter(stone, 0.08f), ink, 2f);
            foreach (var x in new[] { 8f, 20f })
                c.Shape(RoundRect(x, 83, x + 7, 91, 1f), Lighter(stone, 0.08f), ink, 1.5f);
            c.Shape(Circle(37, 85, 3f), Darker(stone, 0.85f), ink, 1.2f);
            c.ShapeClipped(Ellipse(14, 70, 6, 3f), RoundRect(10, 4, 38, 78, 2f), Hex(0x6E8E4A));
            if (climbed)
            {
                c.Shape(Segment(31, 84, 31, 98, 1f), Hex(0x5E4632), ink, 0.5f);
                c.Shape(RoundRect(32, 91, 44, 97, 1f), Hex(0xE0B33E), Hex(0x8C6A1E), 1f);
            }
            return c.ToSprite(key, new Vector2(0.5f, 0.05f));
        }
    }
}
