using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Units;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Skill icons above an adventurer's health bar (P4-34, 2026-09-20 user request). Each icon is
    /// a dark disc with the skill's colour filling it clockwise as the cooldown runs down, so a
    /// full disc means ready. Hidden when the camera is zoomed far out.
    /// </summary>
    public class SkillBarLayer : MonoBehaviour
    {
        /// <summary>Icons hide above this orthographic size, like the name tags.</summary>
        const float MaxZoom = 12f;
        /// <summary>World-space gap above the head (the HP bar sits at 0.75).</summary>
        const float HeadOffset = 1.28f;
        const float IconSize = 15f;
        const float IconGap = 3f;

        class Icon
        {
            public RectTransform Root;
            public Image Base;
            public Image Fill;
        }

        RectTransform _layer;
        readonly List<Icon> _icons = new();
        int _used;

        public void Build(RectTransform layer) => _layer = layer;

        Icon Acquire()
        {
            if (_used < _icons.Count)
            {
                var existing = _icons[_used++];
                if (!existing.Root.gameObject.activeSelf) existing.Root.gameObject.SetActive(true);
                return existing;
            }
            var root = UIBuilder.Rect(_layer, "SkillIcon");
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(IconSize, IconSize);

            var disc = SpriteFactory.Circle(24, Color.white, new Color(0.06f, 0.05f, 0.08f, 1f));
            var bg = UIBuilder.SpriteImage(root, "Base", disc, new Color(0.13f, 0.12f, 0.16f, 0.9f));
            var fill = UIBuilder.SpriteImage(root, "Fill", disc, Color.white);
            // Radial360 from the top, clockwise: the colour sweeps round as the skill recharges.
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;

            var icon = new Icon { Root = root, Base = bg, Fill = fill };
            _icons.Add(icon);
            _used++;
            return icon;
        }

        void Place(Vector3 world, IReadOnlyList<Skill> skills)
        {
            if (!WorldOverlay.TryWorldToLocal(_layer, world, out var local)) return;
            float step = IconSize + IconGap;
            float startX = -(skills.Count - 1) * step * 0.5f;
            for (int i = 0; i < skills.Count; i++)
            {
                var s = skills[i];
                if (s == null) continue;
                var icon = Acquire();
                icon.Root.anchoredPosition = local + new Vector2(startX + i * step, 0f);
                float ready = s.Cooldown <= 0f ? 1f : Mathf.Clamp01(1f - s.CooldownLeft / s.Cooldown);
                icon.Fill.fillAmount = ready;
                // Dim while recharging so a ready skill reads at a glance.
                var c = s.Color;
                icon.Fill.color = ready >= 1f ? c : new Color(c.r, c.g, c.b, 0.75f);
            }
        }

        void LateUpdate()
        {
            _used = 0;
            var cam = Camera.main;
            if (cam != null && cam.orthographicSize <= MaxZoom)
            {
                foreach (var u in UnitRegistry.All)
                {
                    if (u is not Adventurer a || !a.IsAlive || a.IsVisitor) continue;
                    if (a.Skills == null || a.Skills.Count == 0) continue;
                    Place(a.transform.position + new Vector3(0f, HeadOffset), a.Skills);
                }
            }
            for (int i = _used; i < _icons.Count; i++)
                if (_icons[i].Root.gameObject.activeSelf) _icons[i].Root.gameObject.SetActive(false);
        }
    }
}
