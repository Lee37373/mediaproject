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

    /// <summary>A member picked up a quirk (P4-104e): a fear, nerves of steel, the dragon's survivor.</summary>
    public struct QuirkGainedEvent
    {
        public Adventurer Adventurer;
        public string Quirk;
        /// <summary>Its name as shown ("해골 공포").</summary>
        public string Label;
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
        /// <summary>Debt from which it weighs on a member.</summary>
        public const int InDebtFrom = 20;

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
            EventBus.Subscribe<MentalBreakEvent>(OnBreak);
            EventBus.Subscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Subscribe<QuestAcceptedEvent>(OnAccepted);
            EventBus.Subscribe<World.ZoneDiscoveredEvent>(OnZone);
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
            var entry = ThoughtRules.Add(a.Data.thoughts, def, _clock.TotalHours, key, name, a.Data.traitIds, a.Data.values, a.Data.quirks);
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
            float Scaled(ThoughtDef def) => def.Mood * ThoughtRules.TraitScale(def.Tone, d.traitIds, d.values, d.quirks);

            Put(ThoughtRules.HeavyHeart, ThoughtRules.HeavyHeartMood(Welfare.StressRules.LevelOf(d.stress)));
            if (d.fatigue >= Welfare.StressRules.OverworkFatigue - 10f) Put(ThoughtRules.Worn, Scaled(ThoughtRules.Worn));
            bool wounded = Legacy.GuildLegacy.WoundedRefusal(a) != null;
            if (wounded) Put(ThoughtRules.Wounded, Scaled(ThoughtRules.Wounded));
            else if (a.IsAlive && a.HpRatio < 0.5f) Put(ThoughtRules.Hurt, Scaled(ThoughtRules.Hurt));
            // Real debt, not the odd meal on the slate (P4-104e: new members arrive owing a few gold).
            if (d.debt >= InDebtFrom) Put(ThoughtRules.InDebt, Scaled(ThoughtRules.InDebt));
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

            // Against its nature (P4-104e, CK3): a loner in a crowd, the sociable alone out there, the lazy worn at work, the
            // diligent idling at home.
            if (a.IsAlive && a.IsGuildMember && !a.IsVisitor)
            {
                bool home = Services.TryGet<World.WorldService>(out var world) && world.Grid.IsInTown(a.Position);
                int company = home ? 0 : CompanyOut(a);
                if (Has(d.traitIds, "loner") && company >= 2) Put(ThoughtRules.Crowded, ThoughtRules.Crowded.Mood);
                if (Has(d.traitIds, "sociable") && !home && company == 0 && IsWorking(a)) Put(ThoughtRules.Lonely, ThoughtRules.Lonely.Mood);
                if (Has(d.traitIds, "lazy") && !home && d.fatigue >= 40f) Put(ThoughtRules.WantsRest, ThoughtRules.WantsRest.Mood);
                if (Has(d.traitIds, "diligent") && home && a.IsIdleInTown && d.fatigue < 30f) Put(ThoughtRules.Idle, ThoughtRules.Idle.Mood);
                // A kind it fears (a quirk) close by.
                if (d.quirks.Count > 0 && FearedNear(a) is { } feared) Put(ThoughtRules.Afraid, ThoughtRules.Afraid.Mood, feared.Def.displayName);
            }
            return list;
        }

        static bool Has(IReadOnlyList<string> ids, string id)
        {
            if (ids == null) return false;
            foreach (var t in ids) if (t == id) return true;
            return false;
        }

        static readonly List<Adventurer> CompanyBuffer = new();

        /// <summary>How many are out with it (its quest party or hunting band), itself not counted.</summary>
        static int CompanyOut(Adventurer a)
            => Services.TryGet<PartyTactics>(out var tactics) ? Mathf.Max(0, tactics.Party(a, CompanyBuffer).Count - 1) : 0;

        static bool IsWorking(Adventurer a)
            => a.Fsm.Current == a.FreeHuntState || a.Fsm.Current == a.HuntState || a.Fsm.Current == a.TravelState;

        /// <summary>A living monster of a kind it fears, close by (in sight of it).</summary>
        static Monster FearedNear(Adventurer a)
            => UnitRegistry.FindNearest(a.Position, QuirkRules.FearSightRadius, a,
                (u, me) => u is Monster m && m.IsAlive && m.Def != null && QuirkRules.Fears(me.Data.quirks, m.Def.id) && me.CanSee(m)) as Monster;

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
                // One it was not close to is one weight that stacks, not one per name (P4-104e).
                if (def == ThoughtRules.ComradeDied) Add(m, def, null, null);
                else Add(m, def, dead.Data.id, dead.Data.name);
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
            // A close call may leave a fear of the kind behind it — or, enough of them, nerves of steel (P4-104e).
            var a = e.Adventurer;
            if (a == null || a.Data == null || !a.IsGuildMember || a.IsVisitor) return;
            a.Data.closeCalls++;
            string species = (e.Threat as Monster)?.Def?.id;
            var quirk = QuirkRules.AfterCloseCall(a.Data.quirks, species, a.Data.closeCalls, GameRandom.Shared.Value);
            if (quirk != null) Gained(a, quirk);
        }

        /// <summary>A quirk taken: told, and a moment's weight on its mind.</summary>
        void Gained(Adventurer a, string quirk)
        {
            string label = QuirkRules.Label(quirk, SpeciesName);
            if (quirk.StartsWith(QuirkRules.FearPrefix)) Add(a, ThoughtRules.QuirkGained, quirk, label);
            EventBus.Publish(new QuirkGainedEvent { Adventurer = a, Quirk = quirk, Label = label });
        }

        /// <summary>A species' name by its id (for a fear's name).</summary>
        public static string SpeciesName(string id)
            => Services.TryGet<Ecosystem.EcosystemManager>(out var eco) ? System.Linq.Enumerable.FirstOrDefault(eco.Species, s => s.id == id)?.displayName ?? id : id;

        /// <summary>
        /// Taking work against its nature (P4-104e, CK3): the timid, or those who value safety, taking what is 위험 or 무모 for
        /// them dread it (and it weighs on them as stress too); the brave find a 여유 one dull.
        /// </summary>
        void OnAccepted(QuestAcceptedEvent e)
        {
            if (e.Taker is not Adventurer a || a.Data == null || e.Quest == null) return;
            string risk = QuestRisk.RiskLabel(a, e.Quest);
            bool timid = Has(a.Data.traitIds, "coward") || a.Data.values == "안전";
            if (timid && (risk == QuestRisk.Risky || risk == QuestRisk.Reckless))
            {
                Add(a, ThoughtRules.DreadfulWork, e.Quest.Id, null);
                a.Data.stress = Mathf.Min(100f, a.Data.stress + DreadStress);
            }
            else if (Has(a.Data.traitIds, "brave") && risk == QuestRisk.Easy) Add(a, ThoughtRules.DullWork, e.Quest.Id, null);
        }

        /// <summary>Stress of taking dreadful work (the long weight beside the day's thought).</summary>
        public const float DreadStress = 3f;

        /// <summary>A new land seen (P4-102): the adventurous are glad of it, wherever they are.</summary>
        void OnZone(World.ZoneDiscoveredEvent e)
        {
            foreach (var a in new List<Adventurer>(Members()))
                if (a.Data.values == "모험") Add(a, ThoughtRules.NewLand, null, null);
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
                if ((a.Position - e.Position).sqrMagnitude <= DragonSightRadius * DragonSightRadius
                    && !QuirkRules.Has(a.Data.quirks, QuirkRules.DragonSurvivor)) // it has lived through worse (P4-104e)
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

        /// <summary>A fistfight over (P4-104d): both remember it, of each other.</summary>
        void OnBreak(MentalBreakEvent e)
        {
            if (e.Kind != BreakKind.Brawl || e.Stage != MentalBreakEvent.Phase.Over || e.Adventurer == null || e.Other == null) return;
            Add(e.Adventurer, ThoughtRules.Brawled, e.Other.Data.id, e.Other.Data.name);
            Add(e.Other, ThoughtRules.Brawled, e.Adventurer.Data.id, e.Adventurer.Data.name);
        }

        /// <summary>Hit by a comrade gone berserk (P4-104d).</summary>
        void OnDamaged(UnitDamagedEvent e)
        {
            // A hard hit from the dragon lived through may leave its calm (P4-104e).
            if (e.Source is Monster { IsDragon: true } && e.Target is Adventurer burnt && burnt.IsAlive && burnt.IsGuildMember && !burnt.IsVisitor
                && burnt.HpRatio < QuirkRules.DragonHardHitBelow && !QuirkRules.Has(burnt.Data.quirks, QuirkRules.DragonSurvivor)
                && GameRandom.Shared.Value < QuirkRules.DragonSurvivorChance)
            {
                burnt.Data.quirks.Add(QuirkRules.DragonSurvivor);
                Gained(burnt, QuirkRules.DragonSurvivor);
                return;
            }
            if (e.Source is not Adventurer { Berserk: true } berserker || e.Target is not Adventurer victim || victim == berserker) return;
            Add(victim, ThoughtRules.BerserkVictim, berserker.Data.id, berserker.Data.name);
        }

        void OnHour(HourPassedEvent e)
        {
            float now = _clock.TotalHours;
            foreach (var a in _roster.Members)
                if (a != null && a.Data != null) ThoughtRules.Expire(a.Data.thoughts, now);
        }
    }
}
