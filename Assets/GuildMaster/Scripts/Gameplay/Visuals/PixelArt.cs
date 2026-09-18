using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// Placeholder pixel-art library. Every sprite the game uses comes from here, so
    /// swapping to real art only touches this file.
    /// </summary>
    public static class PixelArt
    {
        static readonly Color Outline = new(0.08f, 0.07f, 0.10f);

        // ---------- Terrain ----------

        public static Sprite TerrainTile(TileType type, int variant)
        {
            return type switch
            {
                TileType.Grass => SpriteFactory.Tile("grass", new Color(0.36f, 0.60f, 0.29f), variant, 0.05f, new Color(0.45f, 0.70f, 0.35f), 0.06f),
                TileType.Forest => SpriteFactory.Tile("forest", new Color(0.23f, 0.43f, 0.21f), variant, 0.05f, new Color(0.17f, 0.33f, 0.16f), 0.08f),
                TileType.Sand => SpriteFactory.Tile("sand", new Color(0.85f, 0.78f, 0.54f), variant, 0.04f, new Color(0.76f, 0.69f, 0.46f), 0.07f),
                TileType.Water => SpriteFactory.Tile("water", new Color(0.19f, 0.40f, 0.72f), variant, 0.03f, new Color(0.45f, 0.65f, 0.90f), 0.04f),
                TileType.Mountain => SpriteFactory.Tile("mountain", new Color(0.44f, 0.42f, 0.41f), variant, 0.07f, new Color(0.30f, 0.29f, 0.29f), 0.10f),
                TileType.Road => SpriteFactory.Tile("road", new Color(0.62f, 0.50f, 0.34f), variant, 0.05f, new Color(0.52f, 0.41f, 0.27f), 0.08f),
                TileType.TownFloor => SpriteFactory.Tile("floor", new Color(0.64f, 0.61f, 0.55f), variant, 0.04f, new Color(0.52f, 0.49f, 0.44f), 0.12f),
                TileType.Wall => SpriteFactory.Box(16, 16, new Color(0.46f, 0.41f, 0.38f), new Color(0.27f, 0.24f, 0.22f), 2),
                TileType.Gate => SpriteFactory.Box(16, 16, new Color(0.52f, 0.34f, 0.18f), new Color(0.33f, 0.20f, 0.10f), 2),
                _ => SpriteFactory.White,
            };
        }

        public static Sprite Tree(int variant)
        {
            string key = $"tree:{variant}";
            var rng = new System.Random(variant * 7919);
            var leaf = Color.HSVToRGB(0.28f + (float)rng.NextDouble() * 0.08f, 0.55f, 0.42f + (float)rng.NextDouble() * 0.12f);
            var dark = leaf * 0.7f; dark.a = 1f;
            var light = Color.Lerp(leaf, Color.white, 0.25f);
            var trunk = new Color(0.40f, 0.26f, 0.14f);
            var rows = new[]
            {
                "......oooo......",
                "....ooLLLLoo....",
                "...oLLLgggggo...",
                "..oLLgggggggdo..",
                "..oLggggggggdo..",
                ".oLgggggggggddo.",
                ".oggggggggggddo.",
                ".ogggggggggdddo.",
                "..oggggggdddddo.",
                "..oogddddddddo..",
                "....ooddddoo....",
                "......otto......",
                "......otto......",
                ".....ottto......",
            };
            return SpriteFactory.FromTemplate(key, rows, new Dictionary<char, Color>
            {
                ['o'] = Outline, ['g'] = leaf, ['d'] = dark, ['L'] = light, ['t'] = trunk,
            }, new Vector2(0.5f, 0.1f));
        }

        public static Sprite Rock(int variant)
        {
            var rows = new[]
            {
                "................",
                "................",
                "................",
                ".......ooo......",
                ".....ooLLgoo....",
                "....oLLggggdo...",
                "...oLgggggggdo..",
                "..oLggggggggddo.",
                "..ogggggggggddo.",
                ".oggggggggggdddo",
                ".ogggggggggddddo",
                "..oodddddddddoo.",
                "....oooooooooo..",
            };
            float v = 0.48f + variant * 0.04f;
            var g = new Color(v, v * 0.97f, v * 0.95f);
            return SpriteFactory.FromTemplate($"rock:{variant}", rows, new Dictionary<char, Color>
            {
                ['o'] = Outline, ['g'] = g, ['d'] = g * 0.72f + new Color(0, 0, 0, 0.28f), ['L'] = Color.Lerp(g, Color.white, 0.3f),
            }, new Vector2(0.5f, 0.1f));
        }

        // ---------- Characters ----------

        static readonly string[] HumanoidRows =
        {
            "...oooo...",
            "..ohhhho..",
            "..oseseo..",
            "..osssso..",
            ".obbbbbbo.",
            "obbbbbbbbo",
            "obbbccbbbo",
            "osbbbbbbso",
            ".obbbbbbo.",
            ".obbccbbo.",
            ".oll..llo.",
            ".oll..llo.",
            ".ooo..ooo.",
        };

        public static Sprite Humanoid(string key, Color hair, Color skin, Color body, Color accent, Color legs)
        {
            return SpriteFactory.FromTemplate($"human:{key}", HumanoidRows, new Dictionary<char, Color>
            {
                ['o'] = Outline, ['h'] = hair, ['s'] = skin, ['e'] = Outline, ['b'] = body, ['c'] = accent, ['l'] = legs,
            });
        }

        public static Sprite GuildMaster() => Humanoid("guildmaster",
            new Color(0.75f, 0.75f, 0.78f), new Color(0.96f, 0.80f, 0.66f),
            new Color(0.42f, 0.24f, 0.62f), new Color(0.95f, 0.78f, 0.25f), new Color(0.25f, 0.16f, 0.36f));

        // ---------- Monsters ----------

        static readonly string[] SlimeRows =
        {
            "....oooo....",
            "..oobbLLoo..",
            ".obbbbbLLbo.",
            ".obebbbebbo.",
            "obbbbbbbbbbo",
            "obbbbccbbbbo",
            "obbbbbbbbbbo",
            ".oooooooooo.",
        };

        static readonly string[] BeastRows =
        {
            "..........oo..",
            ".........obco.",
            "oo......obbeo.",
            "obo....obbbbco",
            ".obbbbbbbbbbo.",
            ".obbbbbbbbbo..",
            ".obbbbbbbbbo..",
            "..obbbbbbbo...",
            "..olo.olo.lo..",
            "..olo.olo.lo..",
            "..oo..oo..oo..",
        };

        static readonly string[] BruteRows =
        {
            "....oooooo....",
            "...obbbbbbo...",
            "...obebbebo...",
            "...obbccbbo...",
            ".oobbbbbbbboo.",
            "obbbbbbbbbbbbo",
            "obbbaaaaaabbbo",
            "obbbaaaaaabbbo",
            "obboaaaaaaobbo",
            ".oo.aaaaaa.oo.",
            "....ollllo....",
            "...oll..llo...",
            "...oll..llo...",
            "...ooo..ooo...",
        };

        public static Sprite Monster(Data.MonsterDefinition def)
        {
            var body = def.bodyColor;
            var accent = def.accentColor;
            var dark = Color.Lerp(body, Color.black, 0.35f);
            var light = Color.Lerp(body, Color.white, 0.35f);
            string key = $"monster:{def.id}";
            switch (def.shape)
            {
                case Data.MonsterShape.Slime:
                    return SpriteFactory.FromTemplate(key, SlimeRows, new Dictionary<char, Color>
                    {
                        ['o'] = Outline, ['b'] = body, ['L'] = light, ['e'] = Outline, ['c'] = dark,
                    });
                case Data.MonsterShape.Beast:
                    return SpriteFactory.FromTemplate(key, BeastRows, new Dictionary<char, Color>
                    {
                        ['o'] = Outline, ['b'] = body, ['e'] = accent, ['c'] = light, ['l'] = dark,
                    });
                case Data.MonsterShape.Brute:
                    return SpriteFactory.FromTemplate(key, BruteRows, new Dictionary<char, Color>
                    {
                        ['o'] = Outline, ['b'] = body, ['e'] = new Color(0.9f, 0.2f, 0.1f), ['c'] = dark, ['a'] = accent, ['l'] = dark,
                    });
                default:
                    return SpriteFactory.FromTemplate(key, HumanoidRows, new Dictionary<char, Color>
                    {
                        ['o'] = Outline, ['h'] = dark, ['s'] = body, ['e'] = new Color(0.95f, 0.85f, 0.2f),
                        ['b'] = accent, ['c'] = dark, ['l'] = Color.Lerp(accent, Color.black, 0.3f),
                    });
            }
        }

        public static Sprite Nest(Data.MonsterDefinition def)
        {
            var rows = new[]
            {
                "................",
                "................",
                "................",
                "................",
                "......oooo......",
                "....oodddddoo...",
                "...odmmmmmmmdo..",
                "..odmmmmmmmmmdo.",
                ".odmmmmooommmmdo",
                ".odmmmoxxxommmdo",
                ".odmmoxxxxxommdo",
                "..oooooooooooo..",
            };
            var mound = Color.Lerp(def.bodyColor, new Color(0.35f, 0.25f, 0.18f), 0.7f);
            return SpriteFactory.FromTemplate($"nest:{def.id}", rows, new Dictionary<char, Color>
            {
                ['o'] = Outline, ['d'] = Color.Lerp(mound, Color.black, 0.3f), ['m'] = mound, ['x'] = new Color(0.05f, 0.04f, 0.05f),
            }, new Vector2(0.5f, 0.2f));
        }

        // ---------- Buildings ----------

        public static Sprite Building(Town.BuildingType type, int tilesW, int tilesH)
        {
            string key = $"building:{type}:{tilesW}x{tilesH}";
            int w = tilesW * SpriteFactory.PixelsPerUnit, h = tilesH * SpriteFactory.PixelsPerUnit;
            var roof = type switch
            {
                Town.BuildingType.TownHall => new Color(0.62f, 0.18f, 0.16f),
                Town.BuildingType.GuildHall => new Color(0.22f, 0.32f, 0.62f),
                Town.BuildingType.Blacksmith => new Color(0.30f, 0.30f, 0.33f),
                Town.BuildingType.Apothecary => new Color(0.25f, 0.55f, 0.35f),
                _ => new Color(0.55f, 0.42f, 0.25f),
            };
            var wall = new Color(0.82f, 0.74f, 0.60f);
            var wood = new Color(0.45f, 0.30f, 0.16f);
            var rows = new string[h];
            var buffer = new char[w];
            bool yard = type == Town.BuildingType.TrainingGround;
            int roofStart = Mathf.RoundToInt(h * 0.42f); // rows from top that are roof
            for (int ry = 0; ry < h; ry++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool border = x == 0 || x == w - 1 || ry == 0 || ry == h - 1;
                    char ch;
                    if (yard)
                    {
                        bool fence = x <= 1 || x >= w - 2 || ry <= 1 || ry >= h - 2;
                        bool post = fence && (x % 8 == 0 || ry % 8 == 0);
                        ch = post ? 'd' : fence ? 'f' : ((x * 7 + ry * 13) % 11 == 0 ? 'y' : 'Y');
                    }
                    else if (border) ch = 'o';
                    else if (ry < roofStart) ch = (ry % 4 == 0) ? 'R' : 'r';
                    else
                    {
                        int doorHalf = SpriteFactory.PixelsPerUnit / 3;
                        bool door = Mathf.Abs(x - w / 2) <= doorHalf && ry > h - SpriteFactory.PixelsPerUnit - 3;
                        bool window = !door && (x % 16 >= 5 && x % 16 <= 10) && (ry - roofStart) % 16 >= 4 && (ry - roofStart) % 16 <= 8;
                        ch = door ? 'd' : window ? 'g' : (x % 16 == 0 ? 'b' : 'w');
                    }
                    buffer[x] = ch;
                }
                rows[ry] = new string(buffer);
            }
            return SpriteFactory.FromTemplate(key, rows, new Dictionary<char, Color>
            {
                ['o'] = Outline, ['r'] = roof, ['R'] = Color.Lerp(roof, Color.black, 0.25f),
                ['w'] = wall, ['b'] = Color.Lerp(wall, Color.black, 0.15f), ['d'] = wood,
                ['g'] = new Color(0.55f, 0.75f, 0.9f), ['f'] = Color.Lerp(wood, Color.white, 0.2f),
                ['Y'] = new Color(0.72f, 0.60f, 0.42f), ['y'] = new Color(0.62f, 0.50f, 0.34f),
            }, new Vector2(0.5f, 0.5f));
        }

        public static Sprite Shadow() => SpriteFactory.Circle(10, new Color(0f, 0f, 0f, 0.28f), new Color(0f, 0f, 0f, 0.28f));

        public static Sprite MoveMarker() => SpriteFactory.Circle(8, new Color(1f, 0.95f, 0.4f, 0.0f), new Color(1f, 0.95f, 0.4f, 0.9f));
    }
}
