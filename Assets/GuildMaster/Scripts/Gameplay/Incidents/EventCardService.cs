using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.LLM;
using GuildMaster.Notifications;
using GuildMaster.Recruitment;
using GuildMaster.Session;
using GuildMaster.Social;
using GuildMaster.Town;
using GuildMaster.Welfare;
using UnityEngine;

namespace GuildMaster.Incidents
{
    /// <summary>A new event card is waiting for the guild master (the card window opens on it).</summary>
    public struct EventCardDrawnEvent { public DrawnCard Card; }

    /// <summary>An event card was answered (or ran out of time and got its default answer).</summary>
    public struct EventCardResolvedEvent { public DrawnCard Card; public bool Expired; }

    /// <summary>
    /// Deals the event cards (F-13, user request 2026-10-11): checks at 10:00 and 16:00 whether a card comes
    /// (<see cref="EventCards.ShouldDraw"/>: 0–2 a day, never during a raid or while one is waiting), picks one that
    /// fits the guild right now, and applies the answer from the card's table: gold, stress, fatigue, relationships,
    /// the residents' opinion of the guild, rumors, feelings, gear, a visitor. The model only describes the situation
    /// when it is idle; the fixed text stays otherwise. An unanswered card gets its default answer after
    /// <see cref="EventCards.ExpireHours"/>.
    /// </summary>
    public class EventCardService : IDisposable
    {
        /// <summary>Tests: off deals nothing on the hour (the card window holds the clock until answered).</summary>
        public static bool Enabled = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Enabled = true;

        public const string GoldReasonEvent = "event";
        public const int WelcomedAffinity = 60;
        const int HistorySize = 12;

        readonly GameClock _clock;
        readonly GuildRoster _roster;
        readonly GuildTreasury _treasury;
        readonly NotificationCenter _center;
        readonly LlmGateway _gateway;
        readonly Func<float> _roll;
        readonly List<DrawnCard> _history = new();
        readonly List<string> _recent = new();
        int _serial;
        int _day = -1;
        bool _disposed;

        public DrawnCard Pending { get; private set; }
        public IReadOnlyList<DrawnCard> History => _history;
        public int DrawnToday { get; private set; }

        float Now => _clock != null ? _clock.TotalHours : 0f;

        public EventCardService(GameClock clock, GuildRoster roster, GuildTreasury treasury, NotificationCenter center, LlmGateway gateway,
                                Func<float> roll = null)
        {
            _clock = clock;
            _roster = roster;
            _treasury = treasury;
            _center = center;
            _gateway = gateway;
            _roll = roll ?? (() => GameRandom.Shared.Value);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        public void Dispose()
        {
            _disposed = true;
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
        }

        static string Key(DrawnCard c) => "card:" + c.Serial;

        // ---------------- When cards come ----------------

        void OnHour(HourPassedEvent e)
        {
            if (!Enabled) return;
            if (e.Day != _day)
            {
                _day = e.Day;
                DrawnToday = 0;
            }
            if (Pending != null && Now - Pending.DrawnAt >= EventCards.ExpireHours)
                Resolve(Pending, Pending.Def.Choices.IndexOf(Pending.Def.DefaultChoice), true);
            bool raid = Services.TryGet<WaveManager>(out var waves) && waves.RaidActive;
            if (EventCards.ShouldDraw(e.Day, e.Hour, DrawnToday, Pending != null, raid, _roll())) DrawNow();
        }

        /// <summary>Draws a card now if one fits (also used by debug commands). False when none does or one is waiting.</summary>
        public bool DrawNow()
        {
            if (Pending != null) return false;
            var card = EventCards.Draw(BuildContext(), _roll);
            if (card == null) return false;
            card.Serial = ++_serial;
            card.DrawnAt = Now;
            Pending = card;
            DrawnToday++;
            _recent.Add(card.Def.Id);
            while (_recent.Count > EventCards.RecentMemory) _recent.RemoveAt(0);
            _center?.Post(NotificationKind.Quest, "사건 카드", $"{card.Title} — 길드 마스터의 결정을 기다립니다.", Key(card));
            EventBus.Publish(new EventCardDrawnEvent { Card = card });
            bool llmFree = _gateway != null && _gateway.Enabled && _gateway.IsReady && !_gateway.IsBusy && _gateway.QueueCount == 0;
            if (llmFree) _ = NarrateAsync(card);
            return true;
        }

        CardContext BuildContext()
        {
            Services.TryGet<GameDatabase>(out var db);
            Services.TryGet<GuildWelfare>(out var welfare);
            var ctx = new CardContext
            {
                Gold = _treasury != null ? _treasury.Gold : 0,
                Day = _clock != null ? _clock.Day : 1,
                HasResidents = Services.TryGet<ResidentService>(out var residents) && residents.Residents.Count > 0,
                HasVisitorRoom = Services.TryGet<VisitorService>(out _), // main has no member cap any more (2026-10-31 merge)
                Recent = _recent,
            };
            if (_roster != null)
                foreach (var a in _roster.Members)
                {
                    if (a == null || !a.IsAlive || a.Data == null || a.IsVisitor || !a.IsGuildMember) continue;
                    var d = a.Data;
                    bool onLeave = welfare != null && welfare.IsOnLeave(a);
                    ctx.Members.Add(new CardMember
                    {
                        Id = d.id, Name = d.name, Level = d.level, Stress = d.stress, Fatigue = d.fatigue, GearTier = d.gearTier,
                        CanGearUp = db != null && GearShopping.NextGear(d, db) != null,
                        Free = !onLeave && a.CurrentQuest == null && a.IsAtLeisureInTown,
                    });
                }
            ctx.Bond = (x, y) =>
            {
                var ax = _roster?.Find(x);
                var ay = _roster?.Find(y);
                return ax == null || ay == null ? 0 : RelationGraph.Bond(RelationGraph.Peek(ax.Data, y), RelationGraph.Peek(ay.Data, x));
            };
            return ctx;
        }

        // ---------------- Answering ----------------

        /// <summary>Why this choice cannot be taken now (null = it can).</summary>
        public string CannotChoose(DrawnCard card, int index)
        {
            if (card == null || card != Pending || card.Resolved) return "이미 지나간 사건입니다.";
            if (index < 0 || index >= card.Def.Choices.Count) return "없는 선택지입니다.";
            int cost = card.Def.Choices[index].Cost;
            if (cost > 0 && (_treasury == null || _treasury.Gold < cost)) return $"길드 자금이 부족합니다. (필요 {cost}G)";
            return null;
        }

        /// <summary>The guild master's answer. Returns what happened, or null when the choice could not be taken.</summary>
        public string Choose(DrawnCard card, int index)
        {
            if (CannotChoose(card, index) != null) return null;
            int cost = card.Def.Choices[index].Cost;
            if (cost > 0 && !_treasury.TrySpendGold(cost, GoldReasonEvent)) return null;
            Resolve(card, index, false);
            return card.ResultText;
        }

        void Resolve(DrawnCard card, int index, bool expired)
        {
            if (card == null || card.Resolved) return;
            if (index < 0 || index >= card.Def.Choices.Count) index = card.Def.Choices.Count - 1;
            var choice = card.Def.Choices[index];
            // An unanswered card's default never costs anything (nobody paid).
            var (ok, fx) = EventCards.Resolve(choice, card.A, card.B, _roll());
            var notes = new List<string>();
            Apply(card, fx, notes);
            string text = Fill(card, fx.Result);
            if (notes.Count > 0) text += " " + string.Join(" ", notes);
            if (expired) text = $"답이 없어 '{card.Label(index)}' 쪽으로 흘러갔습니다. " + text;
            card.Resolved = true;
            card.Chosen = index;
            card.Succeeded = ok;
            card.Expired = expired;
            card.ResultText = text;
            if (Pending == card) Pending = null;
            _history.Add(card);
            while (_history.Count > HistorySize) _history.RemoveAt(0);
            _center?.Resolve(Key(card));
            if (expired) _center?.Post(NotificationKind.Info, "사건 정리", $"{card.Title}: {text}");
            if (Services.TryGet<GameLog>(out var log)) log.Add($"[사건] {card.Title} — {card.Label(index)}: {text}", LogCategory.Info);
            Remember(card, $"사건 '{card.Title}': {Fill(card, fx.Result)}");
            EventBus.Publish(new EventCardResolvedEvent { Card = card, Expired = expired });
        }

        static string Fill(DrawnCard card, string template) => EventCards.Format(template, card.A?.Name, card.B?.Name);

        Adventurer Member(CardMember? m) => m.HasValue && _roster != null ? _roster.Find(m.Value.Id) : null;

        void Apply(DrawnCard card, CardEffect fx, List<string> notes)
        {
            var a = Member(card.A);
            var b = Member(card.B);
            float now = Now;
            Services.TryGet<GuildWelfare>(out var welfare);
            Services.TryGet<ResidentService>(out var residents);
            Services.TryGet<EmotionService>(out var emotions);

            if (fx.Gold > 0) _treasury?.AddGold(fx.Gold, GoldReasonEvent);
            else if (fx.Gold < 0 && _treasury != null)
            {
                int loss = Math.Min(_treasury.Gold, -fx.Gold);
                if (loss > 0) _treasury.TrySpendGold(loss, GoldReasonEvent);
            }

            if (welfare != null)
            {
                if (a != null && fx.StressA != 0f) welfare.AdjustStress(a, fx.StressA);
                if (b != null && fx.StressB != 0f) welfare.AdjustStress(b, fx.StressB);
                if (fx.StressAll != 0f && _roster != null)
                    foreach (var m in _roster.Members.ToList())
                        if (m != null && m.IsAlive) welfare.AdjustStress(m, fx.StressAll);
            }
            if (a != null && fx.FatigueA != 0f) a.Data.fatigue = Mathf.Clamp(a.Data.fatigue + fx.FatigueA, 0f, 100f);

            if (a != null && b != null && fx.RelationAB != 0)
            {
                Relationships.Add(a, b, fx.RelationAB, "사건: " + card.Title);
                Relationships.Add(b, a, fx.RelationAB, "사건: " + card.Title);
            }

            if (residents != null)
            {
                if (fx.Opinion != 0)
                    foreach (var r in residents.Residents)
                        r?.Profile.AddOpinion(fx.Opinion);
                if (!string.IsNullOrEmpty(fx.ResidentId) && fx.ResidentOpinion != 0)
                    residents.Find(fx.ResidentId)?.Profile.AddOpinion(fx.ResidentOpinion);
            }

            if (fx.GearA && a != null)
            {
                var item = Services.TryGet<GameDatabase>(out var db) ? GearShopping.NextGear(a.Data, db) : null;
                if (item != null)
                {
                    a.Data.gearTier = item.gearTier;
                    a.RecalculateStats();
                }
                else notes.Add("하지만 이미 더 좋은 장비를 쓰고 있어 쓸모가 없었습니다.");
            }

            if (emotions != null)
            {
                if (a != null) emotions.Feel(a.Data.id, fx.EmotionA, fx.EmotionHours, card.Title);
                if (b != null) emotions.Feel(b.Data.id, fx.EmotionB, fx.EmotionHours, card.Title);
                if (fx.EmotionAll != Emotion.None && _roster != null)
                    foreach (var m in _roster.Members)
                        if (m != null && m.IsAlive) emotions.Feel(m.Data.id, fx.EmotionAll, fx.EmotionHours, card.Title);
            }

            if (!string.IsNullOrEmpty(fx.Rumor) && Services.TryGet<TownTalkService>(out var town))
            {
                var subjects = new List<string>();
                if (a != null) subjects.Add(a.Data.id);
                if (b != null) subjects.Add(b.Data.id);
                // The cast know it, and so does the town's chatterbox, who will pass it on.
                var knownBy = new List<string>(subjects);
                if (residents != null)
                    foreach (var id in new[] { "RES_elly", "RES_bartol" })
                        if (residents.Find(id) != null) knownBy.Add(id);
                town.Rumors.Add(Fill(card, fx.Rumor), now, knownBy, subjects, guildTone: fx.RumorTone, subjectTone: subjects.Count > 0 ? fx.RumorTone : 0);
            }

            if (fx.Wanderer != WandererOffer.None && Services.TryGet<VisitorService>(out var visitors))
            {
                var v = visitors.SpawnVisitor();
                if (v != null && v.Data != null)
                {
                    var d = v.Data;
                    int top = _roster != null && _roster.Members.Count > 0 ? _roster.Members.Max(m => m != null && m.Data != null ? m.Data.level : 1) : 1;
                    d.level = Math.Max(d.level, top + 1);
                    if (d.grade == AdventurerGrade.Common) d.grade = AdventurerGrade.Rare;
                    v.RecalculateStats(true);
                    if (fx.Wanderer == WandererOffer.Welcomed) d.recruitAffinity = WelcomedAffinity;
                    notes.Add($"({d.name}, {AdventurerData.ClassLabel(d.cls)} Lv.{d.level})");
                }
            }
        }

        void Remember(DrawnCard card, string text)
        {
            foreach (var m in new[] { Member(card.A), Member(card.B) })
                if (m != null && m.Data != null) AdventurerMemory.Add(m.Data.memories, Now, 1, text, "card:" + card.Def.Id);
        }

        // ---------------- The model's description ----------------

        async Task NarrateAsync(DrawnCard card)
        {
            try
            {
                string time = _clock != null ? $"{_clock.Day}일차 {_clock.Hour}시" : "";
                var result = await _gateway.RequestAsync(EventCardPrompt.BuildRequest(card, CastNotes(card), time));
                if (_disposed || card.Resolved) return;
                EventCardPrompt.ResponseDto dto = null;
                string text = result.Ok && LlmJson.TryParse<EventCardPrompt.ResponseDto>(result.Text, out dto) ? EventCardPrompt.Interpret(dto, card) : null;
                if (result.Exchange != null) result.Exchange.Note = text != null ? "사건 묘사 채택" : "고정 문장 유지: " + (result.Ok ? "parse/이름 누락" : result.Error);
                if (text == null) return;
                card.Narration = text;
                card.NarratedByLlm = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[EventCard] narration failed: " + ex.Message);
            }
        }

        string CastNotes(DrawnCard card)
        {
            var parts = new List<string>();
            foreach (var m in new[] { card.A, card.B })
            {
                if (!m.HasValue) continue;
                var a = Member(m);
                parts.Add(a != null && a.Data != null
                    ? $"{a.Data.name}({AdventurerData.ClassLabel(a.Data.cls)}, {a.Data.mbti}, 가치관 {a.Data.values})"
                    : m.Value.Name);
            }
            return string.Join(", ", parts);
        }
    }
}
