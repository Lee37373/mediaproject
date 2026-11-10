using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Base for toggleable windows: builds a titled frame once, then refreshes its body
    /// on a throttled interval while visible.
    /// </summary>
    public abstract class UIPanel : MonoBehaviour
    {
        protected RectTransform Body { get; private set; }
        protected RectTransform Frame { get; private set; }
        float _refreshTimer;

        protected virtual float RefreshInterval => 0.5f;

        public abstract string Title { get; }

        /// <summary>Creates the window frame at the given anchored rect and calls Build().</summary>
        public void Create(RectTransform parent, Vector2 size, Vector2 anchoredPos)
        {
            var frameImg = UIBuilder.Panel(parent, $"Window:{Title}", UITheme.PanelBg);
            Frame = frameImg.rectTransform;
            Frame.Anchor(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPos, size);
            HomePosition = anchoredPos;
            transform.SetParent(Frame, false);
            gameObject.name = GetType().Name;

            var header = UIBuilder.Panel(Frame, "Header", UITheme.Header);
            header.rectTransform.Anchor(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 38));
            // Drag the title bar to move the window (several windows can be open at once); it opens there next time (P6-2).
            header.gameObject.AddComponent<WindowDragger>().Init(Frame, () => WindowPlacement.Set(PlacementId, Frame.anchoredPosition));
            var title = UIBuilder.Label(header.transform, Title, 19, TextAnchor.MiddleLeft, UITheme.Accent, FontStyle.Bold);
            title.rectTransform.Stretch(14, 50, 0, 0);
            var close = UIBuilder.Button(header.transform, "X", Close, 30, 30, UITheme.ButtonDanger);
            ((RectTransform)close.transform).Anchor(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-4, 0), new Vector2(30, 30));
            UISkin.Window(frameImg, header, close, title); // 2026-10-31: cartoon frame when the skin is present

            Body = UIBuilder.Rect(Frame, "Body");
            Body.Stretch(10, 10, 46, 10);
            Build(Body);
            Frame.gameObject.SetActive(false);
        }

        protected abstract void Build(RectTransform body);
        protected virtual void Refresh() { }
        protected virtual void OnOpened() { }

        public void Open()
        {
            bool wasOpen = Frame.gameObject.activeSelf;
            if (!wasOpen) GuildMaster.Audio.Sound.Play(GuildMaster.Audio.SoundKeys.Open);
            Frame.gameObject.SetActive(true);
            Frame.SetAsLastSibling();
            if (Core.Services.TryGet<UIRoot>(out var root)) root.NotifyOpened(this, wasOpen);
            _refreshTimer = 0f;
            OnOpened();
            Refresh();
        }

        public void Close()
        {
            if (Frame.gameObject.activeSelf) GuildMaster.Audio.Sound.Play(GuildMaster.Audio.SoundKeys.Close);
            CloseQuietly();
        }

        /// <summary>Closes without the sound: "close all" (P6-2) plays it once for every window.</summary>
        internal void CloseQuietly()
        {
            Frame.gameObject.SetActive(false);
            if (Core.Services.TryGet<UIRoot>(out var root)) root.NotifyClosed(this);
        }

        /// <summary>Raises the window above the others without re-running OnOpened.</summary>
        public void BringToFront()
        {
            if (!Visible) return;
            Frame.SetAsLastSibling();
            if (Core.Services.TryGet<UIRoot>(out var root)) root.NotifyOpened(this, true);
        }

        public void Toggle()
        {
            if (Frame.gameObject.activeSelf) Close();
            else Open();
        }

        public bool Visible => Frame != null && Frame.gameObject.activeSelf;
        internal RectTransform WindowFrame => Frame;
        /// <summary>Position given at creation: where the window opens until the player drags it somewhere else.</summary>
        internal Vector2 HomePosition { get; private set; }
        /// <summary>Key of the remembered spot (P6-2): one per window type.</summary>
        internal string PlacementId => GetType().Name;
        /// <summary>Where the window opens: the spot it was last dragged to, else its home (P6-2).</summary>
        internal Vector2 OpenPosition => WindowPlacement.TryGet(PlacementId, out var pos) ? pos : HomePosition;

        protected virtual void Update()
        {
            if (!Visible) return;
            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = RefreshInterval;
            Refresh();
        }

        /// <summary>Re-populates a list container: ensures exactly 'count' row objects exist and returns them.</summary>
        protected static void EnsureRows<T>(Transform content, System.Collections.Generic.List<T> rows, int count, System.Func<Transform, T> create) where T : Component
        {
            while (rows.Count < count) rows.Add(create(content));
            for (int i = 0; i < rows.Count; i++) rows[i].gameObject.SetActive(i < count);
        }
    }
}
