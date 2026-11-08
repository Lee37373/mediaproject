using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// A "!" beside the head of each resident with an open request (P4-134 F-14, user decision 2026-11-08 "머리 위 '!'"),
    /// bobbing; a "…" while a member is on it. Clicking the resident opens the request window (the world overlay takes no
    /// clicks). On the head's left, so it does not cover the feeling icon on the right (<see cref="EmotionIconLayer"/>).
    /// Hidden far out, like the tags.
    /// </summary>
    public class RequestMarkLayer : MonoBehaviour
    {
        const float MaxZoom = 16f;
        const float Size = 26f;
        static readonly Vector3 Offset = new(-0.48f, 0.66f);
        static readonly Color OpenBack = new(0.85f, 0.6f, 0.08f, 0.95f);
        static readonly Color UnderwayBack = new(0.25f, 0.25f, 0.32f, 0.9f);

        class Mark
        {
            public RectTransform Root;
            public Image Back;
            public Text Label;
        }

        RectTransform _layer;
        readonly List<Mark> _marks = new();
        int _used;

        public void Build(RectTransform layer) => _layer = layer;

        Mark Acquire()
        {
            if (_used < _marks.Count)
            {
                var m = _marks[_used++];
                if (!m.Root.gameObject.activeSelf) m.Root.gameObject.SetActive(true);
                return m;
            }
            var back = UIBuilder.Panel(_layer, "RequestMark", Color.white, raycast: false);
            var rt = back.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Size, Size);
            var label = UIBuilder.Label(rt, "!", 20, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            label.rectTransform.Stretch();
            label.raycastTarget = false;
            var mark = new Mark { Root = rt, Back = back, Label = label };
            _marks.Add(mark);
            _used++;
            return mark;
        }

        void LateUpdate()
        {
            _used = 0;
            var cam = Camera.main;
            if (_layer != null && cam != null && cam.orthographicSize <= MaxZoom
                && Services.TryGet<ResidentRequests>(out var requests) && Services.TryGet<ResidentService>(out var residents))
            {
                float bob = Mathf.Sin(Time.unscaledTime * 4f) * 3f;
                foreach (var r in requests.Active)
                {
                    var who = residents.Find(r.ResidentId);
                    if (who == null || !who.IsAlive) continue;
                    if (!WorldOverlay.TryWorldToLocal(_layer, who.transform.position + Offset, out var local)) continue;
                    var m = Acquire();
                    bool open = r.IsOpen;
                    m.Back.color = open ? OpenBack : UnderwayBack;
                    m.Label.text = open ? "!" : "…";
                    m.Root.anchoredPosition = local + new Vector2(0f, open ? bob : 0f);
                }
            }
            for (int i = _used; i < _marks.Count; i++)
                if (_marks[i].Root.gameObject.activeSelf) _marks[i].Root.gameObject.SetActive(false);
        }
    }
}
