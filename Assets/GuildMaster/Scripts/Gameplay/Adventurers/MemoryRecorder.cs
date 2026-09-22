using System;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Units;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Turns gameplay events into per-adventurer memories (see <see cref="AdventurerMemory"/>).
    /// Must be created before the starting adventurers are recruited so "joined" is recorded.
    /// </summary>
    public class MemoryRecorder : IDisposable
    {
        readonly GameClock _clock;
        readonly GuildRoster _roster;

        public MemoryRecorder(GameClock clock, GuildRoster roster)
        {
            _clock = clock;
            _roster = roster;
            EventBus.Subscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Subscribe<QuestAcceptedEvent>(OnAccepted);
            EventBus.Subscribe<QuestRewardPaidEvent>(OnRewardPaid);
            EventBus.Subscribe<QuestAbandonedEvent>(OnAbandoned);
            EventBus.Subscribe<UnitDiedEvent>(OnUnitDied);
            EventBus.Subscribe<AdventurerRetreatedEvent>(OnRetreated);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Subscribe<AdventurerGearUpgradedEvent>(OnGear);
            EventBus.Subscribe<AdventurerDiedEvent>(OnAdventurerDied);
            EventBus.Subscribe<PartyInviteAnsweredEvent>(OnPartyAnswered);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Unsubscribe<QuestAcceptedEvent>(OnAccepted);
            EventBus.Unsubscribe<QuestRewardPaidEvent>(OnRewardPaid);
            EventBus.Unsubscribe<QuestAbandonedEvent>(OnAbandoned);
            EventBus.Unsubscribe<UnitDiedEvent>(OnUnitDied);
            EventBus.Unsubscribe<AdventurerRetreatedEvent>(OnRetreated);
            EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Unsubscribe<AdventurerGearUpgradedEvent>(OnGear);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnAdventurerDied);
            EventBus.Unsubscribe<PartyInviteAnsweredEvent>(OnPartyAnswered);
        }

        void Remember(Adventurer a, int importance, string text, string key = null)
        {
            if (a == null || a.Data == null) return;
            AdventurerMemory.Add(a.Data.memories, _clock.TotalHours, importance, text, key);
        }

        void OnJoined(AdventurerJoinedEvent e) => Remember(e.Adventurer, 1, "길드에 합류했다");

        void OnAccepted(QuestAcceptedEvent e)
            => Remember(e.Taker as Adventurer, 1, $"'{e.Quest.Title}' 의뢰를 맡았다");

        /// <summary>Both sides remember the invitation — this is what "relationship" means in later prompts.</summary>
        void OnPartyAnswered(PartyInviteAnsweredEvent e)
        {
            string leader = e.Leader.Data.name, invitee = e.Invitee.Data.name;
            if (e.Accepted)
            {
                Remember(e.Invitee, 2, e.Quest != null ? $"{leader}의 제안으로 함께 '{e.Quest.Title}' 의뢰에 나섰다" : $"{leader}의 제안으로 함께 사냥에 나섰다");
                Remember(e.Leader, 2, $"{invitee}이(가) 내 파티 제안을 받아들였다");
            }
            else
            {
                Remember(e.Invitee, 1, $"{leader}의 파티 제안을 거절했다");
                Remember(e.Leader, 1, $"{invitee}이(가) 내 파티 제안을 거절했다");
            }
        }

        void OnRewardPaid(QuestRewardPaidEvent e)
            => Remember(e.Taker as Adventurer, 2, $"'{e.Quest.Title}' 의뢰를 끝내고 {e.Gold}G를 받았다");

        void OnAbandoned(QuestAbandonedEvent e)
            => Remember(e.Taker as Adventurer, 2, $"'{e.Quest.Title}' 의뢰를 포기했다");

        void OnUnitDied(UnitDiedEvent e)
        {
            if (e.Killer is not Adventurer a || e.Victim is not Monster m || m.Def.danger < DangerLevel.Medium) return;
            string name = m.DisplayName;
            Remember(a, 2, $"{name}{Josa(name, "을", "를")} 쓰러뜨렸다 (오늘 {{n}}마리)", "kill:" + m.Def.id);
        }

        void OnRetreated(AdventurerRetreatedEvent e)
        {
            string threat = e.Threat != null ? e.Threat.DisplayName : null;
            Remember(e.Adventurer, 3, threat != null ? $"{threat}에게 당해 죽을 뻔해서 후퇴했다" : "크게 다쳐서 후퇴했다");
        }

        void OnLevelUp(AdventurerLevelUpEvent e) => Remember(e.Adventurer, 2, $"레벨 {e.Level}이 되었다");
        void OnGear(AdventurerGearUpgradedEvent e) => Remember(e.Adventurer, 2, $"{e.Price}G를 모아 {e.Tier}등급 장비를 샀다");

        void OnAdventurerDied(AdventurerDiedEvent e)
        {
            if (e.Adventurer == null) return;
            string dead = e.Adventurer.Data.name;
            string by = e.Killer != null ? $"{e.Killer.DisplayName}에게 " : "";
            foreach (var other in _roster.Members)
                if (other != e.Adventurer) Remember(other, 3, $"동료 {dead}{Josa(dead, "이", "가")} {by}목숨을 잃었다");
        }

        /// <summary>Korean particle by final consonant (e.g. 을/를, 이/가).</summary>
        public static string Josa(string word, string withBatchim, string withoutBatchim)
        {
            if (string.IsNullOrEmpty(word)) return withoutBatchim;
            char c = word[word.Length - 1];
            if (c < 0xAC00 || c > 0xD7A3) return withoutBatchim;
            return (c - 0xAC00) % 28 != 0 ? withBatchim : withoutBatchim;
        }
    }
}
