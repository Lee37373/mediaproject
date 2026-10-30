using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuildMaster.Visuals;
using UnityEditor;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>
    /// Builds Resources/ArtCatalog from the LAYERLAB packages in "Assets/Layer Lab". Runs from the
    /// menu and automatically whenever those folders are (re)imported, so importing the packages on
    /// another machine is all it takes.
    /// </summary>
    public class ArtCatalogBuilder : AssetPostprocessor
    {
        const string CatalogPath = "Assets/GuildMaster/Resources/ArtCatalog.asset";
        const string LayerLab = "Assets/Layer Lab/";

        /// <summary>Cute Adventurers image-sequence folders → skin names used by the game.</summary>
        static readonly (string folder, string skin)[] Skins =
        {
            ("s0", "fox"), ("s1", "penguin"), ("s2", "raccoon"), ("s3", "knight"),
            ("s4", "frog"), ("s5", "robot"), ("s6", "skeleton"),
        };

        /// <summary>Casual Monsters PNG/All images → monster art keys (MonsterDefinition.artKey).</summary>
        static readonly (string file, string key)[] Monsters =
        {
            ("goblin/goblin.png", "goblin"), ("goblin/goblin_archer.png", "goblin_archer"),
            ("goblin_green/goblin_warrior.png", "goblin_warrior"),
            ("green_slime/slime.png", "slime"), ("green_slime/stone_slime.png", "stone_slime"), ("green_slime/slime_king.png", "slime_king"),
            ("skull/skull.png", "skull"), ("skull/skull_archer.png", "skull_archer"),
            // Pack bosses (P4-57).
            ("orange_slime/slime.png", "boss_slime"), ("orange_slime/stone_slime.png", "boss_stone_slime"),
            ("orange_slime/slime_king.png", "boss_slime_king"), ("skull_posion/skull_warrior.png", "boss_skull"),
            ("goblin/goblin_warrior.png", "boss_goblin"),
        };

        /// <summary>Minimal EnemyMonster prefabs (P4-110): every one a rig, keyed by its name in lower case ("toad_king").</summary>
        const string RigPrefabs = LayerLab + "2D Minimal-EnemyMonster/EnemyMonster 3/Prefabs";

        /// <summary>From the menu the rigs' stills are baked again too (after the pack is updated).</summary>
        [MenuItem("GuildMaster/아트 카탈로그 다시 만들기")]
        static void BuildFromMenu() => Build(rebakeStills: true);

        /// <summary>Builds the catalog, baking only the rig stills that are missing.</summary>
        public static void Build() => Build(rebakeStills: false);

        static void Build(bool rebakeStills)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterArtCatalog>(CatalogPath);
            bool created = catalog == null;
            if (created) catalog = ScriptableObject.CreateInstance<CharacterArtCatalog>();
            catalog.skins.Clear();
            catalog.monsters.Clear();
            catalog.rigs.Clear();

            foreach (var (folder, skin) in Skins)
            {
                var frames = LoadSprites(ArtImportSettings.AdventurerFrames + folder);
                if (frames.Count == 0) continue;
                Sprite[] Anim(string action) => frames.Where(s => s.name.Contains("-" + action + "_")).OrderBy(s => s.name).ToArray();
                catalog.skins.Add(new CharacterArtCatalog.Skin { name = skin, idle = Anim("idle"), run = Anim("run"), die = Anim("die2") });
            }
            foreach (var (file, key) in Monsters)
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(ArtImportSettings.MonsterImages + file);
                if (s != null) catalog.monsters.Add(new CharacterArtCatalog.MonsterArt { key = key, sprite = s });
            }
            if (AssetDatabase.IsValidFolder(RigPrefabs))
            {
                var rigs = AssetDatabase.FindAssets("t:Prefab", new[] { RigPrefabs })
                    .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(p => p != null && p.GetComponent<Animator>() != null)
                    .Select(p => (key: p.name.ToLowerInvariant(), prefab: p))
                    .OrderBy(r => r.key, System.StringComparer.Ordinal).ToList();
                var stills = RigPortraitBaker.Bake(rigs, rebakeStills);
                foreach (var (key, prefab) in rigs)
                    catalog.rigs.Add(new CharacterArtCatalog.MonsterRig { key = key, prefab = prefab, portrait = stills[key] });
            }

            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Art] Catalog built: {catalog.skins.Count} skins, {catalog.monsters.Count} monsters, {catalog.rigs.Count} rigs");
        }

        static List<Sprite> LoadSprites(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return new List<Sprite>();
            return AssetDatabase.FindAssets("t:Sprite", new[] { folder })
                .Select(g => AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null).ToList();
        }

        /// <summary>Rebuild after the packages are imported (after import settings were applied).</summary>
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // The rig stills it bakes itself land in Layer Lab too: they must not start another build.
            if (!imported.Any(p => p.StartsWith(LayerLab) && !p.StartsWith(RigPortraitBaker.Folder))) return;
            EditorApplication.delayCall -= Build;
            EditorApplication.delayCall += Build;
        }
    }
}
