using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.Welfare;
using UnityEngine;

namespace GuildMaster.Economy
{
    /// <summary>What kind of line the guild master used to ask for money.</summary>
    public enum TributeTactic { Plead, Reason, Praise, Bargain, Pressure, Rude, Small, Low }

    /// <summary>How much of what they would ever hand over the member offers right now.</summary>
    public enum TributeOffer { None, Little, Some, Much }

    public enum TributeOutcome { Ongoing, Paid, Walkout, Ended }

    /// <summary>
    /// Tribute (상납금, user decision 2026-10-29: "llm 활용 길드원과의 대화를 통해 적절금액 빼앗기"). The guild master
    /// talks a member into handing over some of their own gold. Each line moves how willing they are (<see cref="Will"/>,
    /// 0-100); the willingness caps the offer, and the model picks the offer within one step of that cap and says it in
    /// the member's words. Threats work fast but cost stress and leave a grudge that makes the next talk harder; three
    /// sour points and the member walks out with nothing paid. Pure rules, covered by tests.
    /// </summary>
    public static class TributeRules
    {
        public static readonly string[] TacticCodes = { "plead", "reason", "praise", "bargain", "pressure", "rude", "small", "low" };
        public static readonly string[] OfferCodes = { "none", "little", "some", "much" };

        /// <summary>Lines the guild master gets before the member's last word.</summary>
        public const int MaxTurns = 6;
        /// <summary>Sour points (threats 1, insults 2) at which the member walks out.</summary>
        public const int WalkoutSour = 3;
        /// <summary>Members with less than this are not worth asking.</summary>
        public const int MinGold = 20;
        /// <summary>One talk per member per day.</summary>
        public const float CooldownHours = 24f;
        /// <summary>A grudge from a talk fades after this long.</summary>
        public const float GrudgeFadeHours = 48f;

        public static string Code(TributeTactic t) => TacticCodes[(int)t];
        public static string Code(TributeOffer o) => OfferCodes[(int)o];

        public static TributeTactic ParseTactic(string code)
        {
            int i = Array.IndexOf(TacticCodes, code?.Trim().ToLowerInvariant());
            return i < 0 ? TributeTactic.Small : (TributeTactic)i;
        }

        public static TributeOffer? ParseOffer(string code)
        {
            int i = Array.IndexOf(OfferCodes, code?.Trim().ToLowerInvariant());
            return i < 0 ? null : (TributeOffer)i;
        }

        static bool Has(IEnumerable<string> traits, string id) => traits != null && traits.Contains(id);

        /// <summary>The share of their gold a member would ever hand over.</summary>
        public static float MaxShare(string values, IEnumerable<string> traits)
        {
            float s = 0.5f;
            if (Has(traits, "greedy")) s -= 0.2f;
            if (Has(traits, "loyal")) s += 0.2f;
            if (values == "돈") s -= 0.1f;
            if (values == "동료") s += 0.1f;
            return Mathf.Clamp(s, 0.1f, 0.8f);
        }

        /// <summary>The most this member would hand over at all.</summary>
        public static int Pot(int gold, string values, IEnumerable<string> traits)
            => Mathf.Max(0, Mathf.FloorToInt(Mathf.Max(0, gold) * MaxShare(values, traits)));

        public static int Amount(int pot, TributeOffer offer)
        {
            if (pot <= 0 || offer == TributeOffer.None) return 0;
            return offer switch
            {
                TributeOffer.Little => Mathf.Max(1, Mathf.RoundToInt(pot * 0.25f)),
                TributeOffer.Some => Mathf.Max(1, Mathf.RoundToInt(pot * 0.5f)),
                _ => pot,
            };
        }

        /// <summary>How willing they are when called in: less when stressed or still sore from the last time.</summary>
        public static int StartWill(float stress, IEnumerable<string> traits, int grudge)
        {
            int w = 30;
            if (stress >= StressRules.StrainedAt) w -= 15;
            else if (stress >= StressRules.TenseAt) w -= 5;
            if (Has(traits, "loyal")) w += 15;
            if (Has(traits, "greedy")) w -= 10;
            w -= 10 * Mathf.Max(0, grudge);
            return Mathf.Clamp(w, 0, 100);
        }

        /// <summary>
        /// How much a line moves the member's willingness. The same kind of line twice in a row works half as well
        /// (a quarter the third time); bad lines always hurt in full.
        /// </summary>
        public static int WillDelta(TributeTactic t, string values, IEnumerable<string> traits, int streak)
        {
            int d = t switch
            {
                TributeTactic.Plead => 12 + (values == "동료" ? 6 : 0) + (Has(traits, "loyal") ? 4 : 0),
                TributeTactic.Reason => 15 + (values == "안전" || values == "동료" ? 5 : 0) - (values == "돈" ? 5 : 0),
                TributeTactic.Praise => 8 + (values == "명예" ? 6 : 0) + (Has(traits, "arrogant") ? 6 : 0),
                TributeTactic.Bargain => 18 + (values == "돈" ? 6 : 0),
                TributeTactic.Pressure => 25 + (values == "안전" ? 5 : 0) + (Has(traits, "coward") ? 10 : 0)
                                          - (Has(traits, "arrogant") ? 20 : 0) - (Has(traits, "brave") ? 10 : 0),
                TributeTactic.Rude => -15,
                TributeTactic.Low => -5,
                _ => 0,
            };
            if (d > 0 && streak > 0) d >>= Mathf.Min(streak, 2);
            return d;
        }

        /// <summary>Stress the line itself puts on the member.</summary>
        public static float StressCost(TributeTactic t, IEnumerable<string> traits) => t switch
        {
            TributeTactic.Pressure => 6f + (Has(traits, "coward") ? 3f : 0f),
            TributeTactic.Rude => 10f,
            TributeTactic.Low => 1f,
            _ => 0f,
        };

        public static int SourPoints(TributeTactic t) => t switch
        {
            TributeTactic.Pressure => 1,
            TributeTactic.Rude => 2,
            _ => 0,
        };

        /// <summary>The most they would offer at this willingness.</summary>
        public static TributeOffer Cap(int will)
            => will < 25 ? TributeOffer.None : will < 50 ? TributeOffer.Little : will < 75 ? TributeOffer.Some : TributeOffer.Much;

        /// <summary>The offers the model may pick: the cap and one step below it.</summary>
        public static IEnumerable<TributeOffer> Allowed(int will)
        {
            var cap = Cap(will);
            if (cap > TributeOffer.None) yield return cap - 1;
            yield return cap;
        }

        public static TributeOffer Clamp(TributeOffer offer, int will)
        {
            var cap = Cap(will);
            var floor = cap > TributeOffer.None ? cap - 1 : TributeOffer.None;
            return offer > cap ? cap : offer < floor ? floor : offer;
        }

        /// <summary>Stress from losing the money itself once paid: a bigger share hurts more, being bullied into it more still.</summary>
        public static float PaidStress(int amount, int goldBefore, string values, bool pressured)
        {
            if (amount <= 0) return 0f;
            float s = (float)amount / Mathf.Max(1, goldBefore) * 20f;
            if (values == "돈") s *= 1.5f;
            if (pressured) s += 4f;
            return Mathf.Round(s);
        }

        static readonly string[] RudeWords = { "바보", "멍청", "꺼져", "닥쳐", "한심", "쓰레기", "시시", "거지", "짜증", "버러지" };
        static readonly string[] PressureWords = { "내놔", "내놓", "안 내면", "안내면", "당장", "각오", "쫓아", "내쫓", "해고", "가만 안", "가만두지",
                                                   "재미없", "협박", "뺏", "뜯", "의뢰 안 줄", "의뢰는 없을", "좋은 의뢰는 없", "방 빼" };
        static readonly string[] BargainWords = { "대신", "보답", "돌려줄", "돌려 줄", "갚을", "갚아", "휴가", "우선", "두 배", "배로", "이자", "챙겨 줄", "챙겨줄" };
        static readonly string[] ReasonWords = { "식량", "운영", "장비", "수리", "세금", "월세", "빚", "창고", "길드를 위해", "살림", "밥값", "유지비", "모두를 위해", "다 같이" };
        static readonly string[] PleadWords = { "부탁", "사정", "어려워", "힘들어", "도와", "제발", "한 번만", "좀만", "조금만" };
        static readonly string[] PraiseWords = { "대단", "훌륭", "최고", "믿", "고마", "덕분", "실력", "멋지" };

        /// <summary>What kind of line it is by the words in it (checks the model, and answers without it).</summary>
        public static TributeTactic Classify(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Replace(" ", "").Length < 3) return TributeTactic.Low;
            if (RudeWords.Any(t.Contains)) return TributeTactic.Rude;
            if (PressureWords.Any(t.Contains)) return TributeTactic.Pressure;
            if (BargainWords.Any(t.Contains)) return TributeTactic.Bargain;
            if (ReasonWords.Any(t.Contains)) return TributeTactic.Reason;
            if (PleadWords.Any(t.Contains)) return TributeTactic.Plead;
            if (PraiseWords.Any(t.Contains)) return TributeTactic.Praise;
            return TributeTactic.Small;
        }

        /// <summary>
        /// The model's reading of the line, unless the words say it was a threat, an insult or next to nothing (the
        /// model tends to read a threat politely).
        /// </summary>
        public static TributeTactic Reconcile(TributeTactic model, TributeTactic rules)
            => rules == TributeTactic.Rude || rules == TributeTactic.Pressure || rules == TributeTactic.Low ? rules : model;

        public static string Feedback(TributeTactic t, int delta, float stress)
        {
            string what = t switch
            {
                TributeTactic.Plead => "사정을 듣고 마음이 약해졌다",
                TributeTactic.Reason => "돈이 필요한 이유에 수긍했다",
                TributeTactic.Praise => "추켜세우는 말에 기분이 좋아졌다",
                TributeTactic.Bargain => "보답 약속에 솔깃했다",
                TributeTactic.Pressure => delta > 0 ? "겁을 먹었다 (원망이 쌓인다)" : "협박에 오히려 반발했다 (원망이 쌓인다)",
                TributeTactic.Rude => "모욕에 화가 났다",
                TributeTactic.Low => "성의 없는 말에 시큰둥하다",
                _ => "별 반응이 없다",
            };
            string will = delta > 0 ? $" · 낼 마음 +{delta}" : delta < 0 ? $" · 낼 마음 {delta}" : "";
            string s = stress > 0 ? $" · 스트레스 +{stress:0}" : "";
            return $"[{what}{will}{s}]";
        }

        public static string WillLabel(int will) => Cap(will) switch
        {
            TributeOffer.None => "한 푼도 내기 싫다",
            TributeOffer.Little => "조금은 낼 수도",
            TributeOffer.Some => "어느 정도는 낼 생각",
            _ => "달라는 만큼 낼 생각",
        };
    }

    /// <summary>What one line of the guild master's did.</summary>
    public struct TributeStep
    {
        public TributeTactic Tactic;
        public int WillDelta;
        public float Stress;
        public TributeOffer Offer;
    }

    /// <summary>
    /// The numbers of one tribute talk, without the member or the model (so the rules can be tested end to end):
    /// lines go in as tactics, the offer comes out.
    /// </summary>
    public class TributeTalk
    {
        readonly string _values;
        readonly List<string> _traits;
        TributeTactic? _last;
        int _streak;

        public int Gold { get; }
        public int Pot { get; }
        public int Will { get; private set; }
        public int Sour { get; private set; }
        public int PlayerTurns { get; private set; }
        public bool Pressured { get; private set; }
        public TributeOffer Offer { get; private set; }
        public TributeOutcome Outcome { get; private set; } = TributeOutcome.Ongoing;
        public int OfferGold => TributeRules.Amount(Pot, Offer);
        /// <summary>The member has had their last word: take the offer or leave it.</summary>
        public bool Final => Outcome == TributeOutcome.Ongoing && PlayerTurns >= TributeRules.MaxTurns;
        public bool CanTalk => Outcome == TributeOutcome.Ongoing && !Final;

        public TributeTalk(int gold, string values, IEnumerable<string> traits, float stress, int grudge)
        {
            Gold = Mathf.Max(0, gold);
            _values = values;
            _traits = traits?.ToList() ?? new List<string>();
            Pot = TributeRules.Pot(Gold, values, _traits);
            Will = TributeRules.StartWill(stress, _traits, grudge);
        }

        /// <summary>The willingness this line would leave (before the model has read it), for the model's instructions.</summary>
        public int WillAfter(TributeTactic t)
            => Mathf.Clamp(Will + TributeRules.WillDelta(t, _values, _traits, _last == t ? _streak + 1 : 0), 0, 100);

        /// <summary>One line heard. <paramref name="modelOffer"/> is what the model chose (kept within a step of the cap).</summary>
        public TributeStep Hear(TributeTactic t, TributeOffer? modelOffer)
        {
            if (!CanTalk) return new TributeStep { Tactic = t, Offer = Offer };
            _streak = _last == t ? _streak + 1 : 0;
            _last = t;
            int delta = TributeRules.WillDelta(t, _values, _traits, _streak);
            int before = Will;
            Will = Mathf.Clamp(Will + delta, 0, 100);
            Sour += TributeRules.SourPoints(t);
            if (t == TributeTactic.Pressure) Pressured = true;
            PlayerTurns++;
            Offer = modelOffer.HasValue ? TributeRules.Clamp(modelOffer.Value, Will) : TributeRules.Cap(Will);
            if (Pot <= 0) Offer = TributeOffer.None;
            if (Sour >= TributeRules.WalkoutSour)
            {
                Outcome = TributeOutcome.Walkout;
                Offer = TributeOffer.None;
            }
            return new TributeStep { Tactic = t, WillDelta = Will - before, Stress = TributeRules.StressCost(t, _traits), Offer = Offer };
        }

        /// <summary>Takes the offer on the table; the gold taken (0 when there is nothing to take).</summary>
        public int Accept()
        {
            if (Outcome != TributeOutcome.Ongoing || OfferGold <= 0) return 0;
            Outcome = TributeOutcome.Paid;
            return OfferGold;
        }

        public void End()
        {
            if (Outcome == TributeOutcome.Ongoing) Outcome = TributeOutcome.Ended;
        }
    }

    /// <summary>Written lines for the tribute talk: the guild master's choices, and the member's answers without the model.</summary>
    public static class TributeLines
    {
        public class Choice
        {
            public TributeTactic Tactic;
            public string Text;
        }

        public const int ChoiceCount = 4;

        public static readonly Dictionary<TributeTactic, string[]> Asks = new()
        {
            [TributeTactic.Plead] = new[]
            {
                "길드 사정이 많이 어려워서 그래. 조금만 보태 줄 수 있을까?",
                "부탁 하나만 하자. 이번만 길드에 돈을 좀 보태 줘.",
                "체면 구기는 거 알지만, 제발 이번 한 번만 도와줘.",
            },
            [TributeTactic.Reason] = new[]
            {
                "식량이 바닥나 가. 다 같이 먹을 밥값을 좀 보태 줘.",
                "장비 수리비랑 길드 유지비가 모자라. 길드를 위해 조금 내 줘.",
                "의뢰를 받으려면 길드 살림이 돌아가야 해. 운영비 좀 보태 줘.",
            },
            [TributeTactic.Praise] = new[]
            {
                "자네 실력이면 금방 다시 벌 거야. 이번에 길드에 좀 보태 줘.",
                "역시 우리 길드 에이스는 마음 씀씀이도 최고겠지?",
                "자네 같은 사람이 있어서 믿음직해. 길드에 돈을 좀 보태 주겠나?",
            },
            [TributeTactic.Bargain] = new[]
            {
                "지금 보태 주면 다음 큰 의뢰는 자네한테 우선 줄게.",
                "이번에 내 주면 나중에 두 배로 돌려줄게.",
                "보태 주면 대신 다음엔 넉넉하게 쉬게 해 줄게.",
            },
            [TributeTactic.Pressure] = new[]
            {
                "길드에 계속 있고 싶으면 상납금 정도는 내야지. 당장 내놔.",
                "안 내면 앞으로 좋은 의뢰는 없을 줄 알아.",
                "가진 돈 좀 내놓지? 나 가만 안 있는다.",
            },
        };

        /// <summary>Four lines of different kinds; a threat is always among them (it is a shakedown, after all).</summary>
        public static List<Choice> Offer(ICollection<string> used, GameRandom rng)
        {
            var kinds = new List<TributeTactic> { TributeTactic.Plead, TributeTactic.Reason, TributeTactic.Praise, TributeTactic.Bargain };
            rng.Shuffle(kinds);
            var pick = kinds.Take(ChoiceCount - 1).ToList();
            pick.Add(TributeTactic.Pressure);
            var result = new List<Choice>();
            foreach (var k in pick)
            {
                var pool = Asks[k].Where(t => used == null || !used.Contains(t)).ToList();
                if (pool.Count == 0) pool = Asks[k].ToList();
                result.Add(new Choice { Tactic = k, Text = rng.Pick(pool) });
            }
            rng.Shuffle(result);
            return result;
        }

        static readonly Dictionary<string, string[][]> Replies = new()
        {
            // [Warm, Formal, Curt, Haughty]
            ["open"] = new[]
            {
                new[] { "네? 저 부르셨어요? 무슨 일이에요?", "어, 길드 마스터님! 무슨 일 있어요?" },
                new[] { "부르셨습니까. 무슨 용건이십니까?", "말씀하십시오." },
                new[] { "…무슨 일이에요?", "왜요?" },
                new[] { "뭐야, 무슨 일인데?", "날 다 부르고, 무슨 일이야?" },
            },
            ["none"] = new[]
            {
                new[] { "에이, 그건 좀… 저도 돈이 넉넉하지 않아요.", "음… 지금은 좀 곤란해요." },
                new[] { "죄송하지만 지금은 어렵습니다.", "그 요청은 받아들이기 어렵습니다." },
                new[] { "…싫어요.", "안 돼요." },
                new[] { "내가 왜? 내 돈은 내가 번 거야.", "흥, 꿈도 꾸지 마." },
            },
            ["little"] = new[]
            {
                new[] { "그럼… {gold}골드 정도는 드릴 수 있어요.", "아깝지만 {gold}골드면 될까요?" },
                new[] { "{gold}골드 정도라면 내겠습니다.", "{gold}골드까지는 가능합니다." },
                new[] { "…{gold}골드요. 그 이상은 안 돼요.", "{gold}골드. 그게 다예요." },
                new[] { "뭐, {gold}골드 정도는 던져 주지.", "{gold}골드. 그걸로 만족해." },
            },
            ["some"] = new[]
            {
                new[] { "알겠어요, {gold}골드 낼게요. 길드가 잘 돼야 저도 좋으니까요.", "{gold}골드요. 이 정도면 괜찮죠?" },
                new[] { "{gold}골드를 내겠습니다. 길드 운영에 쓰십시오.", "알겠습니다. {gold}골드면 되겠습니까?" },
                new[] { "{gold}골드 낼게요.", "…{gold}골드면 되죠?" },
                new[] { "{gold}골드 줄게. 고마운 줄 알아.", "좋아, {gold}골드. 생색은 좀 낼 거야." },
            },
            ["much"] = new[]
            {
                new[] { "좋아요, {gold}골드! 길드를 위해서라면요.", "{gold}골드 다 드릴게요. 대신 잊지 마세요!" },
                new[] { "{gold}골드를 내겠습니다. 믿고 맡기겠습니다.", "{gold}골드, 기꺼이 내겠습니다." },
                new[] { "{gold}골드요. 가져가세요.", "…알았어요. {gold}골드." },
                new[] { "{gold}골드 통 크게 쏘지. 날 잘 모셔.", "{gold}골드 가져가. 내 덕인 줄 알고." },
            },
            ["angry"] = new[]
            {
                new[] { "그런 식으로 말하면 서운해요…", "왜 그렇게까지 말씀하세요?" },
                new[] { "그런 말씀은 불쾌합니다.", "말씀이 지나치십니다." },
                new[] { "…그렇게 말하지 마요.", "기분 나쁘네요." },
                new[] { "지금 나한테 그렇게 말한 거야?", "말 조심해." },
            },
            ["walkout"] = new[]
            {
                new[] { "더는 못 듣겠어요. 저 갈게요!", "너무하세요. 저 먼저 일어날게요." },
                new[] { "더 들을 이유가 없군요. 실례하겠습니다.", "이 이야기는 여기까지 하겠습니다." },
                new[] { "됐어요. 갈게요.", "…끝났죠? 가요." },
                new[] { "더 들어줄 가치도 없네. 간다.", "웃기지 마. 난 간다." },
            },
            ["paid"] = new[]
            {
                new[] { "여기요, {gold}골드. 잘 써 주세요.", "{gold}골드예요. 길드에 보탬이 되면 좋겠어요." },
                new[] { "{gold}골드입니다. 확인하십시오.", "{gold}골드 드리겠습니다. 허투루 쓰지 마십시오." },
                new[] { "…{gold}골드. 여기요.", "{gold}골드요." },
                new[] { "{gold}골드. 잘 써.", "자, {gold}골드. 이걸로 생색은 다 낸 거야." },
            },
            ["pass"] = new[]
            {
                new[] { "어… 그럼 그냥 갈게요?", "아, 안 받으셔도 돼요? 그럼 이만!" },
                new[] { "그럼 이만 물러가겠습니다.", "알겠습니다. 가 보겠습니다." },
                new[] { "…그럼 가요.", "네." },
                new[] { "안 받아? 그럼 됐고.", "흥, 마음 바뀌기 전에 간다." },
            },
        };

        public static string Line(string key, NpcVoice.Register register, int gold, GameRandom rng)
        {
            if (!Replies.TryGetValue(key, out var byRegister)) return "…";
            var lines = byRegister[(int)register];
            string line = rng != null ? rng.Pick(lines) : lines[0];
            return line.Replace("{gold}", gold.ToString());
        }

        public static string OfferKey(TributeOffer o) => TributeRules.Code(o);
    }

    /// <summary>
    /// Who has been asked for money when, and how sore they still are (2026-10-29). One talk per member a day;
    /// threats and insults leave a grudge that fades over <see cref="TributeRules.GrudgeFadeHours"/>.
    /// </summary>
    public class TributeService
    {
        class Record
        {
            public float LastHour = -999f;
            public int Grudge;
            public float GrudgeHour;
            public int Total;
        }

        readonly Dictionary<string, Record> _records = new();

        /// <summary>All the tribute taken since the game began.</summary>
        public int Collected { get; private set; }
        public int Times { get; private set; }

        Record Get(Adventurer a)
        {
            if (!_records.TryGetValue(a.Data.id, out var r)) _records[a.Data.id] = r = new Record();
            return r;
        }

        public CareCheck CanAsk(Adventurer a, float now)
        {
            if (a == null || a.Data == null || !a.IsAlive) return CareCheck.No("대화할 수 없습니다.");
            if (a.Data.gold < TributeRules.MinGold) return CareCheck.No($"가진 돈이 {a.Data.gold}G뿐이라 받아낼 게 없습니다.");
            var r = Get(a);
            float wait = r.LastHour + TributeRules.CooldownHours - now;
            if (wait > 0f) return CareCheck.No($"오늘 이미 돈 이야기를 꺼냈습니다. 약 {Mathf.CeilToInt(wait)}시간 뒤에 다시 할 수 있습니다.");
            return CareCheck.Yes;
        }

        /// <summary>How sore the member still is from earlier talks (each point makes them 10 less willing).</summary>
        public int Grudge(Adventurer a, float now)
        {
            var r = Get(a);
            while (r.Grudge > 0 && now - r.GrudgeHour >= TributeRules.GrudgeFadeHours)
            {
                r.Grudge--;
                r.GrudgeHour += TributeRules.GrudgeFadeHours;
            }
            return r.Grudge;
        }

        public int TotalFrom(Adventurer a) => a?.Data != null && _records.TryGetValue(a.Data.id, out var r) ? r.Total : 0;

        /// <summary>The talk got its first line: counts as the day's talk.</summary>
        public void Begin(Adventurer a, float now) => Get(a).LastHour = now;

        public void Finish(Adventurer a, int amount, int sour, float now)
        {
            var r = Get(a);
            Grudge(a, now);
            if (sour > 0)
            {
                r.Grudge += sour;
                r.GrudgeHour = now;
            }
            if (amount > 0)
            {
                r.Total += amount;
                Collected += amount;
                Times++;
            }
        }
    }
}
