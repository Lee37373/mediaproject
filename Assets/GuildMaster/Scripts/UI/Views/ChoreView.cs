using System.Collections.Generic;
using GuildMaster.Audio;
using GuildMaster.Core;
using GuildMaster.Economy;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The odd-jobs minigames (일손 돕기, user decision 2026-10-29): 한스's forge (timing), 마리's herb sorting (pairs)
    /// and 엘리's kneading (rhythm). Opened from the household window (살림). The game is paused while it is open and the
    /// minigames run on real time. Closing early still pays for what was done; each resident once a day.
    /// 2026-11-11 (user: "대장장이는 모루치는 느낌을 더, 성공하면 깡~ 실패하면 팅~"): each game is a little scene now —
    /// the forge with its anvil, swinging hammer, sparks, glowing billet and the quench in the bucket; herb cards that
    /// flip; dough that rolls in and squashes under the hand — with its own sounds, words that pop up, the resident's
    /// reactions and a stamped result. Art and sounds: Resources/UI/Chore, Resources/Audio/Sfx (forge_*, herb_*,
    /// dough_*, chore_*). The rules are unchanged (<see cref="ChoreRules"/>, ForgeGame, HerbGame, DoughGame).
    /// </summary>
    public class ChoreView : UIPanel
    {
        class Driver : MonoBehaviour
        {
            public ChoreView Owner;
            void Update() { if (Owner != null) Owner.DriverTick(); }
        }

        public override string Title => "일손 돕기";
        protected override float RefreshInterval => 0.1f;

        // The scene is drawn in the art's 1x pixels: 800 x 260, origin bottom left.
        const float SceneW = 800f, SceneH = 260f;

        static readonly Color Gold = new(1f, 0.84f, 0.35f), Spark = new(1f, 0.86f, 0.45f), Ash = new(0.72f, 0.72f, 0.76f);
        static readonly Color GoodText = new(0.62f, 0.95f, 0.6f), WarnText = new(1f, 0.72f, 0.38f), BadText = new(1f, 0.55f, 0.5f);

        Text _header, _progress, _howTo, _speech, _status;
        Button _action, _close;
        RectTransform _scene, _content, _forgeRoot, _herbRoot, _doughRoot, _fxLayer;
        Image _bg;

        // Forge
        Image _hearthGlow, _billetGlow, _billet, _blade, _hammer;
        RectTransform _gaugeTrack, _zone, _sweet, _needle;
        readonly List<Image> _pips = new();
        float _swingT = -1f, _pendingQ = -1f, _swingQ, _heat = 1f, _forged, _emberT, _finaleT = -1f;
        bool _quenchPlayed;

        // Herbs
        readonly List<HerbCard> _cards = new();
        Image _timeFill;

        // Dough
        Image _target, _hand;
        readonly List<Image> _balls = new();
        float[] _seen;
        float _pressT = -1f, _pressHold;
        int _combo, _bestCombo;

        // Result
        RectTransform _resultRoot, _stamp;
        Text _stampText, _resultText;
        float _resultT = -1f;

        readonly List<Fx> _fx = new();
        float _shake, _shakeMag, _flashUntil;
        string _flash = "";

        ChoreKind _kind;
        ForgeGame _forge;
        HerbGame _herbs;
        DoughGame _dough;
        float _missShownAt = -1f;
        bool _running, _paid, _holdingClock;

        // ---------------------------------------------------------------- building

        protected override void Build(RectTransform body)
        {
            var root = body.GetComponentInParent<Canvas>().rootCanvas.gameObject;
            root.AddComponent<Driver>().Owner = this;

            UIBuilder.VLayout(body, 6, 0);
            var head = UIBuilder.Row(body, 30, 10);
            _header = UIBuilder.Label(head, "", 19, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold).Size(-1, 30, 1);
            _progress = UIBuilder.Label(head, "", 15, TextAnchor.MiddleRight, UITheme.Text).Size(260, 30);

            // The scene: clipped, so sparks and rolling dough stay inside.
            var frame = UIBuilder.Panel(body, "Scene", new Color(0.08f, 0.06f, 0.07f), false);
            frame.Size(SceneW, SceneH);
            _scene = frame.rectTransform;
            frame.gameObject.AddComponent<RectMask2D>();
            _content = UIBuilder.Rect(_scene, "Content").Stretch();
            _bg = UIBuilder.SpriteImage(_content, "Bg", null, Color.white);

            BuildForge();
            BuildHerbs();
            BuildDough();
            _fxLayer = UIBuilder.Rect(_content, "Fx").Stretch();
            BuildResult();

            _speech = UIBuilder.Label(body, "", 15, TextAnchor.MiddleLeft).Size(-1, 24);
            _status = UIBuilder.Label(body, "", 15, TextAnchor.MiddleLeft, UITheme.TextMuted).Size(-1, 22);
            _howTo = UIBuilder.Label(body, "", 13, TextAnchor.UpperLeft, UITheme.TextMuted).Size(-1, 36);
            _howTo.verticalOverflow = VerticalWrapMode.Overflow;

            var row = UIBuilder.Row(body, 40, 10);
            _action = UIBuilder.Button(row, "두드리기 (F)", Action, 200, 38, UITheme.Button, 16);
            _close = UIBuilder.Button(row, "그만두기", Close, 140, 38, UITheme.ButtonDanger, 16);
        }

        static readonly Dictionary<string, Sprite> s_art = new();

        static Sprite Art(string name)
        {
            if (s_art.TryGetValue(name, out var s)) return s;
            s = Resources.Load<Sprite>("UI/Chore/" + name);
            s_art[name] = s;
            return s;
        }

        /// <summary>An image in the scene at (x, y) in 1x art pixels from the bottom left, placed by its pivot.</summary>
        static Image Put(Transform parent, string name, string art, float x, float y, float w, float h, Vector2? pivot = null, Color? color = null)
        {
            var img = UIBuilder.SpriteImage(parent, name, Art(art), color ?? Color.white);
            img.raycastTarget = false;
            img.rectTransform.Anchor(Vector2.zero, Vector2.zero, pivot ?? new Vector2(0.5f, 0.5f), new Vector2(x, y), new Vector2(w, h));
            return img;
        }

        static RectTransform Box(Transform parent, string name, Color color, float x, float y, float w, float h)
        {
            var p = UIBuilder.Panel(parent, name, color, false);
            p.rectTransform.Anchor(Vector2.zero, Vector2.zero, new Vector2(0, 0), new Vector2(x, y), new Vector2(w, h));
            return p.rectTransform;
        }

        // Forge layout (1x px): anvil at x 310, the billet on its face, the hammer's grip at the right.
        const float BilletX = 395f, BilletY = 143f, HammerX = 525f, HammerY = 176f, RaisedAngle = -34f, HitAngle = 2f;
        const float GaugeX = 150f, GaugeY = 12f, GaugeW = 480f, GaugeH = 18f; // on the floor, clear of the hammer
        static readonly Vector2 Bucket = new(720f, 128f);
        // 2026-11-19 (user: "칼이 물로 안 들어가고 바깥에 있어서 이상함"): the quench used to stop the blade's middle at the
        // water and draw the whole blade over the bucket's front. It now sinks this far under the surface and the part
        // below the water is cut off (the blade fills from the hilt), so it reads as plunged into the bucket.
        const float WaterY = 125f, DipDepth = 12f, BladeLen = 110f;

        void BuildForge()
        {
            _forgeRoot = UIBuilder.Rect(_content, "Forge").Stretch();
            _hearthGlow = Put(_forgeRoot, "HearthGlow", "glow", 125f, 110f, 300f, 240f, null, new Color(1f, 0.55f, 0.2f, 0.35f));
            Put(_forgeRoot, "Anvil", "anvil", 310f, 40f, 150f, 115f, Vector2.zero);
            _billetGlow = Put(_forgeRoot, "BilletGlow", "glow", BilletX, BilletY, 230f, 120f, null, new Color(1f, 0.55f, 0.15f, 0.7f));
            _billet = Put(_forgeRoot, "Billet", "billet", BilletX, BilletY, 85f, 22f);
            _blade = Put(_forgeRoot, "Blade", "blade", BilletX, BilletY + 2f, BladeLen, 30f);
            _blade.type = Image.Type.Filled;
            _blade.fillMethod = Image.FillMethod.Horizontal;
            _blade.fillOrigin = (int)Image.OriginHorizontal.Left; // the hilt end stays, the tip end goes under
            _blade.fillAmount = 1f;
            _blade.gameObject.SetActive(false);

            // The timing gauge above the anvil: a dark track, the zone, its sweet middle and the needle.
            Box(_forgeRoot, "GaugeFrame", new Color(0.05f, 0.04f, 0.05f, 0.92f), GaugeX - 6f, GaugeY - 6f, GaugeW + 12f, GaugeH + 12f);
            _gaugeTrack = Box(_forgeRoot, "GaugeTrack", new Color(0.24f, 0.12f, 0.1f), GaugeX, GaugeY, GaugeW, GaugeH);
            _zone = Box(_gaugeTrack, "Zone", new Color(0.95f, 0.5f, 0.15f), 0, 0, 10, GaugeH);
            _sweet = Box(_gaugeTrack, "Sweet", new Color(1f, 0.93f, 0.6f), 0, 0, 10, GaugeH);
            _needle = Box(_gaugeTrack, "Needle", Color.white, 0, -6f, 4f, GaugeH + 12f);
            for (int i = 0; i < ForgeGame.Strikes; i++)
            {
                var pip = Put(_forgeRoot, "Pip", "glow", GaugeX + GaugeW + 28f + i * 22f, GaugeY + GaugeH / 2f, 18f, 18f, null, new Color(1f, 1f, 1f, 0.25f));
                _pips.Add(pip);
            }
            _hammer = Put(_forgeRoot, "Hammer", "hammer", HammerX, HammerY, 150f, 52f, new Vector2(0.88f, 0.5f));
        }

        class HerbCard
        {
            public Image Card, Icon;
            public Text Name;
            public RectTransform Rt;
            public Button Button;
            public bool ShownUp;
            public float FlipT = -1f, PopT = -1f, ShakeT = -1f;
            public Vector2 Home;
        }

        const float CardW = 93f, CardH = 118f, CardGapX = 14f, CardGapY = 8f;

        void BuildHerbs()
        {
            _herbRoot = UIBuilder.Rect(_content, "Herbs").Stretch();
            int cols = 6;
            float x0 = (SceneW - (cols * CardW + (cols - 1) * CardGapX)) / 2f + CardW / 2f;
            float y0 = (SceneH - 10f - (2 * CardH + CardGapY)) / 2f + CardH / 2f;
            for (int i = 0; i < HerbGame.Pairs * 2; i++)
            {
                int slot = i;
                int r = i / cols, c = i % cols;
                var home = new Vector2(x0 + c * (CardW + CardGapX), y0 + (1 - r) * (CardH + CardGapY));
                var card = Put(_herbRoot, "Card", "card_back", home.x, home.y, CardW, CardH);
                card.raycastTarget = true;
                var b = card.gameObject.AddComponent<Button>();
                b.targetGraphic = card;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => PickHerb(slot));
                var icon = Put(card.transform, "Icon", "herb_0", CardW / 2f, CardH * 0.58f, 64f, 64f);
                var label = UIBuilder.Label(card.transform, "", 14, TextAnchor.MiddleCenter, new Color(0.3f, 0.2f, 0.1f), FontStyle.Bold);
                label.rectTransform.Anchor(Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(CardW / 2f, 20f), new Vector2(CardW, 20f));
                label.raycastTarget = false;
                _cards.Add(new HerbCard { Card = card, Icon = icon, Name = label, Rt = card.rectTransform, Button = b, Home = home });
            }
            Box(_herbRoot, "TimeTrack", new Color(0f, 0f, 0f, 0.45f), 20f, SceneH - 12f, SceneW - 40f, 6f);
            _timeFill = Box(_herbRoot, "TimeFill", new Color(0.5f, 0.85f, 0.4f), 20f, SceneH - 12f, SceneW - 40f, 6f).GetComponent<Image>();
        }

        const float HitX = 120f, LaneY = 112f, LaneEnd = 830f;

        void BuildDough()
        {
            _doughRoot = UIBuilder.Rect(_content, "Dough").Stretch();
            var lane = Box(_doughRoot, "Lane", new Color(1f, 0.97f, 0.9f, 0.16f), 0f, LaneY - 34f, SceneW, 68f);
            lane.SetAsFirstSibling();
            _target = Put(_doughRoot, "Target", "target_ring", HitX, LaneY, 88f, 88f, null, new Color(1f, 0.95f, 0.8f, 0.85f));
            for (int i = 0; i < DoughGame.Beats; i++)
            {
                var ball = Put(_doughRoot, "Ball", "dough_ball", LaneEnd, LaneY, 56f, 46f);
                ball.gameObject.SetActive(false);
                _balls.Add(ball);
            }
            _hand = Put(_doughRoot, "Hand", "hand", HitX, LaneY + 48f, 100f, 100f, new Vector2(0.45f, 0.11f)); // pivot at the fingertips (2026-11-12: a chubby hand, fingers toward the dough, no arm)
        }

        void BuildResult()
        {
            // a plain image, so the skin cannot swap its colour: the scene dims behind the stamp
            var dim = UIBuilder.SpriteImage(_content, "Result", null, new Color(0.05f, 0.03f, 0.05f, 0.8f));
            _resultRoot = dim.rectTransform;
            _resultRoot.Stretch();
            _stamp = UIBuilder.Rect(_resultRoot, "Stamp");
            _stamp.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 46f), new Vector2(420f, 80f));
            _stampText = UIBuilder.Label(_stamp, "", 52, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
            _stampText.rectTransform.Stretch();
            _stampText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ol = _stampText.gameObject.AddComponent<Outline>();
            ol.effectColor = new Color(0.1f, 0.05f, 0.05f, 0.95f);
            ol.effectDistance = new Vector2(3f, -3f);
            _resultText = UIBuilder.Label(_resultRoot, "", 17, TextAnchor.UpperCenter, Color.white);
            _resultText.rectTransform.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(720f, 110f));
            _resultText.verticalOverflow = VerticalWrapMode.Overflow;
            _resultText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            _resultRoot.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- starting and stopping

        /// <summary>Starts a job (the household window checks <see cref="ChoreService.CanDo"/> first).</summary>
        public void Show(ChoreKind kind)
        {
            if (!Services.TryGet<ChoreService>(out var service) || !Services.TryGet<GameClock>(out var clock)) return;
            EndRun();
            _kind = kind;
            _forge = kind == ChoreKind.Forge ? new ForgeGame(GameRandom.Shared) : null;
            _herbs = kind == ChoreKind.Herbs ? new HerbGame(GameRandom.Shared) : null;
            _dough = kind == ChoreKind.Dough ? new DoughGame(GameRandom.Shared) : null;
            _forgeRoot.gameObject.SetActive(_forge != null);
            _herbRoot.gameObject.SetActive(_herbs != null);
            _doughRoot.gameObject.SetActive(_dough != null);
            _bg.sprite = Art(kind == ChoreKind.Forge ? "forge_bg" : "dough_bg");
            _bg.color = kind == ChoreKind.Herbs ? new Color(0.72f, 0.8f, 0.66f) : Color.white;
            _action.gameObject.SetActive(kind != ChoreKind.Herbs);
            _action.SetLabel(kind == ChoreKind.Forge ? "두드리기 (F)" : "반죽 누르기 (F)");
            _close.SetLabel("그만두기");
            _header.text = $"{ChoreRules.Resident(kind)} — {ChoreRules.Title(kind)}";
            _howTo.text = ChoreRules.HowTo(kind);
            _speech.text = Say(ChoreRules.Greeting(kind));
            ClearFx();
            _flash = "";
            _missShownAt = -1f;
            _swingT = -1f; _pendingQ = -1f; _heat = 1f; _forged = 0f; _finaleT = -1f; _quenchPlayed = false;
            _pressT = -1f; _combo = 0; _bestCombo = 0;
            foreach (var p in _presses) p.Img.gameObject.SetActive(false);
            _seen = _dough != null ? new float[_dough.Notes.Count] : null;
            if (_seen != null) for (int i = 0; i < _seen.Length; i++) _seen[i] = float.NaN;
            foreach (var c in _cards) { c.ShownUp = false; c.FlipT = c.PopT = c.ShakeT = -1f; c.Card.sprite = Art("card_back"); c.Card.color = Color.white; }
            _billet.gameObject.SetActive(true);
            _billetGlow.gameObject.SetActive(true);
            _blade.gameObject.SetActive(false);
            _blade.fillAmount = 1f;
            _resultRoot.gameObject.SetActive(false);
            _resultT = -1f;
            service.Begin(kind, clock.TotalHours);
            _running = true;
            _paid = false;
            if (!_holdingClock) { clock.Hold(); _holdingClock = true; }
            Open();
            if (kind == ChoreKind.Forge) Sound.Play(SoundKeys.ForgeStart);
            Refresh();
        }

        bool Over => _forge?.Over ?? _herbs?.Over ?? _dough?.Over ?? true;
        float Score => _forge?.Score ?? _herbs?.Score ?? _dough?.Score ?? 0f;

        void DriverTick()
        {
            if (!Visible)
            {
                if (_running || _holdingClock) EndRun();
                return;
            }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float now = Time.unscaledTime;
            if (_running)
            {
                var kb = Keyboard.current;
                bool press = kb != null && kb.fKey.wasPressedThisFrame;
                if (_forge != null)
                {
                    if (!_forge.Over) _forge.Tick(dt);
                    if (press) Action();
                }
                else if (_herbs != null)
                {
                    _herbs.Tick(dt);
                    if (_herbs.Second != null && _missShownAt >= 0f && now - _missShownAt > 0.7f) { _herbs.HideMiss(); _missShownAt = -1f; }
                }
                else if (_dough != null)
                {
                    _dough.Tick(dt);
                    if (press) Action();
                    CheckMissedNotes();
                }
                if (Over)
                {
                    // The forge ends with the quench; the others pay at once.
                    if (_forge != null) { if (_finaleT < 0f && _swingT < 0f) _finaleT = 0f; }
                    else Pay();
                }
            }
            Animate(dt, now);
        }

        void Action()
        {
            if (!_running) return;
            if (_forge != null)
            {
                if (_forge.Over || _swingT >= 0f && _swingT < SwingDown) return; // one blow at a time
                float q = _forge.Strike();
                _swingT = 0f;
                _pendingQ = q;
                _swingQ = q;
            }
            else if (_dough != null)
            {
                int hit = -1;
                var before = new bool[_dough.Notes.Count];
                for (int i = 0; i < before.Length; i++) before[i] = float.IsNaN(_dough.Result(i));
                float q = _dough.Press();
                for (int i = 0; i < before.Length; i++) if (before[i] && !float.IsNaN(_dough.Result(i))) { hit = i; _seen[i] = _dough.Result(i); }
                _pressT = 0f;
                _pressHold = q > 0f ? PressHoldHand : 0f;
                DoughLanded(q, hit);
            }
            Refresh();
        }

        void PickHerb(int slot)
        {
            if (!_running || _herbs == null) return;
            var r = _herbs.Pick(slot);
            if (r == HerbGame.Flip.Ignored) return;
            Sound.Play(SoundKeys.HerbFlip);
            var card = _cards[slot];
            if (r == HerbGame.Flip.Miss)
            {
                _missShownAt = Time.unscaledTime;
                Sound.Play(SoundKeys.HerbMiss, 0.9f);
                foreach (var i in new[] { _herbs.First, _herbs.Second })
                    if (i.HasValue) _cards[i.Value].ShakeT = 0f;
                Flash(UIBuilder.Colored("MISS", BadText));
                _speech.text = Say(ChoreRules.Cheer(_kind, 0f, Random.Range(0, 8)));
                FloatText(card.Home + new Vector2(0f, 40f), "MISS", 24, BadText, -25f);
            }
            else if (r == HerbGame.Flip.Match)
            {
                Sound.Play(SoundKeys.HerbMatch);
                for (int i = 0; i < _cards.Count; i++)
                    if (_herbs.IsMatched(i) && _cards[i].PopT < 0f && _cards[i].Card.color == Color.white)
                    {
                        _cards[i].PopT = 0f;
                        Burst(_cards[i].Home, 8, new Color(0.7f, 1f, 0.6f), 110f, 0.6f, 9f);
                    }
                Flash(UIBuilder.Colored("MATCH", GoodText));
                _speech.text = Say(ChoreRules.Cheer(_kind, 1f, Random.Range(0, 8)));
                FloatText(card.Home + new Vector2(0f, 50f), "MATCH!", 28, GoodText, 50f);
            }
            Refresh();
        }

        void Flash(string text)
        {
            _flash = text;
            _flashUntil = Time.unscaledTime + 0.8f;
        }

        string Say(string line) => string.IsNullOrEmpty(line) ? "" : UIBuilder.Colored(ChoreRules.Resident(_kind) + ":", UITheme.Accent) + " \"" + line + "\"";

        void Pay()
        {
            if (_paid || !Services.TryGet<ChoreService>(out var service)) return;
            _paid = true;
            _running = false;
            float score = Score;
            var (pay, opinion) = service.Complete(_kind, score);
            _action.gameObject.SetActive(false);
            _close.SetLabel("닫기");
            ShowResult(score, pay, opinion);
            Refresh();
        }

        /// <summary>Closing mid-game pays for what was done so far.</summary>
        void EndRun()
        {
            if (_running) Pay();
            _running = false;
            if (_holdingClock && Services.TryGet<GameClock>(out var clock)) clock.Release();
            _holdingClock = false;
        }

        void ShowResult(float score, int pay, int opinion)
        {
            _resultRoot.gameObject.SetActive(true);
            _resultRoot.SetAsLastSibling();
            _fxLayer.SetAsLastSibling(); // the celebration sparks over the dimmed scene
            _resultT = 0f;
            _stampText.text = StampWord(score);
            _stampText.color = score >= 0.9f ? Gold : score >= 0.6f ? GoodText : score >= 0.3f ? WarnText : BadText;
            string gold = UIBuilder.Colored($"+{pay}G", Gold);
            string op = opinion > 0 ? $"  ·  {ChoreRules.Resident(_kind)}의 길드 평판 +{opinion}" : "";
            string combo = _dough != null && _bestCombo > 1 ? $"  ·  MAX COMBO {_bestCombo}" : "";
            _resultText.text = $"{score * 100f:0}점  ·  {gold}{op}{combo}\n\n\"{ChoreRules.Thanks(_kind, score)}\"";
            _speech.text = "";
            if (Visible) Sound.Play(SoundRules.ChoreResultKey(score)); // not after the window was closed mid-job
            if (score >= 0.6f) Burst(new Vector2(SceneW / 2f, SceneH / 2f + 46f), 22, Gold, 260f, 0.9f, 12f);
        }

        /// <summary>
        /// The stamp at the end, in English like the pop-ups (user, 2026-11-12: "완벽!" read stiff and machine-made;
        /// PERFECT / GOOD / BAD instead). Same edges as <see cref="ChoreRules.Grade"/>, which the log keeps in Korean.
        /// </summary>
        static string StampWord(float score) => score >= 0.9f ? "PERFECT!" : score >= 0.6f ? "GOOD!" : score >= 0.3f ? "NOT BAD" : "BAD...";

        // ---------------------------------------------------------------- the forge

        const float SwingDown = 0.07f, SwingHold = 0.05f, SwingBack = 0.28f;

        void ForgeLanded(float q)
        {
            var at = new Vector2(BilletX + 18f, BilletY + 12f);
            Sound.Play(SoundRules.ChoreHitKey(ChoreKind.Forge, q));
            _speech.text = Say(ChoreRules.Cheer(_kind, q, Random.Range(0, 8)));
            if (q >= 1f)
            {
                Burst(at, 20, Spark, 330f, 0.7f, 8f);
                Ring(at, Gold, 150f);
                Shake(0.18f, 7f);
                FloatText(at + new Vector2(0f, 34f), "PERFECT!", 34, Gold, 70f);
                _heat = Mathf.Min(1f, _heat + 0.18f);
                _forged += 1f;
                Flash(UIBuilder.Colored("PERFECT", Gold));
            }
            else if (q >= 0.6f)
            {
                Burst(at, 10, Spark, 250f, 0.55f, 7f);
                Ring(at, new Color(1f, 0.9f, 0.6f, 0.8f), 100f);
                Shake(0.12f, 4f);
                FloatText(at + new Vector2(0f, 30f), "GOOD", 28, GoodText, 60f);
                _heat = Mathf.Min(1f, _heat + 0.1f);
                _forged += 0.6f;
                Flash(UIBuilder.Colored("GOOD", GoodText));
            }
            else if (q > 0f)
            {
                Burst(at, 4, new Color(1f, 0.55f, 0.25f), 150f, 0.4f, 6f);
                Shake(0.08f, 2f);
                FloatText(at + new Vector2(0f, 26f), "BAD", 24, WarnText, 45f);
                _forged += 0.2f;
                Flash(UIBuilder.Colored("BAD", WarnText));
            }
            else
            {
                Puff(at + new Vector2(10f, 6f), 4, Ash, 0.5f);
                FloatText(at + new Vector2(16f, 24f), "MISS", 24, Ash, -30f);
                _heat = Mathf.Max(0f, _heat - 0.12f);
                Flash(UIBuilder.Colored("MISS", BadText));
            }
        }

        void AnimateForge(float dt, float now)
        {
            // The hearth flickers and sends up embers.
            float flick = 0.3f + 0.08f * Mathf.Sin(now * 7.3f) + 0.06f * Mathf.Sin(now * 13.1f + 1f);
            _hearthGlow.color = new Color(1f, 0.55f, 0.2f, flick);
            _emberT -= dt;
            if (_emberT <= 0f)
            {
                _emberT = Random.Range(0.12f, 0.3f);
                var e = Spawn(Art("spark"), new Vector2(Random.Range(60f, 190f), Random.Range(70f, 110f)), 6f,
                    new Color(1f, Random.Range(0.4f, 0.7f), 0.2f), Random.Range(1.2f, 1.9f));
                e.Vel = new Vector2(Random.Range(-12f, 12f), Random.Range(28f, 55f));
                e.Gravity = -6f;
            }

            // The hammer: raised and bobbing, a fast drop, a short rest on the steel, back up (a glancing blow bounces).
            float angle = RaisedAngle + 3f * Mathf.Sin(now * 2.2f);
            if (_swingT >= 0f)
            {
                _swingT += dt;
                bool miss = _swingQ <= 0f;
                if (_swingT < SwingDown) angle = Mathf.Lerp(RaisedAngle, HitAngle, Ease.In(_swingT / SwingDown));
                else
                {
                    if (_pendingQ >= -0.5f) { ForgeLanded(_pendingQ); _pendingQ = -1f; }
                    float t = _swingT - SwingDown;
                    if (t < SwingHold) angle = HitAngle + (miss ? -10f * Mathf.Sin(t / SwingHold * Mathf.PI) : 0f);
                    else if (t < SwingHold + SwingBack)
                    {
                        float u = (t - SwingHold) / SwingBack;
                        angle = Mathf.Lerp(HitAngle, RaisedAngle, Ease.OutBack(u, miss ? 2.6f : 1.2f));
                    }
                    else _swingT = -1f;
                }
            }
            _hammer.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);

            // The billet: white-hot cooling to red, drawn out a little by every good blow.
            if (_finaleT < 0f) _heat = Mathf.Max(0.15f, _heat - dt * 0.06f);
            var col = _heat > 0.6f ? Color.Lerp(new Color(1f, 0.62f, 0.18f), new Color(1f, 0.96f, 0.78f), (_heat - 0.6f) / 0.4f)
                : Color.Lerp(new Color(0.62f, 0.16f, 0.1f), new Color(1f, 0.62f, 0.18f), _heat / 0.6f);
            _billet.color = col;
            _billetGlow.color = new Color(1f, 0.5f, 0.15f, 0.25f + 0.55f * _heat + 0.05f * Mathf.Sin(now * 9f));
            _billet.rectTransform.localScale = new Vector3(1f + 0.07f * _forged, 1f - 0.035f * _forged, 1f);

            // The gauge.
            if (_forge != null)
            {
                float c = _forge.ZoneCenter, w = _forge.ZoneWidth;
                SetSpan(_zone, Mathf.Clamp01(c - w), Mathf.Clamp01(c + w));
                SetSpan(_sweet, Mathf.Clamp01(c - w * 0.25f), Mathf.Clamp01(c + w * 0.25f));
                _needle.anchoredPosition = new Vector2(_forge.Needle * GaugeW - 2f, -6f);
                bool inSweet = Mathf.Abs(_forge.Needle - c) <= w * 0.25f;
                _sweet.GetComponent<Image>().color = inSweet ? new Color(1f, 1f, 0.85f) : new Color(1f, 0.9f, 0.55f);
                for (int i = 0; i < _pips.Count; i++)
                {
                    var hits = _forge.Hits;
                    _pips[i].color = i < hits.Count ? QualityColor(hits[i]) : i == hits.Count && _running ? new Color(1f, 1f, 1f, 0.5f + 0.3f * Mathf.Sin(now * 6f)) : new Color(1f, 1f, 1f, 0.2f);
                }
            }

            // The quench: the finished blade swings to the bucket and hisses.
            if (_finaleT >= 0f && !_paid)
            {
                _finaleT += dt;
                const float Wait = 0.45f, Move = 0.6f, Dip = 0.25f, Steam = 0.9f;
                if (_finaleT > Wait)
                {
                    _billet.gameObject.SetActive(false);
                    _blade.gameObject.SetActive(true);
                    float u = Mathf.Clamp01((_finaleT - Wait) / Move);
                    var start = new Vector2(BilletX, BilletY + 2f);
                    var top = Bucket + new Vector2(0f, 70f);
                    var p = Vector2.Lerp(start, top, Ease.InOut(u)) + new Vector2(0f, 50f * Mathf.Sin(u * Mathf.PI));
                    float rot = Mathf.Lerp(0f, -80f, Ease.InOut(u));
                    if (_finaleT > Wait + Move)
                    {
                        float v = Mathf.Clamp01((_finaleT - Wait - Move) / Dip);
                        p = Vector2.Lerp(top, Bucket - new Vector2(0f, DipDepth), Ease.In(v));
                        rot = -80f;
                        if (!_quenchPlayed)
                        {
                            _quenchPlayed = true;
                            Sound.Play(SoundKeys.ForgeQuench);
                        }
                    }
                    _blade.rectTransform.anchoredPosition = p;
                    _blade.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rot);
                    // Only what is above the water shows: along the blade (hilt at -L/2, tip at +L/2) a point sits
                    // at p.y + x·sin(rot); with the blade pointing down the cut is where that meets the surface.
                    float down = -Mathf.Sin(rot * Mathf.Deg2Rad);
                    _blade.fillAmount = down > 0.1f
                        ? Mathf.Clamp01(((p.y - WaterY) / down + BladeLen * 0.5f) / BladeLen)
                        : 1f;
                    _blade.color = Color.Lerp(new Color(1f, 0.6f, 0.3f), Color.white, Mathf.Clamp01((_finaleT - Wait - Move) / Dip));
                    _billetGlow.rectTransform.anchoredPosition = p;
                    _billetGlow.color = new Color(1f, 0.5f, 0.15f, 0.6f * (1f - Mathf.Clamp01((_finaleT - Wait - Move) / Dip)));
                    if (_quenchPlayed && _finaleT < Wait + Move + Dip + Steam && Random.value < 0.5f)
                    {
                        var s = Spawn(Art("glow"), Bucket + new Vector2(Random.Range(-30f, 30f), 10f), Random.Range(30f, 50f),
                            new Color(1f, 1f, 1f, 0.45f), Random.Range(0.8f, 1.3f));
                        s.Vel = new Vector2(Random.Range(-15f, 15f), Random.Range(50f, 90f));
                        s.Grow = 1.8f;
                    }
                    if (_finaleT > Wait + Move + Dip + Steam * 0.6f) Pay();
                }
            }
            else if (_finaleT < 0f)
            {
                _blade.gameObject.SetActive(false);
                _billet.gameObject.SetActive(true);
                _billetGlow.rectTransform.anchoredPosition = new Vector2(BilletX, BilletY);
            }
        }

        static Color QualityColor(float q) => q >= 1f ? Gold : q >= 0.6f ? GoodText : q > 0f ? WarnText : new Color(0.5f, 0.5f, 0.55f);

        void SetSpan(RectTransform rt, float a, float b)
        {
            rt.anchoredPosition = new Vector2(a * GaugeW, 0f);
            rt.sizeDelta = new Vector2(Mathf.Max(2f, (b - a) * GaugeW), GaugeH);
        }

        // ---------------------------------------------------------------- the herbs

        void AnimateHerbs(float dt)
        {
            if (_herbs == null) return;
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                bool up = _herbs.IsFaceUp(i);
                if (up != c.ShownUp && c.FlipT < 0f) c.FlipT = 0f;
                float sx = 1f, scale = 1f;
                if (c.FlipT >= 0f)
                {
                    c.FlipT += dt;
                    const float Half = 0.09f;
                    if (c.FlipT < Half) sx = 1f - c.FlipT / Half;
                    else
                    {
                        if (c.ShownUp != up)
                        {
                            c.ShownUp = up;
                            c.Card.sprite = Art(up ? "card_face" : "card_back");
                            c.Icon.sprite = Art("herb_" + System.Array.IndexOf(HerbGame.Herbs, _herbs.HerbAt(i)));
                            c.Name.text = _herbs.HerbAt(i);
                        }
                        sx = Mathf.Min(1f, (c.FlipT - Half) / Half);
                        if (c.FlipT >= Half * 2f) c.FlipT = -1f;
                    }
                }
                c.Icon.gameObject.SetActive(c.ShownUp);
                c.Name.gameObject.SetActive(c.ShownUp);
                if (c.PopT >= 0f)
                {
                    c.PopT += dt;
                    scale = 1f + 0.18f * Mathf.Sin(Mathf.Clamp01(c.PopT / 0.3f) * Mathf.PI);
                    if (c.PopT > 0.3f) { c.PopT = -1f; c.Card.color = new Color(0.78f, 1f, 0.74f); }
                }
                float dx = 0f;
                if (c.ShakeT >= 0f)
                {
                    c.ShakeT += dt;
                    dx = 6f * Mathf.Sin(c.ShakeT * 60f) * (1f - c.ShakeT / 0.35f);
                    if (c.ShakeT > 0.35f) c.ShakeT = -1f;
                }
                if (!_herbs.IsMatched(i) && c.PopT < 0f)
                    c.Card.color = c.ShakeT >= 0f ? new Color(1f, 0.72f, 0.7f) : Color.white;
                c.Rt.anchoredPosition = c.Home + new Vector2(dx, 0f);
                c.Rt.localScale = new Vector3(sx * scale, scale, 1f);
                c.Button.interactable = _running && !_herbs.IsMatched(i);
            }
            float left = _herbs.TimeLeft / HerbGame.TimeLimit;
            var fill = _timeFill.rectTransform;
            fill.sizeDelta = new Vector2((SceneW - 40f) * left, 6f);
            _timeFill.color = left > 0.4f ? new Color(0.5f, 0.85f, 0.4f) : left > 0.2f ? new Color(1f, 0.7f, 0.3f) : new Color(1f, 0.4f, 0.35f);
        }

        // ---------------------------------------------------------------- the dough

        void DoughLanded(float q, int note)
        {
            var at = new Vector2(HitX, LaneY);
            Sound.Play(SoundRules.ChoreHitKey(ChoreKind.Dough, q), q > 0f && q < 0.6f ? 0.7f : 1f);
            if (q > 0f)
            {
                _combo++;
                _bestCombo = Mathf.Max(_bestCombo, _combo);
                StartPress();
                Puff(at + new Vector2(0f, -4f), q >= 1f ? 7 : 4, new Color(1f, 0.98f, 0.92f), 0.55f);
                if (q >= 1f) Ring(at, new Color(1f, 0.95f, 0.75f), 110f);
                Shake(0.07f, q >= 1f ? 3f : 1.5f);
                string word = q >= 1f ? "PERFECT!" : q >= 0.6f ? "GOOD" : "BAD";
                FloatText(at + new Vector2(64f, 40f), word, q >= 1f ? 28 : 24, q >= 1f ? Gold : q >= 0.6f ? GoodText : WarnText, 55f);
                Flash(UIBuilder.Colored(word, q >= 0.6f ? GoodText : WarnText));
            }
            else
            {
                _combo = 0;
                Puff(at + new Vector2(0f, -4f), 2, Ash, 0.4f);
                FloatText(at + new Vector2(64f, 36f), "MISS", 22, Ash, -25f);
                Flash(UIBuilder.Colored("MISS", BadText));
            }
            if (q != 0f && Random.value < 0.45f || q < 0f) _speech.text = Say(ChoreRules.Cheer(_kind, Mathf.Max(0f, q), Random.Range(0, 8)));
        }

        // A hit dough ball squashes under the hand into a patty with finger dents (2026-11-12, user: "눌렀을 때 빵이
        // 좀 눌리는 이미지면 좋겠어"), wobbles, rests a moment and fades.
        class Pressed
        {
            public Image Img;
            public float T;
        }

        readonly List<Pressed> _presses = new();
        const float PressSquash = 0.05f, PressRest = 0.42f, PressFade = 0.3f, PressHoldHand = 0.1f;
        const float BallBottom = LaneY - 23f;

        void StartPress()
        {
            Pressed p = null;
            foreach (var x in _presses) if (!x.Img.gameObject.activeSelf) { p = x; break; }
            if (p == null)
            {
                p = new Pressed { Img = Put(_doughRoot, "Pressed", "dough_ball", HitX, BallBottom, 56f, 46f, new Vector2(0.5f, 0f)) };
                _presses.Add(p);
            }
            p.T = 0f;
            p.Img.gameObject.SetActive(true);
            p.Img.color = Color.white;
            p.Img.transform.SetSiblingIndex(_hand.transform.GetSiblingIndex()); // under the hand, over the rolling balls
            UpdatePress(p);
        }

        void AnimatePresses(float dt)
        {
            foreach (var p in _presses)
            {
                if (!p.Img.gameObject.activeSelf) continue;
                p.T += dt;
                UpdatePress(p);
            }
        }

        void UpdatePress(Pressed p)
        {
            var rt = p.Img.rectTransform;
            if (p.T < PressSquash)
            {
                // the round ball giving way
                float u = p.T / PressSquash;
                p.Img.sprite = Art("dough_ball");
                rt.sizeDelta = Vector2.Lerp(new Vector2(56f, 46f), new Vector2(78f, 32f), u);
                return;
            }
            // the pressed patty, a small wobble as it settles
            float t = p.T - PressSquash;
            float wobble = Mathf.Exp(-t * 12f) * Mathf.Sin(t * 42f);
            p.Img.sprite = Art("dough_pressed");
            rt.sizeDelta = new Vector2(92f * (1f + 0.06f * wobble), 42f * (1f - 0.1f * wobble));
            float fade = t > PressRest ? (t - PressRest) / PressFade : 0f;
            p.Img.color = new Color(1f, 1f, 1f, 1f - Mathf.Clamp01(fade));
            if (fade >= 1f) p.Img.gameObject.SetActive(false);
        }

        /// <summary>A note that went by unpressed: a small "MISS".</summary>
        void CheckMissedNotes()
        {
            for (int i = 0; i < _seen.Length; i++)
            {
                float r = _dough.Result(i);
                if (!float.IsNaN(_seen[i]) || float.IsNaN(r)) continue;
                _seen[i] = r;
                if (r > 0f) continue;
                _combo = 0;
                Sound.Play(SoundKeys.DoughMiss, 0.7f);
                FloatText(new Vector2(HitX + 64f, LaneY + 36f), "MISS", 20, BadText, -20f);
            }
        }

        void AnimateDough(float dt)
        {
            if (_dough == null) return;
            var notes = _dough.Notes;
            for (int i = 0; i < _balls.Count; i++)
            {
                var img = _balls[i];
                bool show = i < notes.Count && float.IsNaN(_dough.Result(i));
                float ahead = show ? notes[i] - _dough.Time : 0f;
                show &= ahead <= DoughGame.Lead && ahead >= -DoughGame.Window;
                img.gameObject.SetActive(show);
                if (!show) continue;
                float x = HitX + ahead / DoughGame.Lead * (LaneEnd - HitX);
                img.rectTransform.anchoredPosition = new Vector2(x, LaneY + 2f * Mathf.Abs(Mathf.Sin(x * 0.08f)));
                img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, x * 1.4f);
            }
            // The ring pulses on every beat so the rhythm can be seen.
            float since = Mathf.Repeat(_dough.Time - DoughGame.Start, DoughGame.Beat);
            float pulse = _dough.Time >= DoughGame.Start - 0.01f ? Mathf.Exp(-since * 9f) : 0f;
            _target.rectTransform.localScale = Vector3.one * (1f + 0.14f * pulse);
            _target.color = new Color(1f, 0.95f, 0.8f, 0.55f + 0.4f * pulse);
            // The hand comes down on a press.
            float drop = 0f;
            if (_pressT >= 0f)
            {
                _pressT += dt;
                drop = _pressT < 0.05f ? _pressT / 0.05f
                    : _pressT < 0.05f + _pressHold ? 1f                       // rests on the dough a moment after a hit
                    : Mathf.Max(0f, 1f - (_pressT - 0.05f - _pressHold) / 0.16f);
                if (_pressT > 0.25f + _pressHold) _pressT = -1f;
            }
            AnimatePresses(dt);
            _hand.rectTransform.anchoredPosition = new Vector2(HitX, LaneY + 48f - 38f * drop + 3f * Mathf.Sin(Time.unscaledTime * 3f));
            _hand.rectTransform.localScale = new Vector3(1f + 0.06f * drop, 1f - 0.06f * drop, 1f);
        }

        // ---------------------------------------------------------------- effects

        class Fx
        {
            public Image Img;
            public Text Txt;
            public RectTransform Rt;
            public float T, Life, Gravity, Spin, Grow;
            public Vector2 Vel, Size;
            public Color Color;
            public bool Stretch;
        }

        readonly Stack<Image> _imgPool = new();
        readonly Stack<Text> _txtPool = new();

        Fx Spawn(Sprite sprite, Vector2 at, float size, Color color, float life)
        {
            var img = _imgPool.Count > 0 ? _imgPool.Pop() : UIBuilder.SpriteImage(_fxLayer, "Fx", null, Color.white);
            img.gameObject.SetActive(true);
            img.sprite = sprite;
            img.raycastTarget = false;
            img.preserveAspect = false;
            var rt = img.rectTransform;
            rt.Anchor(Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), at, new Vector2(size, size));
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            var fx = new Fx { Img = img, Rt = rt, Life = life, Color = color, Size = new Vector2(size, size) };
            img.color = color;
            _fx.Add(fx);
            return fx;
        }

        void FloatText(Vector2 at, string text, int size, Color color, float rise)
        {
            Text t;
            if (_txtPool.Count > 0) t = _txtPool.Pop();
            else
            {
                t = UIBuilder.Label(_fxLayer, "", size, TextAnchor.MiddleCenter, color, FontStyle.Bold);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.raycastTarget = false;
                var ol = t.gameObject.AddComponent<Outline>();
                ol.effectColor = new Color(0.08f, 0.04f, 0.04f, 0.9f);
                ol.effectDistance = new Vector2(2f, -2f);
            }
            t.gameObject.SetActive(true);
            t.text = text;
            t.fontSize = size;
            t.color = color;
            var rt = t.rectTransform;
            rt.Anchor(Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f), at, new Vector2(200f, 50f));
            rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-6f, 6f));
            rt.localScale = Vector3.one * 1.4f;
            _fx.Add(new Fx { Txt = t, Rt = rt, Life = 0.75f, Vel = new Vector2(0f, rise), Color = color, Grow = -1f });
        }

        void Burst(Vector2 at, int count, Color color, float speed, float life, float size)
        {
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(15f, 165f) * Mathf.Deg2Rad;
                float v = speed * Random.Range(0.45f, 1f);
                var s = Spawn(Art("spark"), at, size * Random.Range(0.6f, 1.2f), Color.Lerp(color, Color.white, Random.Range(0f, 0.4f)), life * Random.Range(0.6f, 1f));
                s.Vel = new Vector2(Mathf.Cos(a) * v, Mathf.Sin(a) * v);
                s.Gravity = 520f;
                s.Spin = Random.Range(-400f, 400f);
            }
        }

        void Puff(Vector2 at, int count, Color color, float life)
        {
            for (int i = 0; i < count; i++)
            {
                var s = Spawn(Art("glow"), at + new Vector2(Random.Range(-14f, 14f), Random.Range(-4f, 6f)), Random.Range(16f, 26f),
                    new Color(color.r, color.g, color.b, 0.7f), life * Random.Range(0.7f, 1f));
                s.Vel = new Vector2(Random.Range(-60f, 60f), Random.Range(10f, 50f));
                s.Grow = 1.6f;
            }
        }

        void Ring(Vector2 at, Color color, float size)
        {
            var r = Spawn(Art("ring"), at, size * 0.3f, color, 0.3f);
            r.Grow = size / (size * 0.3f) - 1f;
        }

        void Shake(float seconds, float magnitude)
        {
            _shake = Mathf.Max(_shake, seconds);
            _shakeMag = Mathf.Max(_shakeMag, magnitude);
        }

        void ClearFx()
        {
            foreach (var f in _fx) Recycle(f);
            _fx.Clear();
            _shake = 0f;
            _content.anchoredPosition = Vector2.zero;
        }

        void Recycle(Fx f)
        {
            if (f.Img != null) { f.Img.gameObject.SetActive(false); _imgPool.Push(f.Img); }
            if (f.Txt != null) { f.Txt.gameObject.SetActive(false); _txtPool.Push(f.Txt); }
        }

        void Animate(float dt, float now)
        {
            if (_forge != null) AnimateForge(dt, now);
            AnimateHerbs(dt);
            AnimateDough(dt);

            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                f.T += dt;
                float u = f.T / f.Life;
                if (u >= 1f) { Recycle(f); _fx.RemoveAt(i); continue; }
                f.Vel.y -= f.Gravity * dt;
                f.Rt.anchoredPosition += f.Vel * dt;
                if (f.Img != null)
                {
                    f.Rt.localRotation = Quaternion.Euler(0f, 0f, f.Rt.localEulerAngles.z + f.Spin * dt);
                    f.Rt.sizeDelta = f.Size * (1f + f.Grow * u);
                    f.Img.color = new Color(f.Color.r, f.Color.g, f.Color.b, f.Color.a * (1f - u * u));
                }
                else
                {
                    // words pop in big, settle, rise and fade
                    float s = u < 0.15f ? Mathf.Lerp(1.4f, 1f, u / 0.15f) : 1f;
                    f.Rt.localScale = Vector3.one * s;
                    f.Vel *= 1f - dt * 2.5f;
                    f.Txt.color = new Color(f.Color.r, f.Color.g, f.Color.b, u < 0.6f ? 1f : 1f - (u - 0.6f) / 0.4f);
                }
            }

            if (_shake > 0f)
            {
                _shake -= dt;
                _content.anchoredPosition = _shake > 0f ? Random.insideUnitCircle * _shakeMag : Vector2.zero;
                if (_shake <= 0f) _shakeMag = 0f;
            }

            if (_resultT >= 0f)
            {
                _resultT += dt;
                float u = Mathf.Clamp01(_resultT / 0.28f);
                _stamp.localScale = Vector3.one * Mathf.Lerp(2.3f, 1f, Ease.OutBack(u, 1.6f));
                _stamp.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-18f, -7f, u));
                _stampText.color = new Color(_stampText.color.r, _stampText.color.g, _stampText.color.b, u);
                _resultText.color = new Color(1f, 1f, 1f, Mathf.Clamp01((_resultT - 0.25f) / 0.3f));
                if (_resultT > 0.28f && _resultT - dt <= 0.28f) Shake(0.12f, 5f);
            }
        }

        static class Ease
        {
            public static float In(float t) => t * t * t;
            public static float InOut(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
            public static float OutBack(float t, float s = 1.70158f)
            {
                t -= 1f;
                return 1f + (s + 1f) * t * t * t + s * t * t;
            }
        }

        // ---------------------------------------------------------------- text

        protected override void Refresh()
        {
            if (_forge == null && _herbs == null && _dough == null) return;
            string progress = _forge != null ? $"{_forge.Hits.Count}/{ForgeGame.Strikes}번째 망치질"
                : _herbs != null ? $"짝 {_herbs.Found}/{HerbGame.Pairs} · 틀림 {_herbs.Misses} · {Mathf.CeilToInt(_herbs.TimeLeft)}초"
                : $"반죽 {_dough.Judged}/{_dough.Notes.Count}" + (_combo > 1 ? $" · {UIBuilder.Colored(_combo + " COMBO", Gold)}" : "");
            _progress.text = _running ? progress : "끝!";
            string flash = Time.unscaledTime < _flashUntil ? _flash : "";
            _status.text = _running ? $"지금 점수 {Score * 100f:0}점   {flash}" : "";
        }
    }
}
