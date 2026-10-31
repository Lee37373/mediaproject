using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Visuals;
using UnityEditor;
using UnityEngine;

namespace GuildMaster.EditorTools
{
    /// <summary>
    /// Play-mode checks for the new art (2026-10-30): lay out every Casual VFX effect around the camera, and make the
    /// people on screen swing or cast once, so effects and rig animations can be looked at without waiting for a fight.
    /// Editor only; nothing in the game calls it.
    /// </summary>
    public static class ArtPreviewMenu
    {
        [MenuItem("GuildMaster/아트 미리보기/효과 전부 (플레이 중)")]
        static void AllEffects()
        {
            if (!Ready(out var vfx, out var center)) return;
            var catalog = Resources.Load<CharacterArtCatalog>("ArtCatalog");
            int n = catalog != null ? catalog.vfx.Count : 0;
            const int perRow = 6;
            for (int i = 0; i < n; i++)
            {
                var at = center + new Vector2((i % perRow - (perRow - 1) / 2f) * 2.4f, 3f - (i / perRow) * 2f);
                vfx.Play(catalog.vfx[i].key, at);
            }
            Debug.Log($"[Art] {n} effects played around the camera.");
            double checkAt = EditorApplication.timeSinceStartup + 0.25;
            void Report()
            {
                if (EditorApplication.timeSinceStartup < checkAt) return;
                EditorApplication.update -= Report;
                if (vfx == null) return;
                var sb = new System.Text.StringBuilder("[Art] effect check:");
                var cam = Camera.main;
                if (cam != null) sb.Append($" camera mask {cam.cullingMask} at {cam.transform.position};");
                foreach (Transform fx in vfx.transform)
                {
                    if (!fx.gameObject.activeInHierarchy) continue;
                    int particles = 0, visible = 0;
                    string mat = "";
                    foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>()) particles += ps.particleCount;
                    foreach (var r in fx.GetComponentsInChildren<ParticleSystemRenderer>())
                    {
                        if (r.isVisible) visible++;
                        if (mat.Length == 0 && r.sharedMaterial != null)
                        {
                            var m = r.sharedMaterial;
                            var tex = m.mainTexture;
                            mat = $"{m.name}/{m.shader.name}/order {r.sortingOrder}/tex {(tex != null ? tex.name : "none")}"
                                + $"/tint {(m.HasProperty("_TintColor") ? m.GetColor("_TintColor").ToString() : "-")}"
                                + $"/add {(m.HasProperty("_Additive") ? m.GetFloat("_Additive") : -1)}/mode {r.renderMode}/start {r.GetComponent<ParticleSystem>().main.startColor.color}";
                        }
                    }
                    sb.Append($"\n  {fx.name} at {fx.position} scale {fx.lossyScale.x:0.00}: {particles} particles, {visible} visible, {mat}");
                }
                Debug.Log(sb.ToString());
            }
            EditorApplication.update += Report;
        }

        [MenuItem("GuildMaster/아트 미리보기/사람들 공격·스킬 (플레이 중)")]
        static void Swing()
        {
            if (!Ready(out _, out _)) return;
            int i = 0;
            foreach (var rig in Object.FindObjectsByType<HumanRig>(FindObjectsSortMode.None))
            {
                if (i++ % 2 == 0) rig.PlayAttack(); else rig.PlaySkill();
            }
            Debug.Log($"[Art] {i} people swung or cast.");
        }

        [MenuItem("GuildMaster/아트 미리보기/효과 전부 (플레이 중)", true)]
        [MenuItem("GuildMaster/아트 미리보기/사람들 공격·스킬 (플레이 중)", true)]
        static bool Playing() => Application.isPlaying;

        static bool Ready(out CasualVfx vfx, out Vector2 center)
        {
            vfx = null;
            center = Vector2.zero;
            if (!Application.isPlaying || !Services.TryGet(out vfx) || !CasualVfx.Active)
            {
                Debug.Log("[Art] Play mode with the Cartoon Casual VFX Pack imported is needed.");
                return false;
            }
            if (Services.TryGet<CameraController>(out var cam) && cam.Camera != null)
                center = cam.Camera.transform.position;
            return true;
        }
    }
}
