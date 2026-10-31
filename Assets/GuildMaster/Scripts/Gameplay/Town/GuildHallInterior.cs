using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Units;
using GuildMaster.Visuals;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Town
{
    /// <summary>
    /// The guild hall people walk into (P4-115, user decisions 2026-10-31: the guild's life is the heart of the game, seen in
    /// the same world; big from the start; monsters kept out). Its walls, tables and counter stand in the way; its entrance
    /// and everything inside are shut to monsters like a town gate, so raiders strike its walls from outside and a member can
    /// take cover in it. With the camera close (under <see cref="ShowInsideAt"/>) or the cursor over it, the roof and the front
    /// are taken away and the inside shows — floor, back wall, low front wall and furniture, sorted among the people; zoomed
    /// far out, the roofed hall stands. Members take places in it for what they come for (<see cref="TryTake"/>): a seat to
    /// rest, the board, the counter, the big table, anywhere to hang about — one each, and they face what they came for.
    /// </summary>
    public class GuildHallInterior : MonoBehaviour
    {
        /// <summary>Camera sizes (half the view's height in cells; 12 to start, 4–34) at which the inside opens and the roof comes back.</summary>
        public const float ShowInsideAt = 15f, ShowRoofAt = 17f;

        Building _hall;
        readonly List<SpriteRenderer> _inside = new();
        int _drawnLevel = -1;
        float _open = -1f;
        bool _showInside = true;
        float _faceTimer;

        Unit[] _takenBy;
        readonly Dictionary<Unit, int> _placeOf = new();

        public Building Hall => _hall;
        /// <summary>The inside is showing (or opening).</summary>
        public bool ShowsInside => _showInside;
        public RectInt Footprint => _hall.Footprint;

        public void Init(WorldService world, Building hall)
        {
            _hall = hall;
            _takenBy = new Unit[GuildHallLayout.Spots.Count];
            var r = hall.Footprint;
            var shut = new List<Vector2Int>();
            for (int y = 0; y < GuildHallLayout.Height; y++)
            for (int x = 0; x < GuildHallLayout.Width; x++)
            {
                var cell = new Vector2Int(x, y);
                var at = GuildHallLayout.ToMap(r, cell);
                if (GuildHallLayout.Blocks(cell)) world.Grid.SetBlocked(new RectInt(at.x, at.y, 1, 1), true);
                else shut.Add(at);   // the entrance and the floor: the guild's only
            }
            world.Grid.SetGateClosed(shut, true);
            Draw();
            Apply(_showInside ? 1f : 0f);
        }

        /// <summary>A map cell inside the hall (the entrance counted): where only the guild goes.</summary>
        public bool Contains(Vector2Int cell)
        {
            var local = cell - _hall.Footprint.min;
            return GuildHallLayout.IsInside(local) || local == GuildHallLayout.Entrance;
        }

        // ---------------- Places ----------------

        /// <summary>
        /// A free place for <paramref name="use"/>, taken for <paramref name="who"/> (letting go of the one it had): where to
        /// stand. False when every such place is taken.
        /// </summary>
        public bool TryTake(Unit who, HallUse use, out Vector2 at)
        {
            at = default;
            if (who == null || _hall == null || _hall.IsDestroyed) return false;
            Release(who);
            var spots = GuildHallLayout.Spots;
            int free = 0;
            for (int i = 0; i < spots.Count; i++)
                if (spots[i].Serves(use) && IsFree(i)) free++;
            if (free == 0) return false;
            int pick = GameRandom.Shared.Range(0, free);
            for (int i = 0; i < spots.Count; i++)
            {
                if (!spots[i].Serves(use) || !IsFree(i)) continue;
                if (pick-- > 0) continue;
                _takenBy[i] = who;
                _placeOf[who] = i;
                at = PlaceAt(i);
                return true;
            }
            return false;
        }

        /// <summary>Lets go of the place this one had.</summary>
        public void Release(Unit who)
        {
            if (who == null || !_placeOf.TryGetValue(who, out int i)) return;
            _placeOf.Remove(who);
            if (_takenBy[i] == who) _takenBy[i] = null;
        }

        /// <summary>The place this one holds, if any.</summary>
        public bool TryGetPlace(Unit who, out Vector2 at, out HallUse use)
        {
            at = default;
            use = default;
            if (who == null || !_placeOf.TryGetValue(who, out int i) || _takenBy[i] != who) return false;
            at = PlaceAt(i);
            use = GuildHallLayout.Spots[i].Use;
            return true;
        }

        Vector2 PlaceAt(int i) => GridMap.CellToWorld(GuildHallLayout.ToMap(_hall.Footprint, GuildHallLayout.Spots[i].Cell));

        /// <summary>Nobody holds it, or whoever did has gone (dead, out of town) or moved on to another.</summary>
        bool IsFree(int i)
        {
            var u = _takenBy[i];
            if (u == null) return true;
            bool gone = !u.IsAlive || !_placeOf.TryGetValue(u, out int held) || held != i
                        || !_hall.Footprint.Contains(GridMap.WorldToCell(u.Position)) && (u.Mover == null || !u.Mover.IsMoving);
            if (gone) _takenBy[i] = null;
            return gone;
        }

        // ---------------- Pictures ----------------

        void Draw()
        {
            foreach (var p in _inside) if (p != null) Destroy(p.gameObject);
            _inside.Clear();
            _drawnLevel = _hall.Level;
            var r = _hall.Footprint;
            int level = _hall.Level;
            Add(CartoonArt.HallFloor(level), r.center, MapRenderer.DecorOrder + 1);
            Add(CartoonArt.HallBackWall(level), new Vector2(r.center.x, r.yMax - 1), SortAt(r.yMax - 1));
            Add(CartoonArt.HallFrontWall(level), new Vector2(r.center.x, r.yMin), SortAt(r.yMin));
            Piece(HallFurniture.RoundTable, GuildHallLayout.BigTable.x + 0.5f, GuildHallLayout.BigTable.y + 0.2f);
            Piece(HallFurniture.RoundTable, GuildHallLayout.SmallTable.x + 0.5f, GuildHallLayout.SmallTable.y + 0.2f);
            Piece(HallFurniture.Counter, GuildHallLayout.Counter[1].x, GuildHallLayout.Counter[1].y + 0.12f);
            Piece(HallFurniture.Barrels, 7.55f, 6.15f);
            if (level >= 2)
            {
                Piece(HallFurniture.Plant, 1.35f, 1.12f);
                Piece(HallFurniture.Plant, 7.6f, 1.12f);
            }
            if (level >= 3) Piece(HallFurniture.ArmorStand, 3.5f, 6.2f);
            _open = -1f;   // colour the new pictures
        }

        void Piece(HallFurniture kind, float x, float y)
        {
            var at = GuildHallLayout.ToMap(_hall.Footprint, new Vector2(x, y));
            Add(CartoonArt.HallPiece(kind), at, SortAt(at.y));
        }

        void Add(Sprite sprite, Vector2 at, int order)
        {
            var go = new GameObject("HallInside");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            _inside.Add(sr);
        }

        static int SortAt(float footY) => CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(footY * 8f) + 1;

        /// <summary>The inside's pictures shown by <paramref name="open"/> (0 roofed, 1 open), the outside the other way.</summary>
        void Apply(float open)
        {
            if (Mathf.Approximately(open, _open)) return;
            _open = open;
            var c = new Color(1f, 1f, 1f, open);
            foreach (var p in _inside)
                if (p != null) { p.color = c; p.enabled = open > 0.001f; }
            _hall.SetOutsideAlpha(1f - open);
        }

        void Update()
        {
            if (_hall == null) return;
            if (_hall.Level != _drawnLevel) Draw();   // raised (or loaded raised)
            if (Services.TryGet<CameraControl.CameraController>(out var cam) && cam.Camera != null)
            {
                float size = cam.Camera.orthographicSize;
                _showInside = size <= (_showInside ? ShowRoofAt : ShowInsideAt) || CursorOver(cam);
            }
            float target = _showInside ? 1f : 0f;
            Apply(Mathf.MoveTowards(_open < 0f ? target : _open, target, Time.unscaledDeltaTime * 4f));

            // Those at their places face what they came for.
            _faceTimer -= Time.unscaledDeltaTime;
            if (_faceTimer > 0f) return;
            _faceTimer = 0.4f;
            var spots = GuildHallLayout.Spots;
            for (int i = 0; i < spots.Count; i++)
            {
                var u = _takenBy[i];
                if (u == null || !u.IsAlive || u.Mover == null || u.Mover.IsMoving) continue;
                if ((u.Position - PlaceAt(i)).sqrMagnitude > 0.35f * 0.35f) continue;
                u.Visual?.FaceTowards(GuildHallLayout.ToMap(_hall.Footprint, spots[i].Face));
            }
        }

        bool CursorOver(CameraControl.CameraController cam)
        {
            if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return false;
            var p = cam.MouseWorldPosition();
            var r = _hall.Footprint;
            return p.x >= r.xMin && p.x <= r.xMax && p.y >= r.yMin && p.y <= r.yMax + 1f;
        }
    }
}
