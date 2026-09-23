using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Notifications;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Toasts on the right edge (F-1): each new notification appears for a few real seconds, fades out, and
    /// leads to what it is about when clicked. Also keeps the unread count on the side-menu button.
    /// Timing uses unscaled time, so toasts behave the same paused or at x4.
    /// </summary>
    public class ToastLayer : MonoBehaviour
    {
        const int MaxVisible = 4;
        const float ToastHeight = 84f;
        const float FadeSeconds = 0.4f;
        /// <summary>A hovered toast stays at least this much longer, so it can be read and clicked.</summary>
        const float HoverGraceSeconds = 2f;
        /// <summary>Just below the minimap when it is shown, otherwise right under the top bar.</summary>
        const float StackTopBelowMinimap = -320f;
        const float StackTopNoMinimap = -60f;

        readonly List<Toast> _toasts = new();
        RectTransform _stack;
        int _shownUnread = -1;

        /// <summary>The "알림 (N)" side-menu button; it shows the unread count.</summary>
        public Button MenuButton { get; set; }

        public void Build(RectTransform canvas)
        {
            _stack = UIBuilder.Rect(canvas, "Toasts");
            _stack.Anchor(new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, StackTopBelowMinimap), new Vector2(380, 0));
            UIBuilder.VLayout(_stack, 6, 0);
            UIBuilder.FitContent(_stack);
            EventBus.Subscribe<NotificationPostedEvent>(OnPosted);
        }

        void OnDestroy() => EventBus.Unsubscribe<NotificationPostedEvent>(OnPosted);

        void OnPosted(NotificationPostedEvent e)
        {
            var n = e.Notification;
            if (n == null || !n.Toast || _stack == null) return;
            var toast = _toasts.Find(t => t != null && t.Data == n);
            if (toast == null)
            {
                toast = Toast.Create(_stack);
                _toasts.Add(toast);
                while (_toasts.Count > MaxVisible) Remove(_toasts[0]);
            }
            toast.Bind(n, Time.unscaledTime);
        }

        void Remove(Toast toast)
        {
            _toasts.Remove(toast);
            if (toast != null) Destroy(toast.gameObject);
        }

        void Update()
        {
            UpdateBadge();
            if (_stack == null) return;

            bool minimap = Services.TryGet<MinimapView>(out var mm) && mm.IsShown;
            _stack.anchoredPosition = new Vector2(-8, minimap ? StackTopBelowMinimap : StackTopNoMinimap);

            float now = Time.unscaledTime;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                if (t == null) { _toasts.RemoveAt(i); continue; }
                if (t.Dismissed) { Remove(t); continue; }
                if (t.Hovered) t.HideAt = Mathf.Max(t.HideAt, now + HoverGraceSeconds);
                if (now >= t.HideAt) { Remove(t); continue; }
                t.SetAlpha(Mathf.Clamp01((t.HideAt - now) / FadeSeconds));
            }
        }

        void UpdateBadge()
        {
            if (MenuButton == null || !Services.TryGet<NotificationCenter>(out var center)) return;
            int unread = center.Unread;
            if (unread == _shownUnread) return;
            _shownUnread = unread;
            MenuButton.SetLabel(unread > 0
                ? $"알림 (N)  {UIBuilder.Colored(unread > 99 ? "99+" : unread.ToString(), UITheme.Bad)}"
                : "알림 (N)");
        }

        static float Seconds(NotificationKind kind) => kind switch
        {
            NotificationKind.Danger => 9f,
            NotificationKind.Warning => 7f,
            NotificationKind.Quest => 7f,
            _ => 5f,
        };

        class Toast : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            Image _bg;
            Text _title;
            Text _body;
            CanvasGroup _group;

            public Notification Data { get; private set; }
            public float HideAt { get; set; }
            public bool Hovered { get; private set; }
            /// <summary>Clicked: goes away at once, even under the pointer.</summary>
            public bool Dismissed { get; private set; }

            public static Toast Create(Transform parent)
            {
                var bg = UIBuilder.Panel(parent, "Toast", UITheme.PanelBg);
                bg.Size(-1, ToastHeight);
                var toast = bg.gameObject.AddComponent<Toast>();
                toast._bg = bg;
                toast._group = bg.gameObject.AddComponent<CanvasGroup>();
                toast._title = UIBuilder.Label(bg.transform, "", 15, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold);
                toast._title.rectTransform.Anchor(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(-24, 22));
                toast._body = UIBuilder.Label(bg.transform, "", 14, TextAnchor.UpperLeft);
                toast._body.rectTransform.Stretch(12, 12, 30, 6);
                var button = bg.gameObject.AddComponent<Button>();
                button.targetGraphic = bg;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(toast.Click);
                return toast;
            }

            public void Bind(Notification n, float now)
            {
                Data = n;
                var color = NotificationActions.KindColor(n.Kind);
                _bg.color = Color.Lerp(UITheme.PanelBg, color, 0.22f);
                string count = n.Count > 1 ? $" ×{n.Count}" : "";
                string more = n.HasLink ? "  ›" : "";
                _title.text = UIBuilder.Colored(n.Title + count, color) + UIBuilder.Colored(more, UITheme.TextMuted);
                _body.text = n.Text;
                HideAt = now + Seconds(n.Kind);
                SetAlpha(1f);
            }

            public void SetAlpha(float a) => _group.alpha = a;

            void Click()
            {
                NotificationActions.Go(Data);
                Dismissed = true;
            }

            public void OnPointerEnter(PointerEventData e) => Hovered = true;
            public void OnPointerExit(PointerEventData e) => Hovered = false;
        }
    }
}
