using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Small UI motions with DOTween (2026-11-13, user imported DOTween: "모션이 부드러워지게, 적용할 수 있는 곳에"):
    /// windows and cards pop in, buttons lean in under the pointer and press down, a count that grows bounces, a bar
    /// slides to its new value. All on unscaled time (the clock is often held under menus) and linked to their
    /// GameObject, so a hidden or destroyed object never keeps a tween running. Only DOTween's core is used (no modules).
    /// </summary>
    public static class UIMotion
    {
        /// <summary>Scales up from <paramref name="from"/> with a slight overshoot, and fades in when it has a CanvasGroup.</summary>
        public static void PopIn(RectTransform rt, float from = 0.92f, float seconds = 0.22f, bool fade = true)
        {
            if (rt == null) return;
            if (!Application.isPlaying) return; // edit-mode tests build windows too: no tweens there
            rt.DOKill();
            rt.localScale = new Vector3(from, from, 1f);
            rt.DOScale(1f, seconds).SetEase(Ease.OutBack).SetUpdate(true).SetLink(rt.gameObject, LinkBehaviour.KillOnDisable)
              .OnKill(() => { if (rt != null) rt.localScale = Vector3.one; });
            var group = fade ? rt.GetComponent<CanvasGroup>() : null;
            if (group == null) return;
            DOTween.Kill(group);
            group.alpha = 0f;
            DOTween.To(() => group.alpha, a => group.alpha = a, 1f, seconds * 0.7f).SetTarget(group).SetUpdate(true)
                   .SetLink(rt.gameObject, LinkBehaviour.KillOnDisable).OnKill(() => { if (group != null) group.alpha = 1f; });
        }

        /// <summary>A quick squash-and-back, for something that just changed (a count, a ticked box).</summary>
        public static void Punch(Transform t, float amount = 0.25f, float seconds = 0.45f)
        {
            if (t == null || !Application.isPlaying) return;
            t.DOKill(true);
            t.localScale = Vector3.one;
            t.DOPunchScale(new Vector3(amount, amount, 0f), seconds, 6, 0.6f).SetUpdate(true)
             .SetLink(t.gameObject, LinkBehaviour.KillOnDisable).OnKill(() => { if (t != null) t.localScale = Vector3.one; });
        }

        /// <summary>A few hops to draw the eye (a button with something new behind it).</summary>
        public static void Bounce(Transform t, int hops = 3)
        {
            if (t == null || !Application.isPlaying) return;
            t.DOKill(true);
            t.localScale = Vector3.one;
            t.DOPunchScale(new Vector3(0.18f, 0.18f, 0f), 0.5f, 4, 0.5f).SetLoops(hops).SetUpdate(true)
             .SetLink(t.gameObject, LinkBehaviour.KillOnDisable).OnKill(() => { if (t != null) t.localScale = Vector3.one; });
        }

        /// <summary>Slides a left-anchored fill's right edge to <paramref name="ratio"/> (snaps when told to).</summary>
        public static void Fill(RectTransform fill, float ratio, bool snap, float seconds = 0.35f)
        {
            if (fill == null) return;
            if (Application.isPlaying) DOTween.Kill(fill);
            if (snap || !Application.isPlaying || !fill.gameObject.activeInHierarchy)
            {
                fill.anchorMax = new Vector2(ratio, fill.anchorMax.y);
                return;
            }
            if (Mathf.Abs(fill.anchorMax.x - ratio) < 0.002f) return;
            DOTween.To(() => fill.anchorMax.x, x => fill.anchorMax = new Vector2(x, fill.anchorMax.y), ratio, seconds)
                   .SetEase(Ease.OutCubic).SetTarget(fill).SetUpdate(true).SetLink(fill.gameObject, LinkBehaviour.KillOnDisable)
                   .OnKill(() => { if (fill != null) fill.anchorMax = new Vector2(ratio, fill.anchorMax.y); });
        }

        /// <summary>
        /// A sign hung on ropes (the title's logo, 2026-11-14): drops in from <paramref name="dropFrom"/> above its place
        /// with a bounce, then sways a little for as long as it is shown. Its pivot is where the ropes are tied.
        /// </summary>
        public static void HangSign(RectTransform rt, Vector2 home, float dropFrom, float degrees = 1.4f, float swaySeconds = 3f)
        {
            if (rt == null) return;
            rt.anchoredPosition = home;
            if (!Application.isPlaying) return;
            DOTween.Kill(rt);
            rt.anchoredPosition = home + new Vector2(0f, dropFrom);
            DOTween.To(() => rt.anchoredPosition, p => rt.anchoredPosition = p, home, 0.9f).SetEase(Ease.OutBounce).SetTarget(rt)
                   .SetUpdate(true).SetLink(rt.gameObject, LinkBehaviour.KillOnDisable)
                   .OnKill(() => { if (rt != null) rt.anchoredPosition = home; });
            float angle = -degrees;
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            DOTween.To(() => angle, a => { angle = a; if (rt != null) rt.localRotation = Quaternion.Euler(0f, 0f, a); }, degrees, swaySeconds)
                   .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetTarget(rt).SetUpdate(true)
                   .SetLink(rt.gameObject, LinkBehaviour.KillOnDisable)
                   .OnKill(() => { if (rt != null) rt.localRotation = Quaternion.identity; });
        }

        /// <summary>Runs <paramref name="step"/> from 0 to 1 over real seconds, easing out (a camera glide); at once outside play.</summary>
        public static void Run(GameObject link, float seconds, System.Action<float> step)
        {
            if (link == null || step == null) return;
            if (!Application.isPlaying) { step(1f); return; }
            float t = 0f;
            DOTween.To(() => t, v => { t = v; step(v); }, 1f, seconds).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(link);
        }
    }

    /// <summary>
    /// Every UIBuilder button (2026-11-13): leans in a little under the pointer, presses down, springs back. Disabled
    /// buttons stay still. Scale only, so layouts are not disturbed.
    /// </summary>
    public class ButtonJuice : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        const float Hover = 1.05f, Pressed = 0.93f;
        Selectable _selectable;
        bool _over;

        void Awake() => _selectable = GetComponent<Selectable>();

        bool Live => _selectable == null || _selectable.IsInteractable();

        public void OnPointerEnter(PointerEventData e)
        {
            _over = true;
            if (Live) To(Hover, 0.12f, Ease.OutQuad);
        }

        public void OnPointerExit(PointerEventData e)
        {
            _over = false;
            To(1f, 0.15f, Ease.OutQuad);
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (Live) To(Pressed, 0.06f, Ease.OutQuad);
        }

        public void OnPointerUp(PointerEventData e) => To(_over && Live ? Hover : 1f, 0.3f, Ease.OutBack);

        void To(float scale, float seconds, Ease ease)
        {
            transform.DOKill();
            transform.DOScale(scale, seconds).SetEase(ease).SetUpdate(true).SetLink(gameObject, LinkBehaviour.KillOnDisable);
        }

        void OnDisable()
        {
            _over = false;
            transform.DOKill();
            transform.localScale = Vector3.one;
        }
    }
}
