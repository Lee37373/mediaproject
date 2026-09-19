using GuildMaster.Core;
using GuildMaster.Town;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Top-of-screen raid banner (P3-6): the warning with the direction only (information hiding),
    /// then "raid in progress" until the wave ends, then a short result line.
    /// </summary>
    public class RaidBannerView : MonoBehaviour
    {
        const float ResultSeconds = 6f;

        RectTransform _root;
        Text _text;
        float _hideAt = -1f;

        public void Build(RectTransform canvas)
        {
            var img = UIBuilder.Panel(canvas, "RaidBanner", new Color(0.35f, 0.06f, 0.06f, 0.92f), raycast: false);
            _root = img.rectTransform;
            _root.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -108), new Vector2(620, 40));
            _text = UIBuilder.Label(_root, "", 18, TextAnchor.MiddleCenter, new Color(1f, 0.9f, 0.85f), FontStyle.Bold);
            _text.rectTransform.Stretch();
            _root.gameObject.SetActive(false);
            EventBus.Subscribe<RaidWarningEvent>(OnWarning);
            EventBus.Subscribe<RaidStartedEvent>(OnStarted);
            EventBus.Subscribe<RaidEndedEvent>(OnEnded);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<RaidWarningEvent>(OnWarning);
            EventBus.Unsubscribe<RaidStartedEvent>(OnStarted);
            EventBus.Unsubscribe<RaidEndedEvent>(OnEnded);
        }

        void Show(string text, float seconds)
        {
            _text.text = text;
            _root.gameObject.SetActive(true);
            _hideAt = seconds > 0f ? Time.unscaledTime + seconds : -1f;
        }

        void OnWarning(RaidWarningEvent e) => Show($"침공 경고: {e.HoursLeft}시간 뒤 {e.Direction}쪽에서 무언가 몰려옵니다", -1f);
        void OnStarted(RaidStartedEvent e) => Show($"{e.Wave}번째 침공! {e.Direction}쪽에서 적이 몰려옵니다", -1f);

        void OnEnded(RaidEndedEvent e) => Show(e.Withdrawn > 0
            ? $"동이 트자 적이 물러갔습니다 (처치 {e.Killed})"
            : $"침공을 막아냈습니다! (처치 {e.Killed})", ResultSeconds);

        void Update()
        {
            if (_hideAt > 0f && Time.unscaledTime >= _hideAt)
            {
                _hideAt = -1f;
                _root.gameObject.SetActive(false);
            }
        }
    }
}
