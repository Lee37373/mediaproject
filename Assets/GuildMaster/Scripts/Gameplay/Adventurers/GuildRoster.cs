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

    /// <summary>
    /// The guild's member list. Owns adventurer creation and removal. There is no limit on how many join (P4-92, user
    /// decision 2026-10-19): their keep and how often visitors come hold the number down.
    /// </summary>
    public class GuildRoster : MonoBehaviour, ISaveable, ISaveLink, ISaveResume
    {
        readonly List<Adventurer> _members = new();

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SaveData
        {
            /// <summary>Last adventurer number handed out, so the dead and the departed are never renumbered.</summary>
            public int next_id;
            public List<Adventurer.AdventurerSave> members = new();
        }

        readonly List<(Adventurer member, Adventurer.AdventurerSave saved)> _restoring = new();

        public string SaveKey => "roster";

        public string Capture()
        {
            var s = new SaveData { next_id = AdventurerGenerator.Counter };
            foreach (var m in _members) if (m != null && m.IsAlive) s.members.Add(m.CaptureSave());
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            AdventurerGenerator.Counter = s.next_id;
            _restoring.Clear();
            foreach (var saved in s.members)
            {
                var a = Adventurer.RestoreSave(saved, transform, ctx);
                _members.Add(a);
                _restoring.Add((a, saved));
            }
        }

        public void Link(SaveContext ctx)
        {
            foreach (var (member, saved) in _restoring) member.LinkSave(saved, ctx);
        }

        public void Resume(SaveContext ctx)
        {
            foreach (var (member, saved) in _restoring) member.ResumeSave(saved, ctx);
            _restoring.Clear();
        }

        public ListView<Adventurer> Members => new(_members);
        /// <summary>"7명".</summary>
        public string CountText => $"{_members.Count}명";

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
            if (a == null || _members.Contains(a)) return false;
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
            a.Vanish();
            Destroy(a.gameObject);
        }
    }
}
