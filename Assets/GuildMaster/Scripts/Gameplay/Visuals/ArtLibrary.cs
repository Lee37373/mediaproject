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
    /// Character art from the LAYERLAB 2D Characters packages, found through
    /// <see cref="CharacterArtCatalog"/> (Resources/ArtCatalog). Everything is optional: when the
    /// packages are not imported the references are empty and callers fall back to
    /// <see cref="PixelArt"/>.
    /// </summary>
    public static class ArtLibrary
    {
        static CharacterArtCatalog s_catalog;
        static bool s_loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache()
        {
            s_catalog = null;
            s_loaded = false;
        }

        static CharacterArtCatalog Catalog
        {
            get
            {
                if (!s_loaded)
                {
                    s_loaded = true;
                    s_catalog = Resources.Load<CharacterArtCatalog>("ArtCatalog");
                }
                return s_catalog;
            }
        }

        /// <summary>Full-body monster image, e.g. "goblin", "slime_king". Null if absent.</summary>
        public static Sprite Monster(string key)
        {
            if (string.IsNullOrEmpty(key) || Catalog == null) return null;
            var m = Catalog.monsters.FirstOrDefault(x => x.key == key);
            return m != null && m.sprite != null ? m.sprite : null;
        }

        /// <summary>Animated adventurer skin, e.g. "knight". Null if absent.</summary>
        public static CharacterAnimSet Adventurer(string skin)
        {
            if (string.IsNullOrEmpty(skin) || Catalog == null) return null;
            var s = Catalog.skins.FirstOrDefault(x => x.name == skin);
            var idle = s?.idle?.Where(f => f != null).ToArray();
            if (idle == null || idle.Length == 0) return null;
            return new CharacterAnimSet
            {
                Idle = idle,
                Run = s.run?.Where(f => f != null).ToArray() ?? new Sprite[0],
                Die = s.die?.Where(f => f != null).ToArray() ?? new Sprite[0],
                HasShadow = true,
            };
        }
    }
}
