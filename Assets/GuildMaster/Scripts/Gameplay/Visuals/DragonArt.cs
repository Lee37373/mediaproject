using System;
using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>How the dragon is drawn (P4-93): on the wing, standing on the ground, or asleep on its hoard.</summary>
    public enum DragonPose { Flying, Standing, Sleeping }

    /// <summary>
    /// Code-drawn pixel-art dragon (P4-93, 94x96 frames facing right, pivot on the ground under it). Flying: a
    /// six-frame wing-beat over its own ground shadow, legs tucked, rising a little on each downstroke. Standing:
    /// wings folded, breathing. Sleeping: curled on the ground, head on its paw, a "z" drifting up. Each body part
    /// is filled as shapes, shaded by a dome light from the top left, edged where it overlaps the parts behind it,
    /// then the whole figure is outlined. Also its fire (<see cref="Flames"/>) and the hoard it sleeps on
    /// (<see cref="Hoard"/>).
    /// </summary>
    public static class DragonArt
    {
        public const int Width = 94;
        public const int Height = 96;
        public const int FlameWidth = 44;
        public const int FlameHeight = 20;
        public const int FlameFrames = 3;
        public const int HoardWidth = 88;
        public const int HoardHeight = 26;

        /// <summary>Row (top first) of the ground under the dragon; the sprite pivot sits here.</summary>
        const float GroundY = 92.5f;

        public static int FrameCount(DragonPose pose) => pose == DragonPose.Flying ? 6 : 4;

        // Ramps: highlight, light, base, shade, dark, outline.
        static readonly Color32[] Scales = { C(255, 150, 96), C(236, 90, 66), C(200, 48, 54), C(146, 28, 56), C(92, 18, 50), C(42, 10, 30) };
        static readonly Color32[] Belly = { C(255, 244, 186), C(252, 220, 134), C(236, 184, 96), C(206, 140, 70), C(160, 96, 56), C(70, 32, 28) };
        static readonly Color32[] Membrane = { C(222, 104, 96), C(184, 66, 84), C(146, 42, 74), C(108, 28, 66), C(72, 20, 56), C(36, 10, 30) };
        static readonly Color32[] Horn = { C(255, 252, 236), C(238, 228, 200), C(206, 192, 160), C(152, 136, 114), C(98, 86, 78), C(44, 30, 34) };
        static readonly Color32[] Gold = { C(255, 250, 200), C(255, 222, 110), C(236, 180, 60), C(190, 128, 40), C(128, 80, 34), C(60, 34, 20) };
        static readonly Color32 EyeGlow = C(255, 250, 190);
        static readonly Color32 Eye = C(255, 196, 54);
        static readonly Color32 Snore = C(214, 232, 255);
        static readonly Color32 GroundShadow = new(0, 0, 0, 64);

        // Flying, per frame: wing beat (1 = raised, 0 = edge-on, negative = below the body), backward stretch of
        // the wing, body lift (px, up is negative) and tail wave (root, tip).
        static readonly float[] Beat = { 1f, 0.58f, 0.08f, -0.85f, -0.45f, 0.36f };
        static readonly float[] Sweep = { 1f, 1.06f, 1.12f, 1.02f, 1.08f, 1.02f };
        static readonly int[] Lift = { 1, 1, 0, -1, -1, 0 };
        static readonly float[] TailRoot = { 0f, -1f, -1f, 0f, 1f, 1f };
        static readonly float[] TailTip = { 1f, -1f, -2.5f, -1.5f, 1f, 2.5f };
        // Standing and sleeping, per frame: breath (chest or back rising, px) and tail-tip sway.
        static readonly float[] Breath = { 0f, 0.5f, 1f, 0.5f };
        static readonly float[] Sway = { 0f, 0.6f, 1f, 0.4f };

        static readonly Dictionary<DragonPose, CharacterAnimSet> s_anims = new();
        static Sprite[] s_flames;
        static Sprite s_hoard, s_corpse;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            s_anims.Clear();
            s_flames = null;
            s_hoard = s_corpse = null;
        }

        /// <summary>The pose's loop as idle and run (faster); frames carry their own ground shadow.</summary>
        public static CharacterAnimSet Anim(DragonPose pose)
        {
            if (s_anims.TryGetValue(pose, out var set)) return set;
            int n = FrameCount(pose);
            var frames = new Sprite[n];
            for (int f = 0; f < n; f++) frames[f] = FigureSprite(PaintFrame(pose, f), $"dragon:{pose}:{f}");
            set = pose switch
            {
                DragonPose.Flying => new CharacterAnimSet { Idle = frames, Run = frames, Die = new Sprite[0], HasShadow = true, IdleFps = 9f, RunFps = 13f },
                DragonPose.Sleeping => new CharacterAnimSet { Idle = frames, Run = frames, Die = new Sprite[0], HasShadow = true, IdleFps = 1.5f, RunFps = 1.5f },
                _ => new CharacterAnimSet { Idle = frames, Run = frames, Die = new Sprite[0], HasShadow = true, IdleFps = 3f, RunFps = 8f },
            };
            s_anims[pose] = set;
            return set;
        }

        /// <summary>Lying still with no breath and no "z" (its body as it fades).</summary>
        public static Sprite Corpse() => s_corpse ??= FigureSprite(PaintFrame(DragonPose.Sleeping, 0, snoring: false), "dragon:corpse");

        /// <summary>Its fire: a flickering cone pointing right, pivot at the narrow end (the mouth).</summary>
        public static Sprite[] Flames()
        {
            if (s_flames != null) return s_flames;
            s_flames = new Sprite[FlameFrames];
            for (int f = 0; f < FlameFrames; f++)
                s_flames[f] = ToSprite(PaintFlame(f), FlameWidth, FlameHeight, new Vector2(0f, 0.5f), $"dragon:flame:{f}");
            return s_flames;
        }

        /// <summary>The hoard at its lair: gold, a few gems, old bones; pivot in the middle.</summary>
        public static Sprite Hoard() => s_hoard ??= ToSprite(PaintHoard(), HoardWidth, HoardHeight, new Vector2(0.5f, 0.5f), "dragon:hoard");

        /// <summary>Where the fire leaves its mouth, from the pivot, facing right (world units).</summary>
        public static Vector2 Mouth(DragonPose pose) => pose switch
        {
            DragonPose.Flying => new Vector2(85f - Width * 0.5f, GroundY - 41f) / SpriteFactory.PixelsPerUnit,
            DragonPose.Standing => new Vector2(78f - Width * 0.5f, GroundY - 50f) / SpriteFactory.PixelsPerUnit,
            _ => new Vector2(88f - Width * 0.5f, GroundY - 89f) / SpriteFactory.PixelsPerUnit,
        };

        /// <summary>Top of the figure above the pivot (world units): where a health bar goes.</summary>
        public static float Top(DragonPose pose) => (pose switch { DragonPose.Flying => 70f, DragonPose.Standing => 62f, _ => 22f }) / SpriteFactory.PixelsPerUnit;

        static Sprite FigureSprite(Color32[] top, string name)
            => ToSprite(top, Width, Height, new Vector2(0.5f, (Height - GroundY) / Height), name);

        static Sprite ToSprite(Color32[] top, int w, int h, Vector2 pivot, string name)
        {
            var px = new Color32[top.Length];
            for (int y = 0; y < h; y++)
                Array.Copy(top, y * w, px, (h - 1 - y) * w, w);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), pivot, SpriteFactory.PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }

        /// <summary>One frame's pixels, top row first.</summary>
        public static Color32[] PaintFrame(DragonPose pose, int frame, bool snoring = true)
        {
            int n = FrameCount(pose);
            int f = ((frame % n) + n) % n;
            var c = new Canvas(Width, Height);
            switch (pose)
            {
                case DragonPose.Flying: DrawFlying(c, f); break;
                case DragonPose.Standing: DrawStanding(c, f); break;
                default: DrawSleeping(c, f, snoring); break;
            }
            return c.Pixels;
        }

        // ---------- Flying ----------

        static void DrawFlying(Canvas c, int f)
        {
            c.Shadow(Width * 0.5f, GroundY, 18f, 2.4f);
            c.OffX = 4;
            c.OffY = 10 + Lift[f];
            float w1 = TailRoot[f], w2 = TailTip[f];

            var neck = new Curve((55f, 40f), (62f, 36f), (63f, 30f), (70f, 28f), 6.2f, 4.2f);
            var tailRoot = new Curve((34f, 45f), (24f, 44f), (17f, 48f + w1), (10f, 46f + w1), 5.6f, 2.8f);
            var tailTip = new Curve((10f, 46f + w1), (6f, 44f + w2), (4f, 41f + w2), (2f, 38f + w2), 2.8f, 1.2f);

            // Far side, behind the trunk.
            Wing(c, (51f, 35.5f), (37f, 37.5f), Beat[f], Sweep[f], -14f, 0.88f, 1);
            c.Begin(Scales, 2.5f, 1);
            c.Ellipse((40f, 47f), 5f, 5.5f, 30f);
            c.Tube((41f, 51f), (39f, 54f), (35f, 55f), (32f, 54.5f), 2.5f, 1.8f);
            c.Poly((33f, 52.5f), (28f, 53f), (27f, 55f), (29f, 56.2f), (33f, 56f));
            c.Tube((57f, 45f), (59f, 49f), (58f, 51f), (56.5f, 52f), 2.8f, 2.1f);
            c.Tube((56.5f, 52f), (58.5f, 53.5f), (61f, 53.2f), (63f, 52f), 2f, 1.6f);
            c.Poly((62f, 50.8f), (65.5f, 51.3f), (66.2f, 53f), (62.5f, 54f));
            HornFar(c, 0f, 0f);
            c.Begin(Belly, 1.2f, 1);
            for (float t = 0.2f; t < 0.95f; t += 0.2f) Spike(c, neck, t, -1f, -1f, 1f);
            for (float t = 0.12f; t < 0.95f; t += 0.2f) Spike(c, tailRoot, t, 1f, 1f, 1f - t * 0.4f);
            Spike(c, tailTip, 0.2f, 1f, 1f, 0.55f);
            CheekSpike(c, 0f, 0f);

            c.Begin(Scales, 6f);
            c.Tube(tailRoot); c.Tube(tailTip);
            TailSpade(c, 2f, 38f + w2, 0f);
            c.Ellipse((33f, 45f), 7.5f, 6.5f, 0f);
            c.Ellipse((44f, 44f), 13.5f, 7.5f, -10f);
            c.Ellipse((54f, 41f), 7.2f, 7.2f, 0f);
            c.Tube(neck);
            Head(c, 0f, 0f);
            int trunk = c.Current;

            // Near side: legs tucked under, wing, horn.
            c.Begin(Scales, 3f);
            c.Ellipse((35f, 47.5f), 4.8f, 5.6f, 30f);
            c.Tube((36f, 51f), (33f, 54f), (29f, 54.5f), (26f, 54f), 2.4f, 1.6f);
            c.Poly((27f, 52.5f), (22.5f, 52.8f), (21f, 54.5f), (23f, 55.8f), (27.5f, 55.6f));
            c.Begin(Scales, 2.8f);
            c.Tube((52f, 46f), (53.5f, 49.5f), (52.5f, 51.5f), (51f, 52.5f), 2.7f, 2f);
            c.Tube((51f, 52.5f), (53f, 54f), (55.5f, 54f), (58f, 53f), 1.9f, 1.5f);
            c.Poly((57.5f, 51.8f), (60.5f, 52.3f), (61.2f, 54f), (58f, 54.8f));
            Wing(c, (48f, 37f), (33f, 39f), Beat[f], Sweep[f], 0f, 1f, 0);
            HornNear(c, 0f, 0f);

            c.Shade();
            BellyPlates(c, trunk, neck, new Curve((56.5f, 42f), (55f, 49f), (45f, 52.5f), (34f, 49.5f), 3.6f, 2.8f));
            Face(c, 0, 0, eyesOpen: true);
            Claws(c, (21, 54), (22, 55), (61, 53), (60, 54));
            c.Outline();
        }

        // ---------- Standing ----------

        static void DrawStanding(Canvas c, int f)
        {
            c.Shadow(Width * 0.5f, GroundY, 25f, 2.6f);
            c.OffX = 7;
            c.OffY = 32;
            float lift = -Breath[f] * 0.9f, sway = Sway[f] * 1.5f;
            // The chest rises with each breath; feet stay put.
            (float x, float y) U(float x, float y) => (x, y + lift);
            const float hx = -12f, hy = -13f; // the head, from where it sits in flight

            var neck = new Curve(U(47f, 37f), U(55f, 33f), U(52f, 23f), U(58f, 16f), 6.5f, 4.4f);
            var tailRoot = new Curve(U(28f, 42f), U(17f, 40f), (9f, 46f), (11f, 53f), 6f, 3.2f);
            var tailTip = new Curve((11f, 53f), (13f + sway, 59.5f), (4f + sway, 60f), (2f + sway, 55f), 3.2f, 1.3f);

            // Far side.
            Wing(c, U(45f, 32f), U(31f, 34.5f), 0.4f + Breath[f] * 0.04f, 0.7f, 26f, 0.86f, 1);
            c.Begin(Scales, 3f, 1);
            c.Ellipse(U(35f, 45f), 5.8f, 7.2f, 15f);
            c.Tube(U(36f, 50f), (37.5f, 52.5f), (35.5f, 55f), (33.5f, 56f), 3f, 2.1f);
            c.Tube((33.5f, 56f), (33.8f, 57f), (34.2f, 58f), (34.5f, 58.8f), 2.1f, 2f);
            c.Poly((31f, 58f), (37.5f, 57.8f), (39.5f, 59.3f), (39.5f, 60.6f), (30.5f, 60.6f));
            c.Tube(U(55f, 41f), U(57f, 45f), U(55.5f, 48.5f), (54.5f, 51f), 3.2f, 2.5f);
            c.Tube((54.5f, 51f), (56f, 53.5f), (57.3f, 56f), (58f, 58.5f), 2.4f, 2.1f);
            c.Poly((55.5f, 57.8f), (61.5f, 57.7f), (63.5f, 59.3f), (63.5f, 60.6f), (55f, 60.6f));
            HornFar(c, hx, hy + lift);
            c.Begin(Belly, 1.2f, 1);
            for (float t = 0.2f; t < 0.95f; t += 0.2f) Spike(c, neck, t, -1f, -1f, 1f);
            for (float t = 0.15f; t < 0.95f; t += 0.22f) Spike(c, tailRoot, t, 1f, 1f, 1f - t * 0.35f);
            Spike(c, tailTip, 0.15f, 1f, 1f, 0.6f);
            CheekSpike(c, hx, hy + lift);

            c.Begin(Scales, 6f);
            c.Tube(tailRoot); c.Tube(tailTip);
            TailSpade(c, 2f + sway, 51f, 0f);
            c.Ellipse(U(28f, 42f), 8.5f, 8f, 0f);
            c.Ellipse(U(37f, 42f), 14f, 9.5f, -8f);
            c.Ellipse(U(47f, 40f), 8.5f, 8.5f, 0f);
            c.Tube(neck);
            Head(c, hx, hy + lift);
            int trunk = c.Current;

            // Near side: the hind leg bends back at the hock, the foreleg at the elbow.
            c.Begin(Scales, 3.5f);
            c.Ellipse(U(29f, 46f), 7f, 8.5f, 20f);
            c.Tube(U(31f, 51f), (32.5f, 53f), (30f, 55f), (27f, 56f), 3.4f, 2.4f);
            c.Tube((27f, 56f), (27.3f, 57f), (27.8f, 58f), (28f, 58.8f), 2.3f, 2.1f);
            c.Poly((24.5f, 57.8f), (31f, 57.6f), (33.5f, 59.2f), (33.5f, 60.6f), (24f, 60.6f));
            c.Begin(Scales, 3f);
            c.Tube(U(50f, 42f), U(51f, 46f), U(49f, 49f), (48f, 51f), 3.8f, 2.8f);
            c.Tube((48f, 51f), (49.5f, 53.5f), (51f, 56f), (52f, 58.5f), 2.7f, 2.2f);
            c.Poly((48.5f, 57.8f), (55f, 57.6f), (57.5f, 59.2f), (57.5f, 60.6f), (48f, 60.6f));
            Wing(c, U(42f, 33.5f), U(27f, 36f), 0.42f + Breath[f] * 0.04f, 0.72f, 22f, 0.95f, 0);
            HornNear(c, hx, hy + lift);

            c.Shade();
            BellyPlates(c, trunk, neck, new Curve(U(51.5f, 41f), U(52.5f, 48.5f), U(42f, 52.5f), U(29f, 51f), 3.8f, 3f));
            Face(c, (int)hx, (int)MathF.Round(hy + lift), eyesOpen: true);
            Claws(c, (33, 59), (33, 60), (30, 60), (57, 59), (57, 60), (54, 60));
            c.Outline();
        }

        // ---------- Sleeping ----------

        static void DrawSleeping(Canvas c, int f, bool snoring)
        {
            c.Shadow(Width * 0.5f, GroundY - 0.5f, 30f, 3f);
            float b = snoring ? Breath[f] : 0f, sway = snoring ? Sway[f] : 0f;
            // The back rises with each breath; what lies on the ground stays put.
            (float x, float y) U(float x, float y) => (x, y - b);
            const float hx = 4f, hy = 59f; // the head resting on the ground

            var neck = new Curve(U(56f, 78f), U(64f, 72f), (70f, 77f), (72f, 85f), 5.8f, 4.3f);
            var tailRoot = new Curve(U(28f, 83f), (17f, 84f), (13f, 91f), (25f, 92.2f), 5.4f, 3.2f);
            var tailTip = new Curve((25f, 92.2f), (38f, 92.8f), (50f, 92.8f), (58f + sway, 91.6f), 3.2f, 1.3f);

            // Far side: the folded wing on the back, the far horn.
            Wing(c, U(53f, 73f), U(36f, 76f), 0.34f, 0.74f, 30f, 0.84f, 1);
            HornFar(c, hx, hy);
            c.Begin(Belly, 1.2f, 1);
            for (float t = 0.15f; t < 0.95f; t += 0.25f) Spike(c, neck, t, -1f, -1f, 0.9f);
            for (float t = 0.1f; t < 0.95f; t += 0.22f) Spike(c, tailRoot, t, 1f, 1f, 0.9f - t * 0.3f);
            c.Begin(Belly, 1.2f, 1);
            var ridge = new Curve(U(29f, 77f), U(37f, 71.5f), U(47f, 70.5f), U(56f, 73f), 0f, 0f);
            for (float t = 0.1f; t < 0.95f; t += 0.2f) Spike(c, ridge, t, -1f, -1f, 0.8f);

            c.Begin(Scales, 6f);
            c.Tube(tailRoot);
            c.Ellipse(U(34f, 83f), 8.5f, 8f, 0f);
            c.Ellipse(U(45.5f, 81.5f), 14.5f, 9.8f, 0f);
            c.Ellipse(U(56f, 83f), 7.5f, 7.5f, 0f);
            c.Tube(neck);
            Head(c, hx, hy);
            int trunk = c.Current;

            // Near side: the hind leg folded flat, the foreleg stretched out under the head, the tail wrapped round.
            c.Begin(Scales, 3.5f);
            c.Ellipse(U(35f, 84.5f), 7f, 7f, 10f);
            c.Tube((38f, 89.5f), (41f, 91f), (45f, 91.6f), (48f, 91.6f), 2.3f, 1.9f);
            c.Poly((46.5f, 89.8f), (51.5f, 90f), (53f, 91.4f), (53f, 92.6f), (46f, 92.6f));
            c.Begin(Scales, 3f);
            c.Tube(U(58f, 86f), (61f, 89f), (64f, 91f), (68f, 91.2f), 2.8f, 2.1f);
            c.Poly((66.5f, 89.6f), (72f, 89.8f), (73.5f, 91.4f), (73.5f, 92.6f), (66f, 92.6f));
            c.Begin(Scales, 4f);
            c.Tube(tailTip);
            TailSpade(c, 58f + sway, 91.6f, 90f);
            Wing(c, U(50f, 74.5f), U(33f, 78f), 0.36f, 0.78f, 26f, 0.9f, 0);
            HornNear(c, hx, hy);

            c.Shade();
            BellyPlates(c, trunk, neck, null);
            Face(c, (int)hx, (int)hy, eyesOpen: false);
            Claws(c, (53, 91), (53, 92), (73, 91), (73, 92));
            if (snoring) Zed(c, f);
            c.Outline();
        }

        /// <summary>A small "z" drifting up from the sleeper's head, a step higher each frame.</summary>
        static void Zed(Canvas c, int f)
        {
            int x = 80 + f, y = 77 - f * 3;
            string[] glyph = f < 2 ? new[] { "zzz", "..z", ".z.", "zzz" } : new[] { "zzzz", "...z", "..z.", ".z..", "zzzz" };
            for (int r = 0; r < glyph.Length; r++)
                for (int k = 0; k < glyph[r].Length; k++)
                    if (glyph[r][k] == 'z') c.Paint(x + k, y + r, Snore);
        }

        // ---------- Shared parts (flight coordinates, moved by dx/dy) ----------

        static void Head(Canvas c, float dx, float dy)
        {
            c.Ellipse((72f + dx, 27f + dy), 5.5f, 4.6f, -6f);
            c.Ellipse((73f + dx, 24f + dy), 3.2f, 1.6f, 0f);
            c.Poly((71f + dx, 23f + dy), (79f + dx, 24.5f + dy), (83.5f + dx, 26.2f + dy), (84.5f + dx, 29.4f + dy), (77f + dx, 30.6f + dy), (71f + dx, 30.6f + dy));
            c.Poly((69f + dx, 29.8f + dy), (81f + dx, 30.2f + dy), (82.5f + dx, 31.6f + dy), (77f + dx, 33.6f + dy), (71f + dx, 33.8f + dy), (67.5f + dx, 32f + dy));
        }

        static void HornFar(Canvas c, float dx, float dy)
        {
            c.Begin(Horn, 1.5f, 1);
            c.Tube((72f + dx, 23f + dy), (71f + dx, 19.5f + dy), (68f + dx, 17f + dy), (65f + dx, 15.5f + dy), 1.6f, 0.55f);
        }

        static void HornNear(Canvas c, float dx, float dy)
        {
            c.Begin(Horn, 1.6f);
            c.Tube((70f + dx, 23.5f + dy), (67f + dx, 20.5f + dy), (63f + dx, 19f + dy), (59f + dx, 17f + dy), 1.9f, 0.55f);
        }

        static void CheekSpike(Canvas c, float dx, float dy) => c.Poly((68f + dx, 31.6f + dy), (65.5f + dx, 30.2f + dy), (61.5f + dx, 33.5f + dy));

        static void TailSpade(Canvas c, float x, float y, float deg)
        {
            float cos = MathF.Cos(deg * MathF.PI / 180f), sin = MathF.Sin(deg * MathF.PI / 180f);
            (float x, float y) R(float u, float v) => (x + u * cos - v * sin, y + u * sin + v * cos);
            c.Poly(R(-2.6f, -4.6f), R(2.6f, -1.2f), R(1.4f, 2.6f), R(-2.4f, 1.4f));
        }

        static void Face(Canvas c, int dx, int dy, bool eyesOpen)
        {
            if (eyesOpen)
            {
                c.Set(72 + dx, 25 + dy, Scales[4]); c.Set(73 + dx, 25 + dy, Scales[4]); c.Set(74 + dx, 25 + dy, Scales[3]);
                c.Set(73 + dx, 26 + dy, Eye); c.Set(74 + dx, 26 + dy, EyeGlow);
            }
            else
            {
                c.Set(72 + dx, 26 + dy, Scales[4]); c.Set(73 + dx, 26 + dy, Scales[5]); c.Set(74 + dx, 26 + dy, Scales[5]);
            }
            c.Set(82 + dx, 27 + dy, Scales[4]);
            c.Line(70 + dx, 31 + dy, 81 + dx, 30 + dy, Scales[5]);
            c.Set(74 + dx, 32 + dy, Horn[0]); c.Set(78 + dx, 31 + dy, Horn[0]);
        }

        static void Claws(Canvas c, params (int x, int y)[] at)
        {
            foreach (var (x, y) in at) c.Set(x, y, Horn[1]);
        }

        /// <summary>Belly plates: a strip along the throat (and the underside, when given), striped across its length.</summary>
        static void BellyPlates(Canvas c, int trunk, Curve neck, Curve? under)
        {
            var belly = new List<(float x, float y, float r)>();
            for (int i = 0; i <= 24; i++)
            {
                float t = 1f - i / 24f;
                var (px, py) = neck.Point(t);
                var (nx, ny) = neck.Normal(t, 1f);
                float r = neck.Radius(t);
                belly.Add((px + nx * r * 0.7f, py + ny * r * 0.7f, r * 0.62f));
            }
            if (under is { } u)
                for (int i = 0; i <= 24; i++)
                {
                    var (px, py) = u.Point(i / 24f);
                    belly.Add((px, py, u.Radius(i / 24f)));
                }
            c.Overlay(trunk, belly, Belly, 2.6f);
        }

        /// <summary>
        /// A wing hinged at the shoulder <paramref name="s"/> with its trailing edge tied to the back at
        /// <paramref name="a"/>: an arm to the wrist, three fingers fanning backward, membrane between. It is drawn
        /// raised, turned by <paramref name="rot"/> degrees, then squashed vertically by <paramref name="beat"/>
        /// (1 = raised, 0 = edge-on, negative = below the body) and stretched back by <paramref name="sweep"/>,
        /// which is how a beating wing looks from the side (folded: small beat, short sweep, turned back).
        /// </summary>
        static void Wing(Canvas c, (float x, float y) s, (float x, float y) a, float beat, float sweep, float rot,
            float size, int darken)
        {
            float cos = MathF.Cos(rot * MathF.PI / 180f), sin = MathF.Sin(rot * MathF.PI / 180f);
            (float x, float y) T((float x, float y) p) =>
                (s.x + (p.x * cos + p.y * sin) * size * sweep, s.y + (-p.x * sin + p.y * cos) * size * beat);
            var w0 = Add((0f, 0f), Dir(100f), 22f);
            var w = T(w0);
            var tips = new[] { T(Add(w0, Dir(130f), 30f)), T(Add(w0, Dir(165f), 27f)), T(Add(w0, Dir(212f), 18f)) };
            (float x, float y) Scallop((float x, float y) p, (float x, float y) q, (float x, float y) toward, float pull)
            {
                float mx = (p.x + q.x) * 0.5f, my = (p.y + q.y) * 0.5f;
                return (mx + (toward.x - mx) * pull, my + (toward.y - my) * pull);
            }

            c.Begin(Membrane, 2.5f, darken);
            c.Poly(s, w, tips[0], Scallop(tips[0], tips[1], w, 0.28f), tips[1], Scallop(tips[1], tips[2], w, 0.28f),
                tips[2], Scallop(tips[2], a, s, 0.18f), a);
            c.Begin(Scales, 1.2f, darken - 1, edged: false);
            c.Tube(s, Lerp(s, w, 0.33f), Lerp(s, w, 0.66f), w, 1.8f, 1.1f);
            c.Disk(w.x, w.y, 1.4f);
            for (int i = 0; i < 3; i++) c.Line((int)w.x, (int)w.y, (int)tips[i].x, (int)tips[i].y);
            c.Begin(Horn, 1f, darken - 1);
            var claw = Add(w0, Dir(50f), 2.4f);
            c.Tube(w, T(Add(w0, Dir(50f), 1.2f)), T(claw), T(Add(claw, Dir(-10f), 1f)), 0.8f, 0.4f);
        }

        /// <summary>A back-swept spike on the curve's side <paramref name="side"/> (+1 / -1).</summary>
        static void Spike(Canvas c, Curve k, float t, float side, float sweep, float size)
        {
            var (px, py) = k.Point(t);
            var (tx, ty) = k.Tangent(t);
            var (nx, ny) = k.Normal(t, side);
            float r = k.Radius(t);
            c.Poly((px + nx * (r - 0.8f) + tx * 1.7f * size, py + ny * (r - 0.8f) + ty * 1.7f * size),
                (px + nx * (r + 2.8f * size) + tx * 1.5f * size * sweep, py + ny * (r + 2.8f * size) + ty * 1.5f * size * sweep),
                (px + nx * (r - 0.8f) - tx * 1.7f * size, py + ny * (r - 0.8f) - ty * 1.7f * size));
        }

        // ---------- Fire ----------

        /// <summary>A cone of fire from the left edge (the mouth) widening to the right, flickering per frame.</summary>
        public static Color32[] PaintFlame(int frame)
        {
            const int w = FlameWidth, h = FlameHeight;
            var px = new Color32[w * h];
            float mid = (h - 1) * 0.5f;
            for (int x = 0; x < w; x++)
            {
                float t = x / (float)(w - 1);
                float half = 1.2f + t * 7.6f + (Noise(x / 3, frame, 1) - 0.5f) * 2.4f * t;
                for (int y = 0; y < h; y++)
                {
                    float d = MathF.Abs(y - mid + (Noise(x / 4, frame, 2) - 0.5f) * 2f * t) / half;
                    float edge = 1f - t * 0.25f + (Noise(x, y + frame * 31, 3) - 0.5f) * 0.25f;
                    if (d > edge)
                    {
                        // Sparks flung off the edge.
                        if (d < edge + 0.5f && Noise(x, y, frame + 7) > 0.93f) px[y * w + x] = C(255, 190, 80);
                        continue;
                    }
                    float k = d / edge;
                    Color32 col = k < 0.32f && t < 0.75f ? C(255, 250, 214)
                                : k < 0.58f ? C(255, 222, 92)
                                : k < 0.8f ? C(255, 146, 44)
                                : C(222, 62, 32);
                    // The far end burns out to orange and red.
                    if (t > 0.86f && k < 0.58f) col = C(255, 146, 44);
                    px[y * w + x] = col;
                }
            }
            return px;
        }

        // ---------- Hoard ----------

        /// <summary>A low mound of gold with coins, gems, a sword and old bones, lit from the top left.</summary>
        public static Color32[] PaintHoard()
        {
            var c = new Canvas(HoardWidth, HoardHeight);
            c.Begin(Gold, 3.5f);
            c.Ellipse((44f, 17.5f), 39f, 7.5f, 0f);
            c.Ellipse((28f, 14.5f), 15f, 7f, -4f);
            c.Ellipse((58f, 14f), 16f, 7.5f, 6f);
            c.Ellipse((43f, 11.5f), 12f, 7f, 0f);
            c.Ellipse((12f, 19f), 7f, 4.5f, 0f);
            c.Ellipse((76f, 19f), 7f, 4.5f, 0f);
            c.Begin(Horn, 1.5f);
            c.Ellipse((24f, 19.5f), 2.6f, 2.2f, 0f);
            c.Tube((70f, 17f), (73f, 16.5f), (76f, 17f), (79f, 16.2f), 0.9f, 0.9f);
            c.Disk(69.5f, 17.2f, 1.2f); c.Disk(79.5f, 16f, 1.2f);
            c.Begin(Horn, 1f, 1);
            c.Line(52, 2, 57, 10);
            c.Line(53, 2, 58, 10);
            c.Shade();
            // Coins catching the light, gems, the skull's eyes, the sword's hilt.
            for (int y = 0; y < HoardHeight; y++)
                for (int x = 0; x < HoardWidth; x++)
                {
                    float n = Noise(x, y, 11);
                    if (n > 0.86f) c.Paint(x, y, Gold[0], onlyOver: true);
                    else if (n < 0.07f) c.Paint(x, y, Gold[3], onlyOver: true);
                }
            foreach (var (x, y, col) in new[] { (24, 12, C(220, 40, 60)), (50, 9, C(70, 140, 255)), (38, 17, C(60, 200, 110)), (63, 15, C(220, 40, 60)), (33, 19, C(70, 140, 255)) })
            {
                c.Paint(x, y, col, onlyOver: true);
                c.Paint(x + 1, y, Gold[0], onlyOver: true);
            }
            c.Paint(23, 19, Scales[5], onlyOver: true); c.Paint(25, 19, Scales[5], onlyOver: true);
            c.Paint(57, 10, C(120, 60, 40), onlyOver: true); c.Paint(58, 11, C(220, 40, 60), onlyOver: true);
            c.Outline();
            return c.Pixels;
        }

        /// <summary>Deterministic value noise in [0, 1) for (x, y, seed).</summary>
        static float Noise(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        static (float x, float y) Dir(float deg) => (MathF.Cos(deg * MathF.PI / 180f), -MathF.Sin(deg * MathF.PI / 180f));
        static (float x, float y) Add((float x, float y) p, (float x, float y) d, float k) => (p.x + d.x * k, p.y + d.y * k);
        static (float x, float y) Lerp((float x, float y) p, (float x, float y) q, float t) => (p.x + (q.x - p.x) * t, p.y + (q.y - p.y) * t);
        static Color32 C(int r, int g, int b) => new((byte)r, (byte)g, (byte)b, 255);

        /// <summary>Cubic Bezier with a radius that narrows from start to end.</summary>
        readonly struct Curve
        {
            readonly (float x, float y) _a, _b, _c, _d;
            readonly float _r0, _r1;

            public Curve((float x, float y) a, (float x, float y) b, (float x, float y) c, (float x, float y) d, float r0, float r1)
            {
                _a = a; _b = b; _c = c; _d = d; _r0 = r0; _r1 = r1;
            }

            public (float x, float y) Point(float t)
            {
                float u = 1f - t;
                float k0 = u * u * u, k1 = 3f * u * u * t, k2 = 3f * u * t * t, k3 = t * t * t;
                return (k0 * _a.x + k1 * _b.x + k2 * _c.x + k3 * _d.x, k0 * _a.y + k1 * _b.y + k2 * _c.y + k3 * _d.y);
            }

            public (float x, float y) Tangent(float t)
            {
                float u = 1f - t;
                float dx = 3f * u * u * (_b.x - _a.x) + 6f * u * t * (_c.x - _b.x) + 3f * t * t * (_d.x - _c.x);
                float dy = 3f * u * u * (_b.y - _a.y) + 6f * u * t * (_c.y - _b.y) + 3f * t * t * (_d.y - _c.y);
                float len = MathF.Max(1e-4f, MathF.Sqrt(dx * dx + dy * dy));
                return (dx / len, dy / len);
            }

            /// <summary>Unit normal; side +1 is the tangent turned clockwise on screen (y down).</summary>
            public (float x, float y) Normal(float t, float side)
            {
                var (tx, ty) = Tangent(t);
                return (-ty * side, tx * side);
            }

            public float Radius(float t) => _r0 + (_r1 - _r0) * t;
        }

        /// <summary>
        /// Paint target: parts are filled back to front, each remembers its full shape (for shading) while the
        /// front-most part owns each pixel.
        /// </summary>
        sealed class Canvas
        {
            readonly int W, H;

            sealed class Part
            {
                public bool[] Mask;
                public Color32[] Ramp;
                public float Round;
                public int Darken;
                public bool Edged;
                public int[] Tone;
            }

            readonly List<Part> _parts = new();
            readonly int[] _owner;
            readonly bool[] _shadow;
            readonly int[] _tone;
            /// <summary>Pixels painted straight on the canvas outside every part (a "z"), outlined with the rest.</summary>
            readonly bool[] _loose;
            public readonly Color32[] Pixels;
            Part _cur;

            public Canvas(int w, int h)
            {
                W = w;
                H = h;
                _owner = new int[w * h];
                _shadow = new bool[w * h];
                _tone = new int[w * h];
                _loose = new bool[w * h];
                Pixels = new Color32[w * h];
                for (int i = 0; i < _owner.Length; i++) _owner[i] = -1;
            }

            public int Current => _parts.Count - 1;

            /// <summary>Whole-pixel shift for everything drawn after it except the ground shadow and <see cref="Paint"/>.</summary>
            public int OffX, OffY;

            public void Begin(Color32[] ramp, float round, int darken = 0, bool edged = true)
            {
                _cur = new Part { Mask = new bool[W * H], Ramp = ramp, Round = round, Darken = darken, Edged = edged };
                _parts.Add(_cur);
            }

            /// <summary>
            /// Colours one pixel after shading, at canvas coordinates (no shift): over the figure only when
            /// <paramref name="onlyOver"/>, else anywhere (outlined then like the figure).
            /// </summary>
            public void Paint(int x, int y, Color32 col, bool onlyOver = false)
            {
                if (x < 0 || y < 0 || x >= W || y >= H) return;
                int i = y * W + x;
                if (_owner[i] < 0)
                {
                    if (onlyOver) return;
                    _loose[i] = true;
                }
                Pixels[i] = col;
            }

            void Plot(int x, int y)
            {
                x += OffX; y += OffY;
                if (x < 0 || y < 0 || x >= W || y >= H) return;
                int i = y * W + x;
                _cur.Mask[i] = true;
                _owner[i] = _parts.Count - 1;
            }

            public void Shadow(float cx, float cy, float rx, float ry)
            {
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                        if (dx * dx + dy * dy <= 1f) _shadow[y * W + x] = true;
                    }
            }

            public void Disk(float cx, float cy, float r)
            {
                for (int y = (int)MathF.Floor(cy - r); y <= (int)MathF.Ceiling(cy + r); y++)
                    for (int x = (int)MathF.Floor(cx - r); x <= (int)MathF.Ceiling(cx + r); x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        if (dx * dx + dy * dy <= r * r) Plot(x, y);
                    }
            }

            public void Ellipse((float x, float y) c, float rx, float ry, float deg)
            {
                float cos = MathF.Cos(deg * MathF.PI / 180f), sin = MathF.Sin(deg * MathF.PI / 180f);
                float reach = MathF.Max(rx, ry);
                for (int y = (int)(c.y - reach) - 1; y <= (int)(c.y + reach) + 1; y++)
                    for (int x = (int)(c.x - reach) - 1; x <= (int)(c.x + reach) + 1; x++)
                    {
                        float dx = x + 0.5f - c.x, dy = y + 0.5f - c.y;
                        float u = (dx * cos + dy * sin) / rx, v = (-dx * sin + dy * cos) / ry;
                        if (u * u + v * v <= 1f) Plot(x, y);
                    }
            }

            public void Poly(params (float x, float y)[] p)
            {
                float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
                foreach (var q in p) { minY = MathF.Min(minY, q.y); maxY = MathF.Max(maxY, q.y); minX = MathF.Min(minX, q.x); maxX = MathF.Max(maxX, q.x); }
                for (int y = (int)minY; y <= (int)maxY; y++)
                    for (int x = (int)minX; x <= (int)maxX; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        bool inside = false;
                        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                            if ((p[i].y > py) != (p[j].y > py) &&
                                px < (p[j].x - p[i].x) * (py - p[i].y) / (p[j].y - p[i].y) + p[i].x)
                                inside = !inside;
                        if (inside) Plot(x, y);
                    }
            }

            public void Tube((float x, float y) a, (float x, float y) b, (float x, float y) c, (float x, float y) d, float r0, float r1)
                => Tube(new Curve(a, b, c, d, r0, r1));

            public void Tube(Curve k)
            {
                for (int i = 0; i <= 64; i++)
                {
                    float t = i / 64f;
                    var (x, y) = k.Point(t);
                    Disk(x, y, k.Radius(t));
                }
            }

            public void Line(int x0, int y0, int x1, int y1)
            {
                int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
                while (true)
                {
                    Plot(x0, y0);
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 >= dy) { err += dy; x0 += sx; }
                    if (e2 <= dx) { err += dx; y0 += sy; }
                }
            }

            /// <summary>Colors pixels along a line directly (after shading).</summary>
            public void Line(int x0, int y0, int x1, int y1, Color32 col)
            {
                int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
                while (true)
                {
                    Set(x0, y0, col);
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 >= dy) { err += dy; x0 += sx; }
                    if (e2 <= dx) { err += dx; y0 += sy; }
                }
            }

            public void Set(int x, int y, Color32 col)
            {
                x += OffX; y += OffY;
                if (x < 0 || y < 0 || x >= W || y >= H || _owner[y * W + x] < 0) return;
                Pixels[y * W + x] = col;
            }

            static readonly float LightX = -0.5f / 1.049f, LightY = -0.7f / 1.049f, LightZ = 0.6f / 1.049f;

            /// <summary>
            /// Tones every part from its own full shape (dome profile, top-left light), edges it where it overlaps
            /// parts behind, and darkens what lies just under a part in front.
            /// </summary>
            public void Shade()
            {
                foreach (var p in _parts) p.Tone = Dome(p);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x, o = _owner[i];
                        if (o < 0)
                        {
                            if (_shadow[i]) Pixels[i] = GroundShadow;
                            continue;
                        }
                        var part = _parts[o];
                        int tone = part.Tone[i] + part.Darken;
                        if (part.Edged && (Behind(x - 1, y, o) || Behind(x + 1, y, o) || Behind(x, y - 1, o) || Behind(x, y + 1, o)))
                            tone = 4;
                        else if (InFront(x, y - 1, o) || InFront(x - 1, y - 1, o) || InFront(x - 2, y - 2, o))
                            tone++;
                        _tone[i] = Math.Clamp(tone, 0, 4);
                        Pixels[i] = part.Ramp[_tone[i]];
                    }
            }

            bool Behind(int x, int y, int o)
            {
                if (x < 0 || y < 0 || x >= W || y >= H) return false;
                int n = _owner[y * W + x];
                return n >= 0 && n < o;
            }

            bool InFront(int x, int y, int o)
            {
                if (x < 0 || y < 0 || x >= W || y >= H) return false;
                int i = y * W + x;
                for (int k = o + 1; k < _parts.Count; k++)
                    if (_parts[k].Mask[i]) return true;
                return false;
            }

            int[] Dome(Part p)
            {
                var d = new float[W * H];
                for (int i = 0; i < d.Length; i++) d[i] = p.Mask[i] ? 999f : 0f;
                float D(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? 0f : d[y * W + x];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        if (!p.Mask[i]) continue;
                        d[i] = MathF.Min(d[i], MathF.Min(MathF.Min(D(x - 1, y) + 1f, D(x, y - 1) + 1f),
                            MathF.Min(D(x - 1, y - 1) + 1.414f, D(x + 1, y - 1) + 1.414f)));
                    }
                for (int y = H - 1; y >= 0; y--)
                    for (int x = W - 1; x >= 0; x--)
                    {
                        int i = y * W + x;
                        if (!p.Mask[i]) continue;
                        d[i] = MathF.Min(d[i], MathF.Min(MathF.Min(D(x + 1, y) + 1f, D(x, y + 1) + 1f),
                            MathF.Min(D(x + 1, y + 1) + 1.414f, D(x - 1, y + 1) + 1.414f)));
                    }
                var h = new float[W * H];
                for (int i = 0; i < h.Length; i++)
                {
                    if (!p.Mask[i]) continue;
                    float t = MathF.Min(d[i] - 0.5f, p.Round) / p.Round;
                    h[i] = p.Round * MathF.Sqrt(MathF.Max(0f, 1f - (1f - t) * (1f - t)));
                }
                float Hh(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? 0f : h[y * W + x];
                var tone = new int[W * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        if (!p.Mask[i]) continue;
                        float nx = -(Hh(x + 1, y) - Hh(x - 1, y)) * 0.5f, ny = -(Hh(x, y + 1) - Hh(x, y - 1)) * 0.5f;
                        float len = MathF.Sqrt(nx * nx + ny * ny + 1f);
                        float v = (nx * LightX + ny * LightY + LightZ) / len;
                        tone[i] = v >= 0.86f ? 0 : v >= 0.7f ? 1 : v >= 0.45f ? 2 : v >= 0.18f ? 3 : 4;
                    }
                return tone;
            }

            /// <summary>
            /// Recolors the visible pixels of part <paramref name="owner"/> that fall inside the chain of disks with
            /// <paramref name="ramp"/> (same tones), striped across the chain every <paramref name="stripe"/> px.
            /// </summary>
            public void Overlay(int owner, List<(float x, float y, float r)> chain, Color32[] ramp, float stripe)
            {
                var arc = new float[chain.Count];
                for (int k = 1; k < chain.Count; k++)
                {
                    float dx = chain[k].x - chain[k - 1].x, dy = chain[k].y - chain[k - 1].y;
                    arc[k] = arc[k - 1] + MathF.Sqrt(dx * dx + dy * dy);
                }
                var part = _parts[owner];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        if (_owner[i] != owner) continue;
                        float best = float.MaxValue; int at = -1;
                        for (int k = 0; k < chain.Count; k++)
                        {
                            float dx = x - OffX + 0.5f - chain[k].x, dy = y - OffY + 0.5f - chain[k].y;
                            float d2 = dx * dx + dy * dy;
                            if (d2 <= chain[k].r * chain[k].r && d2 < best) { best = d2; at = k; }
                        }
                        if (at < 0) continue;
                        int tone = _tone[i];
                        if (tone < 4 && (arc[at] / stripe) % 1f < 0.34f) tone++;
                        Pixels[i] = ramp[tone];
                    }
            }

            /// <summary>One-pixel outline around the figure in the outline tone of the part it touches.</summary>
            public void Outline()
            {
                var src = (int[])_owner.Clone();
                var loose = (bool[])_loose.Clone();
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        if (src[i] >= 0 || loose[i]) continue;
                        int n = Owner(src, x, y + 1);
                        if (n < 0) n = Owner(src, x - 1, y);
                        if (n < 0) n = Owner(src, x + 1, y);
                        if (n < 0) n = Owner(src, x, y - 1);
                        if (n >= 0) Pixels[i] = _parts[n].Ramp[5];
                        else if (Loose(loose, x, y + 1) || Loose(loose, x - 1, y) || Loose(loose, x + 1, y) || Loose(loose, x, y - 1))
                            Pixels[i] = LooseOutline;
                    }
            }

            static readonly Color32 LooseOutline = new(40, 44, 76, 255);

            int Owner(int[] src, int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? -1 : src[y * W + x];
            bool Loose(bool[] src, int x, int y) => x >= 0 && y >= 0 && x < W && y < H && src[y * W + x];
        }
    }
}
