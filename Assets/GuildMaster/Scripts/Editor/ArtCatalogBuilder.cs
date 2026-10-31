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

            BuildHumans(catalog);
            BuildVfx(catalog);

            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Art] Catalog built: {catalog.skins.Count} skins, {catalog.monsters.Count} monsters, " +
                      $"people rig {(catalog.humanRig != null ? "yes" : "no")} ({catalog.humanParts.Count} part kinds), {catalog.vfx.Count} effects");
        }

        // ---- People and effects (2026-10-30: "캐릭터(사람)와 애니메이션 효과를 새 에셋으로") ----

        const string CharacterMaker = LayerLab + "2D Minimal-CharacterMaker/";
        const string RigPrefab = CharacterMaker + "Common/Prefabs/Character.prefab";
        const string PartsRoot = CharacterMaker + "Extenstions";
        const string VfxRoot = LayerLab + "Cartoon Casual VFX Pack/Prefabs";

        /// <summary>Part folders of the CharacterMaker packs that the game dresses people with.</summary>
        static readonly string[] PartFolders =
        {
            "Hair", "Helmet_Hair", "Eye", "Beard", "Chest", "Helmet", "Sword", "Axe", "Blunt", "Spear", "Staff", "Wand", "Bow", "Arrow", "Shield",
        };

        /// <summary>The effects the game plays (by prefab file name); the rest of the pack is left out of the build.</summary>
        static readonly string[] VfxNames =
        {
            "Hit1_White", "Hit1_Yellow", "Hit1_Red", "Hit1_Purple", "Hit1_Blue", "Hit1_Green",
            "Hit5_Yellow", "Hit5_Red",
            "Buff1_Blue", "Buff1_Green", "Buff1_Purple", "Buff1_Red", "Buff1_White", "Buff1_Yellow",
            "MagicCircle1_Blue", "MagicCircle1_Purple", "MagicCircle1_Yellow",
            "Levelup_Yellow", "Heal1", "Heal2", "Smoke", "Explosion_Small", "Whirlwind1",
        };

        static void BuildHumans(CharacterArtCatalog catalog)
        {
            catalog.humanParts.Clear();
            catalog.humanRig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            if (catalog.humanRig == null || !AssetDatabase.IsValidFolder(PartsRoot)) return;
            var byFolder = new Dictionary<string, List<(string path, Sprite sprite)>>();
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { PartsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Thumbnail/")) continue;
                string folder = Path.GetFileName(Path.GetDirectoryName(path));
                if (!PartFolders.Contains(folder)) continue;
                string file = Path.GetFileName(path);
                // Gear comes as "FA_..." files; the bow's string lines live next to the bows.
                if (path.Contains("/Parts/") && !file.StartsWith("FA_")) continue;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null) continue;
                if (!byFolder.TryGetValue(folder, out var list)) byFolder[folder] = list = new List<(string, Sprite)>();
                list.Add((file, sprite));
            }
            foreach (var folder in PartFolders)
                if (byFolder.TryGetValue(folder, out var list))
                    catalog.humanParts.Add(new CharacterArtCatalog.HumanPart
                    {
                        type = folder,
                        sprites = list.OrderBy(x => x.path, System.StringComparer.Ordinal).Select(x => x.sprite).ToArray(),
                    });
        }

        static void BuildVfx(CharacterArtCatalog catalog)
        {
            catalog.vfx.Clear();
            if (!AssetDatabase.IsValidFolder(VfxRoot)) return;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { VfxRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (!VfxNames.Contains(name)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) catalog.vfx.Add(new CharacterArtCatalog.Vfx { key = name, prefab = prefab });
            }
            catalog.vfx.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
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
