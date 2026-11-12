using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Fog;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.World
{
    /// <summary>
    /// Trees in front of a unit go see-through (P4-127, user request 2026-11-04: "나무 뒤에 몬스터나 NPC 있으면 투명하게"):
    /// since the trees are drawn by depth, a unit just behind one is hidden by its canopy, so that tree fades to
    /// <see cref="Alpha"/> while anyone stands behind it. Only units the guild can see count — a tree that fades over a
    /// monster in the fog would give it away. Rocks stay as they are (they are low).
    /// </summary>
    public class TreeFade : MonoBehaviour
    {
        /// <summary>How much of a tree shows while it hides someone.</summary>
        public const float Alpha = 0.3f;
        /// <summary>A tree's picture round the middle of its cell (<c>CartoonArt.Tree</c>: 52×64 px at 32 a cell, foot at 12%).</summary>
        public const float HalfWidth = 0.8f, Below = 0.24f, Above = 1.76f;
        /// <summary>A unit's figure round its position (drawn feet 0.3 below, a head about 0.7 above).</summary>
        const float FeetBelow = 0.3f, HeadAbove = 0.7f, BodyHalfWidth = 0.3f;

        MapRenderer _renderer;
        MapData _map;
        HashSet<Vector2Int> _faded = new(), _fadeNow = new();

        public void Init(MapRenderer renderer, MapData map)
        {
            _renderer = renderer;
            _map = map;
        }

        /// <summary>Whether a tree on this cell is drawn over a unit standing at <paramref name="unit"/> (pure, tested).</summary>
        public static bool Hides(Vector2Int tree, Vector2 unit)
        {
            float footY = tree.y + 0.5f, midX = tree.x + 0.5f;
            if (unit.y <= tree.y + 0.8f) return false; // in front of the trunk (MapRenderer.DecorRowOrder): drawn over the tree
            if (Mathf.Abs(unit.x - midX) > HalfWidth + BodyHalfWidth) return false;
            return unit.y - FeetBelow < footY + Above && unit.y + HeadAbove > footY - Below;
        }

        void LateUpdate()
        {
            if (_renderer == null || _map == null) return;
            var cam = Camera.main;
            _fadeNow.Clear();
            if (cam != null)
            {
                Services.TryGet<FogOfWar>(out var fog);
                float hh = cam.orthographicSize + 2f, hw = hh * cam.aspect + 2f;
                Vector2 c = cam.transform.position;
                foreach (var u in UnitRegistry.All)
                {
                    if (!u.IsAlive) continue;
                    var p = u.Position;
                    if (Mathf.Abs(p.x - c.x) > hw || Mathf.Abs(p.y - c.y) > hh) continue;
                    if (fog != null && !fog.IsVisible(p) && !FogOfWar.ShowEverything) continue;
                    if (u.Visual != null && u.Visual.SortingBoost != 0) continue; // flying: over the trees anyway
                    int x0 = Mathf.FloorToInt(p.x - HalfWidth - BodyHalfWidth - 0.5f), x1 = Mathf.FloorToInt(p.x + HalfWidth + BodyHalfWidth - 0.5f);
                    int y0 = Mathf.FloorToInt(p.y - FeetBelow - Above - 0.5f), y1 = Mathf.FloorToInt(p.y - 0.8f);
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var cell = new Vector2Int(x, y);
                        if (_map.HasTree(x, y) && Hides(cell, p)) _fadeNow.Add(cell);
                    }
                }
            }
            foreach (var cell in _faded)
                if (!_fadeNow.Contains(cell)) _renderer.SetTreeAlpha(cell, 1f);
            foreach (var cell in _fadeNow)
                if (!_faded.Contains(cell)) _renderer.SetTreeAlpha(cell, Alpha);
            (_faded, _fadeNow) = (_fadeNow, _faded);
        }
    }
}
