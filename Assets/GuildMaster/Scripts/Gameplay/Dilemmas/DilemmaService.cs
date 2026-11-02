using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Incidents;
using GuildMaster.Recruitment;
using GuildMaster.Seasons;
using GuildMaster.Social;
using GuildMaster.Storytelling;
using GuildMaster.Town;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Dilemmas
{
    /// <summary>A decision card was dealt (P4-111c).</summary>
    public struct DilemmaDealtEvent { public Dilemma Card; }

    /// <summary>A decision card was decided: by the guild master, or by the default when the time ran out. Tone: how the result reads (P4-118).</summary>
    public struct DilemmaResolvedEvent { public Dilemma Card; public int Choice; public bool ByDeadline; public string Result; public DilemmaTone Tone; }

    /// <summary>A dealt card: whom and what it is about, its amounts for the day it came, and when it must be answered.</summary>
    public sealed class Dilemma
    {
        public int Id;
        public DilemmaDef Def;
        public readonly List<string> Actors = new();
        public readonly List<string> Names = new();
        public int[] Gold = System.Array.Empty<int>();
        public string Village, Species;
        public float DealtHour, DeadlineHour;

        public string Title => Fill(Def.Title);
        public string Text => Fill(Def.Text);
        public string Label(int choice) => Fill(Def.Choices[choice].Label);
        public string Hints(int choice) => DilemmaRules.Hints(Def.Choices[choice].Hints);
        public string Fill(string template) => DilemmaRules.Fill(template, Names, Gold, Village, Species);
    }

    /// <summary>A decision taken: what, which way, what came of it.</summary>
    public sealed class DilemmaRecord
    {
        public string Title, Choice, Result;
        public float Hour;
        public bool ByDeadline;
        public DilemmaTone Tone;
    }

    /// <summary>
    /// Decision cards (P4-111c, H3 — user decisions 2026-10-30; the cards in <see cref="DilemmaRules"/>). At nine and five the
    /// storyteller may deal one (more often in the quiet, see <see cref="DilemmaRules.DealChance"/>), at most two open; a card
    /// is cast from what the guild has and knows, never the same one twice in four days. The guild master answers in the
    /// window; one not answered in 24 game hours is decided by its default. A choice may leave a card to follow. Each card's
    /// luck is its own (seeded by the map and the card), so loading a game does not change how it turns out. Saved as "dilemmas".
    /// P4-118: KMS's event cards are folded in (see <see cref="DilemmaRules"/>); a dealt card opens its window and the game
    /// waits (<see cref="AutoOpen"/>), and the cast remember how it went.
    /// </summary>
    public class DilemmaService : MonoBehaviour, ISaveable
    {
        /// <summary>Tests: off deals nothing (a card's thoughts and gold would change what a test measures).</summary>
        public static bool Enabled = true;

        /// <summary>
        /// A dealt card opens the window, which holds the game until it is closed (user decision 2026-11-02). Measurement runs
        /// that leave the game to itself turn it off; the cards then wait for their deadlines.
        /// </summary>
        public static bool AutoOpen = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Enabled = AutoOpen = true;

        /// <summary>A card due to follow another.</summary>
        sealed class Pending
        {
            public string Id;
            public float DueHour;
            public int ParentDay;
            public List<string> Actors = new(), Names = new();
            public int[] Gold;
            public string Village, Species;
        }

        GameClock _clock;
        GuildRoster _roster;
        GuildTreasury _treasury;
        WorldService _world;
        int _seed;
        int _nextId = 1;
        readonly List<Dilemma> _open = new();
        readonly List<Pending> _pending = new();
        readonly List<DilemmaRecord> _history = new();
        readonly Dictionary<string, float> _lastDealt = new();

        public IReadOnlyList<Dilemma> Open => _open;
        public IReadOnlyList<DilemmaRecord> History => _history;

        float Now => _clock.TotalHours;

        /// <summary>Game hours of give on a due time (the 2026-10-31 measure: every deadline came an hour late, at 25 hours).</summary>
        const float DueSlack = 0.1f;

        public void Init(GameClock clock, GuildRoster roster, GuildTreasury treasury, WorldService world, int seed)
        {
            _clock = clock;
            _roster = roster;
            _treasury = treasury;
            _world = world;
            _seed = seed;
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnDestroy() => EventBus.Unsubscribe<HourPassedEvent>(OnHour);

        // ---------------- The round ----------------

        void OnHour(HourPassedEvent e)
        {
            if (!Enabled) return;
            // A card dealt on the hour is a few minutes past it by the time its own hour strikes again: that hour is its due.
            float now = Now + DueSlack;
            foreach (var card in _open.Where(c => now >= c.DeadlineHour).ToList()) Resolve(card, DefaultChoice(card), byDeadline: true);
            foreach (var p in _pending.Where(p => now >= p.DueHour).ToList())
            {
                if (_open.Count >= DilemmaRules.MaxOpen) break;
                _pending.Remove(p);
                DealFollowUp(p);
            }
            if (!DilemmaRules.DealHour(e.Hour) || e.Day < DilemmaRules.FirstDay || _open.Count >= DilemmaRules.MaxOpen) return;
            var phase = Services.TryGet<StorytellerService>(out var story) && StorytellerService.Enabled ? story.Phase : StoryPhase.Calm;
            var rng = StorytellerRules.Stream(_seed, Mathf.FloorToInt(now), 7);
            if (rng.Value < DilemmaRules.DealChance(phase)) Deal(rng);
        }

        /// <summary>Deals a card now (a random one that can come, or <paramref name="id"/>). Null when none could.</summary>
        public Dilemma Deal(GameRandom rng = null, string id = null)
        {
            rng ??= StorytellerRules.Stream(_seed, Mathf.FloorToInt(Now), 7);
            var facts = Facts();
            float now = Now;
            List<DilemmaDef> pool;
            if (id != null)
            {
                var def = DilemmaRules.Find(id);
                if (def == null || !facts.HasCast(def.Cast)) return null;
                pool = new List<DilemmaDef> { def };
            }
            else pool = DilemmaRules.All.Where(d => DilemmaRules.CanCome(d, facts) && _open.All(c => c.Def != d)
                                                     && (!_lastDealt.TryGetValue(d.Id, out float last) || now - last >= DilemmaRules.CooldownHours)).ToList();
            if (pool.Count == 0) return null;
            float total = pool.Sum(DilemmaRules.Weight);
            float roll = rng.Range(0f, total);
            var pick = pool[^1];
            foreach (var d in pool)
                if ((roll -= DilemmaRules.Weight(d)) <= 0f) { pick = d; break; }

            var villages = KnownVillages();
            var kinds = KnownKinds();
            var card = new Dilemma
            {
                Id = _nextId++, Def = pick, DealtHour = now, DeadlineHour = now + DilemmaRules.DeadlineHours,
                Gold = pick.Gold.Select(g => DilemmaRules.ScaledGold(g, _clock.Day)).ToArray(),
                Village = villages.Count > 0 ? villages[rng.Range(0, villages.Count)] : null,
                Species = kinds.Count > 0 ? kinds[rng.Range(0, kinds.Count)] : null,
            };
            foreach (var a in Cast(pick.Cast, rng))
            {
                card.Actors.Add(a.Data.id);
                card.Names.Add(a.Data.name);
            }
            return Lay(card);
        }

        void DealFollowUp(Pending p)
        {
            var def = DilemmaRules.Find(p.Id);
            if (def == null) return;
            var card = new Dilemma
            {
                Id = _nextId++, Def = def, DealtHour = Now, DeadlineHour = Now + DilemmaRules.DeadlineHours,
                // Its own amounts as of the day the first card came (the investment's return on what was put in).
                Gold = def.Gold.Length > 0 ? def.Gold.Select(g => DilemmaRules.ScaledGold(g, p.ParentDay)).ToArray() : p.Gold,
                Village = p.Village, Species = p.Species,
            };
            card.Actors.AddRange(p.Actors);
            card.Names.AddRange(p.Names);
            Lay(card);
        }

        /// <summary>Puts a card on the table and tells of it.</summary>
        Dilemma Lay(Dilemma card)
        {
            _open.Add(card);
            _lastDealt[card.Def.Id] = card.DealtHour;
            Debug.Log($"[Dilemma] #{card.Id} {card.Def.Id}: {card.Text}");
            EventBus.Publish(new DilemmaDealtEvent { Card = card });
            return card;
        }

        // ---------------- Deciding ----------------

        /// <summary>Why this choice cannot be made now (null if it can): gold or things short.</summary>
        public string CannotChoose(Dilemma card, int choice)
        {
            if (card == null || !_open.Contains(card) || choice < 0 || choice >= card.Def.Choices.Length) return "이미 정한 일입니다";
            var c = card.Def.Choices[choice];
            if (c.Cost >= 0 && c.Cost < card.Gold.Length && _treasury.Gold < card.Gold[c.Cost]) return $"골드가 모자랍니다 ({card.Gold[c.Cost]}G 필요)";
            if (c.NeedsItem != null && _treasury.GetStock(c.NeedsItem) < c.NeedsCount) return "물자가 모자랍니다";
            return null;
        }

        /// <summary>The guild master decides. False if the choice cannot be made.</summary>
        public bool Choose(Dilemma card, int choice)
        {
            if (CannotChoose(card, choice) != null) return false;
            Resolve(card, choice, byDeadline: false);
            return true;
        }

        /// <summary>The card's default, or the first choice the guild can still make if it cannot.</summary>
        int DefaultChoice(Dilemma card)
        {
            if (CannotChoose(card, card.Def.Default) == null) return card.Def.Default;
            for (int i = 0; i < card.Def.Choices.Length; i++)
                if (CannotChoose(card, i) == null) return i;
            return card.Def.Default;
        }

        void Resolve(Dilemma card, int choice, bool byDeadline)
        {
            _open.Remove(card);
            var effects = new Effects(this, card);
            var picked = card.Def.Choices[choice];
            picked.Apply?.Invoke(effects);
            string result = string.Join(" ", effects.Told.Concat(effects.Notes));
            var tone = DilemmaRules.ToneOf(picked, effects.Rolled);
            _history.Add(new DilemmaRecord { Title = card.Title, Choice = card.Label(choice), Result = result, Hour = Now, ByDeadline = byDeadline, Tone = tone });
            if (_history.Count > DilemmaRules.MaxHistory) _history.RemoveAt(0);
            // The cast remember it (from the event cards: their talks and counselling bring it up).
            foreach (var id in card.Actors)
                if (Member(id) is { } a) AdventurerMemory.Add(a.Data.memories, Now, 1, $"결정 '{card.Title}': {result}", "dilemma:" + card.Def.Id);
            Debug.Log($"[Dilemma] #{card.Id} {card.Def.Id} → {choice}{(byDeadline ? " (deadline)" : "")} {tone}: {result}");
            EventBus.Publish(new DilemmaResolvedEvent { Card = card, Choice = choice, ByDeadline = byDeadline, Result = result, Tone = tone });
        }

        // ---------------- What the guild has and knows ----------------

        IEnumerable<Adventurer> Members() => _roster.Members.Where(a => a != null && a.IsAlive && a.IsGuildMember && !a.IsVisitor);

        bool IsIdleInTown(Adventurer a) => a.CurrentQuest == null && _world.Grid.IsInTown(a.Position) && a.Data.fatigue < 60f;

        IEnumerable<(Adventurer a, Adventurer b)> Pairs(System.Func<int, bool> opinion)
        {
            var m = Members().ToList();
            for (int i = 0; i < m.Count; i++)
            for (int j = 0; j < m.Count; j++)
                if (i != j && opinion(Relationships.Get(m[i], m[j]))) yield return (m[i], m[j]);
        }

        IEnumerable<Adventurer> Newcomers() => Members().Where(a => a.Data.level <= DilemmaRules.NewcomerLevel);
        IEnumerable<Adventurer> Mentors() => Members().Where(a => a.Data.level >= DilemmaRules.NewcomerLevel + 4);

        /// <summary>Members who could wear better gear, the worst-equipped first (the shady trader's mark).</summary>
        IEnumerable<Adventurer> CouldGearUp()
            => Services.TryGet<GameDatabase>(out var db)
                ? Members().Where(a => GearShopping.NextGear(a.Data, db) != null).OrderBy(a => a.Data.gearTier).ThenBy(a => a.Data.level)
                : Enumerable.Empty<Adventurer>();

        /// <summary>Standing villages the guild knows, by name.</summary>
        static List<string> KnownVillages()
            => Services.TryGet<Villages.VillageService>(out var villages)
                ? villages.Villages.Where(v => v.Known && !v.IsDestroyed).Select(v => v.DisplayName).ToList() : new List<string>();

        /// <summary>Kinds the guild has seen (not a lord or the dragon), by name.</summary>
        static List<string> KnownKinds()
            => Services.TryGet<Ecosystem.GuildIntel>(out var intel)
                ? intel.KnownSpecies.Where(s => !s.isRegionLord && !s.isDragon).Select(s => s.displayName).ToList() : new List<string>();

        public DilemmaFacts Facts()
        {
            var members = Members().ToList();
            return new DilemmaFacts
            {
                Day = _clock.Day, Season = SeasonService.Now, Gold = _treasury.Gold, Members = members.Count,
                Rivals = Pairs(v => v <= DilemmaRules.RivalAt).Any(), Friends = Pairs(v => v >= DilemmaRules.FriendAt).Any(),
                Veteran = members.Any(a => a.Data.level >= DilemmaRules.VeteranLevel),
                NewcomerAndVeteran = Newcomers().Any() && Mentors().Any(),
                InTown = members.Any(IsIdleInTown),
                GearUp = CouldGearUp().Any(),
                Residents = Services.TryGet<ResidentService>(out var residents) && residents.Residents.Count > 0,
                Village = KnownVillages().FirstOrDefault(), Species = KnownKinds().FirstOrDefault(),
                Potions = _treasury.GetStock(DilemmaRules.Potion),
                GatesWorn = Services.TryGet<GateService>(out var gates) && gates.Gates.Any(g => g.Hp < g.MaxHp * 0.9f),
                RuinToFind = Services.TryGet<IncidentService>(out var incidents) && incidents.RuinLeftToFind,
            };
        }

        /// <summary>Whom a card is about, picked by its own dice.</summary>
        List<Adventurer> Cast(DilemmaCast cast, GameRandom rng)
        {
            T Pick<T>(IList<T> list) => list[rng.Range(0, list.Count)];
            var members = Members().ToList();
            switch (cast)
            {
                case DilemmaCast.Anyone: return new List<Adventurer> { Pick(members) };
                case DilemmaCast.Veteran: return new List<Adventurer> { Pick(members.Where(a => a.Data.level >= DilemmaRules.VeteranLevel).ToList()) };
                case DilemmaCast.InTown: return new List<Adventurer> { Pick(members.Where(IsIdleInTown).ToList()) };
                case DilemmaCast.TwoRivals:
                {
                    var (a, b) = Pick(Pairs(v => v <= DilemmaRules.RivalAt).ToList());
                    return new List<Adventurer> { a, b };
                }
                case DilemmaCast.TwoFriends:
                {
                    var (a, b) = Pick(Pairs(v => v >= DilemmaRules.FriendAt).ToList());
                    return new List<Adventurer> { a, b };
                }
                case DilemmaCast.NewcomerAndVeteran:
                    return new List<Adventurer> { Pick(Newcomers().ToList()), Pick(Mentors().ToList()) };
                case DilemmaCast.WeakestGear: return new List<Adventurer> { CouldGearUp().First() };
                default: return new List<Adventurer>();
            }
        }

        Adventurer Member(string id) => Members().FirstOrDefault(a => a.Data.id == id);

        // ---------------- Carrying a choice out ----------------

        sealed class Effects : IDilemmaEffects
        {
            readonly DilemmaService _s;
            readonly Dilemma _card;
            readonly GameRandom _rng;
            public readonly List<string> Told = new();
            /// <summary>Small print after what was told (who the wanderer is).</summary>
            public readonly List<string> Notes = new();
            /// <summary>How a rolled choice went (null: it was not rolled).</summary>
            public bool? Rolled;

            public Effects(DilemmaService s, Dilemma card)
            {
                _s = s;
                _card = card;
                // The card's own luck: decided the moment it was dealt, whenever it is answered (and after a load).
                _rng = StorytellerRules.Stream(s._seed, card.Id, 11);
            }

            IEnumerable<Adventurer> Who(int actor)
            {
                if (actor < 0) return _s.Members();
                var a = actor < _card.Actors.Count ? _s.Member(_card.Actors[actor]) : null;
                return a == null ? Enumerable.Empty<Adventurer>() : new[] { a };
            }

            public int G(int index) => index >= 0 && index < _card.Gold.Length ? _card.Gold[index] : 0;
            public bool Chance(float p) => _rng.Value < p;

            public void Gold(int amount)
            {
                var t = _s._treasury;
                if (amount > 0) t.AddGold(amount, GoldReason.Decision);
                else if (amount < 0) t.TrySpendGold(Mathf.Min(-amount, t.Gold), GoldReason.Decision);
            }

            public void Stock(string itemId, int count)
            {
                var t = _s._treasury;
                if (count > 0) t.AddStock(itemId, count);
                else if (count < 0) t.TryRemoveStock(itemId, Mathf.Min(-count, t.GetStock(itemId)));
            }

            public void Think(int actor, ThoughtDef def, int about = -1)
            {
                string aboutId = about >= 0 && about < _card.Actors.Count ? _card.Actors[about] : null;
                string name = about >= 0 && about < _card.Names.Count ? _card.Names[about] : null;
                foreach (var a in Who(actor).ToList()) ThoughtRecorder.Give(a, def, aboutId, name);
            }

            public void ThinkOthers(ThoughtDef def)
            {
                foreach (var a in _s.Members().Where(a => !_card.Actors.Contains(a.Data.id)).ToList()) ThoughtRecorder.Give(a, def);
            }

            public void Stress(int actor, float amount)
            {
                foreach (var a in Who(actor)) a.Data.stress = Mathf.Clamp(a.Data.stress + amount, 0f, 100f);
            }

            /// <summary>Everyone's (-1) here means those idle in town: the ones the work was asked of.</summary>
            public void Fatigue(int actor, float amount)
            {
                foreach (var a in Who(actor).Where(a => actor >= 0 || _s.IsIdleInTown(a)))
                    a.Data.fatigue = Mathf.Clamp(a.Data.fatigue + amount, 0f, 100f);
            }

            public void Fame(int amount)
            {
                if (amount != 0 && Services.TryGet<Guild.GuildRankService>(out var rank)) rank.AddFame(amount, $"결정: {_card.Title}");
            }

            public void Relate(int a, int b, int delta)
            {
                var from = Who(a).FirstOrDefault();
                var to = Who(b).FirstOrDefault();
                if (from != null && to != null) Relationships.Add(from, to, delta, "길드의 결정");
            }

            public void Exp(int actor, int amount)
            {
                foreach (var a in Who(actor)) a.GainExp(amount);
            }

            public void Depart(int actor, string notice)
            {
                var a = Who(actor).FirstOrDefault();
                if (a == null) return;
                string text = _card.Fill(notice);
                Told.Add(text);
                if (Services.TryGet<Welfare.GuildWelfare>(out var welfare)) welfare.Release(a, text);
                else _s._roster.Dismiss(a);
            }

            public bool Visitor() => Services.TryGet<VisitorService>(out var v) && v.SpawnVisitor() != null;

            public bool RuinRumour() => Rumour(Services.TryGet<IncidentService>(out var i) ? i.StartRuin() : null);

            public bool RareRumour() => Rumour(Services.TryGet<IncidentService>(out var i) ? i.StartRareMonster() : null);

            /// <summary>The word comes now, with the card (not an hour or two later as a rumour would).</summary>
            bool Rumour(Incident incident)
            {
                if (incident == null) return false;
                incident.RumourAtHour = _s.Now;
                return true;
            }

            public void RepairGates()
            {
                if (!Services.TryGet<GateService>(out var gates)) return;
                foreach (var g in gates.Gates) if (g.Hp < g.MaxHp) g.RepairFull();
            }

            public void CutTax(float share) => _s._treasury.CutTodaysTax(share);

            public void FollowUp(string id, float hours)
            {
                _s._pending.Add(new Pending
                {
                    Id = id, DueHour = _s.Now + hours, ParentDay = Mathf.FloorToInt(_card.DealtHour / 24f) + 1,
                    Actors = new List<string>(_card.Actors), Names = new List<string>(_card.Names), Gold = _card.Gold,
                    Village = _card.Village, Species = _card.Species,
                });
            }

            public void Tell(string text) => Told.Add(_card.Fill(text));

            // ---- From KMS's event cards (P4-118) ----

            public int Level(int actor) => Who(actor).FirstOrDefault()?.Data.level ?? 1;

            public void Standing(int amount, string residentId = null)
            {
                if (amount == 0 || !Services.TryGet<ResidentService>(out var residents)) return;
                if (residentId != null) residents.Find(residentId)?.Profile.AddOpinion(amount);
                else foreach (var r in residents.Residents) r?.Profile.AddOpinion(amount);
            }

            public void Rumor(string text, int tone = 0)
            {
                if (!Services.TryGet<TownTalkService>(out var town)) return;
                var subjects = _card.Actors.Where(id => _s.Member(id) != null).ToList();
                var knownBy = new List<string>(subjects);
                if (Services.TryGet<ResidentService>(out var residents))
                    foreach (var id in DilemmaRules.Chatterboxes)
                        if (residents.Find(id) != null) knownBy.Add(id);
                town.Rumors.Add(_card.Fill(text), _s.Now, knownBy, subjects, guildTone: tone, subjectTone: subjects.Count > 0 ? tone : 0);
            }

            public void Feel(int actor, Emotion feeling, float hours = 5f)
            {
                if (feeling == Emotion.None || !Services.TryGet<EmotionService>(out var emotions)) return;
                foreach (var a in Who(actor)) emotions.Feel(a.Data.id, feeling, hours, _card.Title);
            }

            public bool GearUp(int actor)
            {
                var a = Who(actor).FirstOrDefault();
                var item = a != null && Services.TryGet<GameDatabase>(out var db) ? GearShopping.NextGear(a.Data, db) : null;
                if (item == null) return false;
                a.Data.gearTier = item.gearTier;
                a.RecalculateStats();
                return true;
            }

            public bool Wanderer(bool welcomed)
            {
                var v = Services.TryGet<VisitorService>(out var visitors) ? visitors.SpawnVisitor() : null;
                if (v == null || v.Data == null) return false;
                var d = v.Data;
                // A name to reckon with: above the guild's best, and a rare one.
                int best = _s.Members().Select(m => m.Data.level).DefaultIfEmpty(1).Max();
                d.level = Mathf.Max(d.level, best + 1);
                if (d.grade == AdventurerGrade.Common) d.grade = AdventurerGrade.Rare;
                v.RecalculateStats(true);
                if (welcomed) d.recruitAffinity = DilemmaRules.WelcomedAffinity;
                Notes.Add($"({d.name}, {AdventurerData.ClassLabel(d.cls)} Lv.{d.level})");
                return true;
            }

            public void Outcome(bool success) => Rolled = success;
        }

        // ---------------- Debug ----------------

        public string Describe()
        {
            var s = new StringBuilder($"결정 카드 {(Enabled ? "켬" : "끔")}: 열림 {_open.Count}, 후속 대기 {_pending.Count}\n");
            foreach (var c in _open)
            {
                s.AppendLine($"  #{c.Id} [{c.Def.Id}] {c.Title} — {c.Text} (기한 {c.DeadlineHour - Now:0.0}시간, 기본 {c.Def.Default})");
                for (int i = 0; i < c.Def.Choices.Length; i++)
                    s.AppendLine($"     {i}. {c.Label(i)} [{c.Hints(i)}]{(CannotChoose(c, i) is { } why ? " — " + why : "")}");
            }
            foreach (var p in _pending) s.AppendLine($"  후속 {p.Id} {p.DueHour - Now:0.0}시간 뒤");
            foreach (var r in _history) s.AppendLine($"  {r.Hour:0.0}시 {r.Title}: {r.Choice}{(r.ByDeadline ? " (기한)" : "")} → {r.Result}");
            var f = Facts();
            s.Append($"  사실: 라이벌 {f.Rivals} 친구 {f.Friends} 고참 {f.Veteran} 신입+선배 {f.NewcomerAndVeteran} 마을 {f.InTown} 이웃 {f.Village} 종 {f.Species} 유적 {f.RuinToFind} 성문 {f.GatesWorn}");
            return s.ToString();
        }

        // ---------------- Save ----------------

        [System.Serializable]
        class SavedCard
        {
            public int id;
            public string def;
            public List<string> actors = new(), names = new();
            public int[] gold;
            public string village, species;
            public float dealt, deadline;
        }

        [System.Serializable]
        class SavedPending
        {
            public string id;
            public float due;
            public int parent_day;
            public List<string> actors = new(), names = new();
            public int[] gold;
            public string village, species;
        }

        [System.Serializable]
        class SavedRecord
        {
            public string title, choice, result;
            public float hour;
            public bool by_deadline;
            public int tone;
        }

        [System.Serializable]
        class SaveData
        {
            public int next_id = 1;
            public List<SavedCard> open = new();
            public List<SavedPending> pending = new();
            public List<SavedRecord> history = new();
            public List<string> dealt_ids = new();
            public List<float> dealt_hours = new();
        }

        public string SaveKey => "dilemmas";

        public string Capture()
        {
            var s = new SaveData { next_id = _nextId };
            foreach (var c in _open)
                s.open.Add(new SavedCard
                {
                    id = c.Id, def = c.Def.Id, actors = new List<string>(c.Actors), names = new List<string>(c.Names), gold = c.Gold,
                    village = c.Village, species = c.Species, dealt = c.DealtHour, deadline = c.DeadlineHour,
                });
            foreach (var p in _pending)
                s.pending.Add(new SavedPending
                {
                    id = p.Id, due = p.DueHour, parent_day = p.ParentDay, actors = new List<string>(p.Actors), names = new List<string>(p.Names),
                    gold = p.Gold, village = p.Village, species = p.Species,
                });
            foreach (var r in _history)
                s.history.Add(new SavedRecord { title = r.Title, choice = r.Choice, result = r.Result, hour = r.Hour, by_deadline = r.ByDeadline, tone = (int)r.Tone });
            foreach (var kv in _lastDealt.OrderBy(kv => kv.Key))
            {
                s.dealt_ids.Add(kv.Key);
                s.dealt_hours.Add(kv.Value);
            }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _nextId = s.next_id;
            _open.Clear();
            foreach (var c in s.open)
            {
                var def = DilemmaRules.Find(c.def);
                if (def == null) continue;
                var card = new Dilemma
                {
                    Id = c.id, Def = def, Gold = c.gold ?? System.Array.Empty<int>(), Village = c.village, Species = c.species,
                    DealtHour = c.dealt, DeadlineHour = c.deadline,
                };
                card.Actors.AddRange(c.actors);
                card.Names.AddRange(c.names);
                _open.Add(card);
            }
            _pending.Clear();
            foreach (var p in s.pending)
                _pending.Add(new Pending
                {
                    Id = p.id, DueHour = p.due, ParentDay = p.parent_day, Actors = new List<string>(p.actors), Names = new List<string>(p.names),
                    Gold = p.gold ?? System.Array.Empty<int>(), Village = p.village, Species = p.species,
                });
            _history.Clear();
            foreach (var r in s.history)
                _history.Add(new DilemmaRecord { Title = r.title, Choice = r.choice, Result = r.result, Hour = r.hour, ByDeadline = r.by_deadline, Tone = (DilemmaTone)r.tone });
            _lastDealt.Clear();
            for (int i = 0; i < s.dealt_ids.Count && i < s.dealt_hours.Count; i++) _lastDealt[s.dealt_ids[i]] = s.dealt_hours[i];
        }
    }
}
