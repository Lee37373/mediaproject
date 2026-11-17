using UnityEngine;

namespace GuildMaster.Visuals
{
    public enum ShotKind { Orb, Arrow }

    /// <summary>
    /// What a projectile looks like (P6-9, after the defence project's shot looks): an arrow (the very picture the shooter
    /// holds, turned to where it flies) or a glowing orb with a white core and a fading tail. A tail can trail an arrow too
    /// (the ranger's skill arrows, in the skill's colour). Pictures only: speed, homing, cover and harm stay in
    /// <see cref="Units.ProjectileSpec"/>.
    /// </summary>
    public struct ShotLook
    {
        /// <summary>Arrow lengths in world units (people are about one unit tall).</summary>
        public const float ArrowLength = 0.62f, MonsterArrowLength = 0.52f, TowerArrowLength = 0.7f;

        public ShotKind Kind;
        /// <summary>Arrow: its picture (null = <see cref="Core.SpriteFactory.Arrow"/>). Unused by orbs.</summary>
        public Sprite Sprite;
        /// <summary>Arrow: length; orb: diameter of the glow. World units.</summary>
        public float Size;
        /// <summary>Orb: the glow's colour.</summary>
        public Color Color;
        /// <summary>The tail's colour; alpha 0 = no tail.</summary>
        public Color Tail;

        public bool HasTail => Tail.a > 0f;
        /// <summary>A look nobody filled in (default struct): drawn as a plain orb in the stand-in colour.</summary>
        public bool IsEmpty => Size <= 0f;

        public static ShotLook Arrow(Sprite sprite, float length = ArrowLength)
            => new() { Kind = ShotKind.Arrow, Sprite = sprite, Size = length, Color = Color.white };

        /// <summary>A glowing orb of <paramref name="diameter"/> with a tail in its own colour.</summary>
        public static ShotLook Orb(Color color, float diameter)
            => new() { Kind = ShotKind.Orb, Size = diameter, Color = color, Tail = color };

        public ShotLook WithTail(Color tail)
        {
            var look = this;
            look.Tail = tail;
            return look;
        }

        /// <summary>The arrow towers' arrow: a plain grey one from the CharacterMaker pack, else the code arrow.</summary>
        public static ShotLook TowerArrow => Arrow(TowerSprite(), TowerArrowLength);

        static Sprite TowerSprite()
        {
            var arrows = ArtLibrary.HumanPart("Arrow");
            if (arrows == null || arrows.Length == 0) return null;
            int i = HumanRig.PartIndex("Arrow", "FA_Consumable_Arrow_003_Gray", 0);
            return arrows[Mathf.Clamp(i, 0, arrows.Length - 1)];
        }
    }
}
