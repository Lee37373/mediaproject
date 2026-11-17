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

        /// <summary>
        /// Casual Monsters PNG/All images → monster art keys (MonsterDefinition.artKey), with the pack's prefab of the same
        /// monster in parts (P4-128: moved in code, so the weapon swings).
        /// </summary>
        static readonly (string file, string key, string parts)[] Monsters =
        {
            ("goblin/goblin.png", "goblin", "goblin"), ("goblin/goblin_archer.png", "goblin_archer", "goblin_archer"),
            ("goblin_green/goblin_warrior.png", "goblin_warrior", "green_goblin_warrior"),
            ("green_slime/slime.png", "slime", "green_slime"), ("green_slime/stone_slime.png", "stone_slime", "green_stone_slime"),
            ("green_slime/slime_king.png", "slime_king", "green_slime_king"),
            ("skull/skull.png", "skull", "skull"), ("skull/skull_archer.png", "skull_archer", "skull_archer"),
            // Pack bosses (P4-57).
            ("orange_slime/slime.png", "boss_slime", "slime"), ("orange_slime/stone_slime.png", "boss_stone_slime", "stone_slime"),
            ("orange_slime/slime_king.png", "boss_slime_king", "slime_king"), ("skull_posion/skull_warrior.png", "boss_skull", "posion_skull_warrior"),
            ("goblin/goblin_warrior.png", "boss_goblin", "goblin_warrior"),
        };

        const string MonsterPartPrefabs = LayerLab + "2D Characters-CasualMonsters/Prefabs/";

        /// <summary>What the archers shoot (P6-9): the arrow image in each one's own PNG parts folder, by art key.</summary>
        static readonly (string key, string file)[] MonsterShots =
        {
            ("goblin_archer", "Goblin/goblin/goblin_archer/arrow.png"), ("skull_archer", "Skull/skull/skull_archer/arrow.png"),
        };

        const string MonsterPartImages = LayerLab + "2D Characters-CasualMonsters/PNG/";

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
            foreach (var (file, key, parts) in Monsters)
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(ArtImportSettings.MonsterImages + file);
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPartPrefabs + parts + ".prefab");
                var shotFile = MonsterShots.FirstOrDefault(m => m.key == key).file;
                var shot = shotFile != null ? AssetDatabase.LoadAssetAtPath<Sprite>(MonsterPartImages + shotFile) : null;
                if (s != null) catalog.monsters.Add(new CharacterArtCatalog.MonsterArt { key = key, sprite = s, parts = p, shot = shot });
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
                      $"{catalog.rigs.Count} rigs, people rig {(catalog.humanRig != null ? "yes" : "no")} ({catalog.humanParts.Count} part kinds), {catalog.vfx.Count} effects");
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
            "Sub_Item", // left-hand items: the guild master's ledger (2026-10-31)
        };

        /// <summary>The effects the game plays (by prefab file name); the rest of the pack is left out of the build.</summary>
        static readonly string[] VfxNames =
        {
            "Hit1_White", "Hit1_Yellow", "Hit1_Red", "Hit1_Purple", "Hit1_Blue", "Hit1_Green",
            "Hit5_Yellow", "Hit5_Red",
            "Buff1_Blue", "Buff1_Green", "Buff1_Purple", "Buff1_Red", "Buff1_White", "Buff1_Yellow",
            "MagicCircle1_Blue", "MagicCircle1_Purple", "MagicCircle1_Yellow",
            "Levelup_Yellow", "Heal1", "Heal2", "Smoke", "Explosion_Small", "Whirlwind1",
            // The adept, master and beast skills (P4-126).
            "Shield", "Hit10_Red", "Buff2_Red", "Buff3_Yellow", "Booster_ArrowUp", "Booster_Lightning", "Poisoning1", "Poisoning2",
            "Lightning_Small_Yellow", "Lightning_Small_Blue", "Explosion_Effect_Blue", "Explosion_Effect_Green", "Explosion_Effect_White",
            "Explosion_Large", "Cloud1", "Cloud2", "Bubble",
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
            // The rig stills it bakes itself land in Layer Lab too: they must not start another build.
            if (!imported.Any(p => p.StartsWith(LayerLab) && !p.StartsWith(RigPortraitBaker.Folder))) return;
            EditorApplication.delayCall -= Build;
            EditorApplication.delayCall += Build;
        }
    }
}
