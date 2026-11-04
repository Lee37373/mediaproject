using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>How a one-picture body is moved, stretched and tilted at a moment (identity = as drawn).</summary>
    public struct SpritePose
    {
        /// <summary>Shift of the picture, x along the way it faces.</summary>
        public Vector2 Offset;
        /// <summary>Stretch of the picture (1, 1 = as drawn).</summary>
        public Vector2 Scale;
        /// <summary>Lean forward in degrees (towards the way it faces).</summary>
        public float Tilt;

        public static SpritePose Identity => new() { Scale = Vector2.one };

        /// <summary>Two poses at once: shifts and leans add, stretches multiply.</summary>
        public static SpritePose operator *(SpritePose a, SpritePose b)
            => new() { Offset = a.Offset + b.Offset, Scale = Vector2.Scale(a.Scale, b.Scale), Tilt = a.Tilt + b.Tilt };
    }

    /// <summary>
    /// Motions drawn in code for monsters that come as a single picture (P4-127, user request 2026-11-04: "애니메이션 없는
    /// 몬스터들의 공격 모션 같은 거 만들어 줄 수 없냐"): the goblins, slimes and skulls of the 2D Characters packs and the
    /// disaster kinds that borrow them. A blow is a crouch and a lunge (a shot: a draw and a kick back), a hit a squash
    /// away from the blow, a skill a swell and a hop, and standing still a slow breath. Pure functions of the time since the
    /// motion began, so they can be tested; <see cref="CharacterVisual"/> plays them.
    /// </summary>
    public static class SpriteMotion
    {
        public const float AttackSeconds = 0.36f, HitSeconds = 0.22f, SkillSeconds = 0.45f;
        /// <summary>One breath (seconds) and how much it stretches the picture.</summary>
        public const float BreathSeconds = 2.6f, BreathDepth = 0.035f;

        /// <summary>A melee blow: crouch back, lunge and lean in, come back. Seconds since it began.</summary>
        public static SpritePose Attack(float t, bool ranged)
        {
            if (t < 0f || t >= AttackSeconds) return SpritePose.Identity;
            float u = t / AttackSeconds;
            if (ranged)
            {
                // Draw (lean back, crouch), loose, kick back a touch, settle.
                if (u < 0.45f)
                {
                    float k = Smooth(u / 0.45f);
                    return new SpritePose { Offset = new Vector2(-0.07f * k, 0f), Scale = new Vector2(1f + 0.06f * k, 1f - 0.07f * k), Tilt = -6f * k };
                }
                float r = 1f - Smooth((u - 0.45f) / 0.55f);
                return new SpritePose { Offset = new Vector2(-0.07f * r, 0f), Scale = new Vector2(1f - 0.04f * r, 1f + 0.05f * r), Tilt = -6f * r };
            }
            if (u < 0.3f)
            {
                // Crouch: back and squashed, ready to spring.
                float k = Smooth(u / 0.3f);
                return new SpritePose { Offset = new Vector2(-0.08f * k, 0f), Scale = new Vector2(1f + 0.1f * k, 1f - 0.12f * k), Tilt = -5f * k };
            }
            if (u < 0.55f)
            {
                // Lunge: forward, stretched along the blow, leaning into it.
                float k = Smooth((u - 0.3f) / 0.25f);
                return new SpritePose
                {
                    Offset = new Vector2(Mathf.Lerp(-0.08f, 0.3f, k), 0.04f * Mathf.Sin(k * Mathf.PI)),
                    Scale = new Vector2(Mathf.Lerp(1.1f, 1.14f, k), Mathf.Lerp(0.88f, 0.92f, k)),
                    Tilt = Mathf.Lerp(-5f, 12f, k),
                };
            }
            // Recover to where it stood.
            float b = 1f - Smooth((u - 0.55f) / 0.45f);
            return new SpritePose { Offset = new Vector2(0.3f * b, 0f), Scale = new Vector2(1f + 0.14f * b, 1f - 0.08f * b), Tilt = 12f * b };
        }

        /// <summary>Struck: squashed and pushed back, springing back.</summary>
        public static SpritePose Hit(float t)
        {
            if (t < 0f || t >= HitSeconds) return SpritePose.Identity;
            float k = Mathf.Sin(t / HitSeconds * Mathf.PI); // in and out
            return new SpritePose { Offset = new Vector2(-0.1f * k, 0f), Scale = new Vector2(1f + 0.1f * k, 1f - 0.12f * k), Tilt = -8f * k };
        }

        /// <summary>A skill: a swell and a little hop.</summary>
        public static SpritePose Skill(float t)
        {
            if (t < 0f || t >= SkillSeconds) return SpritePose.Identity;
            float k = Mathf.Sin(t / SkillSeconds * Mathf.PI);
            return new SpritePose { Offset = new Vector2(0f, 0.16f * k), Scale = new Vector2(1f + 0.12f * k, 1f + 0.12f * k) };
        }

        /// <summary>Standing still: a slow breath (taller and a little narrower, then back). <paramref name="phase"/> offsets it per body.</summary>
        public static SpritePose Breath(float time, float phase)
        {
            float s = Mathf.Sin((time / BreathSeconds + phase) * Mathf.PI * 2f);
            return new SpritePose { Scale = new Vector2(1f - BreathDepth * 0.5f * s, 1f + BreathDepth * s) };
        }

        /// <summary>Walking: squashed as it lands, stretched at the top of each bob (<paramref name="bob"/> 0..1).</summary>
        public static SpritePose Step(float bob)
            => new() { Scale = new Vector2(1f + 0.05f * (0.5f - bob), 1f - 0.07f * (0.5f - bob)) };

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    }
}
