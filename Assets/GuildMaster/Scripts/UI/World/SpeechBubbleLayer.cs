using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    public struct SpeechRequestEvent
    {
        public Transform Speaker;
        public string Text;
        public float Duration;
    }

    /// <summary>Speech bubbles that follow speakers (adventurers, visitors) on screen.</summary>
    public class SpeechBubbleLayer : MonoBehaviour
    {
        const float DefaultDuration = 4f;

        class Bubble
        {
            public RectTransform Root;
            public Text Text;
            public Transform Target;
            public float TimeLeft;
        }

        RectTransform _layer;
        readonly Dictionary<Transform, Bubble> _active = new();
        readonly Stack<Bubble> _free = new();
        readonly List<Transform> _remove = new();

        public void Build(RectTransform layer)
        {
            _layer = layer;
            EventBus.Subscribe<AdventurerSpeechEvent>(OnAdventurerSpeech);
            EventBus.Subscribe<SpeechRequestEvent>(OnSpeech);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<AdventurerSpeechEvent>(OnAdventurerSpeech);
            EventBus.Unsubscribe<SpeechRequestEvent>(OnSpeech);
        }

        void OnAdventurerSpeech(AdventurerSpeechEvent e) => Show(e.Adventurer.transform, e.Text, DefaultDuration);
        void OnSpeech(SpeechRequestEvent e) => Show(e.Speaker, e.Text, e.Duration > 0 ? e.Duration : DefaultDuration);

        public void Show(Transform speaker, string text, float duration)
        {
            if (speaker == null || string.IsNullOrWhiteSpace(text)) return;
            if (!_active.TryGetValue(speaker, out var b))
            {
                b = _free.Count > 0 ? _free.Pop() : Create();
                b.Target = speaker;
                _active[speaker] = b;
            }
            b.Text.text = text.Length > 80 ? text.Substring(0, 78) + "…" : text;
            b.TimeLeft = duration;
            b.Root.gameObject.SetActive(true);
        }

        Bubble Create()
        {
            var img = UIBuilder.Panel(_layer, "Bubble", new Color(0.98f, 0.96f, 0.9f, 0.95f), raycast: false);
            var rt = img.rectTransform;
            rt.pivot = new Vector2(0.5f, 0f);
            var layout = UIBuilder.VLayout(img, 0, 6);
            layout.padding = new RectOffset(10, 10, 5, 5);
            layout.childForceExpandWidth = false;
            var fitter = img.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var text = UIBuilder.Label(img.transform, "", 14, TextAnchor.MiddleCenter, new Color(0.12f, 0.1f, 0.14f));
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.Size(-1, -1).GetComponent<LayoutElement>().preferredWidth = 220;
            return new Bubble { Root = rt, Text = text };
        }

        void LateUpdate()
        {
            _remove.Clear();
            foreach (var kv in _active)
            {
                var b = kv.Value;
                b.TimeLeft -= Time.unscaledDeltaTime;
                if (b.Target == null || !b.Target.gameObject.activeInHierarchy || b.TimeLeft <= 0f)
                {
                    _remove.Add(kv.Key);
                    continue;
                }
                bool visible = WorldOverlay.TryWorldToLocal(_layer, b.Target.position + new Vector3(0, 0.75f), out var local);
                b.Root.gameObject.SetActive(visible);
                if (visible) b.Root.anchoredPosition = local;
            }
            foreach (var t in _remove)
            {
                var b = _active[t];
                b.Root.gameObject.SetActive(false);
                _active.Remove(t);
                _free.Push(b);
            }
        }
    }
}
