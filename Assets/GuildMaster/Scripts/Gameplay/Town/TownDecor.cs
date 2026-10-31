using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>
    /// Sets out the town's decorations (P4-114, user request 2026-10-31: the town should look like one; where they go:
    /// <see cref="TownDecorRules"/>) — lamp posts by the streets, bushes, flower beds, trees, barrels, crates and benches on
    /// the grass. Pictures only: nobody walks round them, they never stop a building, and they sort among the people like
    /// everything else that stands. One under a building or on its doorstep hides while the building stands (built,
    /// demolished or loaded alike); when the walls move out the town is set out again.
    /// </summary>
    public class TownDecor : MonoBehaviour
    {
        WorldService _world;
        TownService _town;
        readonly List<(Vector2Int cell, TownProp kind, SpriteRenderer sr)> _props = new();
        int _seenVersion = -1;

        public IEnumerable<(Vector2Int cell, TownProp kind, bool shown)> Props
        {
            get { foreach (var p in _props) yield return (p.cell, p.kind, p.sr != null && p.sr.enabled); }
        }

        public void Init(WorldService world, TownService town)
        {
            _world = world;
            _town = town;
            EventBus.Subscribe<TownGroundChangedEvent>(OnTownGround);
            Lay();
        }

        void OnDestroy() => EventBus.Unsubscribe<TownGroundChangedEvent>(OnTownGround);

        void OnTownGround(TownGroundChangedEvent e) => Lay();

        void Lay()
        {
            foreach (var p in _props)
                if (p.sr != null) Destroy(p.sr.gameObject);
            _props.Clear();
            var map = _world.Map;
            var town = map.TownRect;
            for (int y = town.yMin; y < town.yMax; y++)
            for (int x = town.xMin; x < town.xMax; x++)
            {
                var kind = TownDecorRules.At(map, x, y);
                if (kind == TownProp.None) continue;
                var cell = new Vector2Int(x, y);
                int variant = TownDecorRules.Variant(x, y);
                var go = new GameObject("Decor_" + kind);
                go.transform.SetParent(transform, false);
                var at = GridMap.CellToWorld(cell) + new Vector2(0f, -0.3f);
                go.transform.position = at;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = kind == TownProp.Tree ? CartoonArt.Tree(variant) : CartoonArt.Decoration(kind, variant);
                sr.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(at.y * 8f) + 1;
                _props.Add((cell, kind, sr));
            }
            _seenVersion = -1;
        }

        /// <summary>The town's buildings changed: what stands under one, or on its doorstep, hides.</summary>
        void Update()
        {
            if (_town == null || _town.Version == _seenVersion) return;
            _seenVersion = _town.Version;
            foreach (var (cell, _, sr) in _props)
            {
                if (sr == null) continue;
                bool covered = false;
                foreach (var b in _town.Buildings)
                    if (b.Footprint.Contains(cell) || b.DoorCell == cell) { covered = true; break; }
                sr.enabled = !covered;
            }
        }
    }
}
