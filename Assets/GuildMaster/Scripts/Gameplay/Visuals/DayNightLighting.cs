using GuildMaster.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace GuildMaster.Visuals
{
    /// <summary>Drives the URP 2D global light from the in-game clock.</summary>
    [RequireComponent(typeof(Light2D))]
    public class DayNightLighting : MonoBehaviour
    {
        static readonly Color Night = new(0.45f, 0.52f, 0.85f);
        static readonly Color Dawn = new(1.0f, 0.78f, 0.62f);
        static readonly Color Day = Color.white;

        Light2D _light;
        GameClock _clock;

        public static DayNightLighting Create(Transform parent, GameClock clock)
        {
            var go = new GameObject("GlobalLight2D");
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            var dn = go.AddComponent<DayNightLighting>();
            dn._light = light;
            dn._clock = clock;
            return dn;
        }

        void Update()
        {
            if (_clock == null || _light == null) return;
            float d = _clock.Daylight; // 0 midnight .. 1 noon
            Color c = d < 0.5f ? Color.Lerp(Night, Dawn, d / 0.5f) : Color.Lerp(Dawn, Day, (d - 0.5f) / 0.5f);
            _light.color = c * Seasons.SeasonService.Tint(_clock); // the season's tint (P4-111b)
            _light.intensity = Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 1f, d));
        }
    }
}
