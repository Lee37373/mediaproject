using UnityEditor;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>
    /// Import settings for the generated UI skin and sounds (2026-10-31), applied on import so a fresh clone needs no
    /// manual setup:
    /// - Resources/UI/Skin/*.png: uncompressed sprites with 9-slice borders (2x art; UISkin shrinks them on the canvas).
    /// - Resources/Audio/Sfx: short clips decompressed on load, mono. Resources/Audio/Music: streamed Vorbis.
    /// After changing a border here, right-click the Skin folder → Reimport.
    /// </summary>
    public class UiSoundImport : AssetPostprocessor
    {
        const string SkinFolder = "Assets/GuildMaster/Resources/UI/Skin/";
        const string SfxFolder = "Assets/GuildMaster/Resources/Audio/Sfx/";
        const string MusicFolder = "Assets/GuildMaster/Resources/Audio/Music/";

        /// <summary>Sprite Editor border (left, bottom, right, top) in pixels of the 2x art.</summary>
        public static Vector4 Border(string fileName)
        {
            if (fileName.StartsWith("panel_window")) return new Vector4(56, 56, 56, 56);
            if (fileName == "panel_tint") return new Vector4(14, 14, 14, 14);
            if (fileName.StartsWith("button_") && fileName != "button_close") return new Vector4(36, 40, 36, 30);
            return Vector4.zero; // icons, the round close button
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(SkinFolder)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.spritePixelsPerUnit = 100f;
            ti.spriteBorder = Border(System.IO.Path.GetFileNameWithoutExtension(assetPath));
        }

        void OnPreprocessAudio()
        {
            bool sfx = assetPath.StartsWith(SfxFolder), music = assetPath.StartsWith(MusicFolder);
            if (!sfx && !music) return;
            var ai = (AudioImporter)assetImporter;
            ai.forceToMono = sfx;
            ai.loadInBackground = music;
            var s = ai.defaultSampleSettings;
            s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = music ? 0.6f : 0.8f;
            ai.defaultSampleSettings = s;
        }
    }
}
