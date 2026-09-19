using UnityEditor;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>
    /// Import settings for character art under Resources/Art (LAYERLAB 2D Characters).
    /// Pivots sit at the feet so the art lines up with the old 16 px sprites (1 cell ≈ 1 unit):
    /// - Adventurers/*: 195×182 animation frames, feet (incl. baked shadow) ~23% up, ~105 px tall.
    /// - Monsters/*: tightly cropped full-body images, feet at the bottom edge.
    /// </summary>
    public class ArtImportSettings : AssetPostprocessor
    {
        const string Root = "Assets/GuildMaster/Resources/Art/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
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
            if (assetPath.StartsWith(Root + "Adventurers/"))
            {
                settings.spritePivot = new Vector2(0.5f, 0.23f);
                settings.spritePixelsPerUnit = 100f;
            }
            else
            {
                settings.spritePivot = new Vector2(0.5f, 0.02f);
                settings.spritePixelsPerUnit = 90f;
            }
            ti.SetTextureSettings(settings);
        }
    }
}
