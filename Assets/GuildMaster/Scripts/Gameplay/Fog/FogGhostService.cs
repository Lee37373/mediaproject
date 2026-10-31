using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Ecosystem;
using UnityEngine;

namespace GuildMaster.Fog
{
    /// <summary>
    /// Fog memory (P4-23): when the fog swallows a monster or a dropped item, it leaves a frozen
    /// grey copy where the guild last saw it. The ghost never tracks the real object, so nothing
    /// outside vision leaks; it is removed only when that spot comes back into view, or when the
    /// same subject is seen and lost again. Discovered nests get the same treatment in place:
    /// they are tinted as a memory while out of sight.
    /// </summary>
    public class FogGhostService : MonoBehaviour, ISaveable, ISaveLink
    {
        const float SweepInterval = 0.25f;
        /// <summary>
        /// Ghosts never expire on their own (user decision, 2026-09-20), so this is only a
        /// performance guard; the oldest sighting goes first.
        /// </summary>
        public const int MaxGhosts = 120;

        readonly List<FogGhost> _ghosts = new();
        readonly Dictionary<FogHideable, FogGhost> _bySubject = new();
        PoolService _pool;
        FogOfWar _fog;
        GameClock _clock;
        EcosystemManager _eco;
        float _timer;

        public IReadOnlyList<FogGhost> Ghosts => _ghosts;

        public void Init(PoolService pool, FogOfWar fog, GameClock clock, EcosystemManager eco)
        {
            _pool = pool;
            _fog = fog;
            _clock = clock;
            _eco = eco;
            _pool.Register(FogGhost.PoolKey, FogGhost.CreatePooledInstance, 8, maxSize: MaxGhosts + 8);
            FogHideable.VisibilityChanged += OnFogVisibilityChanged;
            FogHideable.Released += OnSubjectReleased;
        }

        void OnDestroy()
        {
            FogHideable.VisibilityChanged -= OnFogVisibilityChanged;
            FogHideable.Released -= OnSubjectReleased;
        }

        void OnFogVisibilityChanged(FogHideable h, bool visible)
        {
            if (visible) return;
            if (h == null) return;
            var subject = h.GetComponent<IFogGhostSubject>();
            if (subject == null || !subject.WantsGhost) return;
            if (subject.GhostBody == null || subject.GhostBody.sprite == null) return;
            // The same subject only ever has one memory: replace the older one.
            if (_bySubject.TryGetValue(h, out var old)) Remove(old);
            if (_ghosts.Count >= MaxGhosts) Remove(_ghosts[0]);

            var ghost = _pool.Spawn<FogGhost>(FogGhost.PoolKey, subject.GhostBody.transform.position);
            ghost.Capture(subject, _clock.TotalHours);
            NoteSource(ghost, subject);
            _ghosts.Add(ghost);
            _bySubject[h] = ghost;
        }

        /// <summary>A pooled subject went away; its memory stays but stops belonging to that object.</summary>
        void OnSubjectReleased(FogHideable h)
        {
            if (h != null) _bySubject.Remove(h);
        }

        void Update()
        {
            if (_fog == null) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = SweepInterval;
            Sweep();
            TintRememberedNests();
        }

        /// <summary>
        /// Seeing the spot again settles the question: either the real thing is standing there
        /// (and is drawn normally) or it left. Either way the memory goes.
        /// </summary>
        void Sweep()
        {
            for (int i = _ghosts.Count - 1; i >= 0; i--)
            {
                var g = _ghosts[i];
                if (g == null) { _ghosts.RemoveAt(i); continue; }
                if (_fog.IsVisible(g.Position)) Remove(g); // IsVisible is true for everything when fog is off (F3)
            }
        }

        /// <summary>Discovered nests are dimmed while out of sight: what is drawn is a memory.</summary>
        void TintRememberedNests()
        {
            if (_eco == null) return;
            foreach (var n in _eco.Nests)
            {
                if (!n.Discovered) continue;
                n.SetRemembered(!_fog.IsVisible(n.Cell));
            }
        }

        void Remove(FogGhost g)
        {
            _ghosts.Remove(g);
            FogHideable owner = null;
            foreach (var kv in _bySubject)
                if (kv.Value == g) { owner = kv.Key; break; }
            if (owner != null) _bySubject.Remove(owner);
            if (g != null) _pool.Despawn(g.gameObject);
        }

        /// <summary>Nearest ghost to a map point, for inspector clicks. Null when none is close.</summary>
        public FogGhost PickAt(Vector2 p, float radius)
        {
            FogGhost best = null;
            float bestSq = radius * radius;
            foreach (var g in _ghosts)
            {
                if (g == null || !g.IsActive) continue;
                float sq = ((Vector2)g.Position - p).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = g; }
            }
            return best;
        }

        /// <summary>"3시간 전" / "방금" for a sighting time in total game hours.</summary>
        public string AgoText(float hour) => _clock.AgoText(hour);

        // ---------------- Save (P5-4) ----------------

        /// <summary>Remembers what the ghost shows, so a load can draw it again without the original.</summary>
        static void NoteSource(FogGhost ghost, IFogGhostSubject subject)
        {
            ghost.SourceId = null;
            ghost.SpeciesId = null;
            ghost.ItemId = null;
            switch (subject)
            {
                case Monsters.Monster m:
                    ghost.SourceId = m.UnitId;
                    ghost.SpeciesId = m.Def != null ? m.Def.id : null;
                    ghost.Boss = m.IsBoss;
                    ghost.Ranged = m.IsRangedVariant;
                    break;
                case Items.GroundItem g:
                    ghost.ItemId = g.Item != null ? g.Item.id : null;
                    break;
            }
        }

        [System.Serializable]
        class SavedGhost
        {
            public string source;
            public string species;
            public bool boss;
            public bool ranged;
            public string item;
            public Vector3 position;
            public Color color;
            public Vector3 scale;
            public bool flip;
            public Vector3 bar;
            public string label;
            public float hp;
            public float hour;
        }

        [System.Serializable]
        class SaveData { public List<SavedGhost> ghosts = new(); }

        SaveData _restoring;
        readonly List<FogGhost> _restored = new();

        public string SaveKey => "ghosts";

        public string Capture()
        {
            var s = new SaveData();
            foreach (var g in _ghosts)
            {
                if (g == null || !g.IsActive) continue;
                s.ghosts.Add(new SavedGhost
                {
                    source = g.SourceId, species = g.SpeciesId, boss = g.Boss, ranged = g.Ranged, item = g.ItemId,
                    position = g.transform.position, color = g.BodyColor, scale = g.BodyScale, flip = g.BodyFlip, bar = g.BarPosition,
                    label = g.Label, hp = g.HpRatio, hour = g.SeenAtHour,
                });
            }
            return JsonUtility.ToJson(s);
        }

        /// <summary>Each memory drawn again from its kind's picture (a monster's art, an item's dot).</summary>
        public void Restore(string json, SaveContext ctx)
        {
            _restoring = JsonUtility.FromJson<SaveData>(json);
            var db = Services.Get<Data.GameDatabase>();
            _restored.Clear();
            foreach (var sg in _restoring.ghosts)
            {
                Sprite sprite = null;
                if (db.Monster(sg.species) is { } def) sprite = Monsters.Monster.ArtFor(def, sg.boss, sg.ranged);
                else if (!string.IsNullOrEmpty(sg.item)) sprite = Items.GroundItem.DotSprite;
                var ghost = _pool.Spawn<FogGhost>(FogGhost.PoolKey, sg.position);
                ghost.Restore(sprite, sg.color, sg.scale, sg.flip, sg.bar, sg.label, sg.hp, sg.hour);
                ghost.SourceId = sg.source;
                ghost.SpeciesId = sg.species;
                ghost.Boss = sg.boss;
                ghost.Ranged = sg.ranged;
                ghost.ItemId = sg.item;
                _ghosts.Add(ghost);
                _restored.Add(ghost);
            }
        }

        /// <summary>A memory of a monster still alive belongs to it again (seeing and losing it replaces the memory).</summary>
        public void Link(SaveContext ctx)
        {
            for (int i = 0; i < _restored.Count; i++)
            {
                var id = _restoring.ghosts[i].source;
                if (string.IsNullOrEmpty(id)) continue;
                if (ctx.Find<Units.Unit>(id) is { } u && u.GetComponent<FogHideable>() is { } h) _bySubject[h] = _restored[i];
            }
            _restored.Clear();
            _restoring = null;
        }
    }
}
