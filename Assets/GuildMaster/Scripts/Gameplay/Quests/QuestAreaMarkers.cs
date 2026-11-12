using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Quests
{
    /// <summary>
    /// Draws the hunting area of every hunt someone has taken (a faint ring around its den), above the fog so it
    /// stays readable. P4-82: the town posts a hunt on every den the guild has seen, so rings for untaken ones
    /// would cover the map; a ring now shows where a group is headed. An incident's quest (P4-85) shows its rumour's
    /// circle from the moment it is posted, in its own colour, and the ring follows when the guild sees the real spot.
    /// </summary>
    public class QuestAreaMarkers : MonoBehaviour
    {
        const int SpritePx = 128;
        const int SortingOrder = Fog.FogOfWar.SortingOrder + 1; // just above FogOfWar

        static readonly Color Fill = new(1f, 0.85f, 0.3f, 0.07f);
        static readonly Color Outline = new(1f, 0.85f, 0.3f, 0.75f);
        // Fainter than a hunt's: a rumour's circle is wide and mostly over the fog, where linear blending shows a faint fill strongly.
        static readonly Color RumourFill = new(0.85f, 0.45f, 1f, 0.025f);
        static readonly Color RumourOutline = new(0.85f, 0.45f, 1f, 0.8f);

        readonly Dictionary<Quest, GameObject> _markers = new();

        public void Init()
        {
            EventBus.Subscribe<QuestPostedEvent>(OnPosted);
            EventBus.Subscribe<QuestAcceptedEvent>(OnAccepted);
            EventBus.Subscribe<QuestAbandonedEvent>(OnAbandoned);
            EventBus.Subscribe<QuestClosedEvent>(OnClosed);
            EventBus.Subscribe<QuestCompletedEvent>(OnCompleted);
            EventBus.Subscribe<Save.GameLoadedEvent>(OnLoaded);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<QuestPostedEvent>(OnPosted);
            EventBus.Unsubscribe<QuestAcceptedEvent>(OnAccepted);
            EventBus.Unsubscribe<QuestAbandonedEvent>(OnAbandoned);
            EventBus.Unsubscribe<QuestClosedEvent>(OnClosed);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnCompleted);
            EventBus.Unsubscribe<Save.GameLoadedEvent>(OnLoaded);
        }

        /// <summary>A loaded game takes nothing: mark the hunts still being worked and the rumours still open (P5-4).</summary>
        void OnLoaded(Save.GameLoadedEvent e)
        {
            if (!Services.TryGet<QuestBoard>(out var board)) return;
            foreach (var q in board.Quests)
                if (Shown(q)) Add(q);
        }

        /// <summary>A taken hunt, or any open incident's quest.</summary>
        static bool Shown(Quest q)
            => ErrandRules.IsHuntLike(q.Type) && (q.Status == QuestStatus.InProgress || q.Incident != null && q.Status == QuestStatus.Open);

        void OnPosted(QuestPostedEvent e) { if (Shown(e.Quest)) Add(e.Quest); }

        void OnAccepted(QuestAcceptedEvent e) => Add(e.Quest);

        // Everyone gave it up: back on the board untaken (an incident's rumour stays on the map).
        void OnAbandoned(QuestAbandonedEvent e)
        {
            if (e.Quest.Status == QuestStatus.Open && e.Quest.Incident == null) Remove(e.Quest);
        }

        void Add(Quest q)
        {
            if (!Shown(q) || _markers.ContainsKey(q)) return;
            var go = new GameObject($"HuntArea {q.Id}");
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            bool rumour = q.Incident != null;
            sr.sprite = SpriteFactory.Circle(SpritePx, rumour ? RumourFill : Fill, rumour ? RumourOutline : Outline);
            sr.sortingOrder = SortingOrder;
            Place(go, q);
            _markers[q] = go;
        }

        static void Place(GameObject go, Quest q)
        {
            go.transform.position = q.Point;
            float size = (float)SpritePx / SpriteFactory.PixelsPerUnit;
            float s = q.Radius * 2f / size;
            go.transform.localScale = new Vector3(s, s, 1f);
        }

        /// <summary>An incident's area moves when the guild sees what it is about (P4-85).</summary>
        void LateUpdate()
        {
            foreach (var kv in _markers)
                if (kv.Key.Incident != null && kv.Value != null && (Vector2)kv.Value.transform.position != kv.Key.Point) Place(kv.Value, kv.Key);
        }

        // Done hunting (only the turn-in is left) or closed: the area no longer matters.
        void OnCompleted(QuestCompletedEvent e) => Remove(e.Quest);
        void OnClosed(QuestClosedEvent e) => Remove(e.Quest);

        void Remove(Quest q)
        {
            if (!_markers.TryGetValue(q, out var go)) return;
            _markers.Remove(q);
            if (go != null) Destroy(go);
        }
    }
}
