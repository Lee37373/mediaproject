using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// A single-picture monster drawn from its pack prefab in parts (P4-128, user request 2026-11-04: "무기가 자연스럽게
    /// 움직이게, 애니메이션 없는 애들"): the 2D Characters Casual Monsters prefabs are the same goblins, slimes and skulls
    /// as the pictures, split into body, arms or weapon, bow, shield and legs, with no Animator. The weapon (a part named
    /// weapon, a bow, else the front arm that holds it) swings round its grip on a blow, the legs step while walking and the
    /// weapon sways while standing; the body's crouch, lunge, squash and breath stay with <see cref="SpriteMotion"/>, which
    /// moves this whole. Fitted onto the picture it stands for (same height, feet and middle), so a body switching between
    /// the two does not jump; sorted as one piece; tinted part by part.
    /// </summary>
    public class PartPuppet
    {
        public GameObject Prefab { get; }

        readonly GameObject _root;
        readonly SortingGroup _group;
        readonly SpriteRenderer[] _parts;
        readonly Color[] _colors;
        readonly Color[] _written;
        readonly Transform _weaponPivot;
        readonly bool _bow;
        readonly List<(Transform t, Vector3 pos, float side)> _legs = new();
        readonly List<Transform> _shields = new();
        readonly Vector3 _fitPosition;
        readonly float _fitScale;
        readonly float _phase;

        public PartPuppet(GameObject prefab, Transform parent, Sprite still, Material material, int sortingLayerId)
        {
            Prefab = prefab;
            _root = Object.Instantiate(prefab, parent, false);
            _root.name = prefab.name + " (parts)";
            _group = _root.GetComponent<SortingGroup>();
            if (_group == null) _group = _root.AddComponent<SortingGroup>();
            _group.sortingLayerID = sortingLayerId;
            _phase = Mathf.Repeat(_root.GetInstanceID() * 0.618f, 1f);

            // Roles by part name; the pack's own shadow gives way to the body's.
            Transform body = null, weapon = null, frontArm = null;
            int frontOrder = int.MinValue;
            var all = new List<SpriteRenderer>();
            foreach (Transform t in _root.transform)
            {
                var sr = t.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                string n = t.name.ToLowerInvariant();
                if (n.StartsWith("shadow")) { t.gameObject.SetActive(false); continue; }
                all.Add(sr);
                if (n.StartsWith("body")) body = t;
                else if (n.StartsWith("weapon")) weapon = t;
                else if (n.StartsWith("bow")) { weapon ??= t; _bow = weapon == t; }
                else if (n.StartsWith("leg")) _legs.Add((t, t.localPosition, 0f));
                else if (n.StartsWith("shield")) _shields.Add(t);
                else if (n.StartsWith("arm") && sr.sortingOrder > frontOrder) { frontOrder = sr.sortingOrder; frontArm = t; }
            }
            weapon ??= frontArm;
            _parts = all.ToArray();
            _colors = new Color[_parts.Length];
            _written = new Color[_parts.Length];
            for (int i = 0; i < _parts.Length; i++)
            {
                // The pack's material is unlit; the game's sprites take the day and night light.
                _parts[i].sharedMaterial = material;
                _colors[i] = _written[i] = _parts[i].color;
            }
            // Legs step in turn: the one further left one way, the other the other.
            for (int i = 0; i < _legs.Count; i++)
                _legs[i] = (_legs[i].t, _legs[i].pos, _legs[i].pos.x <= Average(_legs) ? 1f : -1f);

            if (weapon != null)
            {
                var pivot = new GameObject("WeaponPivot").transform;
                pivot.SetParent(_root.transform, false);
                pivot.localPosition = Grip(weapon, body != null ? (Vector2)body.localPosition : Vector2.zero, _bow);
                weapon.SetParent(pivot, true);
                _weaponPivot = pivot;
            }

            // Fit onto the picture: same height, the same feet and the same middle.
            var parts = Bounds(_parts, _root.transform);
            var target = still != null ? still.bounds : new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
            _fitScale = parts.size.y > 0.001f ? target.size.y / parts.size.y : 1f;
            _fitPosition = new Vector3(target.center.x - parts.center.x * _fitScale, target.min.y - parts.min.y * _fitScale, 0f);
            Reset();
        }

        static float Average(List<(Transform t, Vector3 pos, float side)> legs)
        {
            float s = 0f;
            foreach (var l in legs) s += l.pos.x;
            return legs.Count > 0 ? s / legs.Count : 0f;
        }

        /// <summary>
        /// Where a weapon is held, in the prefab's space: a bow round its middle; a part named weapon near its foot, on the
        /// body's side; an arm at its end nearest the body (the shoulder).
        /// </summary>
        public static Vector2 Grip(Transform part, Vector2 bodyCentre, bool bow)
        {
            var sr = part.GetComponent<SpriteRenderer>();
            var b = sr != null && sr.sprite != null ? sr.sprite.bounds : new Bounds(Vector3.zero, Vector3.one * 0.2f);
            Vector2 c = (Vector2)part.localPosition + (Vector2)b.center;
            Vector2 e = b.extents;
            if (bow) return c;
            if (part.name.ToLowerInvariant().StartsWith("weapon"))
                return new Vector2(Mathf.Clamp(bodyCentre.x, c.x - e.x, c.x + e.x), c.y - e.y * 0.6f);
            Vector2 best = c;
            float bestD = float.MaxValue;
            foreach (var p in new[] { c + new Vector2(-e.x, 0f), c + new Vector2(e.x, 0f), c + new Vector2(0f, -e.y), c + new Vector2(0f, e.y) })
            {
                float d = (p - bodyCentre).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        static Bounds Bounds(SpriteRenderer[] parts, Transform root)
        {
            bool any = false;
            var total = new Bounds();
            foreach (var p in parts)
            {
                if (p.sprite == null) continue;
                var b = p.sprite.bounds;
                var c = root.InverseTransformPoint(p.transform.TransformPoint(b.center));
                var one = new Bounds(c, b.size);
                if (!any) { total = one; any = true; }
                else total.Encapsulate(one);
            }
            return any ? total : new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>Standing, untinted (a pooled body taking up the same picture again).</summary>
        public void Reset()
        {
            for (int i = 0; i < _parts.Length; i++)
            {
                _parts[i].color = _colors[i];
                _written[i] = _colors[i];
            }
            if (_weaponPivot != null) _weaponPivot.localRotation = Quaternion.identity;
            foreach (var l in _legs) { l.t.localPosition = l.pos; l.t.localRotation = Quaternion.identity; }
        }

        /// <summary>
        /// Moves the parts for this frame: <paramref name="attack"/> and <paramref name="hit"/> are the seconds since a blow
        /// or a flinch began (−1 when none), <paramref name="step"/> the walk's phase in radians.
        /// </summary>
        public void Tick(bool moving, bool flip, int order, Color tint, float attack, bool ranged, float hit, float step, bool still)
        {
            _root.transform.localScale = new Vector3(flip ? -_fitScale : _fitScale, _fitScale, 1f);
            _root.transform.localPosition = new Vector3(flip ? -_fitPosition.x : _fitPosition.x, _fitPosition.y, 0f);
            _group.sortingOrder = order;

            if (_weaponPivot != null)
                _weaponPivot.localRotation = Quaternion.Euler(0f, 0f, still ? 0f : WeaponAngle(attack, ranged, hit, moving, step, Time.time + _phase * 3f, _bow));
            foreach (var (t, pos, side) in _legs)
            {
                float s = moving && !still ? Mathf.Sin(step) * side : 0f;
                t.localRotation = Quaternion.Euler(0f, 0f, s * 14f);
                t.localPosition = pos + new Vector3(0f, Mathf.Max(0f, s) * 0.035f, 0f);
            }
            float shieldKick = hit >= 0f && !still ? Mathf.Sin(Mathf.Clamp01(hit / SpriteMotion.HitSeconds) * Mathf.PI) * 0.04f : 0f;
            foreach (var s in _shields) s.localRotation = Quaternion.Euler(0f, 0f, shieldKick * 150f);

            for (int i = 0; i < _parts.Length; i++)
            {
                var p = _parts[i];
                var now = p.color;
                if (now != _written[i]) _colors[i] = now;
                var shown = _colors[i] * tint;
                if (shown != now) p.color = shown;
                _written[i] = shown;
            }
        }

        /// <summary>
        /// The weapon's turn round its grip in degrees (counter-clockwise, for a picture facing right): a blow lifts it back
        /// and brings it down in front; a shot raises the bow and kicks; a flinch jerks it; walking swings it with the step;
        /// standing it sways slowly. Pure, tested.
        /// </summary>
        public static float WeaponAngle(float attack, bool ranged, float hit, bool moving, float step, float time, bool bow)
        {
            if (attack >= 0f && attack < SpriteMotion.AttackSeconds)
            {
                float u = attack / SpriteMotion.AttackSeconds;
                if (ranged || bow)
                {
                    // Raise and draw, loose with a kick, settle.
                    if (u < 0.45f) return Mathf.Lerp(0f, 14f, Smooth(u / 0.45f));
                    if (u < 0.6f) return Mathf.Lerp(14f, -6f, Smooth((u - 0.45f) / 0.15f));
                    return Mathf.Lerp(-6f, 0f, Smooth((u - 0.6f) / 0.4f));
                }
                // Lift back (with the crouch), bring it down hard in front (with the lunge), come back.
                if (u < 0.3f) return Mathf.Lerp(0f, 40f, Smooth(u / 0.3f));
                if (u < 0.5f) return Mathf.Lerp(40f, -75f, Smooth((u - 0.3f) / 0.2f));
                return Mathf.Lerp(-75f, 0f, Smooth((u - 0.5f) / 0.5f));
            }
            if (hit >= 0f && hit < SpriteMotion.HitSeconds) return 12f * Mathf.Sin(hit / SpriteMotion.HitSeconds * Mathf.PI);
            if (moving) return Mathf.Sin(step) * 7f;
            return Mathf.Sin(time / SpriteMotion.BreathSeconds * Mathf.PI * 2f) * 3f;
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Gone at once (the body is being drawn as something else).</summary>
        public void Release()
        {
            _root.SetActive(false);
            _root.transform.SetParent(null, false);
            Object.Destroy(_root);
        }
    }
}
