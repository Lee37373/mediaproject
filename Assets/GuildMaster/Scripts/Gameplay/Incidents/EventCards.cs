using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Social;

namespace GuildMaster.Incidents
{
    /// <summary>Who an event card is about.</summary>
    public enum CastRule
    {
        /// <summary>Nobody in particular (the whole guild).</summary>
        None,
        /// <summary>Any member.</summary>
        Any,
        /// <summary>A member free in town.</summary>
        AnyFree,
        /// <summary>The free member with the weakest gear who can still be geared up.</summary>
        LowestGear,
        /// <summary>The free member who is least tired.</summary>
        Freshest,
        /// <summary>Two free members who do not get along.</summary>
        HostilePair,
        /// <summary>Two members who get along very well.</summary>
        FriendlyPair,
    }

    /// <summary>A visitor the card brings to town.</summary>
    public enum WandererOffer { None, Plain, Welcomed }

    /// <summary>
    /// What one result of a choice does. Numbers only — the code applies them (EventCardService); the model, when it
    /// is used, only describes the situation. Text fields are templates: {A}, {B} and with a particle {A이} {A을}
    /// {A와} {A은} (the particle follows the name).
    /// </summary>
    public class CardEffect
    {
        /// <summary>Gold in (+) or lost (-), on top of the choice's cost.</summary>
        public int Gold;
        public float StressA, StressB, StressAll;
        public float FatigueA;
        /// <summary>Added to how A and B feel about each other (both ways).</summary>
        public int RelationAB;
        /// <summary>Every resident's opinion of the guild (the guild's standing in town).</summary>
        public int Opinion;
        /// <summary>One resident's opinion of the guild.</summary>
        public string ResidentId;
        public int ResidentOpinion;
        /// <summary>A's gear goes one tier up.</summary>
        public bool GearA;
        public Emotion EmotionA, EmotionB, EmotionAll;
        public float EmotionHours = 5f;
        public string Rumor;
        public int RumorTone;
        public WandererOffer Wanderer;
        /// <summary>What happened, shown after the choice.</summary>
        public string Result;
    }

    public class CardChoice
    {
        public string Label;
        /// <summary>Gold paid up front (the choice is greyed out without it).</summary>
        public int Cost;
        /// <summary>Chance of <see cref="Success"/> (1 = sure; otherwise <see cref="Failure"/>).</summary>
        public float Chance = 1f;
        /// <summary>The chance is A beating B in a duel (levels decide it, see <see cref="EventCards.DuelChance"/>).</summary>
        public bool DuelOdds;
        public CardEffect Success = new();
        public CardEffect Failure;
        /// <summary>What happens when nobody answers in time.</summary>
        public bool Default;
    }

    public class EventCardDef
    {
        public string Id;
        public string Title;
        public string Situation;
        public CastRule Cast;
        public bool NeedsResidents;
        public bool NeedsVisitorRoom;
        public int MinGold;
        public List<CardChoice> Choices = new();

        public CardChoice DefaultChoice => Choices.FirstOrDefault(c => c.Default) ?? Choices[Choices.Count - 1];
    }

    /// <summary>A member as the card rules see them.</summary>
    public struct CardMember
    {
        public string Id, Name;
        public int Level;
        public float Stress, Fatigue;
        public int GearTier;
        /// <summary>A better gear tier exists for them.</summary>
        public bool CanGearUp;
        /// <summary>In town and not on a quest.</summary>
        public bool Free;
    }

    /// <summary>The state of the guild when a card is drawn (built by EventCardService; plain data for tests).</summary>
    public class CardContext
    {
        public List<CardMember> Members = new();
        /// <summary>How two members get along (average of both directions, 0 when they have no feelings yet).</summary>
        public Func<string, string, int> Bond = (_, _) => 0;
        public int Gold;
        public int Day;
        public bool HasResidents;
        public bool HasVisitorRoom;
        /// <summary>Cards drawn lately, not drawn again soon.</summary>
        public ICollection<string> Recent = new List<string>();
    }

    /// <summary>A card drawn for the guild master, waiting for (or done with) an answer.</summary>
    public class DrawnCard
    {
        public int Serial;
        public EventCardDef Def;
        public CardMember? A, B;
        public float DrawnAt;
        /// <summary>The situation as shown: the formatted template, or the model's description once it arrives.</summary>
        public string Narration;
        public bool NarratedByLlm;
        public bool Resolved;
        public int Chosen = -1;
        public bool Succeeded;
        public bool Expired;
        public string ResultText;

        public string Title => EventCards.Format(Def.Title, A?.Name, B?.Name);
        public string Label(int i) => EventCards.Format(Def.Choices[i].Label, A?.Name, B?.Name);
    }

    /// <summary>
    /// Event cards (F-13, user request 2026-10-11): 0–2 a day, each a small situation with two or three choices —
    /// a duel, a shady merchant, a noble's reckless job, a sickness in town, a famous wanderer passing through, a
    /// festival, a birthday, a thief, the village child's wish, two friends who want to work together. Results move
    /// stress, relationships, the residents' opinion of the guild, gold, rumors and feelings, and come from this table
    /// only. Pure rules and data, covered by EditMode tests.
    /// </summary>
    public static class EventCards
    {
        // ---------------- When cards come ----------------

        public static readonly int[] DrawHours = { 10, 16 };
        public const float DrawChance = 0.5f;
        public const int MaxPerDay = 2;
        /// <summary>The first day starts quietly: no card before this hour.</summary>
        public const int FirstDayFromHour = 16;
        /// <summary>Unanswered for this long, the default choice happens.</summary>
        public const float ExpireHours = 8f;
        /// <summary>A card is not drawn again while it is among the last few.</summary>
        public const int RecentMemory = 4;
        public const int HostileBond = -10;
        public const int FriendlyBond = 30;

        public static bool ShouldDraw(int day, int hour, int drawnToday, bool pending, bool raid, float roll)
        {
            if (pending || raid || drawnToday >= MaxPerDay || Array.IndexOf(DrawHours, hour) < 0) return false;
            if (day <= 1 && hour < FirstDayFromHour) return false;
            return roll < DrawChance;
        }

        /// <summary>Chance that A wins a duel against B: even at the same level, 10 points per level of difference.</summary>
        public static float DuelChance(int levelA, int levelB) => Math.Clamp(0.5f + 0.1f * (levelA - levelB), 0.2f, 0.8f);

        public static float ChanceOf(CardChoice c, CardMember? a, CardMember? b)
            => c.DuelOdds && a.HasValue && b.HasValue ? DuelChance(a.Value.Level, b.Value.Level) : c.Chance;

        // ---------------- Drawing ----------------

        /// <summary>Whether the card can happen now, and to whom.</summary>
        public static bool TryCast(EventCardDef def, CardContext ctx, Func<float> roll, out CardMember? a, out CardMember? b)
        {
            a = null;
            b = null;
            if (def == null || ctx == null) return false;
            if (def.NeedsResidents && !ctx.HasResidents) return false;
            if (def.NeedsVisitorRoom && !ctx.HasVisitorRoom) return false;
            if (ctx.Gold < def.MinGold) return false;
            var members = ctx.Members;
            var free = members.Where(m => m.Free).ToList();
            switch (def.Cast)
            {
                case CastRule.None:
                    return true;
                case CastRule.Any:
                    if (members.Count == 0) return false;
                    a = members[Index(roll, members.Count)];
                    return true;
                case CastRule.AnyFree:
                    if (free.Count == 0) return false;
                    a = free[Index(roll, free.Count)];
                    return true;
                case CastRule.LowestGear:
                {
                    var pick = free.Where(m => m.CanGearUp).OrderBy(m => m.GearTier).ThenBy(m => m.Level).ToList();
                    if (pick.Count == 0) return false;
                    a = pick[0];
                    return true;
                }
                case CastRule.Freshest:
                    if (free.Count == 0) return false;
                    a = free.OrderBy(m => m.Fatigue).First();
                    return true;
                case CastRule.HostilePair:
                case CastRule.FriendlyPair:
                {
                    var pool = def.Cast == CastRule.HostilePair ? free : members;
                    CardMember? bestA = null, bestB = null;
                    int best = def.Cast == CastRule.HostilePair ? int.MaxValue : int.MinValue;
                    for (int i = 0; i < pool.Count; i++)
                    for (int j = i + 1; j < pool.Count; j++)
                    {
                        int bond = ctx.Bond(pool[i].Id, pool[j].Id);
                        bool better = def.Cast == CastRule.HostilePair ? bond < best : bond > best;
                        if (!better) continue;
                        best = bond;
                        bestA = pool[i];
                        bestB = pool[j];
                    }
                    bool fits = def.Cast == CastRule.HostilePair ? best <= HostileBond : best >= FriendlyBond;
                    if (!bestA.HasValue || !fits) return false;
                    a = bestA;
                    b = bestB;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Picks a card that can happen now (not one of the recent ones), or null.</summary>
        public static DrawnCard Draw(CardContext ctx, Func<float> roll, IReadOnlyList<EventCardDef> pool = null)
        {
            pool ??= All;
            var candidates = new List<(EventCardDef def, CardMember? a, CardMember? b)>();
            foreach (var def in pool)
            {
                if (ctx.Recent != null && ctx.Recent.Contains(def.Id)) continue;
                if (TryCast(def, ctx, roll, out var a, out var b)) candidates.Add((def, a, b));
            }
            if (candidates.Count == 0) return null;
            var c = candidates[Index(roll, candidates.Count)];
            return new DrawnCard { Def = c.def, A = c.a, B = c.b, Narration = Format(c.def.Situation, c.a?.Name, c.b?.Name) };
        }

        static int Index(Func<float> roll, int count) => Math.Clamp((int)(roll() * count), 0, count - 1);

        /// <summary>Which result a choice gets: success when the roll is under the chance (or there is no failure).</summary>
        public static (bool success, CardEffect effect) Resolve(CardChoice c, CardMember? a, CardMember? b, float roll)
        {
            if (c.Failure == null) return (true, c.Success);
            bool ok = roll < ChanceOf(c, a, b);
            return (ok, ok ? c.Success : c.Failure);
        }

        public static bool CanAfford(CardChoice c, int gold) => gold >= c.Cost;

        /// <summary>The short line under a choice: cost and odds (what it does stays a surprise).</summary>
        public static string Hint(CardChoice c, CardMember? a, CardMember? b)
        {
            var parts = new List<string>();
            if (c.Cost > 0) parts.Add($"비용 {c.Cost}G");
            if (c.Failure != null) parts.Add($"성공 확률 {Math.Round(ChanceOf(c, a, b) * 100)}%");
            return string.Join(" · ", parts);
        }

        // ---------------- Text ----------------

        /// <summary>Fills {A} {B} and the name-dependent particles {A이} {A을} {A와} {A은} (same for B).</summary>
        public static string Format(string template, string a, string b)
        {
            if (string.IsNullOrEmpty(template)) return template ?? "";
            var s = template;
            foreach (var (key, name) in new[] { ("A", a), ("B", b) })
            {
                string n = name ?? "누군가";
                s = s.Replace("{" + key + "이}", n + MemoryRecorder.Josa(n, "이", "가"))
                     .Replace("{" + key + "을}", n + MemoryRecorder.Josa(n, "을", "를"))
                     .Replace("{" + key + "와}", n + MemoryRecorder.Josa(n, "과", "와"))
                     .Replace("{" + key + "은}", n + MemoryRecorder.Josa(n, "은", "는"))
                     .Replace("{" + key + "}", n);
            }
            return s;
        }

        // ---------------- The cards ----------------

        public static readonly IReadOnlyList<EventCardDef> All = new List<EventCardDef>
        {
            new()
            {
                Id = "duel", Title = "결투 신청", Cast = CastRule.HostilePair,
                Situation = "{A이} 길드 앞마당에서 {B}에게 결투를 신청했습니다. 둘 사이에 쌓인 앙금이 터지기 직전입니다.",
                Choices =
                {
                    new()
                    {
                        Label = "결투를 허락한다", DuelOdds = true,
                        Success = new() { FatigueA = 10, StressB = 8, RelationAB = 10, EmotionA = Emotion.Excited, EmotionB = Emotion.Angry,
                            Rumor = "{A이} 결투에서 {B을} 꺾었다", Result = "{A이} 이겼습니다. 한바탕 붙고 나니 둘 사이의 앙금이 조금 풀렸습니다." },
                        Failure = new() { FatigueA = 10, StressA = 8, RelationAB = 10, EmotionA = Emotion.Angry, EmotionB = Emotion.Excited,
                            Rumor = "{B이} 결투에서 {A을} 꺾었다", Result = "{B이} 이겼습니다. {A은} 분해하지만, 둘 사이의 앙금은 조금 풀렸습니다." },
                    },
                    new()
                    {
                        Label = "직접 중재한다", Chance = 0.6f,
                        Success = new() { RelationAB = 12, StressA = -5, StressB = -5, EmotionA = Emotion.Happy, EmotionB = Emotion.Happy,
                            Result = "길드 마스터의 중재로 둘이 마지못해 악수했습니다." },
                        Failure = new() { RelationAB = -6, StressA = 5, StressB = 5, EmotionA = Emotion.Angry, EmotionB = Emotion.Angry,
                            Result = "중재는 실패했습니다. 둘 다 길드 마스터가 상대 편을 든다고 여깁니다." },
                    },
                    new()
                    {
                        Label = "둘을 말린다", Default = true,
                        Success = new() { StressA = 5, StressB = 5, RelationAB = -3, EmotionA = Emotion.Angry,
                            Result = "결투는 막았지만 둘 다 분이 덜 풀렸습니다." },
                    },
                },
            },
            new()
            {
                Id = "shady", Title = "수상한 상인", Cast = CastRule.LowestGear, MinGold = 60,
                Situation = "망토를 깊게 눌러쓴 상인이 '마법이 깃든 장비'를 시세의 절반에 넘기겠다고 합니다. {A이} 눈을 반짝입니다.",
                Choices =
                {
                    new()
                    {
                        Label = "산다", Cost = 60, Chance = 0.5f,
                        Success = new() { GearA = true, EmotionA = Emotion.Excited, Result = "진짜였습니다! {A}의 장비가 한 단계 좋아졌습니다." },
                        Failure = new() { EmotionA = Emotion.Sad, Opinion = -1, Rumor = "길드가 떠돌이 상인에게 가짜 장비를 샀다", RumorTone = -1,
                            Result = "가짜였습니다. 상인은 이미 사라졌습니다." },
                    },
                    new()
                    {
                        Label = "경비병에게 알린다",
                        Success = new() { Opinion = 2, ResidentId = "RES_greta", ResidentOpinion = 8, Rumor = "길드가 수상한 상인을 경비대에 알렸다", RumorTone = 1,
                            Result = "경비병 그레타가 상인을 마을 밖으로 쫓아냈습니다. 주민들이 고마워합니다." },
                    },
                    new()
                    {
                        Label = "무시한다", Default = true,
                        Success = new() { Result = "상인은 투덜거리며 다른 손님을 찾아 떠났습니다." },
                    },
                },
            },
            new()
            {
                Id = "noble", Title = "귀족의 무모한 부탁", Cast = CastRule.None,
                Situation = "이웃 영지의 젊은 귀족이 금화 주머니를 흔들며, 길드원들을 데리고 몬스터 둥지를 구경하겠다고 합니다. 위험하기 짝이 없는 일입니다.",
                Choices =
                {
                    new()
                    {
                        Label = "받는다 (+150G)",
                        Success = new() { Gold = 150, StressAll = 8, Opinion = -2, Rumor = "길드가 귀족의 위험한 나들이를 맡았다",
                            Result = "두둑한 돈을 받았지만 길드원들은 귀족을 지키느라 진땀을 뺐습니다." },
                    },
                    new()
                    {
                        Label = "조건을 깎는다", Chance = 0.6f,
                        Success = new() { Gold = 80, StressAll = 3, Result = "덜 위험한 길로 돌아가는 조건으로 80G를 받았습니다." },
                        Failure = new() { Rumor = "귀족이 길드와 흥정하다 화를 내고 떠났다", Result = "귀족은 기분이 상해 떠났습니다." },
                    },
                    new()
                    {
                        Label = "거절한다", Default = true,
                        Success = new() { StressAll = -2, Result = "길드원들이 안도의 한숨을 쉬었습니다." },
                    },
                },
            },
            new()
            {
                Id = "plague", Title = "역병의 조짐", Cast = CastRule.Freshest, NeedsResidents = true,
                Situation = "마을에 기침하는 사람이 부쩍 늘었습니다. 약초상 마리가 약을 지을 약초가 모자란다며 길드에 도움을 청합니다.",
                Choices =
                {
                    new()
                    {
                        Label = "약초 값을 댄다", Cost = 60,
                        Success = new() { Opinion = 5, ResidentId = "RES_mari", ResidentOpinion = 10, Rumor = "길드가 역병 약값을 댔다", RumorTone = 1,
                            Result = "마리가 약을 지어 돌렸고, 마을 사람들이 길드를 칭찬합니다." },
                    },
                    new()
                    {
                        Label = "{A을} 보내 약초를 캔다",
                        Success = new() { FatigueA = 25, Opinion = 4, ResidentId = "RES_mari", ResidentOpinion = 8, EmotionA = Emotion.Happy,
                            Rumor = "{A이} 역병 약초를 캐 왔다", RumorTone = 1, Result = "{A이} 지친 몸으로 약초 바구니를 가득 채워 왔습니다." },
                    },
                    new()
                    {
                        Label = "모른 척한다", Default = true,
                        Success = new() { Opinion = -5, ResidentId = "RES_mari", ResidentOpinion = -10, Rumor = "길드가 역병을 모른 척했다", RumorTone = -1,
                            Result = "마리는 실망한 얼굴로 돌아갔습니다." },
                    },
                },
            },
            new()
            {
                Id = "wanderer", Title = "떠돌이 실력자", Cast = CastRule.None, NeedsVisitorRoom = true,
                Situation = "이름난 떠돌이 모험가가 하루만 마을에 머문다고 합니다. 길드에 들어올지는 그의 마음에 달렸습니다.",
                Choices =
                {
                    new()
                    {
                        Label = "환영 연회를 연다", Cost = 40,
                        Success = new() { Wanderer = WandererOffer.Welcomed, StressAll = -3, EmotionAll = Emotion.Happy, EmotionHours = 3f,
                            Result = "연회 덕분에 그가 길드를 좋게 보고 있습니다. 말을 걸어 권유해 보세요." },
                    },
                    new()
                    {
                        Label = "인사만 건넨다",
                        Success = new() { Wanderer = WandererOffer.Plain, Result = "그가 마을에 들렀습니다. 영입하려면 직접 설득해야 합니다." },
                    },
                    new()
                    {
                        Label = "내버려 둔다", Default = true,
                        Success = new() { Result = "그는 술집에서 하룻밤을 보내고 떠났습니다." },
                    },
                },
            },
            new()
            {
                Id = "festival", Title = "마을 잔치", Cast = CastRule.None, NeedsResidents = true,
                Situation = "추수를 기념하는 마을 잔치가 열립니다. 촌장 바르톨이 길드도 비용을 조금 보태 주길 바랍니다.",
                Choices =
                {
                    new()
                    {
                        Label = "비용을 보탠다", Cost = 50,
                        Success = new() { StressAll = -10, EmotionAll = Emotion.Happy, Opinion = 4, ResidentId = "RES_bartol", ResidentOpinion = 8,
                            Result = "길드원들이 잔치에서 실컷 웃고 떠들었습니다. 촌장도 흡족해합니다." },
                    },
                    new()
                    {
                        Label = "길드원들만 보낸다",
                        Success = new() { StressAll = -5, Opinion = -1, ResidentId = "RES_bartol", ResidentOpinion = -4,
                            Result = "길드원들은 즐거웠지만, 촌장은 길드가 인색하다고 투덜댑니다." },
                    },
                    new()
                    {
                        Label = "일이 바빠 넘어간다", Default = true,
                        Success = new() { StressAll = 2, Result = "길드원들이 창문 너머로 잔치를 부러운 듯 바라봅니다." },
                    },
                },
            },
            new()
            {
                Id = "birthday", Title = "{A}의 생일", Cast = CastRule.Any,
                Situation = "오늘은 {A}의 생일입니다. 본인은 아무렇지 않은 척하지만 은근히 기대하는 눈치입니다.",
                Choices =
                {
                    new()
                    {
                        Label = "선물을 사 준다", Cost = 30,
                        Success = new() { StressA = -15, EmotionA = Emotion.Happy, EmotionHours = 8f, Result = "{A이} 선물을 받고 환하게 웃었습니다." },
                    },
                    new()
                    {
                        Label = "축하한다는 말만 건넨다",
                        Success = new() { StressA = -5, EmotionA = Emotion.Happy, EmotionHours = 3f, Result = "{A이} 쑥스러워하며 고맙다고 합니다." },
                    },
                    new()
                    {
                        Label = "모른 척한다", Default = true,
                        Success = new() { StressA = 6, EmotionA = Emotion.Sad, EmotionHours = 8f, Result = "{A이} 하루 종일 시무룩합니다." },
                    },
                },
            },
            new()
            {
                Id = "thief", Title = "금고 도둑", Cast = CastRule.Freshest, MinGold = 40,
                Situation = "길드 금고에 도둑이 들었습니다! {A이} 골목으로 달아나는 그림자를 봤다고 합니다.",
                Choices =
                {
                    new()
                    {
                        Label = "{A}에게 쫓게 한다", Chance = 0.6f,
                        Success = new() { FatigueA = 15, EmotionA = Emotion.Excited, Opinion = 2, Rumor = "{A이} 길드 금고를 턴 도둑을 잡았다", RumorTone = 1,
                            Result = "{A이} 도둑을 붙잡아 돈을 되찾았습니다!" },
                        Failure = new() { Gold = -40, FatigueA = 15, EmotionA = Emotion.Angry, Result = "도둑을 놓쳤고 40G를 잃었습니다." },
                    },
                    new()
                    {
                        Label = "경비대에 맡긴다", Chance = 0.4f,
                        Success = new() { ResidentId = "RES_greta", ResidentOpinion = 5, Result = "경비병 그레타가 도둑을 잡아 돈을 돌려줬습니다." },
                        Failure = new() { Gold = -40, Result = "경비대도 도둑을 찾지 못했습니다. 40G를 잃었습니다." },
                    },
                    new()
                    {
                        Label = "그냥 넘어간다", Default = true,
                        Success = new() { Gold = -40, Result = "40G를 잃었습니다." },
                    },
                },
            },
            new()
            {
                Id = "toby", Title = "꼬마 토비의 부탁", Cast = CastRule.AnyFree, NeedsResidents = true,
                Situation = "꼬마 토비가 {A}의 옷자락을 붙잡고, 진짜 모험가의 훈련을 구경하게 해 달라고 조릅니다.",
                Choices =
                {
                    new()
                    {
                        Label = "훈련을 보여 준다",
                        Success = new() { ResidentId = "RES_toby", ResidentOpinion = 12, Opinion = 1, FatigueA = 5, EmotionA = Emotion.Happy,
                            Result = "토비가 눈을 반짝이며 {A}의 몸놀림을 따라 합니다." },
                    },
                    new()
                    {
                        Label = "목검을 하나 사 준다", Cost = 20,
                        Success = new() { ResidentId = "RES_toby", ResidentOpinion = 20, Opinion = 2, Rumor = "길드가 토비에게 목검을 사 줬다", RumorTone = 1,
                            Result = "토비가 목검을 들고 온 마을을 뛰어다닙니다." },
                    },
                    new()
                    {
                        Label = "위험하다며 돌려보낸다", Default = true,
                        Success = new() { ResidentId = "RES_toby", ResidentOpinion = -6, Result = "토비가 입을 삐죽 내밀고 돌아갔습니다." },
                    },
                },
            },
            new()
            {
                Id = "bond", Title = "의기투합", Cast = CastRule.FriendlyPair,
                Situation = "{A와} {B은} 요즘 늘 붙어 다닙니다. 앞으로도 같은 조로 의뢰를 다니고 싶다고 합니다.",
                Choices =
                {
                    new()
                    {
                        Label = "한 조로 밀어 준다",
                        Success = new() { RelationAB = 8, StressA = -3, StressB = -3, EmotionA = Emotion.Happy, EmotionB = Emotion.Happy,
                            Result = "둘은 신이 나서 함께 다닐 계획을 세웁니다." },
                    },
                    new()
                    {
                        Label = "공과 사는 구분하라고 한다", Default = true,
                        Success = new() { RelationAB = -2, StressA = 2, StressB = 2, Result = "둘은 아쉬운 얼굴로 고개를 끄덕였습니다." },
                    },
                },
            },
        };
    }
}
