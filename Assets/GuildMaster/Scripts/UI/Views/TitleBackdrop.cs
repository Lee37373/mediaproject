using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace GuildMaster.UI
{
    /// <summary>
    /// Scenes of play behind the title (2026-11-14, user: "뒷배경은 애니메이션 몇 개 … 게임 진행하는 여러 모습"): short
    /// clips recorded from the game itself with the HUD and the name labels off (Resources/Title/*.mp4, no sound), played
    /// one after another through a short fade to dark. They run on real time — the clock is held under the title.
    /// Without any clip nothing is made and the title keeps the live town.
    /// </summary>
    public class TitleBackdrop : MonoBehaviour
    {
        const float FadeSeconds = 0.6f;
        const int Width = 1920, Height = 1080;
        static readonly Color Dark = new(0.07f, 0.05f, 0.04f, 1f);

        enum Phase { Loading, FadingIn, Playing, FadingOut }

        VideoClip[] _clips;
        VideoPlayer _player;
        RawImage _screen;
        RenderTexture _target;
        int _index = -1;
        Phase _phase;
        float _alpha;

        /// <summary>Fills <paramref name="parent"/> behind whatever is added after it; null when there are no clips.</summary>
        public static TitleBackdrop Create(RectTransform parent)
        {
            var clips = Resources.LoadAll<VideoClip>("Title");
            if (clips == null || clips.Length == 0) return null;
            System.Array.Sort(clips, (a, b) => string.CompareOrdinal(a.name, b.name));

            var back = UIBuilder.Rect(parent, "Backdrop").Stretch();
            var dark = back.gameObject.AddComponent<Image>();
            dark.color = Dark;
            dark.raycastTarget = false;
            // The picture covers the screen whatever its shape (cut at the sides or top and bottom, never stretched).
            var screenRt = UIBuilder.Rect(back, "Clip");
            var screen = screenRt.gameObject.AddComponent<RawImage>();
            screen.raycastTarget = false;
            screen.color = new Color(1f, 1f, 1f, 0f);
            var fit = screenRt.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = Width / (float)Height;

            var me = back.gameObject.AddComponent<TitleBackdrop>();
            me._clips = clips;
            me._screen = screen;
            return me;
        }

        void OnEnable()
        {
            if (_clips == null || !Application.isPlaying) return;
            if (_target == null)
            {
                _target = new RenderTexture(Width, Height, 0) { name = "TitleBackdrop" };
                _screen.texture = _target;
            }
            if (_player == null)
            {
                _player = gameObject.AddComponent<VideoPlayer>();
                _player.playOnAwake = false;
                _player.source = VideoSource.VideoClip;
                _player.renderMode = VideoRenderMode.RenderTexture;
                _player.targetTexture = _target;
                _player.audioOutputMode = VideoAudioOutputMode.None;
                _player.isLooping = false;
                _player.skipOnDrop = true;
                _player.waitForFirstFrame = true;
                _player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime; // the title holds the clock (time scale 0)
                _player.prepareCompleted += p => p.Play();
                _player.loopPointReached += _ => { if (_phase != Phase.Loading) _phase = Phase.FadingOut; };
                _player.errorReceived += (_, message) => { Debug.LogWarning("[TitleBackdrop] " + message); enabled = false; };
            }
            _alpha = 0f;
            Next(Random.Range(0, _clips.Length));
        }

        void OnDisable()
        {
            if (_player != null) _player.Stop();
            if (_screen != null) _screen.color = new Color(1f, 1f, 1f, 0f);
        }

        void OnDestroy()
        {
            if (_target != null) { _target.Release(); Destroy(_target); }
        }

        void Next(int index)
        {
            _index = index % _clips.Length;
            _phase = Phase.Loading;
            _player.Stop();
            _player.clip = _clips[_index];
            _player.Prepare();
        }

        void Update()
        {
            if (_player == null) return;
            float step = Time.unscaledDeltaTime / FadeSeconds;
            switch (_phase)
            {
                case Phase.Loading:
                    if (_player.isPlaying && _player.frame > 0) _phase = Phase.FadingIn; // its first picture is on the texture
                    break;
                case Phase.FadingIn:
                    _alpha = Mathf.MoveTowards(_alpha, 1f, step);
                    if (_alpha >= 1f) _phase = Phase.Playing;
                    break;
                case Phase.Playing:
                    // Start the fade a little before the end, so the last picture never stands still.
                    if (_player.length > 0 && _player.time >= _player.length - FadeSeconds) _phase = Phase.FadingOut;
                    break;
                case Phase.FadingOut:
                    _alpha = Mathf.MoveTowards(_alpha, 0f, step);
                    if (_alpha <= 0f) Next(_index + 1);
                    break;
            }
            _screen.color = new Color(1f, 1f, 1f, _alpha);
        }
    }
}
