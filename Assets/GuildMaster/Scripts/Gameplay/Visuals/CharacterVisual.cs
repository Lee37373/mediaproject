using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Visuals
{
    /// <summary>
    /// Sprite presentation for moving characters: shadow, facing flip, walk bob,
    /// hit flash, and Y-based sorting so lower units draw in front.
    /// </summary>
    public class CharacterVisual : MonoBehaviour
    {
        /// <summary>
        /// Order of something standing at y = 0; one cell further north is 8 lower. On the 512 map (P4-81) the north
        /// edge is 4096 lower, so this must stay above that plus the ground (0) and the telegraphs (6): at 2000 every
        /// unit and building front north of y = 250 — our town too — was drawn under the ground. The overlays (fog,
        /// bars, effects) sit above the highest unit, see <see cref="Fog.FogOfWar.SortingOrder"/>. P4-85: 4200 → 4800 —
        /// the underground band above the surface reaches y = 588 (4704 lower), and the ruins' halls were under the ground.
        /// </summary>
        public const int BaseSortingOrder = 4800;

        SpriteRenderer _body;
        SpriteRenderer _shadow;
        Transform _bodyT;
        PathFollower _mover;
        float _bobPhase;
        float _flashTimer;
        Color _tint = Color.white;
        bool _facingLeftOverride;
        float _faceOverrideTimer;
        bool _shownLeft;

        public SpriteRenderer Body => _body;

        /// <summary>Times the sprite turned round (left ↔ right), never reset (a pooled unit keeps counting): read differences. Debug, P4-92.</summary>
        public int Turns { get; private set; }

        // ---- People drawn with the CharacterMaker rig (2026-10-30, see HumanRig; monsters use CharacterRig below) ----
        HumanRig _humanRig;
        Units.Unit _unit;
        float _attackStamp;
        Color _rigTint = Color.white;
        bool _rigLying;

        public HumanRig Rig => _humanRig;

        /// <summary>The arrow in its hands, drawn by the rig (P6-9), or null.</summary>
        public Sprite HeldArrow => _humanRig != null ? _humanRig.HeldArrow : null;

        /// <summary>
        /// Draws this character with the CharacterMaker rig (people only). False when that pack is not imported:
        /// the caller then uses the old art.
        /// </summary>
        public bool InitHuman(HumanLook look)
        {
            if (!ArtLibrary.HasHumans) return false;
            Init(null, 1f);
            var rig = HumanRig.Create(transform, look);
            if (rig == null) return false;
            _humanRig = rig;
            // The rig has its own shadow and parts; the plain body sprite stays empty.
            _shadow.enabled = false;
            _unit = GetComponent<Units.Unit>();
            _attackStamp = _unit != null ? _unit.NextAttackTime : 0f;
            return true;
        }

        /// <summary>Plays the rig's skill swing (no-op for sprite characters).</summary>
        public void PlaySkill()
        {
            if (_humanRig != null) _humanRig.PlaySkill();
            else if (_rig == null) _skillTime = 0f; // a single picture swells (P4-127)
        }

        // ---- Frame animation (imported art, see ArtLibrary) ----
        CharacterAnimSet _anim;
        float _animTime;
        bool _dying;
        bool _wasMoving;

        /// <summary>Art faces left by default (flip logic inverted).</summary>
        public bool ArtFacesLeft { get; set; }

        // ---- Rigged art (P4-110, see CharacterRig) ----
        CharacterRig _rig;

        /// <summary>Drawn by a rigged prefab (<see cref="InitRig"/>).</summary>
        public bool HasRig => _rig != null;

        /// <summary>Uses animated frames (idle/run loops, one-shot death) instead of a single sprite.</summary>
        public void InitAnimated(CharacterAnimSet set, float scale = 1f)
        {
            Init(set.Idle[0], scale);
            _anim = set;
            _animTime = 0f;
            _dying = false;
            // The frames carry their own ground shadow.
            _shadow.enabled = !set.HasShadow;
        }

        /// <summary>
        /// Drawn by a rigged prefab (P4-110) with its own shadow. The body keeps its still, never drawn, for what reads
        /// one sprite: a fog ghost's copy, the corpse's colour.
        /// </summary>
        public void InitRig(CharacterArtCatalog.MonsterRig art, float scale = 1f)
        {
            if (_rig != null && _rig.Prefab != art.prefab) ReleaseRig();
            if (_puppet != null) ReleasePuppet();
            InitBody(art.portrait, scale);
            _body.forceRenderingOff = true;
            _shadow.forceRenderingOff = true;
            if (_rig == null) _rig = new CharacterRig(art.prefab, _bodyT, _body.sharedMaterial, _body.sortingLayerID);
            else _rig.Reset();
        }

        void ReleaseRig()
        {
            _rig.Release();
            _rig = null;
        }

        /// <summary>Plays the death frames once (animated characters and rigs only).</summary>
        public void PlayDeath()
        {
            if (_humanRig != null) { _humanRig.PlayDeath(); return; }
            if (_rig != null) { _rig.PlayDeath(); return; }
            if (_anim == null || _anim.Die.Length == 0 || _dying) return;
            _dying = true;
            _animTime = 0f;
        }

        /// <summary>Swings once: a rig plays its clip, a single picture its drawn-in-code lunge (P4-127, <see cref="SpriteMotion"/>).</summary>
        public void PlayAttack()
        {
            if (_rig != null) _rig.PlayAttack();
            else _attackTime = 0f;
        }

        /// <summary>Flinches once (a rig's clip, or a single picture's squash).</summary>
        public void PlayHit()
        {
            if (_rig != null) _rig.PlayHit();
            else if (_attackTime < 0f) _hitTime = 0f; // a blow under way is not broken off by a flinch
        }

        // ---- Motions drawn in code for single pictures (P4-127) ----
        float _baseScale = 1f;
        float _attackTime = -1f, _hitTime = -1f, _skillTime = -1f, _breathPhase;

        /// <summary>The pose of a single picture now (no rig, no person rig); advances the motion clocks.</summary>
        SpritePose SpritePoseNow(bool moving, float bob)
        {
            float dt = Time.deltaTime;
            var pose = SpritePose.Identity;
            if (_unit != null && !_unit.IsAlive)
            {
                _attackTime = _hitTime = _skillTime = -1f; // a body lies still
                return pose;
            }
            if (_attackTime >= 0f)
            {
                pose *= SpriteMotion.Attack(_attackTime, _unit != null && _unit.Stats.IsRanged);
                _attackTime += dt;
                if (_attackTime >= SpriteMotion.AttackSeconds) _attackTime = -1f;
            }
            if (_hitTime >= 0f)
            {
                pose *= SpriteMotion.Hit(_hitTime);
                _hitTime += dt;
                if (_hitTime >= SpriteMotion.HitSeconds) _hitTime = -1f;
            }
            if (_skillTime >= 0f)
            {
                pose *= SpriteMotion.Skill(_skillTime);
                _skillTime += dt;
                if (_skillTime >= SpriteMotion.SkillSeconds) _skillTime = -1f;
            }
            // Frame-animated art breathes and steps in its own frames.
            if (_anim == null) pose *= moving ? SpriteMotion.Step(bob / 0.08f) : SpriteMotion.Breath(Time.time, _breathPhase);
            return pose;
        }

        void TickAnimation(bool moving)
        {
            if (moving != _wasMoving) { _wasMoving = moving; _animTime = 0f; }
            _animTime += Time.deltaTime;
            Sprite[] frames; float fps; bool loop = true;
            if (_dying) { frames = _anim.Die; fps = _anim.DieFps; loop = false; }
            else if (moving && _anim.Run.Length > 0) { frames = _anim.Run; fps = _anim.RunFps; }
            else { frames = _anim.Idle; fps = _anim.IdleFps; }
            int i = Mathf.FloorToInt(_animTime * fps);
            i = loop ? i % frames.Length : Mathf.Min(i, frames.Length - 1);
            _body.sprite = frames[i];
        }

        public void Init(Sprite sprite, float scale = 1f)
        {
            if (_rig != null) ReleaseRig();
            if (_puppet != null) ReleasePuppet();
            InitBody(sprite, scale);
        }

        // ---- A single picture in parts (P4-128) ----
        PartPuppet _puppet;
        float _stepPhase;

        /// <summary>
        /// Drawn from the picture's pack prefab in parts (P4-128), so the weapon swings and the legs step; the body keeps the
        /// picture, never drawn, for what reads one sprite (a fog ghost's copy, the corpse's colour), and its own shadow.
        /// </summary>
        public void InitPuppet(GameObject parts, Sprite still, float scale = 1f)
        {
            if (_rig != null) ReleaseRig();
            if (_puppet != null && _puppet.Prefab != parts) ReleasePuppet();
            InitBody(still, scale);
            _body.forceRenderingOff = true;
            if (_puppet == null) _puppet = new PartPuppet(parts, _bodyT, still, _body.sharedMaterial, _body.sortingLayerID);
            else _puppet.Reset();
        }

        void ReleasePuppet()
        {
            _puppet.Release();
            _puppet = null;
        }

        /// <summary>Drawn in parts (<see cref="InitPuppet"/>).</summary>
        public bool HasPuppet => _puppet != null;

        void InitBody(Sprite sprite, float scale)
        {
            if (_body == null)
            {
                var shadowGo = new GameObject("Shadow");
                shadowGo.transform.SetParent(transform, false);
                _shadow = shadowGo.AddComponent<SpriteRenderer>();
                _shadow.sprite = PixelArt.Shadow();
                shadowGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
                shadowGo.transform.localScale = new Vector3(1f, 0.45f, 1f);

                var bodyGo = new GameObject("Body");
                bodyGo.transform.SetParent(transform, false);
                _bodyT = bodyGo.transform;
                _body = bodyGo.AddComponent<SpriteRenderer>();
            }
            _body.sprite = sprite;
            _anim = null;
            SortingBoost = 0;
            if (_shadow) _shadow.enabled = true;
            _body.forceRenderingOff = false;
            _shadow.forceRenderingOff = false;
            _bodyT.localScale = Vector3.one * scale;
            _baseScale = scale;
            _attackTime = _hitTime = _skillTime = -1f;
            _breathPhase = (GetInstanceID() & 1023) / 1024f;
            _shadow.transform.localScale = new Vector3(scale, 0.45f * scale, 1f);
            _mover = GetComponent<PathFollower>();
            // Anchor feet slightly below the logical position (cell center).
            _bodyT.localPosition = new Vector3(0f, -0.3f, 0f);
            _shadow.transform.localPosition = new Vector3(0f, -0.28f, 0f);
            SetTint(Color.white);
        }

        /// <summary>Asleep by a campfire (P4-66): the body lies on its side, head away from the fire.</summary>
        public bool Lying { get; set; }

        /// <summary>
        /// Drawn this much above its row's usual order (P4-93: a flying dragon passes over what stands on the ground).
        /// Reset by <see cref="Init"/>; stays below the fog.
        /// </summary>
        public int SortingBoost { get; set; }

        /// <summary>Extra transparency on top of the tint (1 = solid). Used by stealth, P4-19.</summary>
        public float Fade { get; set; } = 1f;

        void UpdateRig(bool moving, bool left)
        {
            _humanRig.Face(left);
            _humanRig.SetMoving(moving);
            // A new attack started (Combat sets the next attack time when it swings): play the swing.
            if (_unit != null && _unit.NextAttackTime > _attackStamp + 0.01f)
            {
                _attackStamp = _unit.NextAttackTime;
                _humanRig.PlayAttack();
            }
            if (Lying != _rigLying)
            {
                _rigLying = Lying;
                _humanRig.SetLying(Lying);
            }
            _humanRig.SetSorting(BaseSortingOrder + SortingBoost - Mathf.RoundToInt(transform.position.y * 8f));
            Color want;
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                want = Shown(_flashTimer > 0f ? HitTint(new Color(1f, 0.45f, 0.45f, _tint.a)) : _tint);
            }
            else want = Shown(_tint);
            if (want != _rigTint)
            {
                _rigTint = want;
                _humanRig.SetTint(want);
            }
        }

        public void SetTint(Color c)
        {
            _tint = c;
            if (_body) _body.color = Shown(c);
        }

        /// <summary>A mark laid over the tint (P4-87: poison, chill, broken armour; white = none).</summary>
        public Color StatusTint { get; set; } = Color.white;

        Color Shown(Color c) => new(c.r * StatusTint.r, c.g * StatusTint.g, c.b * StatusTint.b, c.a * Mathf.Clamp01(Fade));

        public void Flash(float duration = 0.1f) => _flashTimer = duration;

        /// <summary>How far the hit tint goes with the settings' calmer flashes (P6-3): still shows who was struck.</summary>
        public const float CalmHitTint = 0.35f;

        /// <summary>The red of a hit, or a faint touch of it with calmer flashes (P6-3).</summary>
        Color HitTint(Color red) => Session.GameSettings.CalmFlashes ? Color.Lerp(_tint, red, CalmHitTint) : red;

        /// <summary>
        /// Within this many cells of straight above or below the target a unit keeps the way it faces (P4-92: skilled
        /// units side-stepping round a target crossed above and below it every few frames and the sprite flickered).
        /// </summary>
        public const float FaceDeadZone = 0.3f;

        /// <summary>
        /// A walking direction has to hold this long before the sprite turns to it (P4-124: a re-planned path whose first
        /// step went back a little turned a walker round and back within two frames).
        /// </summary>
        public const float WalkTurnDelay = 0.2f;

        Units.Unit _keepFacing;
        float _keepFacingUntil;
        float _walkTurnTime;

        /// <summary>
        /// Keeps facing <paramref name="target"/> (where it is now, as it moves) for <paramref name="seconds"/>, while it
        /// lives (P4-124: an archer shooting as it fell back turned to walk and back to shoot every arrow). A new call or a
        /// <see cref="FaceTowards"/> elsewhere replaces it.
        /// </summary>
        public void KeepFacing(Units.Unit target, float seconds)
        {
            _keepFacing = target;
            _keepFacingUntil = Time.time + seconds;
            if (target != null) FaceTowards(target.Position, keep: true);
        }

        /// <summary>Face toward a world x position for a moment (e.g. when attacking).</summary>
        public void FaceTowards(Vector2 target) => FaceTowards(target, keep: false);

        void FaceTowards(Vector2 target, bool keep)
        {
            if (!keep) _keepFacing = null;
            float dx = target.x - transform.position.x;
            if (Mathf.Abs(dx) > FaceDeadZone) _facingLeftOverride = dx < 0f;
            else if (_faceOverrideTimer <= 0f) _facingLeftOverride = _shownLeft;
            _faceOverrideTimer = 0.4f;
        }

        void LateUpdate()
        {
            if (_body == null) return;
            bool moving = _mover != null && (_mover.IsMoving || _mover.IsSteering);
            if (_keepFacing != null)
            {
                if (_keepFacing.IsAlive && Time.time < _keepFacingUntil) FaceTowards(_keepFacing.Position, keep: true);
                else _keepFacing = null;
            }
            // A walking direction counts once it has held a moment (P4-124).
            bool walkLeft = _shownLeft;
            if (moving && _mover != null && _mover.FacingLeft != _shownLeft)
            {
                _walkTurnTime += Time.deltaTime;
                if (_walkTurnTime >= WalkTurnDelay) walkLeft = _mover.FacingLeft;
            }
            else _walkTurnTime = 0f;
            // Looking somewhere on purpose (a blow, a target), else the way it walks — and standing still, the way it
            // last looked (P4-92: after each blow a unit turned back to where it had last walked, twice a swing).
            bool left = _faceOverrideTimer > 0f ? _facingLeftOverride : walkLeft;
            if (left != _shownLeft) { _shownLeft = left; Turns++; }
            _faceOverrideTimer -= Time.deltaTime;
            if (_humanRig != null) { UpdateRig(moving, left); return; }
            // Rigs face left (P4-110).
            _body.flipX = ArtFacesLeft || _rig != null ? !left : left;

            if (moving) _bobPhase += Time.deltaTime * 14f;
            else _bobPhase = Mathf.MoveTowards(_bobPhase, 0f, Time.deltaTime * 10f);
            // Animated art has its own motion; single sprites bob while walking.
            float bob = moving && _anim == null && _rig == null ? Mathf.Abs(Mathf.Sin(_bobPhase)) * 0.08f : 0f;
            if (_anim != null) TickAnimation(moving);
            // The parts follow the same blow and flinch as the whole (read before the pose moves the clocks on).
            float attackAt = _attackTime, hitAt = _hitTime;
            if (Lying)
            {
                _bodyT.localRotation = Quaternion.Euler(0f, 0f, left ? -80f : 80f);
                _bodyT.localPosition = new Vector3(left ? 0.3f : -0.3f, -0.12f, 0f);
                if (_rig == null) _bodyT.localScale = Vector3.one * _baseScale;
            }
            else if (_rig == null && !_dying)
            {
                // A single picture is moved in code (P4-127): breath, steps, blows, flinches, skills.
                var pose = SpritePoseNow(moving, bob);
                float dir = left ? -1f : 1f;
                _bodyT.localRotation = Quaternion.Euler(0f, 0f, -pose.Tilt * dir);
                _bodyT.localPosition = new Vector3(pose.Offset.x * dir, -0.3f + bob + pose.Offset.y, 0f);
                _bodyT.localScale = new Vector3(_baseScale * pose.Scale.x, _baseScale * pose.Scale.y, 1f);
            }
            else
            {
                _bodyT.localRotation = Quaternion.identity;
                _bodyT.localPosition = new Vector3(0f, -0.3f + bob, 0f);
                if (_rig == null) _bodyT.localScale = Vector3.one * _baseScale;
            }

            int order = BaseSortingOrder + SortingBoost - Mathf.RoundToInt(transform.position.y * 8f);
            _body.sortingOrder = order;
            _shadow.sortingOrder = order - 1;

            Color shown;
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                shown = Shown(_flashTimer > 0f ? HitTint(new Color(1f, 0.35f, 0.35f, _tint.a)) : _tint);
            }
            else shown = Shown(_tint);
            if (_body.color != shown) _body.color = shown;
            _rig?.Tick(moving, _body.flipX, order, shown);
            _puppet?.Tick(moving, _body.flipX, order, shown, attackAt, _unit != null && _unit.Stats.IsRanged, hitAt, _bobPhase,
                          still: Lying || _unit != null && !_unit.IsAlive);
        }
    }
}
