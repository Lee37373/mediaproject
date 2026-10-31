using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// What a person wears (2026-10-30: people drawn with the 2D Minimal-CharacterMaker rig). Part numbers are taken
    /// modulo what the pack has, -1 means none. Pure data, made from a seed so the same person always looks the same.
    /// </summary>
    public struct HumanLook
    {
        public int Hair, Eye, Beard, Helmet, Chest;
        /// <summary>Main-hand gear folder ("Sword", "Bow", ...) or null.</summary>
        public string Weapon;
        public int WeaponIndex;
        public int Shield;
        public Color Skin, HairColor;
        /// <summary>Size against an adult (children are smaller).</summary>
        public float Scale;
        /// <summary>Runs when moving (adventurers, the guild master); walks otherwise (townsfolk).</summary>
        public bool Runs;

        public const int None = -1;

        static readonly Color[] SkinTones =
        {
            new(1f, 0.87f, 0.76f), new(0.98f, 0.8f, 0.66f), new(0.93f, 0.74f, 0.58f), new(0.82f, 0.6f, 0.44f), new(0.62f, 0.44f, 0.32f),
        };

        static readonly Color[] HairTones =
        {
            new(0.25f, 0.17f, 0.12f), new(0.45f, 0.3f, 0.18f), new(0.75f, 0.55f, 0.3f), new(0.95f, 0.85f, 0.55f),
            new(0.15f, 0.15f, 0.18f), new(0.7f, 0.3f, 0.2f), new(0.85f, 0.85f, 0.85f),
        };

        /// <summary>A stable number for a name or id (same text, same number).</summary>
        public static int Hash(string s)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in s ?? "") h = h * 31 + c;
                return h & 0x7fffffff;
            }
        }

        /// <summary>A plain person from a seed: hair, eyes, clothes and colours, no gear.</summary>
        public static HumanLook Plain(string seed)
        {
            var rng = new System.Random(Hash(seed));
            return new HumanLook
            {
                Hair = rng.Next(1000), Eye = rng.Next(1000), Beard = rng.NextDouble() < 0.25 ? rng.Next(1000) : None,
                Helmet = None, Chest = rng.Next(1000), Weapon = null, WeaponIndex = 0, Shield = None,
                Skin = SkinTones[rng.Next(SkinTones.Length)], HairColor = HairTones[rng.Next(HairTones.Length)],
                Scale = 1f, Runs = false,
            };
        }

        /// <summary>
        /// An adventurer by class: warriors with a blade, axe or mace and often a shield and helmet, rangers with a bow,
        /// mages with a staff, priests with a wand. Skin and hair keep the colours the adventurer was born with.
        /// </summary>
        public static HumanLook Adventurer(string id, Adventurers.AdventurerClass cls, Color skin, Color hair)
        {
            var look = Plain(id);
            var rng = new System.Random(Hash(id) ^ 0x5bd1e995);
            switch (cls)
            {
                case Adventurers.AdventurerClass.Warrior:
                    double w = rng.NextDouble();
                    look.Weapon = w < 0.6 ? "Sword" : w < 0.8 ? "Axe" : "Blunt";
                    look.Shield = rng.NextDouble() < 0.6 ? rng.Next(1000) : None;
                    look.Helmet = rng.NextDouble() < 0.5 ? rng.Next(1000) : None;
                    break;
                case Adventurers.AdventurerClass.Ranger:
                    look.Weapon = "Bow";
                    look.Helmet = rng.NextDouble() < 0.2 ? rng.Next(1000) : None;
                    break;
                case Adventurers.AdventurerClass.Priest:
                    look.Weapon = "Wand";
                    break;
                default:
                    look.Weapon = "Staff";
                    break;
            }
            look.WeaponIndex = rng.Next(1000);
            if (skin.a > 0f) look.Skin = Opaque(skin);
            if (hair.a > 0f) look.HairColor = Opaque(hair);
            look.Runs = true;
            return look;
        }

        static Color Opaque(Color c) => new(c.r, c.g, c.b, 1f);
    }

    /// <summary>
    /// One person drawn with the 2D Minimal-CharacterMaker rig (2026-10-30): the pack's animated character, dressed
    /// part by part from <see cref="ArtLibrary"/>, sorted as one sprite, facing left or right, and playing Idle /
    /// Walk / Run / Attack / Skill / Dead1. Works without the pack's scripts (none are referenced), so a machine
    /// without the package still compiles and falls back to the old art.
    /// </summary>
    public class HumanRig : MonoBehaviour
    {
        /// <summary>The rig is about 0.8 units tall; this brings an adult to the old characters' height.</summary>
        public const float BaseScale = 1.25f;
        /// <summary>Feet sit this far below the rig's origin before scaling (the body sprite's lower edge).</summary>
        const float FeetOffset = 0.06f;
        /// <summary>An attack plays in about this long whatever the clip's length.</summary>
        const float AttackSeconds = 0.6f;
        const float SkillSeconds = 0.7f;

        static readonly string[] Weapons = { "Sword", "Axe", "Blunt", "Spear", "Staff", "Wand", "Bow" };

        Animator _animator;
        SortingGroup _group;
        readonly Dictionary<string, Transform> _parts = new();
        readonly List<(SpriteRenderer r, Color c)> _tintable = new();
        Color _tint = Color.white;
        float _scale = 1f;
        bool _left, _moving, _runs, _dead;
        string _state;
        float _actionUntil;

        /// <summary>A dressed person under <paramref name="parent"/>, or null when the pack is not imported.</summary>
        public static HumanRig Create(Transform parent, HumanLook look)
        {
            var prefab = ArtLibrary.HumanRigPrefab;
            if (prefab == null) return null;
            var go = Instantiate(prefab, parent, false);
            go.name = "Rig";
            var rig = go.AddComponent<HumanRig>();
            rig.Setup(look);
            return rig;
        }

        void Setup(HumanLook look)
        {
            _animator = GetComponent<Animator>();
            // The clips call OnAttackHit/OnSkillHit; answer them so Unity does not log a missing receiver.
            if (GetComponent<RigAnimationEvents>() == null) gameObject.AddComponent<RigAnimationEvents>();
            _group = gameObject.AddComponent<SortingGroup>();
            foreach (var t in GetComponentsInChildren<Transform>(true)) _parts[Path(t)] = t;
            Dress(look);
            _scale = Mathf.Max(0.1f, look.Scale) * BaseScale;
            _runs = look.Runs;
            transform.localPosition = new Vector3(0f, -0.3f + FeetOffset * _scale, 0f);
            ApplyFacing();
            Play("Idle");
        }

        string Path(Transform t)
        {
            var names = new List<string>();
            for (var p = t; p != null && p != transform; p = p.parent) names.Insert(0, p.name);
            return string.Join("/", names);
        }

        SpriteRenderer Part(string path) => _parts.TryGetValue(path, out var t) ? t.GetComponent<SpriteRenderer>() : null;

        void Show(string path, bool on)
        {
            if (_parts.TryGetValue(path, out var t)) t.gameObject.SetActive(on);
        }

        static Sprite Pick(string type, int index)
        {
            if (index < 0) return null;
            var list = ArtLibrary.HumanPart(type);
            return list == null || list.Length == 0 ? null : list[index % list.Length];
        }

        void Set(string path, Sprite sprite, Color? color = null)
        {
            var r = Part(path);
            if (r == null) return;
            r.gameObject.SetActive(sprite != null);
            r.sprite = sprite;
            if (color.HasValue) r.color = color.Value;
        }

        /// <summary>Puts the clothes, hair and gear of <paramref name="look"/> on the rig.</summary>
        public void Dress(HumanLook look)
        {
            bool helmet = look.Helmet != HumanLook.None && Pick("Helmet", look.Helmet) != null;
            var skin = look.Skin.a > 0f ? look.Skin : Color.white;
            var hair = look.HairColor.a > 0f ? look.HairColor : Color.white;
            if (Part("Body") is { } body) body.color = skin;
            if (Part("Body/Head") is { } head) head.color = skin;
            Set("Body/Head/Eye", Pick("Eye", look.Eye));
            Set("Body/Head/Hair", helmet ? null : Pick("Hair", look.Hair), hair);
            Set("Body/Head/Hair_Helmet", helmet ? Pick("Helmet_Hair", look.Hair) : null, hair);
            Set("Body/Head/Helmet", helmet ? Pick("Helmet", look.Helmet) : null);
            Set("Body/Head/Beard", Pick("Beard", look.Beard), hair);
            var chest = Pick("Chest", look.Chest);
            if (chest != null) Set("Body/Chest", chest);

            foreach (var w in Weapons)
                if (w != "Bow") Set("HandRight/" + w, w == look.Weapon ? Pick(w, look.WeaponIndex) : null);
            bool bow = look.Weapon == "Bow";
            Set("HandRight/Bow/Bow", bow ? Pick("Bow", look.WeaponIndex) : null);
            Show("HandRight/Bow/Bow_Line_Up", bow);
            Show("HandRight/Bow/Bow_Line_Down", bow);
            Set("HandRight/Bow/Arrow", bow ? Pick("Arrow", look.WeaponIndex) : null);
            // No crossbows in the game.
            foreach (var cb in new[] { "Crossbow", "Crossbow_Down", "Crossbow_Line_Up", "Crossbow_Line_Down", "Bolt" })
                Show("HandRight/Crossbow/" + cb, false);
            Set("HandLeft/Shield", Pick("Shield", look.Shield));
            Show("HandLeft/Sub_Item", false);

            _tintable.Clear();
            foreach (var path in new[] { "Body", "Body/Head", "Body/Chest", "Body/Head/Eye", "Body/Head/Hair", "Body/Head/Hair_Helmet",
                                         "Body/Head/Helmet", "Body/Head/Beard", "HandLeft/Shield", "HandRight/Bow/Bow" })
                if (Part(path) is { } r) _tintable.Add((r, r.color));
            foreach (var w in Weapons)
                if (w != "Bow" && Part("HandRight/" + w) is { } r) _tintable.Add((r, r.color));
            SetTint(_tint);
        }

        // ---------------- Driven by CharacterVisual every frame ----------------

        public void SetSorting(int order) => _group.sortingOrder = order;

        public void Face(bool left)
        {
            if (left == _left) return;
            _left = left;
            ApplyFacing();
        }

        void ApplyFacing() => transform.localScale = new Vector3(_left ? -_scale : _scale, _scale, 1f);

        public void SetMoving(bool moving)
        {
            _moving = moving;
            if (_dead || Time.time < _actionUntil) return;
            Play(moving ? (_runs ? "Run" : "Walk") : "Idle");
        }

        public void PlayAttack() => PlayAction("Attack", AttackSeconds);

        public void PlaySkill() => PlayAction("Skill", SkillSeconds);

        void PlayAction(string state, float seconds)
        {
            if (_dead || _animator == null) return;
            float length = ClipLength(state);
            _animator.speed = length > 0f ? Mathf.Clamp(length / seconds, 1f, 4f) : 1f;
            _state = null;
            Play(state, restart: true);
            _actionUntil = Time.time + seconds;
        }

        /// <summary>Falls over once and stays down.</summary>
        public void PlayDeath()
        {
            if (_dead || _animator == null) return;
            _dead = true;
            _animator.speed = 1f;
            Play("Dead1", restart: true);
            _actionUntil = Time.time + ClipLength("Dead1");
        }

        void Update()
        {
            if (_animator == null) return;
            if (_dead)
            {
                // The death clip loops; hold its last pose.
                if (Time.time >= _actionUntil && _animator.speed != 0f) _animator.speed = 0f;
                return;
            }
            if (_actionUntil > 0f && Time.time >= _actionUntil)
            {
                _actionUntil = 0f;
                _animator.speed = 1f;
                Play(_moving ? (_runs ? "Run" : "Walk") : "Idle");
            }
        }

        void Play(string state, bool restart = false)
        {
            if (_animator == null || (!restart && state == _state)) return;
            _state = state;
            _animator.Play(state, 0, 0f);
        }

        float ClipLength(string name)
        {
            var ctrl = _animator != null ? _animator.runtimeAnimatorController : null;
            if (ctrl == null) return 0f;
            foreach (var clip in ctrl.animationClips)
                if (clip != null && clip.name == name) return clip.length;
            return 0f;
        }

        /// <summary>Hit flash, stealth fade and the like: multiplies every worn part's colour.</summary>
        public void SetTint(Color c)
        {
            _tint = c;
            foreach (var (r, baseColor) in _tintable)
                if (r != null) r.color = baseColor * c;
        }

        /// <summary>Lying by a campfire: the whole body on its side.</summary>
        public void SetLying(bool lying)
        {
            transform.localRotation = lying ? Quaternion.Euler(0f, 0f, _left ? -80f : 80f) : Quaternion.identity;
            transform.localPosition = lying
                ? new Vector3(_left ? 0.3f : -0.3f, -0.12f, 0f)
                : new Vector3(0f, -0.3f + FeetOffset * _scale, 0f);
        }
    }

    /// <summary>Receives the rig clips' animation events (nothing to do with them; damage is the combat code's).</summary>
    public class RigAnimationEvents : MonoBehaviour
    {
        public void OnAttackHit() { }
        public void OnSkillHit() { }
    }
}
