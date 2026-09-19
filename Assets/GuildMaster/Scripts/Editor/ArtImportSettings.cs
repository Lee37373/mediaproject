using UnityEditor;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>
    /// Import settings for the LAYERLAB 2D Characters art used by the game, applied automatically
    /// when the Asset Store packages are imported (so a fresh machine needs no manual setup).
    /// Pivots sit at the feet so the art lines up with the map (1 cell ≈ 1 unit):
    /// - Cute Adventurers image sequences: 195×182 frames, feet (incl. baked shadow) ~23% up, ~105 px tall.
    /// - Casual Monsters PNG/All: tightly cropped full-body images, feet at the bottom edge.
    /// </summary>
    public class ArtImportSettings : AssetPostprocessor
    {
        public const string AdventurerFrames = "Assets/Layer Lab/2D Characters-Cute Adventurers/Sprites/ImageSequence/";
        public const string MonsterImages = "Assets/Layer Lab/2D Characters-CasualMonsters/PNG/All/";

        void OnPreprocessTexture()
        {
            bool adventurer = assetPath.StartsWith(AdventurerFrames);
            bool monster = assetPath.StartsWith(MonsterImages);
            if (!adventurer && !monster) return;

            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.wrapMode = TextureWrapMode.Clamp;

            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spritePivot = adventurer ? new Vector2(0.5f, 0.23f) : new Vector2(0.5f, 0.02f);
            settings.spritePixelsPerUnit = adventurer ? 100f : 90f;
            ti.SetTextureSettings(settings);
        }
    }
}
