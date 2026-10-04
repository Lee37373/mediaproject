using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Quests
{
    /// <summary>
    /// Draws the hunting area of every active hunt quest on the map (a faint ring around the point the
    /// player marked), above the fog so the player's own marks stay readable.
    /// </summary>
    public class QuestAreaMarkers : MonoBehaviour
    {
        const int SpritePx = 128;
        const int SortingOrder = 3001; // just above FogOfWar

        static readonly Color Fill = new(1f, 0.85f, 0.3f, 0.07f);
        static readonly Color Outline = new(1f, 0.85f, 0.3f, 0.75f);

        readonly Dictionary<Quest, GameObject> _markers = new();

        public void Init()
        {
            EventBus.Subscribe<QuestPostedEvent>(OnPosted);
            EventBus.Subscribe<QuestClosedEvent>(OnClosed);
            EventBus.Subscribe<QuestCompletedEvent>(OnCompleted);
            EventBus.Subscribe<Save.GameLoadedEvent>(OnLoaded);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<QuestPostedEvent>(OnPosted);
            EventBus.Unsubscribe<QuestClosedEvent>(OnClosed);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnCompleted);
            EventBus.Unsubscribe<Save.GameLoadedEvent>(OnLoaded);
        }

        /// <summary>A loaded game posts nothing: mark the hunts still being worked (P5-4).</summary>
        void OnLoaded(Save.GameLoadedEvent e)
        {
            if (!Services.TryGet<QuestBoard>(out var board)) return;
            foreach (var q in board.Quests)
                if (q.IsActive && q.Status != QuestStatus.Completed) Add(q);
        }

        void OnPosted(QuestPostedEvent e) => Add(e.Quest);

        void Add(Quest q)
        {
            if (q.Type != QuestType.Hunt || _markers.ContainsKey(q)) return;
            var go = new GameObject($"HuntArea {q.Id}");
            go.transform.SetParent(transform, false);
            go.transform.position = q.Point;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.Circle(SpritePx, Fill, Outline);
            sr.sortingOrder = SortingOrder;
            float size = (float)SpritePx / SpriteFactory.PixelsPerUnit;
            float s = q.Radius * 2f / size;
            go.transform.localScale = new Vector3(s, s, 1f);
            _markers[q] = go;
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
