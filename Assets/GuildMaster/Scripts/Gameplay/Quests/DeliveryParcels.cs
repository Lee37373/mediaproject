using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Units;
using GuildMaster.Visuals;
using UnityEngine;

namespace GuildMaster.Quests
{
    /// <summary>
    /// The goods of a delivery on the road (P4-133): a parcel on the back of the one carrying it — the first of its takers still
    /// alive — from the moment they leave town until it is handed over. Drawn just in front of the carrier.
    /// </summary>
    public class DeliveryParcels : MonoBehaviour
    {
        const float Scale = 0.7f;
        static readonly Vector3 OnTheBack = new(-0.36f, 0.45f, 0f);

        readonly Dictionary<Quest, SpriteRenderer> _parcels = new();
        readonly List<Quest> _gone = new();
        QuestBoard _board;

        public void Init() => _board = Services.Get<QuestBoard>();

        /// <summary>Who carries the goods of this delivery now, or null (not on the road, or nobody left).</summary>
        public static Unit CarrierOf(Quest q)
        {
            if (q == null || q.Type != QuestType.Deliver || !q.GoodsOut || q.Status != QuestStatus.InProgress) return null;
            foreach (var t in q.Assignees)
                if (t is Unit u && u.IsAlive) return u;
            return null;
        }

        void LateUpdate()
        {
            if (_board == null) return;
            foreach (var q in _board.Quests)
            {
                var carrier = CarrierOf(q);
                if (carrier == null) continue;
                if (!_parcels.TryGetValue(q, out var sr) || sr == null)
                {
                    var go = new GameObject("Parcel");
                    sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = CartoonArt.Parcel();
                    go.transform.localScale = Vector3.one * Scale;
                    _parcels[q] = sr;
                }
                if (sr.transform.parent != carrier.transform) sr.transform.SetParent(carrier.transform, false);
                var visual = carrier.Visual;
                bool left = visual != null && visual.Body != null && visual.Body.flipX;
                sr.transform.localPosition = new Vector3(left ? -OnTheBack.x : OnTheBack.x, OnTheBack.y, 0f);
                // The carrier's own depth (a rig sorts by its group, not the body renderer), just in front of it.
                int depth = CharacterVisual.BaseSortingOrder + (visual != null ? visual.SortingBoost : 0)
                            - Mathf.RoundToInt((visual != null ? visual.transform.position.y : carrier.Position.y) * 8f);
                sr.sortingOrder = depth + 2;
            }
            _gone.Clear();
            foreach (var kv in _parcels)
                if (kv.Value == null || CarrierOf(kv.Key) == null) _gone.Add(kv.Key);
            foreach (var q in _gone)
            {
                if (_parcels[q] != null) Destroy(_parcels[q].gameObject);
                _parcels.Remove(q);
            }
        }
    }
}
