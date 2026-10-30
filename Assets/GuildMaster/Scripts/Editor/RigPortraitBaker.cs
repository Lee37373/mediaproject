using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GuildMaster.Visuals;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>
    /// Stills of the rigged monsters (P4-110) for what holds one sprite (fog ghosts): each prefab rendered in a
    /// preview scene at its first idle frame, cropped, with the pivot where the rig stands. They are the pack's art,
    /// so they live in "Assets/Layer Lab" (git-ignored with the packages) under a GUID fixed by name: the tracked
    /// catalog points at the same asset on every machine that bakes them.
    /// </summary>
    public static class RigPortraitBaker
    {
        public const string Folder = "Assets/Layer Lab/GuildMaster Portraits";
        /// <summary>Pixels per prefab unit; the import divides by <see cref="CharacterRig.WorldScale"/> so the still matches the rig.</summary>
        const float PixelsPerPrefabUnit = 100f;
        const float Padding = 0.04f;

        public static string PathOf(string key) => $"{Folder}/{key}.png";

        /// <summary>Bakes the stills that are missing (all of them when <paramref name="rebake"/>) and returns every still by key.</summary>
        public static Dictionary<string, Sprite> Bake(IReadOnlyList<(string key, GameObject prefab)> rigs, bool rebake)
        {
            var todo = rigs.Where(r => rebake || !File.Exists(PathOf(r.key))).ToList();
            if (todo.Count > 0)
            {
                Directory.CreateDirectory(Folder);
                var pivots = new Dictionary<string, Vector2>();
                var scene = EditorSceneManager.NewPreviewScene();
                bool asyncShaders = ShaderUtil.allowAsyncCompilation;
                ShaderUtil.allowAsyncCompilation = false; // a placeholder shader would be baked in
                var camGo = new GameObject("PortraitCamera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                try
                {
                    foreach (var (key, prefab) in todo) pivots[key] = Render(scene, cam, key, prefab);
                }
                finally
                {
                    Object.DestroyImmediate(camGo);
                    EditorSceneManager.ClosePreviewScene(scene);
                    ShaderUtil.allowAsyncCompilation = asyncShaders;
                }

                // Imported with the editor's own defaults (a .meta holding only a GUID imports as a cubemap), then the
                // GUID in a new .meta is swapped for the fixed one, then the sprite settings.
                AssetDatabase.StartAssetEditing();
                try { foreach (var key in pivots.Keys) AssetDatabase.ImportAsset(PathOf(key), ImportAssetOptions.ForceUpdate); }
                finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var key in pivots.Keys)
                    {
                        string meta = PathOf(key) + ".meta", text = File.ReadAllText(meta);
                        string fixedText = Regex.Replace(text, "guid: [0-9a-f]{32}", "guid: " + GuidFor(key));
                        if (fixedText == text) continue;
                        File.WriteAllText(meta, fixedText);
                        AssetDatabase.ImportAsset(PathOf(key), ImportAssetOptions.ForceUpdate);
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.StartAssetEditing();
                try { foreach (var kv in pivots) Configure(PathOf(kv.Key), kv.Value); }
                finally { AssetDatabase.StopAssetEditing(); }
            }
            return rigs.ToDictionary(r => r.key, r => AssetDatabase.LoadAssetAtPath<Sprite>(PathOf(r.key)));
        }

        /// <summary>Writes the still; returns the pivot.</summary>
        static Vector2 Render(UnityEngine.SceneManagement.Scene scene, Camera cam, string key, GameObject prefab)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            try
            {
                go.transform.position = Vector3.zero;
                var animator = go.GetComponent<Animator>();
                var controller = animator != null ? animator.runtimeAnimatorController : null;
                var idle = controller != null ? controller.animationClips.FirstOrDefault(c => c.name == "Idle") : null;
                if (idle != null) idle.SampleAnimation(go, 0f);

                var parts = go.GetComponentsInChildren<SpriteRenderer>()
                    .Where(r => r.enabled && r.sprite != null).ToList();
                var b = parts[0].bounds;
                foreach (var r in parts) b.Encapsulate(r.bounds);
                float left = b.min.x - Padding, bottom = b.min.y - Padding;
                int w = Mathf.CeilToInt((b.size.x + 2f * Padding) * PixelsPerPrefabUnit);
                int h = Mathf.CeilToInt((b.size.y + 2f * Padding) * PixelsPerPrefabUnit);
                float width = w / PixelsPerPrefabUnit, height = h / PixelsPerPrefabUnit;

                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                try
                {
                    cam.targetTexture = rt;
                    cam.orthographicSize = height * 0.5f;
                    cam.transform.position = new Vector3(left + width * 0.5f, bottom + height * 0.5f, -10f);
                    cam.Render();
                    var active = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    RenderTexture.active = active;
                    cam.targetTexture = null;

                    // The sprite shader blends premultiplied over clear black: back to straight alpha.
                    var px = tex.GetPixels32();
                    for (int i = 0; i < px.Length; i++)
                    {
                        var p = px[i];
                        if (p.a == 0 || p.a == 255) continue;
                        px[i] = new Color32((byte)Mathf.Min(255, p.r * 255 / p.a), (byte)Mathf.Min(255, p.g * 255 / p.a),
                                            (byte)Mathf.Min(255, p.b * 255 / p.a), p.a);
                    }
                    tex.SetPixels32(px);
                    File.WriteAllBytes(PathOf(key), tex.EncodeToPNG());
                }
                finally
                {
                    rt.Release();
                    Object.DestroyImmediate(rt);
                    Object.DestroyImmediate(tex);
                }
                return new Vector2(-left / width, -bottom / height);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static void Configure(string path, Vector2 pivot)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spritePivot = pivot;
            settings.spritePixelsPerUnit = PixelsPerPrefabUnit / CharacterRig.WorldScale;
            ti.SetTextureSettings(settings);
            ti.SaveAndReimport();
        }

        /// <summary>Same on every machine, so the tracked catalog's reference resolves wherever the stills are baked.</summary>
        static string GuidFor(string key)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes("GuildMaster.RigPortrait/" + key));
            return string.Concat(hash.Select(x => x.ToString("x2")));
        }
    }
}
