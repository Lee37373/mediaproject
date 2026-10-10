using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Fog;
using GuildMaster.Units;
using GuildMaster.Visuals;
using UnityEngine;

namespace GuildMaster.Items
{
    /// <summary>
    /// An item stack lying on the map (P4-21): dropped by the dead, picked up by monsters (worn or
    /// swallowed) and adventurers (carried back as loot). Pooled; hidden under the fog like units.
    /// </summary>
    public class GroundItem : MonoBehaviour, IPoolable, IFogGhostSubject
    {
        public ItemDefinition Item { get; private set; }
        public int Count { get; private set; }
        /// <summary>Game hour (TotalHours) it was dropped.</summary>
        public float DroppedAtHour { get; private set; }
        /// <summary>Real-time seconds since the drop (short delays, e.g. pack leaders first).</summary>
        public float Age => Time.time - _droppedAt;
        /// <summary>Unit walking to it right now; others leave it alone.</summary>
        public Unit ReservedBy { get; private set; }
        /// <summary>
        /// A fallen member's relic (F-6): it lies for its full time, however crowded the ground gets
        /// (P4-77: the item cap swept away the oldest drop anywhere, relics included, long before they rotted).
        /// </summary>
        public bool Kept { get; internal set; }
        public Vector2 Position => transform.position;

        SpriteRenderer _body;
        FogHideable _fog;
        float _droppedAt;
        float _bobPhase;

        /// <summary>The dot an item is drawn as (fog memories of items use it too).</summary>
        public static Sprite DotSprite => SpriteFactory.Circle(12, Color.white, new Color(0.1f, 0.08f, 0.06f, 1f));

        void Awake()
        {
            // The dot lives on a child so its bobbing never moves the item itself.
            var body = new GameObject("Body");
            body.transform.SetParent(transform, false);
            _body = body.AddComponent<SpriteRenderer>();
            _body.sprite = DotSprite;
            _fog = gameObject.AddComponent<FogHideable>();
        }

        internal void Init(ItemDefinition item, int count, float hour)
        {
            Item = item;
            Count = Mathf.Max(1, count);
            DroppedAtHour = hour;
            _droppedAt = Time.time;
            ReservedBy = null;
            Kept = false;
            _bobPhase = Random.value * 6f;
            _body.color = item != null ? item.color : Color.white;
            // Bigger dot for wearable items so they read as "gear" at a glance.
            float size = item != null && item.equipSlot != EquipSlot.None ? 0.42f : 0.32f;
            float spriteSize = _body.sprite.bounds.size.x;
            _body.transform.localScale = new Vector3(size / spriteSize, size / spriteSize, 1f);
            _body.sortingOrder = CharacterVisual.BaseSortingOrder - Mathf.RoundToInt(transform.position.y * 8f) - 3;
            _fog.Refresh();
        }

        /// <summary>Save (P5-4): the drop moment as seconds from now (it decides who may take it first).</summary>
        internal float DroppedAtRelative => SaveTime.Rel(_droppedAt);

        internal void RestoreDroppedAt(float relative) => _droppedAt = SaveTime.Abs(relative);

        internal void RestoreReservation(Unit who) => ReservedBy = who;

        // ---------------- Fog memory (P4-23) ----------------

        bool IFogGhostSubject.WantsGhost => Item != null;
        string IFogGhostSubject.GhostLabel => Item == null ? "" : Count > 1 ? $"{Item.displayName} x{Count}" : Item.displayName;
        SpriteRenderer IFogGhostSubject.GhostBody => _body;
        float IFogGhostSubject.GhostHpRatio => -1f;

        /// <summary>Claims the item for a unit walking to it; false if someone else (alive) has it.</summary>
        public bool Reserve(Unit who)
        {
            if (ReservedBy != null && ReservedBy != who && ReservedBy.IsAlive) return false;
            ReservedBy = who;
            return true;
        }

        public void Release(Unit who)
        {
            if (ReservedBy == who) ReservedBy = null;
        }

        public bool IsFreeFor(Unit who) => ReservedBy == null || ReservedBy == who || !ReservedBy.IsAlive;

        public void OnSpawned() { }

        public void OnDespawned()
        {
            Item = null;
            ReservedBy = null;
            Kept = false;
        }

        void Update()
        {
            // Gentle bob so items stand out from the ground.
            if (_body == null) return;
            _body.transform.localPosition = new Vector3(0f, 0.05f + Mathf.Sin(Time.time * 3f + _bobPhase) * 0.04f, 0f);
        }
    }
}
