using System.Collections.Generic;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// A group that went out hunting together without a quest (P4-37). Members join in the order
    /// they decided to go, and the first of them leads. It is the free hunt's answer to a quest's
    /// assignee list, so <see cref="PartyTactics"/> can give it the same formation and focus fire.
    /// </summary>
    public class HuntBand
    {
        public readonly List<Adventurer> Members = new();

        /// <summary>First to decide, and the one who picks where the band goes.</summary>
        public Adventurer Leader => Members.Count > 0 ? Members[0] : null;

        /// <summary>Where the band is headed; the leader sets it, the followers keep formation.</summary>
        public Vector2 Spot;
        public bool HasSpot;

        /// <summary>Game hour the leader stops waiting at the gate for company.</summary>
        public float MusterUntilHour;

        public bool Mustering { get; internal set; } = true;
        public bool Has(Adventurer a) => Members.Contains(a);
    }

    /// <summary>
    /// Keeps the hunting bands (P4-37, user request 2026-09-20: "they should move as a party").
    /// A member that decides to hunt joins a band that is still mustering at the gate; when none
    /// is, it starts one and waits a while for company itself.
    /// </summary>
    public class HuntBandService : MonoBehaviour
    {
        /// <summary>Game hours the first member waits at the gate before setting off.</summary>
        public const float MusterHours = 1.5f;

        readonly List<HuntBand> _bands = new();
        GameClock _clock;
        int _capacity = 4;

        public void Init(GameClock clock, int capacity)
        {
            _clock = clock;
            _capacity = Mathf.Max(1, capacity);
        }

        public IReadOnlyList<HuntBand> Bands => _bands;

        public HuntBand BandOf(Adventurer a)
        {
            foreach (var b in _bands) if (b.Has(a)) return b;
            return null;
        }

        /// <summary>Joins the band still gathering at the gate, or opens a new one and leads it.</summary>
        public HuntBand Join(Adventurer a)
        {
            var existing = BandOf(a);
            if (existing != null) return existing;
            foreach (var b in _bands)
            {
                if (!b.Mustering || b.Members.Count >= _capacity) continue;
                b.Members.Add(a);
                return b;
            }
            var band = new HuntBand { MusterUntilHour = (_clock != null ? _clock.TotalHours : 0f) + MusterHours };
            band.Members.Add(a);
            _bands.Add(band);
            return band;
        }

        public void Leave(Adventurer a)
        {
            for (int i = _bands.Count - 1; i >= 0; i--)
            {
                if (!_bands[i].Members.Remove(a)) continue;
                if (_bands[i].Members.Count == 0) _bands.RemoveAt(i);
            }
        }

        /// <summary>Drops the dead and closes the gate on bands whose muster time is up.</summary>
        void Update()
        {
            float hour = _clock != null ? _clock.TotalHours : 0f;
            for (int i = _bands.Count - 1; i >= 0; i--)
            {
                var b = _bands[i];
                b.Members.RemoveAll(m => m == null || !m.IsAlive || !m.IsGuildMember);
                if (b.Members.Count == 0) { _bands.RemoveAt(i); continue; }
                if (b.Mustering && (hour >= b.MusterUntilHour || b.Members.Count >= _capacity)) b.Mustering = false;
            }
        }
    }
}
