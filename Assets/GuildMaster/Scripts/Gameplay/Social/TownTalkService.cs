using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.LLM;
using GuildMaster.Notifications;
using GuildMaster.Quests;
using GuildMaster.Recruitment;
using GuildMaster.Session;
using GuildMaster.Town;
using GuildMaster.Units;
using GuildMaster.Welfare;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Social
{
    /// <summary>
    /// Asks the UI to show a speech bubble over someone who is not an adventurer (the merchant, residents). Gameplay
    /// cannot see the UI's own speech event, so <c>UI.TownSpeechRelay</c> passes this on.
    /// </summary>
    public struct TownSpeechEvent
    {
        public Transform Speaker;
        public string Text;
    }

    /// <summary>A town talk started playing (debug view, tests of the wiring).</summary>
    public struct TownTalkEvent
    {
        public TownTalkKind Kind;
        public TalkOutcome Outcome;
        public string A, B;
        /// <summary>Who talked (ids of members, visitors, residents or the merchant), for feelings and the relationship graph (F-10).</summary>
        public string AId, BId;
        public bool FromLlm;
    }

    /// <summary>Someone who can take part in a town talk: a member, a visitor, a resident or the merchant.</summary>
    public class Talker
    {
        public TalkerKind Kind;
        /// <summary>Members and visitors.</summary>
        public Adventurer Adventurer;
        /// <summary>Townsfolk.</summary>
        public Resident Resident;
        public Unit Unit;
        public string Id;
        public string Name;

        public Vector2 Position => Unit != null ? Unit.Position : Vector2.zero;

        public static Talker Of(Adventurer a) => new()
        {
            Kind = a.IsVisitor ? TalkerKind.Visitor : TalkerKind.Member, Adventurer = a, Unit = a, Id = a.Data.id, Name = a.Data.name,
        };

        public static Talker Of(Merchant m) => new()
        {
            Kind = TalkerKind.Merchant, Unit = m, Id = TownTalkService.MerchantId, Name = m.DisplayName,
        };

        public static Talker Of(Resident r) => new()
        {
            Kind = TalkerKind.Resident, Resident = r, Unit = r, Id = r.Profile.Id, Name = r.Profile.Name,
        };

        public void Say(string text)
        {
            if (Adventurer != null) Adventurer.Say(text);
            else if (Unit != null) EventBus.Publish(new TownSpeechEvent { Speaker = Unit.transform, Text = text });
        }
    }

    /// <summary>
    /// Everyday life in town (user requests 2026-10-01 and 2026-10-07: "모든 npc간의 대화", "마을에 있는 주민이나
    /// 방문자간의 대화나 영입이 아예 없는 느낌"). Anyone in town — members, visitors, the residents
    /// (<see cref="ResidentService"/>) and the travelling merchant — strikes up four-line talks with someone nearby:
    /// small talk, a friend cheering up someone who is struggling, quarrels and making up, rumors
    /// (<see cref="RumorBook"/>), residents talking about their work or putting in a word for (or against) the guild,
    /// a member or a resident asking a visitor to join (<see cref="TownTalkKind.Invite"/>: the visitor answers on the
    /// spot, and a yes is a new member). What a talk is about comes from who they are and how they feel
    /// (<see cref="TownTalkRules"/>), and how it went changes feelings, stress, visitors' minds and the residents'
    /// opinion of the guild.
    ///
    /// Up to <see cref="MaxConcurrentTalks"/> talks at once. The model writes a talk only when it is idle (and one at a
    /// time); otherwise fixed lines play (user decision 2026-10-07), so the town is never silent. A resident walks over
    /// to the one they talk to first. Next to the older member small talk (BanterService), which it leaves as it is.
    /// </summary>
    public class TownTalkService : MonoBehaviour
    {
        public const string MerchantId = "MERCHANT";
        /// <summary>Chances to start a talk per game hour, and the chance each one does.</summary>
        public const float AttemptsPerGameHour = 3f;
        public const float Chance = 0.8f;
        public const float MaxPairDistance = 9f;
        public const int MaxConcurrentTalks = 2;
        /// <summary>Game-time seconds between two lines of a talk (the bubbles need time to be read).</summary>
        public const float LineDelay = 2.6f;
        /// <summary>Game-time seconds between attempts at least.</summary>
        const float MinAttemptSeconds = 6f;
        /// <summary>A resident walks over when farther than this, and the talk starts once within <see cref="TalkDistance"/>.</summary>
        const float ApproachDistance = 2.5f;
        const float TalkDistance = 2.0f;
        const float ApproachTimeout = 10f;

        static readonly string[] OutsideRumors =
        {
            "북쪽 고갯길에 늑대 떼가 부쩍 늘었다",
            "옆 마을 대장간이 문을 닫아서 무기값이 오른다",
            "동쪽 폐광에서 밤마다 이상한 불빛이 보인다",
            "올해는 밀 농사가 잘돼서 빵값이 내린다",
            "수도의 큰 길드가 실력 좋은 모험가를 찾고 있다",
            "남쪽 늪지대에 커다란 뭔가가 산다",
        };

        LlmGateway _gateway;
        GuildRoster _roster;
        WorldService _world;
        VisitorService _visitors;
        MerchantService _merchant;
        ResidentService _residents;
        GameClock _clock;
        float _timer;
        bool _inFlight;
        int _nextTalkId = 1;
        readonly HashSet<string> _busy = new();
        readonly Dictionary<string, float> _pairTalkedAt = new();
        readonly Dictionary<string, (float hour, string about)> _fights = new();
        readonly List<(int talk, Talker speaker, Talker listener, string line, float at)> _scheduled = new();
        readonly Dictionary<int, (Talker a, Talker b)> _active = new();
        readonly List<Pending> _approaching = new();
        readonly List<string> _recent = new();

        class Pending
        {
            public Talker A, B;
            public TownTalkKind Kind;
            public Rumor Rumor;
            public string Detail;
            public float Waited;
        }

        public RumorBook Rumors { get; } = new();
        public int Talks { get; private set; }
        public int LlmTalks { get; private set; }
        public int FixedTalks { get; private set; }
        /// <summary>Talks played from the written dialogue database (<see cref="TalkBank"/>).</summary>
        public int BankTalks { get; private set; }
        /// <summary>
        /// Whether the model may write town talks when it is idle. Off since the team decision of 2026-10-18: everyday
        /// talk comes from the written database, the model is kept for recruiting and counselling.
        /// </summary>
        public static bool UseLlm = false;
        /// <summary>Scripts this many talks back are not used again while others fit.</summary>
        public const int RecentScriptMemory = 40;
        TalkBank _bank;
        readonly List<string> _recentScripts = new();
        public TalkBank Bank => _bank;
        public int TownRecruits { get; private set; }
        /// <summary>The latest talks, newest last ("[Comfort/warm] 아리스 → 리아"), for debugging.</summary>
        public IReadOnlyList<string> Recent => _recent;

        public void Init(LlmGateway gateway, GuildRoster roster, WorldService world, VisitorService visitors, MerchantService merchant, GameClock clock)
        {
            _gateway = gateway;
            _roster = roster;
            _world = world;
            _visitors = visitors;
            _merchant = merchant;
            _clock = clock;
            _bank = TalkBank.LoadResources();
            Debug.Log($"[TOWN-TALK] dialogue database: {_bank.Count} scripts");
            EventBus.Subscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Subscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Subscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Subscribe<AdventurerLeftEvent>(OnLeft);
            EventBus.Subscribe<CounselEndedEvent>(OnCounsel);
            EventBus.Subscribe<MerchantArrivedEvent>(OnMerchant);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        /// <summary>The townsfolk (created after this service in GameBootstrap).</summary>
        public void SetResidents(ResidentService residents) => _residents = residents;

        void OnDestroy()
        {
            EventBus.Unsubscribe<QuestClosedEvent>(OnQuestClosed);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnDied);
            EventBus.Unsubscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Unsubscribe<AdventurerLeftEvent>(OnLeft);
            EventBus.Unsubscribe<CounselEndedEvent>(OnCounsel);
            EventBus.Unsubscribe<MerchantArrivedEvent>(OnMerchant);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
        }

        float Now => _clock != null ? _clock.TotalHours : 0f;

        void Update()
        {
            if (_clock == null) return;
            // Game time: nothing is said while the world stands still (pause, a dialogue window).
            float dt = Time.deltaTime;
            TickApproaches(dt);
            for (int i = _scheduled.Count - 1; i >= 0; i--)
            {
                var s = _scheduled[i];
                s.at -= dt;
                _scheduled[i] = s;
                if (s.at > 0f) continue;
                _scheduled.RemoveAt(i);
                if (StillThere(s.speaker))
                {
                    if (s.speaker.Unit != null && s.listener?.Unit != null && s.speaker.Unit.Visual != null)
                        s.speaker.Unit.Visual.FaceTowards(s.listener.Position);
                    s.speaker.Say(s.line);
                }
                if (!_scheduled.Exists(x => x.talk == s.talk)) EndTalk(s.talk);
            }

            // Safety net: nobody stays "busy" when no talk is going on.
            if (_busy.Count > 0 && _active.Count == 0 && _approaching.Count == 0 && !_inFlight) ReleaseAll();

            _timer += dt;
            float interval = Mathf.Max(MinAttemptSeconds, _clock.RealSecondsPerGameHour / AttemptsPerGameHour);
            if (_timer < interval) return;
            _timer = 0f;
            if (GameRandom.Shared.Value < Chance) TryStart();
        }

        // ---------------- Who can talk ----------------

        bool InTown(Unit u) => u != null && _world != null && _world.Grid.IsInTown(u.Position);

        bool AvailableMember(Adventurer x)
            => x != null && x.IsAlive && !x.IsVisitor && x.IsGuildMember && x.CurrentQuest == null && !x.InConversation
               && (x.Fsm.Current == x.IdleState || x.Fsm.Current == x.DecidingState) && InTown(x)
               && !(Services.TryGet<PartyService>(out var party) && party.IsInvited(x));

        bool AvailableVisitor(Adventurer x)
            => x != null && x.IsAlive && x.IsVisitor && !x.InConversation && x.Fsm.Current == x.VisitorWanderState && InTown(x);

        bool AvailableResident(Resident r) => r != null && r.IsAlive && !r.InTalk && !r.Asleep && InTown(r);

        bool StillThere(Talker t)
        {
            if (t == null || t.Unit == null || !t.Unit.IsAlive) return false;
            if (t.Resident != null) return InTown(t.Unit);
            if (t.Adventurer == null) return _merchant != null && _merchant.Merchant == t.Unit && _merchant.IsOpen;
            return t.Adventurer.CurrentQuest == null && !t.Adventurer.InConversation && InTown(t.Unit);
        }

        List<Talker> AvailableTalkers()
        {
            var list = new List<Talker>();
            if (_roster != null)
                foreach (var m in _roster.Members) if (AvailableMember(m) && !_busy.Contains(m.Data.id)) list.Add(Talker.Of(m));
            if (_visitors != null)
                foreach (var v in _visitors.Visitors) if (AvailableVisitor(v) && !_busy.Contains(v.Data.id)) list.Add(Talker.Of(v));
            if (_residents != null)
                foreach (var r in _residents.Residents) if (AvailableResident(r) && !_busy.Contains(r.Profile.Id)) list.Add(Talker.Of(r));
            if (_merchant != null && _merchant.IsOpen && _merchant.Merchant != null && !_busy.Contains(MerchantId)) list.Add(Talker.Of(_merchant.Merchant));
            return list;
        }

        static string PairKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;

        static int Feeling(Talker from, Talker to)
        {
            if (from.Adventurer != null && to.Adventurer != null) return Relationships.Get(from.Adventurer, to.Adventurer);
            if (from.Resident != null) return from.Resident.Profile.FeelingToward(to.Id);
            if (to.Resident != null) return to.Resident.Profile.FeelingToward(from.Id);
            return 0;
        }

        TalkContext Context(Talker a, Talker b)
        {
            float now = Now;
            var visitor = a.Kind == TalkerKind.Visitor ? a : b.Kind == TalkerKind.Visitor ? b : null;
            return new TalkContext
            {
                KindA = a.Kind,
                KindB = b.Kind,
                FeelAB = Feeling(a, b),
                FeelBA = Feeling(b, a),
                StressA = a.Kind == TalkerKind.Member ? a.Adventurer.Data.stress : 0f,
                StressB = b.Kind == TalkerKind.Member ? b.Adventurer.Data.stress : 0f,
                RecentFight = _fights.TryGetValue(PairKey(a.Id, b.Id), out var fight) && now - fight.hour <= TownTalkRules.MakeUpWindowHours,
                AHasNews = Rumors.HasNewsFor(a.Id, b.Id, now),
                VisitorAffinity = visitor != null ? visitor.Adventurer.Data.recruitAffinity : -1,
                ResidentOpinionA = a.Resident != null ? a.Resident.Profile.GuildOpinion : 0,
            };
        }

        /// <summary>How much a pair is worth picking: people with something to settle, share or offer come first.</summary>
        float Weight(Talker a, Talker b)
        {
            float w = 1f;
            var ab = Context(a, b);
            var ba = Context(b, a);
            if (TownTalkRules.WantsComfort(ab) || TownTalkRules.WantsComfort(ba)) w *= 3f;
            if (TownTalkRules.WantsInvite(ab) || TownTalkRules.WantsInvite(ba)) w *= 2.5f;
            if (ab.RecentFight) w *= 2f;
            if (Mathf.Min(ab.FeelAB, ab.FeelBA) <= -15) w *= 1.5f;
            if (ab.AHasNews || ba.AHasNews) w *= 1.4f;
            if (a.Kind == TalkerKind.Merchant || b.Kind == TalkerKind.Merchant) w *= 1.5f;
            if (a.Kind == TalkerKind.Visitor || b.Kind == TalkerKind.Visitor) w *= 2f;
            return w;
        }

        (Talker, Talker)? PickPair()
        {
            var talkers = AvailableTalkers();
            float now = Now;
            var pairs = new List<(Talker a, Talker b, float w)>();
            for (int i = 0; i < talkers.Count; i++)
                for (int j = i + 1; j < talkers.Count; j++)
                {
                    var a = talkers[i];
                    var b = talkers[j];
                    // Residents walk over to whoever they want to talk to; the others talk to whoever is near.
                    float reach = a.Resident != null || b.Resident != null ? MaxPairDistance * 2f : MaxPairDistance;
                    if (Vector2.Distance(a.Position, b.Position) > reach) continue;
                    if (_pairTalkedAt.TryGetValue(PairKey(a.Id, b.Id), out var at) && now - at < TownTalkRules.PairCooldownHours) continue;
                    pairs.Add((a, b, Weight(a, b)));
                }
            if (pairs.Count == 0) return null;
            float total = pairs.Sum(p => p.w);
            float roll = GameRandom.Shared.Value * total;
            foreach (var p in pairs)
            {
                roll -= p.w;
                if (roll <= 0f) return (p.a, p.b);
            }
            var last = pairs[pairs.Count - 1];
            return (last.a, last.b);
        }

        /// <summary>Who speaks first: the friend reaching out, the one inviting, the one with news, else either.</summary>
        (Talker, Talker) Orient(Talker a, Talker b)
        {
            var ab = Context(a, b);
            var ba = Context(b, a);
            if (TownTalkRules.WantsComfort(ba) && !TownTalkRules.WantsComfort(ab)) return (b, a);
            if (TownTalkRules.WantsComfort(ab)) return (a, b);
            if (TownTalkRules.WantsInvite(ba) && !TownTalkRules.WantsInvite(ab)) return (b, a);
            if (TownTalkRules.WantsInvite(ab)) return (a, b);
            if (ba.AHasNews && !ab.AHasNews) return (b, a);
            if (ab.AHasNews && !ba.AHasNews) return (a, b);
            // A resident usually starts it (they walk over).
            if (b.Resident != null && a.Resident == null) return (b, a);
            if (a.Resident != null && b.Resident == null) return (a, b);
            return GameRandom.Shared.Value < 0.5f ? (a, b) : (b, a);
        }

        int ActiveTalks => _active.Count + _approaching.Count + (_inFlight ? 1 : 0);

        // ---------------- Starting a talk ----------------

        /// <summary>Starts a talk now if a pair is free. False if nothing started (also used by debug commands).</summary>
        public bool TryStart()
        {
            if (ActiveTalks >= MaxConcurrentTalks) return false;
            var pair = PickPair();
            if (pair == null) return false;
            var (a, b) = Orient(pair.Value.Item1, pair.Value.Item2);
            var ctx = Context(a, b);
            var kind = TownTalkRules.ChooseKind(ctx, GameRandom.Shared.Value);
            float now = Now;
            var rumor = kind == TownTalkKind.Gossip ? Rumors.Pick(a.Id, b.Id, now) : null;
            if (kind == TownTalkKind.Gossip && rumor == null) kind = TownTalkKind.Chat;
            // What the talk is concretely about: B's worry, A's grievance, the fight being made up, the visitor's mood.
            string detail = null;
            if (kind == TownTalkKind.Comfort && b.Adventurer != null)
            {
                Services.TryGet<GuildWelfare>(out var welfare);
                detail = CounselConcerns.Detect(CounselSession.ReadState(b.Adventurer, welfare?.Care(b.Adventurer))).Detail;
            }
            else if (kind == TownTalkKind.Quarrel) detail = TownTalkLines.Grievance(Ga(b.Name), GameRandom.Shared);
            else if (kind == TownTalkKind.MakeUp && _fights.TryGetValue(PairKey(a.Id, b.Id), out var fight)) detail = fight.about;
            else if (kind == TownTalkKind.Invite) detail = NpcVoice.RecruitMood(b.Adventurer.Data.recruitAffinity);

            _busy.Add(a.Id);
            _busy.Add(b.Id);
            var pending = new Pending { A = a, B = b, Kind = kind, Rumor = rumor, Detail = detail };
            // A resident walks over first (user report 2026-10-07: "진짜 사람처럼").
            if (a.Resident != null && Vector2.Distance(a.Position, b.Position) > ApproachDistance)
            {
                a.Resident.WalkTo(b.Position);
                _approaching.Add(pending);
                return true;
            }
            Begin(pending);
            return true;
        }

        void TickApproaches(float dt)
        {
            for (int i = _approaching.Count - 1; i >= 0; i--)
            {
                var p = _approaching[i];
                p.Waited += dt;
                bool gone = !StillThere(p.A) || !StillThere(p.B);
                bool close = Vector2.Distance(p.A.Position, p.B.Position) <= TalkDistance;
                if (!gone && !close && p.Waited < ApproachTimeout)
                {
                    // Keep heading for them if they moved.
                    if (!p.A.Resident.Approaching) p.A.Resident.WalkTo(p.B.Position);
                    continue;
                }
                _approaching.RemoveAt(i);
                if (gone || !close) { Release(p.A, p.B); continue; }
                Begin(p);
            }
        }

        /// <summary>Writes the talk with the model when it is free, with fixed lines otherwise (user decision 2026-10-07).</summary>
        void Begin(Pending p)
        {
            Hold(p.A, p.B, true);
            bool llmFree = UseLlm && !_inFlight && _gateway != null && _gateway.Enabled && _gateway.IsReady && !_gateway.IsBusy && _gateway.QueueCount == 0;
            if (llmFree)
            {
                _ = RunAsync(p);
                return;
            }
            if (PlayFromBank(p)) return;
            PlayFixed(p);
        }

        /// <summary>
        /// A talk from the written database (user decision 2026-10-18): how it goes is rolled from how the two feel
        /// (<see cref="TownTalkRules.RollOutcome"/>), then a script for that outcome and pair is played in their own
        /// speech levels. False when the database has nothing for them (the older fixed lines take over).
        /// </summary>
        bool PlayFromBank(Pending p)
        {
            if (_bank == null || _bank.Count == 0) return false;
            if (TownTalkRules.ShouldSwap(p.Kind, p.A.Kind, p.B.Kind)) (p.A, p.B) = (p.B, p.A);
            var ctx = Context(p.A, p.B);
            var outcome = TownTalkRules.RollOutcome(p.Kind, ctx, GameRandom.Shared.Value);
            var sideA = TalkSide.Of(p.A);
            var sideB = TalkSide.Of(p.B);
            var script = PickEitherWay(p, ref outcome, ref sideA, ref sideB);
            if (script == null) return false;
            var lines = TalkBank.Render(script, sideA, sideB, p.Rumor?.Text);
            if (lines.Count < 2) return false;
            if (!string.IsNullOrEmpty(script.about)) p.Detail = script.about;
            _recentScripts.Add(script.id);
            while (_recentScripts.Count > RecentScriptMemory) _recentScripts.RemoveAt(0);
            if (!Play(p, lines, outcome, false)) { Release(p.A, p.B); return true; }
            BankTalks++;
            FixedTalks--; // Play counted it as a fixed talk
            Debug.Log($"[TOWN-TALK] script {script.id} ({p.A.Name}:{sideA.Register}/{sideA.Values} → {p.B.Name}:{sideB.Register}/{sideB.Values})");
            return true;
        }

        /// <summary>
        /// A script for the pair, written either way round (2026-10-29: residents' pair talks are written once per pair,
        /// and a member telling a resident the news can use the resident's gossip the other way round); when nothing is
        /// written for the rolled mood, the plain one (<see cref="NeutralStandsIn"/>) rather than the old fixed lines.
        /// Swaps <paramref name="p"/>'s sides when the script is the other way round.
        /// </summary>
        TalkScriptDto PickEitherWay(Pending p, ref TalkOutcome outcome, ref TalkSide sideA, ref TalkSide sideB)
        {
            var tries = NeutralStandsIn(p.Kind) && outcome != TalkOutcome.Neutral ? new[] { outcome, TalkOutcome.Neutral } : new[] { outcome };
            foreach (var o in tries)
            {
                var script = _bank.Pick(p.Kind, o, sideA, sideB, () => GameRandom.Shared.Value, _recentScripts);
                if (script != null) { outcome = o; return script; }
                script = _bank.Pick(p.Kind, o, sideB, sideA, () => GameRandom.Shared.Value, _recentScripts);
                if (script != null)
                {
                    (p.A, p.B) = (p.B, p.A);
                    (sideA, sideB) = (sideB, sideA);
                    outcome = o;
                    return script;
                }
            }
            return null;
        }

        /// <summary>Talks where an ordinary exchange can stand in for a mood nobody wrote lines for.</summary>
        public static bool NeutralStandsIn(TownTalkKind kind)
            => kind == TownTalkKind.Chat || kind == TownTalkKind.Gossip || kind == TownTalkKind.Welcome || kind == TownTalkKind.Trade;

        void PlayFixed(Pending p)
        {
            var outcome = p.Kind == TownTalkKind.Invite ? TownTalkRules.InviteVerdict(p.B.Adventurer.Data.recruitAffinity) : TalkOutcome.Neutral;
            var resident = p.A.Resident?.Profile ?? p.B.Resident?.Profile;
            var script = TownTalkLines.Pick(p.Kind, p.A.Kind, p.B.Kind, resident, p.Rumor?.Text, GameRandom.Shared, outcome);
            var lines = script.Lines.Select((t, i) => (i % 2, t)).ToList();
            if (!Play(p, lines, script.Outcome, false)) Release(p.A, p.B);
        }

        async Task RunAsync(Pending p)
        {
            _inFlight = true;
            bool played = false;
            try
            {
                var result = await _gateway.RequestAsync(TownTalkPrompt.BuildRequest(p.Kind, p.A, p.B, p.Rumor?.Text, p.Detail));
                if (this == null) return;
                TownTalkPrompt.ResponseDto dto = null;
                var lines = result.Ok && LlmJson.TryParse<TownTalkPrompt.ResponseDto>(result.Text, out dto)
                    ? TownTalkPrompt.Interpret(dto, p.A.Name, p.B.Name)
                    : new List<(int, string)>();
                if (lines.Count >= 2)
                {
                    var outcome = TownTalkRules.ParseOutcome(dto.outcome, p.Kind);
                    // No yes from a visitor who does not like the guild that much yet, whatever the model wrote.
                    if (p.Kind == TownTalkKind.Invite) outcome = TownTalkRules.ResolveInvite(outcome, p.B.Adventurer.Data.recruitAffinity);
                    if (result.Exchange != null) result.Exchange.Note = $"마을 대화 {p.Kind}: {dto.outcome}→{TownTalkRules.Code(outcome)}";
                    played = Play(p, lines, outcome, true);
                }
                else
                {
                    if (result.Exchange != null) result.Exchange.Note = "고정 대사로 대체: " + (result.Ok ? "parse" : result.Error);
                    _inFlight = false;
                    PlayFixed(p);
                    played = true;
                }
            }
            finally
            {
                _inFlight = false;
                if (!played) Release(p.A, p.B);
            }
        }

        static void Hold(Talker a, Talker b, bool on)
        {
            foreach (var (t, other) in new[] { (a, b), (b, a) })
            {
                if (t.Resident == null) continue;
                t.Resident.InTalk = on;
                t.Resident.TalkPartner = on ? other.Unit?.transform : null;
            }
        }

        void Release(Talker a, Talker b)
        {
            Hold(a, b, false);
            _busy.Remove(a.Id);
            _busy.Remove(b.Id);
        }

        void ReleaseAll()
        {
            _busy.Clear();
            if (_residents == null) return;
            foreach (var r in _residents.Residents)
                if (r != null) { r.InTalk = false; r.TalkPartner = null; }
        }

        void EndTalk(int talk)
        {
            if (!_active.TryGetValue(talk, out var pair)) return;
            _active.Remove(talk);
            Release(pair.a, pair.b);
        }

        // ---------------- Playing it out ----------------

        bool Play(Pending p, List<(int speaker, string text)> lines, TalkOutcome outcome, bool fromLlm)
        {
            var a = p.A;
            var b = p.B;
            if (!StillThere(a) || !StillThere(b) || lines.Count == 0) return false;
            float now = Now;
            Talks++;
            if (fromLlm) LlmTalks++; else FixedTalks++;
            int id = _nextTalkId++;
            _active[id] = (a, b);
            Hold(a, b, true);
            for (int i = 0; i < lines.Count; i++)
            {
                var speaker = lines[i].speaker == 0 ? a : b;
                var listener = lines[i].speaker == 0 ? b : a;
                _scheduled.Add((id, speaker, listener, lines[i].text, i * LineDelay));
            }
            Record(a, b, lines, now);
            _pairTalkedAt[PairKey(a.Id, b.Id)] = now;
            Apply(a, b, p.Kind, outcome, p.Rumor, now, p.Detail);

            string summary = $"[{p.Kind}/{TownTalkRules.Code(outcome)}] {a.Name} → {b.Name}" + (fromLlm ? "" : " (고정)");
            _recent.Add(summary);
            if (_recent.Count > 20) _recent.RemoveAt(0);
            Debug.Log($"[TOWN-TALK] {summary}: " + string.Join(" / ", lines.Select(l => (l.speaker == 0 ? a.Name : b.Name) + ": " + l.text)));
            EventBus.Publish(new TownTalkEvent { Kind = p.Kind, Outcome = outcome, A = a.Name, B = b.Name, AId = a.Id, BId = b.Id, FromLlm = fromLlm });
            return true;
        }

        /// <summary>Into both adventurers' conversation history (residents and the merchant keep none).</summary>
        static void Record(Talker a, Talker b, List<(int speaker, string text)> lines, float hour)
        {
            foreach (var owner in new[] { a, b })
            {
                if (owner.Adventurer == null) continue;
                var other = owner == a ? b : a;
                foreach (var (speaker, text) in lines)
                    DialogueLog.Add(owner.Adventurer.Data, new DialogueLine
                    {
                        hour = hour, kind = DialogueKind.Banter, speaker = speaker == 0 ? a.Name : b.Name, with = other.Name, text = text,
                    });
            }
        }

        void Apply(Talker a, Talker b, TownTalkKind kind, TalkOutcome outcome, Rumor rumor, float now, string detail)
        {
            var e = TownTalkRules.Effects(kind, outcome);
            var A = a.Adventurer;
            var B = b.Adventurer;
            // Feelings: between adventurers as always; residents keep their own about everyone they talk to.
            if (A != null && B != null)
            {
                Relationships.Add(A, B, e.FeelAB, "마을 대화");
                Relationships.Add(B, A, e.FeelBA, "마을 대화");
            }
            if (a.Resident != null) a.Resident.Profile.AddFeeling(b.Id, e.FeelAB);
            if (b.Resident != null) b.Resident.Profile.AddFeeling(a.Id, e.FeelBA);
            // A resident's opinion of the guild follows how members treat them.
            if (a.Resident != null && b.Kind == TalkerKind.Member) a.Resident.Profile.AddOpinion(TownTalkRules.OpinionDelta(outcome));
            if (b.Resident != null && a.Kind == TalkerKind.Member) b.Resident.Profile.AddOpinion(TownTalkRules.OpinionDelta(outcome));

            Services.TryGet<GuildWelfare>(out var welfare);
            if (welfare != null)
            {
                if (a.Kind == TalkerKind.Member) welfare.AdjustStress(A, e.StressA);
                if (b.Kind == TalkerKind.Member) welfare.AdjustStress(B, e.StressB);
            }

            int guildNews = 0;
            if (kind == TownTalkKind.Gossip && rumor != null && Rumors.Learn(rumor, b.Id))
            {
                guildNews = rumor.GuildTone;
                if (b.Resident != null) b.Resident.Profile.AddOpinion(rumor.GuildTone * TownTalkRules.RumorOpinion);
                if (B != null)
                {
                    AdventurerMemory.Add(B.Data.memories, now, 1, $"{a.Name}에게서 들은 이야기: {rumor.Text}", "rumor:" + rumor.Id);
                    // Hearing about someone colours how one feels about them.
                    if (rumor.SubjectTone != 0 && rumor.Subjects.Count > 0 && _roster != null && _roster.Find(rumor.Subjects[0]) is { } subject && subject != B)
                        Relationships.Add(B, subject, rumor.SubjectTone * TownTalkRules.RumorFeeling, "소문");
                }
            }

            if (kind == TownTalkKind.Invite && outcome == TalkOutcome.Joined && b.Kind == TalkerKind.Visitor)
            {
                JoinFromTown(a, B);
                return;
            }
            // A visitor's mind: how the talk went, what they heard, and what a resident said about the guild.
            int fromResidentA = a.Resident != null && kind != TownTalkKind.Invite ? TownTalkRules.WordOfMouth(a.Resident.Profile.GuildOpinion) : 0;
            int fromResidentB = b.Resident != null && kind != TownTalkKind.Invite ? TownTalkRules.WordOfMouth(b.Resident.Profile.GuildOpinion) : 0;
            if (a.Kind == TalkerKind.Visitor) VisitorOpinion(A, e.VisitorAffinity + fromResidentB);
            if (b.Kind == TalkerKind.Visitor) VisitorOpinion(B, e.VisitorAffinity + guildNews * TownTalkRules.RumorAffinity + fromResidentA);

            // What people will be talking about next, and what the two will remember.
            if (A == null || B == null) return;
            string na = a.Name, nb = b.Name;
            string key = PairKey(a.Id, b.Id);
            if (outcome == TalkOutcome.Fight)
            {
                _fights[key] = (now, kind == TownTalkKind.Quarrel && !string.IsNullOrEmpty(detail) ? detail : "사소한 말다툼");
                Rumors.Add($"{Ga(na)} {nb}{Wa(nb)} 크게 다퉜다", now, new[] { a.Id, b.Id }, new[] { a.Id, b.Id }, guildTone: -1, subjectTone: -1);
                AdventurerMemory.Add(A.Data.memories, now, 2, $"{nb}{Wa(nb)} 크게 다퉜다", "fight:" + b.Id);
                AdventurerMemory.Add(B.Data.memories, now, 2, $"{na}{Wa(na)} 크게 다퉜다", "fight:" + a.Id);
                Log($"{Ga(na)} {nb}{Wa(nb)} 마을에서 크게 다퉜습니다.");
            }
            else if (outcome == TalkOutcome.Reconciled)
            {
                _fights.Remove(key);
                Rumors.Add($"{Ga(na)} {nb}{Wa(nb)} 화해했다", now, new[] { a.Id, b.Id }, new[] { a.Id, b.Id }, subjectTone: 1);
                AdventurerMemory.Add(A.Data.memories, now, 2, $"{nb}{Wa(nb)} 화해했다", "makeup:" + b.Id);
                AdventurerMemory.Add(B.Data.memories, now, 2, $"{na}{Wa(na)} 화해했다", "makeup:" + a.Id);
                Log($"{Ga(na)} {nb}{Wa(nb)} 화해했습니다.");
            }
            else if (kind == TownTalkKind.Comfort && outcome == TalkOutcome.Warm)
            {
                Rumors.Add($"{Ga(na)} 힘들어하는 {nb}{Reul(nb)} 다독여 줬다", now, new[] { a.Id, b.Id }, new[] { a.Id, b.Id }, guildTone: 1, subjectTone: 1);
                AdventurerMemory.Add(B.Data.memories, now, 2, $"힘들 때 {Ga(na)} 위로해 줬다", "comforted:" + a.Id);
                AdventurerMemory.Add(A.Data.memories, now, 1, $"힘들어하는 {nb}{Reul(nb)} 위로했다", "comfort:" + b.Id);
                Log($"{Ga(na)} 지친 {nb}{Reul(nb)} 위로했습니다.");
            }
        }

        /// <summary>
        /// A visitor said yes to an invitation in town (user request 2026-10-07): they join without the guild master,
        /// and the guild master hears about it.
        /// </summary>
        void JoinFromTown(Talker inviter, Adventurer visitor)
        {
            // Someone who turned the guild master down never joins (user decision 2026-10-29).
            if (visitor == null || !visitor.IsVisitor || visitor.Data.recruitRefused) return;
            if (!Services.TryGet<GuildRoster>(out var roster) || !roster.AddExisting(visitor)) return;
            TownRecruits++;
            var d = visitor.Data;
            string who = inviter.Kind == TalkerKind.Resident ? $"주민 {inviter.Name}" : inviter.Name;
            AdventurerMemory.Add(d.memories, Now, 2, $"{who}의 권유로 길드에 들어왔다", "joined-town");
            if (inviter.Adventurer != null)
                AdventurerMemory.Add(inviter.Adventurer.Data.memories, Now, 2, $"방문자 {Reul(d.name)} 길드로 데려왔다", "recruited:" + d.id);
            Log($"{who}의 권유로 {Ga(d.name)} 길드에 합류했습니다.");
            if (Services.TryGet<NotificationCenter>(out var center))
                center.Post(NotificationKind.Success, "마을에서 영입", $"{who}의 권유로 {Ga(d.name)} 길드에 합류했습니다.", null, NotificationLink.Adventurer(d.id));
            EventBus.Publish(new RecruitmentEndedEvent { Visitor = visitor, Outcome = RecruitOutcome.Joined, Turns = d.recruitTurns });
        }

        /// <summary>
        /// A visitor's opinion of the guild after a talk: the same persistent affinity the recruit talk uses
        /// (<see cref="RecruitmentSession.NextRecruitAffinity"/>); enough of it and they join on the spot.
        /// </summary>
        void VisitorOpinion(Adventurer visitor, int delta)
        {
            if (visitor == null || !visitor.IsVisitor || delta == 0) return;
            var d = visitor.Data;
            d.recruitAffinity = RecruitmentSession.NextRecruitAffinity(d.recruitAffinity, visitor.Traits, delta);
            if (d.recruitAffinity < RecruitmentSession.AutoJoinAffinity || d.recruitRefused) return;
            if (!Services.TryGet<GuildRoster>(out var roster) || !roster.AddExisting(visitor)) return;
            TownRecruits++;
            Log($"{Ga(d.name)} 마을에서 길드 이야기를 듣고 스스로 합류했습니다.");
            if (Services.TryGet<NotificationCenter>(out var center))
                center.Post(NotificationKind.Success, "마을에서 영입", $"{Ga(d.name)} 마을에서 길드 이야기를 듣고 스스로 합류했습니다.", null, NotificationLink.Adventurer(d.id));
            EventBus.Publish(new RecruitmentEndedEvent { Visitor = visitor, Outcome = RecruitOutcome.Joined, Turns = d.recruitTurns });
        }

        static void Log(string text)
        {
            if (Services.TryGet<GameLog>(out var log)) log.Add(text, LogCategory.Info);
        }

        static string Ga(string w) => w + MemoryRecorder.Josa(w, "이", "가");
        static string Reul(string w) => w + MemoryRecorder.Josa(w, "을", "를");
        static string Wa(string w) => MemoryRecorder.Josa(w, "과", "와");

        // ---------------- Where rumors come from ----------------

        IEnumerable<string> MemberIds() => _roster != null ? _roster.Members.Where(m => m != null).Select(m => m.Data.id) : Enumerable.Empty<string>();

        /// <summary>Everyone who lives in town hears the big news: members and residents.</summary>
        IEnumerable<string> TownIds()
            => MemberIds().Concat(_residents != null ? _residents.Residents.Where(r => r != null).Select(r => r.Profile.Id) : Enumerable.Empty<string>());

        void OnQuestClosed(QuestClosedEvent e)
        {
            var q = e.Quest;
            if (q == null) return;
            var party = q.Assignees.OfType<Adventurer>().Where(x => x != null && x.Data != null).ToList();
            if (party.Count == 0) return;
            string who = party[0].Data.name + (party.Count > 1 ? " 일행" : "");
            if (e.Status == QuestStatus.Failed)
                Rumors.Add($"{Ga(who)} '{q.Title}' 의뢰에 실패했다", Now, party.Select(p => p.Data.id), party.Select(p => p.Data.id), guildTone: -1, subjectTone: -1);
            else if (e.Status == QuestStatus.Completed || e.Status == QuestStatus.TurnedIn)
                Rumors.Add($"{Ga(who)} '{q.Title}' 의뢰를 해냈다", Now, party.Select(p => p.Data.id), party.Select(p => p.Data.id), guildTone: 1, subjectTone: 1);
        }

        void OnDied(AdventurerDiedEvent e)
        {
            var dead = e.Adventurer;
            if (dead == null || dead.Data == null) return;
            string by = e.Killer != null ? $"{e.Killer.DisplayName}에게 당해 " : "";
            Rumors.Add($"길드원 {Ga(dead.Data.name)} {by}목숨을 잃었다", Now, TownIds(), new[] { dead.Data.id }, guildTone: -1);
            Rumors.Forget(dead.Data.id);
        }

        void OnJoined(AdventurerJoinedEvent e)
        {
            // The founders join at the start; that is nobody's news.
            if (e.Adventurer == null || e.Adventurer.Data == null || Now < 1f) return;
            Rumors.Add($"{Ga(e.Adventurer.Data.name)} 새로 길드에 들어왔다", Now, TownIds(), new[] { e.Adventurer.Data.id }, guildTone: 1);
        }

        void OnLeft(AdventurerLeftEvent e)
        {
            if (e.Adventurer == null || e.Adventurer.Data == null) return;
            Rumors.Add($"{Ga(e.Adventurer.Data.name)} 길드를 떠났다", Now, TownIds(), new[] { e.Adventurer.Data.id }, guildTone: -1);
            Rumors.Forget(e.Adventurer.Data.id);
        }

        void OnCounsel(CounselEndedEvent e)
        {
            if (e.Member == null || e.Member.Data == null) return;
            string name = e.Member.Data.name;
            if (e.Outcome == CounselOutcome.Relieved)
                Rumors.Add($"길드 마스터가 {name}의 고민을 끝까지 들어 줬다", Now, new[] { e.Member.Data.id }, new[] { e.Member.Data.id }, guildTone: 1);
            else if (e.Outcome == CounselOutcome.Walkout)
                Rumors.Add($"{Ga(name)} 길드 마스터와 면담하다 언짢아서 나왔다", Now, new[] { e.Member.Data.id }, new[] { e.Member.Data.id }, guildTone: -1);
        }

        void OnMerchant(MerchantArrivedEvent e)
        {
            // The merchant brings news from the road.
            var picks = OutsideRumors.OrderBy(_ => GameRandom.Shared.Value).Take(2);
            foreach (var r in picks) Rumors.Add(r, Now, new[] { MerchantId });
        }

        void OnHour(HourPassedEvent e) => Rumors.Prune(Now);
    }
}
