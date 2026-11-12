using System.Collections.Generic;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Director
{
    /// <summary>A title card to show: a moment the camera just went to, or how the one it showed ended.</summary>
    public struct SpectatorCardEvent
    {
        public string Title;
        public string Subtitle;
        public HighlightKind Kind;
        /// <summary>How a shown fight ended ("… 쓰러짐!"), not its start.</summary>
        public bool Result;
    }

    public struct SpectatorToggledEvent { public bool On; }

    /// <summary>
    /// The spectator mode (P4-129, H7 — user decision 2026-11-05: on/off, off by default). While on, the camera goes to
    /// the most interesting moment <see cref="HighlightService"/> knows of and follows it, with a title card for each
    /// new one; it stays on a moment at least <see cref="HighlightRules.MinHold"/> seconds unless a much better one
    /// comes up, and lingers a little after one ends. Moving the camera by hand (keys, drag, the minimap) or following
    /// a unit hands it back to the player for <see cref="HighlightRules.ManualPause"/> seconds. With nothing going on
    /// the camera stays where it is. The switch is the viewer's: kept for the rest of the run (loading too), not saved.
    /// </summary>
    public class SpectatorCamera : MonoBehaviour
    {
        public static bool On { get; private set; }

        HighlightService _highlights;
        Transform _anchor;
        Highlight _shown;
        float _shownSince;
        float _endedAt = -1f;
        float _handsOffAt = -999f;
        readonly List<float> _scores = new();

        /// <summary>The moment the camera is on (null = none).</summary>
        public Highlight Shown => _shown;

        public static void Set(bool on)
        {
            if (On == on) return;
            On = on;
            EventBus.Publish(new SpectatorToggledEvent { On = on });
        }

        public void Init(HighlightService highlights)
        {
            _highlights = highlights;
            _anchor = new GameObject("SpectatorAnchor").transform;
            _anchor.SetParent(transform, false);
            EventBus.Subscribe<HighlightEndedEvent>(OnEnded);
        }

        void OnDestroy() => EventBus.Unsubscribe<HighlightEndedEvent>(OnEnded);

        void OnEnded(HighlightEndedEvent e)
        {
            if (!On || e.Highlight != _shown) return;
            _endedAt = Time.unscaledTime;
            if (e.Highlight.Outcome != null)
                EventBus.Publish(new SpectatorCardEvent { Title = e.Highlight.Outcome, Subtitle = e.Highlight.Title, Kind = e.Highlight.Kind, Result = true });
        }

        /// <summary>The player has the camera (moved it by hand lately, or follows something else).</summary>
        public bool HandsOff(CameraController cam)
        {
            float now = Time.unscaledTime;
            if (cam.FollowTarget != null && cam.FollowTarget != _anchor) _handsOffAt = now;
            return now - Mathf.Max(cam.LastManualInput, _handsOffAt) < HighlightRules.ManualPause;
        }

        void LateUpdate()
        {
            if (!Services.TryGet<CameraController>(out var cam) || _highlights == null) return;
            if (!On)
            {
                if (cam.FollowTarget == _anchor) cam.Follow(null);
                _shown = null;
                return;
            }
            float now = Time.unscaledTime;
            bool handsOff = HandsOff(cam);

            // A moment that just ended: stay a little, then on to the next (or stay where we are).
            if (_shown != null && _shown.Ended)
            {
                if (now - _endedAt < HighlightRules.Linger) return;
                _shown = null;
            }

            var active = _highlights.Active;
            _scores.Clear();
            int current = -1;
            for (int i = 0; i < active.Count; i++)
            {
                _scores.Add(active[i].Score);
                if (active[i] == _shown) current = i;
            }
            int pick = HighlightRules.Pick(_scores, current, now - _shownSince);
            if (pick < 0 || handsOff)
            {
                // Nothing to show, or the player has the camera: let it go where it is.
                if (cam.FollowTarget == _anchor) cam.Follow(null);
                return;
            }

            var next = active[pick];
            if (next != _shown)
            {
                _shown = next;
                _shownSince = now;
                _endedAt = -1f;
                EventBus.Publish(new SpectatorCardEvent { Title = next.Title, Subtitle = next.Subtitle, Kind = next.Kind });
                _anchor.position = next.Position;
            }
            _anchor.position = new Vector3(_shown.Position.x, _shown.Position.y, 0f);
            if (cam.FollowTarget != _anchor) cam.Focus(_anchor);
        }
    }
}
