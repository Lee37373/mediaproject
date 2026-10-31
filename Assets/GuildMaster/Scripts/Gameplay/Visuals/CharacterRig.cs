using UnityEngine;
using UnityEngine.Rendering;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// A rigged monster from Minimal EnemyMonster (P4-110) played under a <see cref="CharacterVisual"/>'s body: the
    /// prefab's parts move by its Animator, whose five clips (Idle, Walk, Attack, Hit, Dead) all loop and have no
    /// transitions, so this picks the clip: walking or standing, a blow or a hit played once, death held on its
    /// last frame. Faces left; sorted as one piece (its SortingGroup); tinted part by part over what the clips
    /// set (the death clip fades some parts).
    /// </summary>
    public class CharacterRig
    {
        /// <summary>Prefab units → game units: an orc rookie stands about as tall as a goblin at the same scale.</summary>
        public const float WorldScale = 0.5f;

        static readonly int IdleHash = Animator.StringToHash("Idle");
        static readonly int WalkHash = Animator.StringToHash("Walk");
        static readonly int AttackHash = Animator.StringToHash("Attack");
        static readonly int HitHash = Animator.StringToHash("Hit");
        static readonly int DeadHash = Animator.StringToHash("Dead");

        enum Clip { Idle, Walk, Attack, Hit, Dead }

        public GameObject Prefab { get; }

        readonly GameObject _root;
        readonly Animator _animator;
        readonly SortingGroup _group;
        readonly SpriteRenderer[] _parts;
        readonly Color[] _prefabColors;
        /// <summary>Each part's colour before the tint: the prefab's, or what a clip last wrote.</summary>
        readonly Color[] _base;
        /// <summary>What this last wrote to each part, to tell a clip's write from its own.</summary>
        readonly Color[] _written;
        readonly float _attackLength, _hitLength, _deadLength;

        Clip _clip;
        /// <summary>When a blow or hit gives way to walking or standing, or the death pose is held.</summary>
        float _clipEnd;
        bool _frozen;

        public CharacterRig(GameObject prefab, Transform parent, Material material, int sortingLayerId)
        {
            Prefab = prefab;
            _root = Object.Instantiate(prefab, parent, false);
            _root.transform.localPosition = Vector3.zero;
            _root.transform.localRotation = Quaternion.identity;
            _animator = _root.GetComponent<Animator>();
            // Nothing is learnt from its pose: off screen or under the fog (renderers off) it need not move.
            _animator.cullingMode = AnimatorCullingMode.CullCompletely;
            // The attack clips mark the moment of the blow with events naming no function (an error each swing).
            _animator.fireEvents = false;
            _group = _root.GetComponent<SortingGroup>();
            if (_group == null) _group = _root.AddComponent<SortingGroup>();
            _group.sortingLayerID = sortingLayerId;
            _parts = _root.GetComponentsInChildren<SpriteRenderer>(true);
            _prefabColors = new Color[_parts.Length];
            _base = new Color[_parts.Length];
            _written = new Color[_parts.Length];
            for (int i = 0; i < _parts.Length; i++)
            {
                // The pack's material is unlit; the game's sprites take the day and night light.
                _parts[i].sharedMaterial = material;
                _prefabColors[i] = _parts[i].color;
            }
            foreach (var c in _animator.runtimeAnimatorController.animationClips)
            {
                if (c.name == "Attack") _attackLength = c.length;
                else if (c.name == "Hit") _hitLength = c.length;
                else if (c.name == "Dead") _deadLength = c.length;
            }
            Reset();
        }

        /// <summary>Standing, untinted, alive (a pooled body taking up the same picture again).</summary>
        public void Reset()
        {
            _animator.speed = 1f;
            _frozen = false;
            for (int i = 0; i < _parts.Length; i++)
            {
                _parts[i].color = _prefabColors[i];
                _base[i] = _written[i] = _prefabColors[i];
            }
            // Out of step with its pack: the idle starts somewhere of its own (no random draw, seeded worlds stay the same).
            Play(Clip.Idle, IdleHash, Mathf.Repeat(_root.GetInstanceID() * 0.618f, 1f));
        }

        public void PlayAttack()
        {
            if (_clip == Clip.Dead) return;
            Play(Clip.Attack, AttackHash, 0f);
            _clipEnd = Time.time + _attackLength;
        }

        /// <summary>Flinches, unless already flinching or in the middle of a blow (a brawl would keep it from swinging).</summary>
        public void PlayHit()
        {
            if (_clip == Clip.Dead || _clip == Clip.Attack || _clip == Clip.Hit) return;
            Play(Clip.Hit, HitHash, 0f);
            _clipEnd = Time.time + _hitLength;
        }

        public void PlayDeath()
        {
            if (_clip == Clip.Dead) return;
            Play(Clip.Dead, DeadHash, 0f);
            _clipEnd = Time.time + _deadLength;
        }

        void Play(Clip clip, int hash, float normalizedTime)
        {
            _clip = clip;
            // A body set up while pooled away starts from the controller's default state (Idle) when it wakes.
            if (_animator.isActiveAndEnabled) _animator.Play(hash, 0, normalizedTime);
        }

        /// <summary>Called after the Animator has moved the parts this frame (LateUpdate).</summary>
        public void Tick(bool moving, bool flip, int order, Color tint)
        {
            if (_clip == Clip.Dead)
            {
                // The clip loops: held just before its end.
                if (!_frozen && Time.time >= _clipEnd)
                {
                    _frozen = true;
                    Play(Clip.Dead, DeadHash, 0.999f);
                    _animator.speed = 0f;
                }
            }
            else if ((_clip != Clip.Attack && _clip != Clip.Hit) || Time.time >= _clipEnd)
            {
                var want = moving ? Clip.Walk : Clip.Idle;
                if (want != _clip) Play(want, moving ? WalkHash : IdleHash, 0f);
            }

            _root.transform.localScale = new Vector3(flip ? -WorldScale : WorldScale, WorldScale, 1f);
            _group.sortingOrder = order;

            for (int i = 0; i < _parts.Length; i++)
            {
                var p = _parts[i];
                var now = p.color;
                if (now != _written[i]) _base[i] = now;
                var shown = _base[i] * tint;
                if (shown != now) p.color = shown;
                _written[i] = shown;
            }
        }

        /// <summary>Gone at once (the body is being drawn as something else).</summary>
        public void Release()
        {
            _root.SetActive(false);
            _root.transform.SetParent(null, false);
            Object.Destroy(_root);
        }
    }
}
