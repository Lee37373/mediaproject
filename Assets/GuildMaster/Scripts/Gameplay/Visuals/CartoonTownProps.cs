using UnityEngine;
using Prop = GuildMaster.Town.TownProp;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// The town's decorations (P4-114): lamp posts, bushes, flower beds, barrels, crates and benches, drawn like the buildings
    /// (as finely, with a soft shadow at the foot). The trees are the woods' own. The pivot is at the foot.
    /// </summary>
    public static partial class CartoonArt
    {
        public static Sprite Decoration(Prop kind, int variant)
        {
            variant &= 3;
            string key = $"c_decor:{kind}:{variant}";
            if (s_cache.TryGetValue(key, out var cached)) return cached;
            Canvas c;
            switch (kind)
            {
                case Prop.Lamp: c = new Canvas(24, 60, BuildingDetail); LampPost(c); break;
                case Prop.Bush: c = new Canvas(40, 34, BuildingDetail); Shrub(c, variant); break;
                case Prop.FlowerBed: c = new Canvas(44, 26, BuildingDetail); Bed(c, variant); break;
                case Prop.Barrels:
                    c = new Canvas(36, 30, BuildingDetail);
                    Foot(c, 15f);
                    Barrel(c, 11f, 3f);
                    Barrel(c, 24f, 3f);
                    if (variant % 2 == 1) Crate(c, 12f, 15f, 11f);
                    break;
                case Prop.Crates:
                    c = new Canvas(34, 32, BuildingDetail);
                    Foot(c, 14f);
                    Crate(c, 3f, 3f, 14f);
                    Crate(c, 18f, 3f, 12f);
                    Crate(c, 8f, 16.5f, 12f);
                    break;
                default: c = new Canvas(44, 26, BuildingDetail); Bench(c); break;
            }
            return c.ToSprite(key, new Vector2(0.5f, 5f / c.H));
        }

        static void Foot(Canvas c, float rx) => c.Shape(Ellipse(c.W * 0.5f, 5f, rx, 3.5f), FootShadow, FootShadow, 0f);

        static void LampPost(Canvas c)
        {
            Foot(c, 7f);
            var post = Hex(0x3E434C);
            c.Shape(Circle(12f, 48f, 11f), new Color(1f, 0.85f, 0.4f, 0.22f), new Color(1f, 0.85f, 0.4f, 0.22f), 0f);   // its glow
            c.Shape(RoundRect(10.5f, 6f, 13.5f, 43f, 1f), post, Ink, 1.5f);
            c.Shape(RoundRect(7f, 3f, 17f, 8.5f, 2f), post, Ink, 1.8f);
            c.Shape(RoundRect(8.5f, 41f, 15.5f, 44.5f, 1f), post, Ink, 1.5f);
            var glass = RoundRect(7f, 43f, 17f, 54f, 2f);
            c.Shape(glass, Hex(0xFFE08A), Ink, 2f);
            c.ShapeClipped(RoundRect(11.3f, 40f, 12.7f, 56f, 0f), (x, y) => glass(x, y) + 2f, Iron);
            c.ShapeClipped(Circle(9.5f, 51f, 2f), (x, y) => glass(x, y) + 2f, new Color(1f, 1f, 1f, 0.7f));
            c.Shape(Poly(1f, new Vector2(5f, 53.5f), new Vector2(19f, 53.5f), new Vector2(12f, 58f)), post, Ink, 1.8f);
        }

        static void Shrub(Canvas c, int variant)
        {
            Foot(c, 15f);
            var leaf = variant switch { 0 => Hex(0x5DBB44), 1 => Hex(0x4FAE3C), 2 => Hex(0x6BC04A), _ => Hex(0x55B548) };
            var body = Union(Circle(12f, 14f, 9f), Circle(28f, 14f, 9f), Circle(20f, 20f, 10.5f));
            c.Shape(body, leaf, Ink, 2.5f);
            System.Func<float, float, float> inside = (x, y) => body(x, y) + 2.5f;
            c.ShapeClipped(Union(Circle(15f, 24f, 4f), Circle(9f, 17f, 3f)), inside, Lighter(leaf, 0.25f));
            c.ShapeClipped(RoundRect(25f, 0f, 40f, 34f, 0f), body, ShadeTint);
            if (variant % 2 == 1)   // in flower, or with berries
            {
                var dot = variant == 1 ? Hex(0xFFFFFF) : Hex(0xE84A5F);
                foreach (var (x, y) in new[] { (12f, 13f), (19f, 22f), (26f, 15f), (23f, 9f) })
                    c.Shape(Circle(x, y, 1.8f), dot, Ink, 0.9f);
            }
        }

        static void Bed(Canvas c, int variant)
        {
            Foot(c, 19f);
            var soil = Ellipse(22f, 9f, 19f, 6.5f);
            c.Shape(soil, Hex(0x8A5A3A), Ink, 2.2f);
            c.ShapeClipped(Ellipse(22f, 11f, 16f, 3.5f), (x, y) => soil(x, y) + 2.2f, Hex(0x6E4630));
            for (int i = 0; i < 7; i++)
            {
                float x = 8f + i * 4.7f, y = 10f + (i % 2) * 3f;
                c.Shape(Ellipse(x, y + 1f, 3.2f, 2.2f), Leaves, Ink, 1f);
                c.Shape(Circle(x, y + 3.5f, 2.2f), Blooms[(i + variant) % Blooms.Length], Ink, 1f);
                c.Shape(Circle(x, y + 3.5f, 0.8f), Hex(0xFFE9A0), Hex(0xFFE9A0), 0f);
            }
        }

        static void Bench(Canvas c)
        {
            Foot(c, 18f);
            c.Shape(RoundRect(7f, 3f, 10f, 11f, 1f), Iron, Ink, 1.3f);
            c.Shape(RoundRect(34f, 3f, 37f, 11f, 1f), Iron, Ink, 1.3f);
            c.Shape(RoundRect(5f, 13f, 39f, 22f, 2f), Plank, Ink, 2f);   // the back
            c.ShapeClipped(RoundRect(5f, 17f, 39f, 18f, 0f), RoundRect(5f, 13f, 39f, 22f, 2f), Darker(Plank, 0.78f));
            var seat = RoundRect(3f, 9f, 41f, 14f, 2f);
            c.Shape(seat, Lighter(Plank, 0.1f), Ink, 2f);
            c.ShapeClipped(RoundRect(30f, 0f, 44f, 26f, 0f), Union(seat, RoundRect(5f, 13f, 39f, 22f, 2f)), ShadeTint);
        }
    }
}
