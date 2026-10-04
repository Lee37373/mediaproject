using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Quests;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    public struct AdventurerJoinedEvent { public Adventurer Adventurer; }
    public struct AdventurerDiedEvent { public Adventurer Adventurer; public Unit Killer; }
    public struct AdventurerLeftEvent { public Adventurer Adventurer; }

    /// <summary>The guild's member list. Owns adventurer creation and removal.</summary>
    public class GuildRoster : MonoBehaviour
    {
        readonly List<Adventurer> _members = new();

        public ListView<Adventurer> Members => new(_members);
        public int Capacity { get; set; } = 6;
        public bool IsFull => _members.Count >= Capacity;

        public Adventurer Recruit(AdventurerData data, Vector2 position)
        {
            var a = Adventurer.Create(data, position, transform);
            _members.Add(a);
            EventBus.Publish(new AdventurerJoinedEvent { Adventurer = a });
            return a;
        }

        /// <summary>Adds an already spawned visitor to the guild (recruitment).</summary>
        public bool AddExisting(Adventurer a)
        {
            if (a == null || IsFull || _members.Contains(a)) return false;
            a.transform.SetParent(transform, true);
            a.JoinGuild();
            _members.Add(a);
            EventBus.Publish(new AdventurerJoinedEvent { Adventurer = a });
            return true;
        }

        public Adventurer Find(string id) => _members.Find(m => m.Data.id == id);

        internal void NotifyDeath(Adventurer a, Unit killer)
        {
            if (!_members.Remove(a)) return;
            if (Services.TryGet<QuestBoard>(out var board)) board.RemoveTakerEverywhere(a);
            a.CurrentQuest = null;
            EventBus.Publish(new AdventurerDiedEvent { Adventurer = a, Killer = killer });
        }

        public void Dismiss(Adventurer a)
        {
            if (!_members.Remove(a)) return;
            if (Services.TryGet<QuestBoard>(out var board)) board.RemoveTakerEverywhere(a);
            a.IsGuildMember = false;
            EventBus.Publish(new AdventurerLeftEvent { Adventurer = a });
            Destroy(a.gameObject);
        }
    }
}
