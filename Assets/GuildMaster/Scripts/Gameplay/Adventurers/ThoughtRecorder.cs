using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Monsters;
using GuildMaster.Quests;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>A member took a thought to heart (P4-104c): a new one, or the same again (stacked, or lasting longer).</summary>
    public struct ThoughtAddedEvent
    {
        public Adventurer Adventurer;
        public ThoughtEntry Thought;
        public ThoughtDef Def;
        public bool IsNew;
        /// <summary>What went through its head, for a bubble over it (null = nothing to show).</summary>
        public string Inner;
    }

    /// <summary>
    /// Gives members their thoughts from what happens to them and round them (P4-104c, G2), fades them, and reads a member's
    /// mood with the situational thoughts of the moment (stress, fatigue, wounds, debt, leave, the company in its party). The
    /// rules — what each thought weighs, how traits colour it, how they stack — are <see cref="ThoughtRules"/>. The guild
    /// master's care (F-2) and a last stand (F-6) give theirs through <see cref="Give"/>.
    /// </summary>
    public class ThoughtRecorder
    {
        /// <summary>A comrade falling this close is seen falling.</summary>
        public const float WitnessRadius = 10f;
        /// <summary>A lord's, the dragon's or a rare monster's fall counts for the members this close.</summary>
        public const float GreatKillRadius = 15f;
        /// <summary>The dragon is seen by members this close to where it was sighted.</summary>
        public const float DragonSightRadius = 14f;
        /// <summary>A raid held off counts for the members this close to the town's centre.</summary>
        public const float RaidHeldRadius = 30f;

        readonly GameClock _clock;
        readonly GuildRoster _roster;
        readonly Dictionary<Adventurer, float> _lastBubble = new();

        public ThoughtRecorder(GameClock clock, GuildRoster roster)
        {
            _clock = clock;
            _roster = roster;
            EventBus.Subscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Subscribe<QuestRewardPaidEvent>(OnPaid);
            EventBus.Subscribe<QuestClosedEvent>(OnClosed);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Subscribe<AdventurerGearUpgradedEvent>(OnGear);
            EventBus.Subscribe<AdventurerRetreatedEvent>(OnRetreated);
            EventBus.Subscribe<UnitDiedEvent>(OnUnitDied);
            EventBus.Subscribe<Dragons.DragonSightedEvent>(OnDragonSighted);
            EventBus.Subscribe<Town.RaidEndedEvent>(OnRaidEnded);
            EventBus.Subscribe<World.TrapSprungEvent>(OnTrap);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        // ---------------- Giving ----------------

        /// <summary>Gives a guild member a thought (nobody else holds any). <paramref name="about"/> keeps one per subject.</summary>
        public static ThoughtEntry Give(Adventurer a, ThoughtDef def, string about = null, string name = null)
            => Services.TryGet<ThoughtRecorder>(out var r) ? r.Add(a, def, about, name) : null;

        ThoughtEntry Add(Adventurer a, ThoughtDef def, string about, string name)
        {
            if (a == null || a.Data == null || !a.IsAlive || !a.IsGuildMember || a.IsVisitor || def == null) return null;
            string key = about ?? "";
            bool isNew = !a.Data.thoughts.Exists(t => t.id == def.Id && t.about == key);
            var entry = ThoughtRules.Add(a.Data.thoughts, def, _clock.TotalHours, key, name, a.Data.traitIds);
            EventBus.Publish(new ThoughtAddedEvent { Adventurer = a, Thought = entry, Def = def, IsNew = isNew, Inner = isNew ? Inner(a, def, entry, name) : null });
            return entry;
        }

        /// <summary>
        /// The line for a bubble: a strong thought with words for one, at most one bubble an hour a member. The line is picked
        /// by the time, not the game's dice, so a bubble never moves the world's random numbers.
        /// </summary>
        string Inner(Adventurer a, ThoughtDef def, ThoughtEntry entry, string name)
        {
            if (def.Inner.Length == 0 || Mathf.Abs(entry.value) < ThoughtRules.StrongAt) return null;
            float now = _clock.TotalHours;
            if (_lastBubble.TryGetValue(a, out float last) && now - last < ThoughtRules.BubbleCooldownHours) return null;
            _lastBubble[a] = now;
            return ThoughtRules.Fill(def.Inner[Mathf.Abs((int)(now * 60f)) % def.Inner.Length], name);
        }

        // ---------------- Reading ----------------

        /// <summary>What a member's state says now (never stored): the weight of stress, fatigue, wounds, debt, leave, its party's company.</summary>
        public static List<ThoughtEntry> Situational(Adventurer a)
        {
            var list = new List<ThoughtEntry>();
            if (a == null || a.Data == null) return list;
            var d = a.Data;
            void Put(ThoughtDef def, float value, string name = null)
            {
                if (value == 0f) return;
                list.Add(new ThoughtEntry { id = def.Id, label = ThoughtRules.Fill(def.Label, name), value = value });
            }
            float Scaled(ThoughtDef def) => def.Mood * ThoughtRules.TraitScale(def.Tone, d.traitIds);

            Put(ThoughtRules.HeavyHeart, ThoughtRules.HeavyHeartMood(Welfare.StressRules.LevelOf(d.stress)));
            if (d.fatigue >= Welfare.StressRules.OverworkFatigue - 10f) Put(ThoughtRules.Worn, Scaled(ThoughtRules.Worn));
            bool wounded = Legacy.GuildLegacy.WoundedRefusal(a) != null;
            if (wounded) Put(ThoughtRules.Wounded, Scaled(ThoughtRules.Wounded));
            else if (a.IsAlive && a.HpRatio < 0.5f) Put(ThoughtRules.Hurt, Scaled(ThoughtRules.Hurt));
            if (d.debt > 0) Put(ThoughtRules.InDebt, Scaled(ThoughtRules.InDebt));
            if (Services.TryGet<Welfare.GuildWelfare>(out var welfare) && welfare.IsOnLeave(a)) Put(ThoughtRules.OnLeave, Scaled(ThoughtRules.OnLeave));

            // The company it keeps (its standing party): its best friend there, and the one it likes least.
            if (Services.TryGet<GuildParties>(out var parties) && parties.PartyOf(a) is { } party)
            {
                Adventurer friend = null, rival = null;
                int best = Welfare.StressRules.FriendRelation - 1, worst = -Welfare.StressRules.FriendRelation + 1;
                foreach (var m in party.Members)
                {
                    if (m == a || m == null || !m.IsAlive) continue;
                    int v = Relationships.Get(a, m);
                    if (v > best) { best = v; friend = m; }
                    if (v < worst) { worst = v; rival = m; }
                }
                if (friend != null) Put(ThoughtRules.WithFriend, Scaled(ThoughtRules.WithFriend), friend.Data.name);
                if (rival != null) Put(ThoughtRules.WithRival, Scaled(ThoughtRules.WithRival), rival.Data.name);
            }
            return list;
        }

        /// <summary>A member's mood now, 0..100: its thoughts and its situation.</summary>
        public static float MoodOf(Adventurer a)
        {
            if (a == null || a.Data == null) return ThoughtRules.BaseMood;
            float situational = 0f;
            foreach (var t in Situational(a)) situational += t.value;
            return ThoughtRules.Mood(a.Data.thoughts, situational);
        }

        /// <summary>Everything on its mind, the heaviest first: the held thoughts (with their stacks) and the situational ones.</summary>
        public static List<ThoughtEntry> OnItsMind(Adventurer a)
        {
            var all = new List<ThoughtEntry>();
            if (a == null || a.Data == null) return all;
            all.AddRange(a.Data.thoughts);
            all.AddRange(Situational(a));
            all.Sort((x, y) => Mathf.Abs(ThoughtRules.Weight(y)).CompareTo(Mathf.Abs(ThoughtRules.Weight(x))));
            return all;
        }

        // ---------------- What happens ----------------

        IEnumerable<Adventurer> Members()
        {
            foreach (var m in _roster.Members)
                if (m != null && m.IsAlive && m.IsGuildMember && !m.IsVisitor) yield return m;
        }

        void OnDied(AdventurerDiedEvent e)
        {
            var dead = e.Adventurer;
            if (dead == null || dead.Data == null) return;
            _lastBubble.Remove(dead);
            foreach (var m in new List<Adventurer>(Members()))
            {
                if (m == dead) continue;
                int feel = Relationships.Get(m, dead);
                var def = feel >= 40 ? ThoughtRules.ComradeDiedClose : feel >= Welfare.StressRules.FriendRelation ? ThoughtRules.ComradeDiedFriend
                        : feel <= -40 ? ThoughtRules.RivalDied : ThoughtRules.ComradeDied;
                Add(m, def, dead.Data.id, dead.Data.name);
                if ((m.Position - dead.Position).sqrMagnitude <= WitnessRadius * WitnessRadius)
                    Add(m, ThoughtRules.SawComradeFall, dead.Data.id, dead.Data.name);
            }
        }

        void OnPaid(QuestRewardPaidEvent e) => Add(e.Taker as Adventurer, ThoughtRules.QuestDone, null, null);

        void OnClosed(QuestClosedEvent e)
        {
            if (e.Status != QuestStatus.Failed || e.Quest == null) return;
            foreach (var t in e.Quest.Assignees)
                if (t is Adventurer a) Add(a, ThoughtRules.QuestFailed, null, null);
        }

        void OnLevelUp(AdventurerLevelUpEvent e) => Add(e.Adventurer, ThoughtRules.LevelUp, null, null);

        void OnGear(AdventurerGearUpgradedEvent e) => Add(e.Adventurer, ThoughtRules.NewGear, null, null);

        void OnRetreated(AdventurerRetreatedEvent e)
        {
            string by = e.Threat != null ? e.Threat.DisplayName : "적";
            Add(e.Adventurer, ThoughtRules.NearDeath, by, by);
        }

        void OnUnitDied(UnitDiedEvent e)
        {
            if (e.Victim is not Monster m || m.Def == null) return;
            // The guild's biggest fights: everyone who was there.
            if (m.Def.isRegionLord || m.Def.isDragon || m.IsRare)
            {
                if (e.Killer == null || e.Killer.Faction != Faction.Guild) return;
                foreach (var a in new List<Adventurer>(Members()))
                    if ((a.Position - m.Position).sqrMagnitude <= GreatKillRadius * GreatKillRadius)
                        Add(a, ThoughtRules.GreatKill, m.Def.id, m.DisplayName);
                return;
            }
            if (e.Killer is Adventurer killer && m.Danger >= DangerLevel.Medium)
                Add(killer, ThoughtRules.Slew, null, m.Def.displayName);
        }

        void OnDragonSighted(Dragons.DragonSightedEvent e)
        {
            foreach (var a in new List<Adventurer>(Members()))
                if ((a.Position - e.Position).sqrMagnitude <= DragonSightRadius * DragonSightRadius)
                    Add(a, ThoughtRules.SawDragon, null, null);
        }

        void OnRaidEnded(Town.RaidEndedEvent e)
        {
            if (!e.Sighted || e.Killed <= 0 || !Services.TryGet<World.WorldService>(out var world)) return;
            var centre = world.TownCenterWorld;
            foreach (var a in new List<Adventurer>(Members()))
                if ((a.Position - centre).sqrMagnitude <= RaidHeldRadius * RaidHeldRadius)
                    Add(a, ThoughtRules.RaidHeld, null, null);
        }

        void OnTrap(World.TrapSprungEvent e) => Add(e.Victim as Adventurer, ThoughtRules.Trapped, null, null);

        void OnHour(HourPassedEvent e)
        {
            float now = _clock.TotalHours;
            foreach (var a in _roster.Members)
                if (a != null && a.Data != null) ThoughtRules.Expire(a.Data.thoughts, now);
        }
    }
}
