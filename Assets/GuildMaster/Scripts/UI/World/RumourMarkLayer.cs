using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Incidents;
using GuildMaster.Quests;
using UnityEngine;
using UnityEngine.UI;

namespace GuildMaster.UI
{
    /// <summary>
    /// Incidents on the map as notices (2026-11-13, user: the purple rumour circles piled up and looked messy — "they are
    /// notices: ! or ? as fits, gone when clicked"). A "?" where a rumour says something is (the guild has not seen it), a
    /// "!" once the guild has seen it, or for someone in trouble (a rescue) and a known nemesis. Marks close together on
    /// screen merge into one with a count. A click on a mark dismisses it (and everything merged into it); a "?" that
    /// turns into a "!" comes back. Replaces the incident rings of QuestAreaMarkers. Kept the same size at any zoom.
    /// </summary>
    public class RumourMarkLayer : MonoBehaviour
    {
        const float Size = 34f, MergePx = 30f;

        class Mark
        {
            public RectTransform Root;
            public Image Icon;
            public Text Count;
            public Vector2 At;
            public bool Alert;
            public readonly List<Quest> Quests = new();
        }

        RectTransform _layer;
        readonly List<Mark> _marks = new();
        int _used;
        /// <summary>Dismissed quests, with whether their mark was a "!" then.</summary>
        readonly Dictionary<Quest, bool> _dismissed = new();
        readonly List<Quest> _gone = new();

        public void Build(RectTransform layer) => _layer = layer;

        public static bool Shown(Quest q) =>
            q?.Incident != null && !q.Incident.Over && (q.Status == QuestStatus.Open || q.Status == QuestStatus.InProgress);

        public static bool IsAlert(Incident i) => i.Sighted || i.Kind == IncidentKind.Rescue || i.Kind == IncidentKind.Nemesis;

        Mark Acquire()
        {
            if (_used < _marks.Count)
            {
                var m = _marks[_used++];
                if (!m.Root.gameObject.activeSelf) m.Root.gameObject.SetActive(true);
                return m;
            }
            var icon = HudStyle.NewImage(_layer, "mark_rumour", "RumourMark");
            icon.preserveAspect = true;
            if (icon.sprite == null) icon.color = new Color(0.6f, 0.36f, 0.85f, 1f);
            var rt = icon.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Size, Size);
            var count = HudStyle.Label(rt, "", 13, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            var crt = count.rectTransform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
            crt.anchoredPosition = new Vector2(Size * 0.42f, -Size * 0.42f);
            crt.sizeDelta = new Vector2(24f, 18f);
            UISkin.Outline(count);
            var mark = new Mark { Root = rt, Icon = icon, Count = count };
            _marks.Add(mark);
            _used++;
            return mark;
        }

        void LateUpdate()
        {
            _used = 0;
            if (_layer != null && Services.TryGet<QuestBoard>(out var board))
            {
                foreach (var q in board.Quests)
                {
                    if (!Shown(q)) continue;
                    bool alert = IsAlert(q.Incident);
                    if (_dismissed.TryGetValue(q, out bool wasAlert) && (wasAlert || !alert)) continue;
                    if (!WorldOverlay.TryWorldToLocal(_layer, q.Point, out var local)) continue;
                    Mark m = null;
                    for (int i = 0; i < _used; i++)
                        if ((_marks[i].At - local).sqrMagnitude < MergePx * MergePx) { m = _marks[i]; break; }
                    if (m == null)
                    {
                        m = Acquire();
                        m.At = local;
                        m.Alert = false;
                        m.Quests.Clear();
                    }
                    m.Quests.Add(q);
                    m.Alert |= alert;
                }
                float bob = Mathf.Sin(Time.unscaledTime * 4f) * 3f;
                for (int i = 0; i < _used; i++)
                {
                    var m = _marks[i];
                    HudStyle.Slice(m.Icon, m.Alert ? "mark_alert" : "mark_rumour");
                    m.Icon.type = Image.Type.Simple;
                    m.Root.anchoredPosition = m.At + new Vector2(0f, m.Alert ? bob : 0f);
                    m.Count.text = m.Quests.Count > 1 ? m.Quests.Count.ToString() : "";
                }
                // Forget dismissals of quests that are over.
                _gone.Clear();
                foreach (var kv in _dismissed)
                    if (!Shown(kv.Key)) _gone.Add(kv.Key);
                foreach (var q in _gone) _dismissed.Remove(q);
            }
            for (int i = _used; i < _marks.Count; i++)
                if (_marks[i].Root.gameObject.activeSelf) _marks[i].Root.gameObject.SetActive(false);
        }

        /// <summary>A click on a mark (the inspector asks before picking): dismisses it. True if one was hit.</summary>
        public bool TryDismissAt(Vector2 screen)
        {
            for (int i = 0; i < _used; i++)
            {
                var m = _marks[i];
                if (!m.Root.gameObject.activeSelf || !RectTransformUtility.RectangleContainsScreenPoint(m.Root, screen, null)) continue;
                foreach (var q in m.Quests)
                    if (q?.Incident != null) _dismissed[q] = IsAlert(q.Incident);
                m.Root.gameObject.SetActive(false);
                GuildMaster.Audio.Sound.Click();
                return true;
            }
            return false;
        }
    }
}
