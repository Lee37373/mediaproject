using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// References to the imported LAYERLAB character art (Resources/ArtCatalog). It stores only
    /// asset references (GUIDs), never the images, so it can live in git: on a machine that has
    /// imported the Asset Store packages the references resolve by themselves; without them they
    /// are null and the game falls back to generated sprites. Rebuilt by the editor tool
    /// "GuildMaster/아트 카탈로그 다시 만들기" and automatically after the packages are imported.
    /// </summary>
    public class CharacterArtCatalog : ScriptableObject
    {
        [Serializable]
        public class Skin
        {
            public string name;
            public Sprite[] idle;
            public Sprite[] run;
            public Sprite[] die;
        }

        [Serializable]
        public class MonsterArt
        {
            public string key;
            public Sprite sprite;
            /// <summary>The same monster as separate parts (the pack's prefab, P4-128): moved in code so its weapon swings.</summary>
            public GameObject parts;
            /// <summary>What it shoots (an archer's arrow from the pack's PNG folder, P6-9), or null.</summary>
            public Sprite shot;
        }

        /// <summary>
        /// A rigged monster from Minimal EnemyMonster (P4-110): the prefab (parts, Animator with Idle/Walk/Attack/Hit/Dead)
        /// and a still of its first idle frame, baked by the editor tool, for what can only hold one sprite (fog ghosts).
        /// </summary>
        [Serializable]
        public class MonsterRig
        {
            public string key;
            public GameObject prefab;
            public Sprite portrait;
        }

        public List<Skin> skins = new();
        public List<MonsterArt> monsters = new();

        // ---- 2D Minimal-CharacterMaker (people, 2026-10-30) and Cartoon Casual VFX Pack ----

        /// <summary>Sprites for one kind of body part or gear ("Hair", "Sword", ...), in file-name order.</summary>
        [Serializable]
        public class HumanPart
        {
            public string type;
            public Sprite[] sprites;
        }

        [Serializable]
        public class Vfx
        {
            public string key;
            public GameObject prefab;
        }

        /// <summary>The CharacterMaker's animated character (Animator + one SpriteRenderer per part). Null without the package.</summary>
        public GameObject humanRig;
        public List<HumanPart> humanParts = new();
        /// <summary>Effect prefabs by file name ("Hit1_White", "Levelup_Yellow", ...). Empty without the package.</summary>
        public List<Vfx> vfx = new();
        public List<MonsterRig> rigs = new();
    }
}
