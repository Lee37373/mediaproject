using GuildMaster.Core;
using GuildMaster.Director;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// The spectator mode's title cards (P4-129, H7 — user decision 2026-11-05: a big title and a small line when the
    /// camera goes to a moment, a short one when a shown fight ends). Fades in and out at the top of the screen, never
    /// takes clicks. A card that comes while one is up replaces it. Also a small "관전 중" tag while the mode is on.
    /// </summary>
    public class SpectatorCardView : MonoBehaviour
    {
        const float Fade = 0.3f;

        RectTransform _card;
        CanvasGroup _group;
        Image _bg;
        Text _title, _line;
        float _shownAt = -1f;
        float _seconds;
        RectTransform _tag;
        Text _tagText;

        public void Build(RectTransform canvas)
        {
            _bg = UIBuilder.Panel(canvas, "SpectatorCard", new Color(0.1f, 0.08f, 0.12f, 0.88f), raycast: false);
            _card = _bg.rectTransform;
            _card.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -160), new Vector2(680, 100));
            _group = _bg.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            UIBuilder.VLayout(_bg, 2, 10).childAlignment = TextAnchor.MiddleCenter;
            _title = UIBuilder.Label(_bg.transform, "", 34, TextAnchor.MiddleCenter, UITheme.Accent, FontStyle.Bold).Size(-1, 46);
            _line = UIBuilder.Label(_bg.transform, "", 17, TextAnchor.MiddleCenter).Size(-1, 26);
            _card.gameObject.SetActive(false);

            var tag = UIBuilder.Panel(canvas, "SpectatorTag", new Color(0.12f, 0.1f, 0.16f, 0.85f), raycast: false);
            _tag = tag.rectTransform;
            _tag.Anchor(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -56), new Vector2(300, 26));
            _tagText = UIBuilder.Label(tag.transform, "", 14, TextAnchor.MiddleCenter, UITheme.TextMuted);
            _tagText.rectTransform.Stretch();
            _tag.gameObject.SetActive(SpectatorCamera.On);

            EventBus.Subscribe<SpectatorCardEvent>(OnCard);
            EventBus.Subscribe<SpectatorToggledEvent>(OnToggled);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<SpectatorCardEvent>(OnCard);
            EventBus.Unsubscribe<SpectatorToggledEvent>(OnToggled);
        }

        /// <summary>Gold for big foes, red for members in trouble, orange for the town; green for a won fight.</summary>
        static Color Tint(HighlightKind k, bool result)
        {
            if (result) return UITheme.Good;
            if (HighlightRules.IsBigFoe(k)) return UITheme.Accent;
            return k <= HighlightKind.Retreat ? UITheme.Bad : new Color(1f, 0.62f, 0.3f);
        }

        void OnCard(SpectatorCardEvent e)
        {
            _title.text = e.Title;
            _title.color = Tint(e.Kind, e.Result);
            _title.fontSize = e.Result ? 28 : 34;
            _line.text = e.Subtitle ?? "";
            _seconds = e.Result ? HighlightRules.CardSeconds * 0.7f : HighlightRules.CardSeconds;
            _shownAt = Time.unscaledTime;
            _card.gameObject.SetActive(true);
            _card.SetAsLastSibling();
        }

        void OnToggled(SpectatorToggledEvent e)
        {
            _tag.gameObject.SetActive(e.On);
            if (!e.On) _card.gameObject.SetActive(false);
        }

        void Update()
        {
            if (_tag.gameObject.activeSelf)
            {
                var shown = Services.TryGet<SpectatorCamera>(out var s) ? s.Shown : null;
                _tagText.text = shown != null ? $"● 관전 중 — {shown.Title}  (C로 끄기)" : "● 관전 중 — 볼 만한 장면을 기다리는 중  (C)";
            }
            if (_shownAt < 0f) return;
            float t = Time.unscaledTime - _shownAt;
            if (t >= _seconds)
            {
                _shownAt = -1f;
                _card.gameObject.SetActive(false);
                return;
            }
            _group.alpha = Mathf.Min(1f, t / Fade, (_seconds - t) / Fade);
        }
    }
}
