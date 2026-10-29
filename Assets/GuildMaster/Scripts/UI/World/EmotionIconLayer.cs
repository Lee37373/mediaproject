using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Social;
using GuildMaster.Units;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Feeling icons beside the heads of members, visitors and residents (F-10, user request 2026-10-11): angry right
    /// after a fight, sad after losing a friend, strained, excited, happy, sleepy. What each one feels comes from
    /// <see cref="EmotionService"/>; this only draws it. Hidden when zoomed far out, like the name tags.
    /// </summary>
    public class EmotionIconLayer : MonoBehaviour
    {
        const float MaxZoom = 14f;
        const float Size = 26f;
        /// <summary>World offset from the unit's feet: beside the head, clear of the name tag and the speech bubble.</summary>
        static readonly Vector3 Offset = new(0.48f, 0.62f);

        class Badge
        {
            public RectTransform Root;
            public Image Back;
            public Image Icon;
        }

        RectTransform _layer;
        readonly List<Badge> _badges = new();
        int _used;

        public void Build(RectTransform layer) => _layer = layer;

        Badge Acquire()
        {
            if (_used < _badges.Count)
            {
                var b = _badges[_used++];
                if (!b.Root.gameObject.activeSelf) b.Root.gameObject.SetActive(true);
                return b;
            }
            var back = UIBuilder.Panel(_layer, "Emotion", Color.white, raycast: false);
            var rt = back.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Size, Size);
            var icon = UIBuilder.SpriteImage(rt, "Icon", null, Color.white);
            icon.rectTransform.Stretch(4, 4, 4, 4);
            icon.preserveAspect = true;
            var badge = new Badge { Root = rt, Back = back, Icon = icon };
            _badges.Add(badge);
            _used++;
            return badge;
        }

        void LateUpdate()
        {
            _used = 0;
            var cam = Camera.main;
            if (_layer != null && cam != null && cam.orthographicSize <= MaxZoom && Services.TryGet<EmotionService>(out var emotions))
            {
                float bob = Mathf.Sin(Time.unscaledTime * 3f) * 2f;
                foreach (var u in UnitRegistry.All)
                {
                    if (u == null || !u.IsAlive) continue;
                    var e = emotions.Of(u, out _);
                    if (e == Emotion.None) continue;
                    if (!WorldOverlay.TryWorldToLocal(_layer, u.transform.position + Offset, out var local)) continue;
                    var b = Acquire();
                    b.Back.color = EmotionIcons.Background(e);
                    b.Icon.sprite = EmotionIcons.IconFor(e);
                    b.Root.anchoredPosition = local + new Vector2(0f, e == Emotion.Sleepy ? bob : 0f);
                }
            }
            for (int i = _used; i < _badges.Count; i++)
                if (_badges[i].Root.gameObject.activeSelf) _badges[i].Root.gameObject.SetActive(false);
        }
    }

    /// <summary>Pixel icons and colors for feelings (shared by the head badges and the relationship graph).</summary>
    public static class EmotionIcons
    {
        static readonly Dictionary<Emotion, Sprite> s_sprites = new();

        public static Color Background(Emotion e) => e switch
        {
            Emotion.Angry => new Color(0.55f, 0.1f, 0.08f, 0.92f),
            Emotion.Sad => new Color(0.1f, 0.2f, 0.45f, 0.92f),
            Emotion.Strained => new Color(0.3f, 0.12f, 0.4f, 0.92f),
            Emotion.Excited => new Color(0.5f, 0.38f, 0.05f, 0.92f),
            Emotion.Happy => new Color(0.5f, 0.15f, 0.3f, 0.92f),
            Emotion.Sleepy => new Color(0.18f, 0.18f, 0.24f, 0.88f),
            _ => new Color(0, 0, 0, 0),
        };

        /// <summary>Text color for the feeling's name.</summary>
        public static Color Tint(Emotion e) => e switch
        {
            Emotion.Angry => new Color(1f, 0.45f, 0.4f),
            Emotion.Sad => new Color(0.55f, 0.75f, 1f),
            Emotion.Strained => new Color(0.85f, 0.6f, 1f),
            Emotion.Excited => new Color(1f, 0.85f, 0.3f),
            Emotion.Happy => new Color(1f, 0.6f, 0.75f),
            Emotion.Sleepy => new Color(0.75f, 0.75f, 0.85f),
            _ => UITheme.TextMuted,
        };

        public static Sprite IconFor(Emotion e)
        {
            if (e == Emotion.None) return null;
            if (s_sprites.TryGetValue(e, out var s) && s != null) return s;
            var (rows, palette) = Template(e);
            s = SpriteFactory.FromTemplate("emotion." + e, rows, palette, new Vector2(0.5f, 0.5f));
            s_sprites[e] = s;
            return s;
        }

        static (string[] rows, Dictionary<char, Color> palette) Template(Emotion e)
        {
            var white = new Color(1f, 1f, 1f);
            switch (e)
            {
                case Emotion.Angry: // the comic "anger vein"
                    return (new[]
                    {
                        ".RR...RR.",
                        "RRR...RRR",
                        ".RRR.RRR.",
                        ".........",
                        ".........",
                        ".RRR.RRR.",
                        "RRR...RRR",
                        ".RR...RR.",
                    }, new Dictionary<char, Color> { ['R'] = new Color(1f, 0.32f, 0.25f) });
                case Emotion.Sad: // a tear
                    return (new[]
                    {
                        "...B...",
                        "...B...",
                        "..BBB..",
                        "..BBB..",
                        ".BBBBB.",
                        "BBBBWBB",
                        "BBBBWBB",
                        ".BBBBB.",
                        "..BBB..",
                    }, new Dictionary<char, Color> { ['B'] = new Color(0.45f, 0.7f, 1f), ['W'] = white });
                case Emotion.Strained: // a dizzy swirl
                    return (new[]
                    {
                        "..SSSSS..",
                        ".S.....S.",
                        "S..SSS..S",
                        "S.S...S.S",
                        "S.S.S.S.S",
                        "S.S..SS.S",
                        "S..S....S",
                        ".S..SSSS.",
                        "..S......",
                    }, new Dictionary<char, Color> { ['S'] = new Color(0.85f, 0.65f, 1f) });
                case Emotion.Excited: // a star
                    return (new[]
                    {
                        "....Y....",
                        "...YYY...",
                        "YYYYYYYYY",
                        ".YYYYYYY.",
                        "..YYYYY..",
                        "..YY.YY..",
                        ".YY...YY.",
                    }, new Dictionary<char, Color> { ['Y'] = new Color(1f, 0.85f, 0.25f) });
                case Emotion.Happy: // a heart
                    return (new[]
                    {
                        ".HH.HH.",
                        "HHHHHHH",
                        "HHHHHHH",
                        ".HHHHH.",
                        "..HHH..",
                        "...H...",
                    }, new Dictionary<char, Color> { ['H'] = new Color(1f, 0.45f, 0.65f) });
                default: // sleepy: z z
                    return (new[]
                    {
                        ".....ZZZZ",
                        ".......Z.",
                        "......Z..",
                        ".....ZZZZ",
                        "ZZZ......",
                        "..Z......",
                        ".Z.......",
                        "ZZZ......",
                    }, new Dictionary<char, Color> { ['Z'] = white });
            }
        }
    }
}
