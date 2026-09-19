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
        };

        [MenuItem("GuildMaster/아트 카탈로그 다시 만들기")]
        public static void Build()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterArtCatalog>(CatalogPath);
            bool created = catalog == null;
            if (created) catalog = ScriptableObject.CreateInstance<CharacterArtCatalog>();
            catalog.skins.Clear();
            catalog.monsters.Clear();

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

            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Art] Catalog built: {catalog.skins.Count} skins, {catalog.monsters.Count} monsters");
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
            if (!imported.Any(p => p.StartsWith(LayerLab))) return;
            EditorApplication.delayCall -= Build;
            EditorApplication.delayCall += Build;
        }
    }
}
