using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>Frame lists for an animated character (idle loop, run loop, one-shot death).</summary>
    public class CharacterAnimSet
    {
        public Sprite[] Idle;
        public Sprite[] Run;
        public Sprite[] Die;
        /// <summary>The frames already contain a ground shadow (skip the generated one).</summary>
        public bool HasShadow;
        public float IdleFps = 10f;
        public float RunFps = 12f;
        public float DieFps = 12f;
    }

    /// <summary>
    /// Imported character art under Resources/Art (LAYERLAB 2D Characters: Cute Adventurers,
    /// Casual Monsters). Everything is optional: callers fall back to <see cref="PixelArt"/>
    /// when a sprite is missing, so art can be swapped in piece by piece.
    /// </summary>
    public static class ArtLibrary
    {
        const string Root = "Art/";

        static readonly Dictionary<string, Sprite> s_sprites = new();
        static readonly Dictionary<string, CharacterAnimSet> s_anims = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            s_sprites.Clear();
            s_anims.Clear();
        }

        /// <summary>Full-body monster image, e.g. "goblin", "slime_king". Null if absent.</summary>
        public static Sprite Monster(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!s_sprites.TryGetValue(key, out var s))
                s_sprites[key] = s = Resources.Load<Sprite>(Root + "Monsters/" + key);
            return s;
        }

        /// <summary>Animated adventurer skin, e.g. "knight". Null if absent.</summary>
        public static CharacterAnimSet Adventurer(string skin)
        {
            if (string.IsNullOrEmpty(skin)) return null;
            if (s_anims.TryGetValue(skin, out var set)) return set;
            var all = Resources.LoadAll<Sprite>(Root + "Adventurers/" + skin);
            Sprite[] Frames(string prefix) => all.Where(f => f.name.StartsWith(prefix)).OrderBy(f => f.name).ToArray();
            set = all.Length == 0 ? null : new CharacterAnimSet
            {
                Idle = Frames("idle_"),
                Run = Frames("run_"),
                Die = Frames("die_"),
                HasShadow = true,
            };
            if (set != null && set.Idle.Length == 0) set = null;
            s_anims[skin] = set;
            return set;
        }
    }
}
