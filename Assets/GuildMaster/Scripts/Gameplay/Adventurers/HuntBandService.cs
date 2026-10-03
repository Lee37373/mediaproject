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

        /// <summary>
        /// Someone has left the hunt — hurt, bag full, called away, fallen — so the rest head home
        /// too instead of carrying on short-handed (P4-38, user report: members ending up alone).
        /// </summary>
        public bool GoingHome { get; internal set; }

        /// <summary>
        /// The band stopped to mend a hurt member (P4-71): it pulled back and/or made camp, and goes
        /// on hunting afterwards. Members leaving the hunt meanwhile do not break the band up.
        /// </summary>
        public bool Paused { get; internal set; }

        /// <summary>Game hour the band set out from the guild (its time out counts from here, camps included).</summary>
        public float SetOutHour { get; internal set; }

        public bool Has(Adventurer a) => Members.Contains(a);
    }

    /// <summary>
    /// Keeps the hunting bands (P4-37, user request 2026-09-20: "they should move as a party").
    /// A member that decides to hunt joins a band still mustering at the guild; when none is, it
    /// starts one and invites others in town (P4-51, <see cref="PartyService.InviteToHunt"/>).
    /// </summary>
    public class HuntBandService : MonoBehaviour
    {
        /// <summary>
        /// Game hours the band waits at the guild before setting off, beyond the time its invitations
        /// take to answer (P4-51: company now comes by invitation, so this is only for walking up).
        /// P4-40 cut it from 1.5 to 0.75 — a measured day spent 14% of everyone's time waiting.
        /// </summary>
        public const float MusterHours = 0.25f;

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

        /// <summary>
        /// Joins the band still gathering at the guild, or opens a new one and leads it. A standing
        /// party's leader always opens its own and brings the members who are free in town (P4-50);
        /// others may still join it while it musters.
        /// </summary>
        public HuntBand Join(Adventurer a)
        {
            var existing = BandOf(a);
            if (existing != null) return existing;
            Services.TryGet<GuildParties>(out var parties);
            var party = parties?.PartyOf(a);
            bool leadsParty = party != null && party.Leader == a;
            if (!leadsParty)
            {
                foreach (var b in _bands)
                {
                    if (!b.Mustering || b.Members.Count >= _capacity) continue;
                    b.Members.Add(a);
                    return b;
                }
            }
            var band = new HuntBand { MusterUntilHour = (_clock != null ? _clock.TotalHours : 0f) + MusterHours };
            band.Members.Add(a);
            _bands.Add(band);
            if (leadsParty)
            {
                foreach (var m in party.Members)
                {
                    if (m == a || band.Members.Count >= _capacity || !m.IsAlive || m.CurrentQuest != null) continue;
                    if (!m.IsIdleInTown || !Welfare.GuildWelfare.MayGoOut(m)) continue; // on leave, wounded or breaking: stays home
                    band.Members.Add(m); // before the state change, so its Enter finds this band
                    m.Say("같이 가자!");
                    m.ChangeState(m.FreeHuntState);
                }
            }
            // The free slots are filled by invitation (P4-51): each invitee decides for itself.
            if (band.Members.Count < _capacity && Services.TryGet<PartyService>(out var invites))
                invites.InviteToHunt(a, band, _capacity - band.Members.Count);
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
                // A paused band keeps those pulling back or at the fire; one that got home is out of it.
                if (b.Paused) b.Members.RemoveAll(m => m.Fsm.Current != m.FreeHuntState && m.Fsm.Current != m.ReturnState && m.Fsm.Current != m.CampState);
                else b.Members.RemoveAll(m => m.Fsm.Current == m.ReturnState || m.Fsm.Current == m.CampState); // hunting again: whoever went home is out
                if (b.Members.Count == 0) { _bands.RemoveAt(i); continue; }
                // Waits while hunt invitations are being answered (P4-51), then leaves together.
                bool answering = Services.TryGet<PartyService>(out var invites) && invites.IsGathering(b);
                if (b.Mustering && !answering && (hour >= b.MusterUntilHour || b.Members.Count >= _capacity))
                {
                    b.Mustering = false;
                    b.SetOutHour = hour;
                    // Setting out together: a standing party forms or goes on an outing (P4-50).
                    if (b.Members.Count >= 2 && Services.TryGet<GuildParties>(out var parties)) parties.SetOut(b.Members);
                }
            }
        }
    }
}
