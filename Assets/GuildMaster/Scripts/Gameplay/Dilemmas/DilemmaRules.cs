using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Seasons;
using GuildMaster.Social;
using GuildMaster.Storytelling;
using UnityEngine;

namespace GuildMaster.Dilemmas
{
    /// <summary>
    /// What a choice does, roughly — all the card shows of it (user decision 2026-10-30: the direction only). Town: what the
    /// townsfolk think of the guild (P4-118, from the event cards).
    /// </summary>
    public enum DilemmaHint { GoldUp, GoldDown, MoodUp, MoodDown, FameUp, FameDown, StockUp, StockDown, Growth, Tired, Risk, Later, Leaving, TownUp, TownDown }

    /// <summary>
    /// How a decision's result reads (P4-118, from KMS's event cards — user request 2026-11-01: success green, failure red, a
    /// sound each). A rolled choice succeeds or fails; a sure one is good, bad or neither by what it does.
    /// </summary>
    public enum DilemmaTone { Neutral, Success, Failure, Good, Bad }

    /// <summary>Whom a card is about, picked from the guild when it is dealt.</summary>
    public enum DilemmaCast
    {
        None,
        /// <summary>Any one member.</summary>
        Anyone,
        /// <summary>Two who do not get on (one thinks the other awkward or worse).</summary>
        TwoRivals,
        /// <summary>Two who are close.</summary>
        TwoFriends,
        /// <summary>A seasoned member (level <see cref="DilemmaRules.VeteranLevel"/> or more).</summary>
        Veteran,
        /// <summary>A newcomer (level <see cref="DilemmaRules.NewcomerLevel"/> or less), then a seasoned one to learn from.</summary>
        NewcomerAndVeteran,
        /// <summary>One idle in town, not worn out.</summary>
        InTown,
        /// <summary>The member with the weakest gear who can still be geared up (P4-118).</summary>
        WeakestGear,
    }

    /// <summary>What the guild knows and has when a card might be dealt (everything here is the guild's own: information hiding).</summary>
    public struct DilemmaFacts
    {
        public int Day;
        public Season Season;
        public int Gold;
        public int Members;
        public bool Rivals, Friends, Veteran, NewcomerAndVeteran, InTown;
        /// <summary>Someone could wear better gear (<see cref="DilemmaCast.WeakestGear"/>).</summary>
        public bool GearUp;
        /// <summary>The town has its residents (cards that are about one of them).</summary>
        public bool Residents;
        /// <summary>A standing village the guild knows, by name; null if none.</summary>
        public string Village;
        /// <summary>A kind of monster the guild knows, by name; null if none.</summary>
        public string Species;
        public int Potions;
        public bool GatesWorn;
        public bool RuinToFind;
        public bool HasCast(DilemmaCast c) => c switch
        {
            DilemmaCast.None => true,
            DilemmaCast.Anyone => Members > 0,
            DilemmaCast.TwoRivals => Rivals,
            DilemmaCast.TwoFriends => Friends,
            DilemmaCast.Veteran => Veteran,
            DilemmaCast.NewcomerAndVeteran => NewcomerAndVeteran,
            DilemmaCast.WeakestGear => GearUp,
            _ => InTown,
        };
    }

    /// <summary>
    /// What a choice can do (P4-111c). Actors are the card's members by number (0, 1); -1 means every member. Texts are
    /// templates: {0} {1} names, {g0} {g1} the card's gold, {v} village, {s} kind, with 이(가)·을(를)·와(과)·은(는) after them.
    /// </summary>
    public interface IDilemmaEffects
    {
        /// <summary>The card's gold amount <paramref name="index"/>, for the day.</summary>
        int G(int index);
        bool Chance(float p);
        /// <summary>Into the vault (positive) or out of it, as much as there is (negative).</summary>
        void Gold(int amount);
        void Stock(string itemId, int count);
        /// <summary>A thought for <paramref name="actor"/> (-1: every member); <paramref name="about"/>: the actor its words name.</summary>
        void Think(int actor, ThoughtDef def, int about = -1);
        /// <summary>A thought for every member but the card's own.</summary>
        void ThinkOthers(ThoughtDef def);
        void Stress(int actor, float amount);
        void Fatigue(int actor, float amount);
        void Fame(int amount);
        void Relate(int a, int b, int delta);
        void Exp(int actor, int amount);
        /// <summary>The member leaves the guild; <paramref name="notice"/> is what the guild hears.</summary>
        void Depart(int actor, string notice);
        /// <summary>A visitor comes to the door now. False if nobody could.</summary>
        bool Visitor();
        /// <summary>Word of a ruin to explore, now. False if there is none left to find.</summary>
        bool RuinRumour();
        /// <summary>Word of a rare monster, now. False if none could be found.</summary>
        bool RareRumour();
        void RepairGates();
        void CutTax(float share);
        /// <summary>A card that follows on from this one, dealt <paramref name="hours"/> from now (same members and amounts).</summary>
        void FollowUp(string id, float hours);
        /// <summary>A line of what came of it.</summary>
        void Tell(string text);

        // ---- From KMS's event cards (P4-118) ----

        /// <summary>The member's level (1 if they are gone).</summary>
        int Level(int actor);
        /// <summary>What the townsfolk think of the guild: every resident's opinion, or one resident's (<paramref name="residentId"/>).</summary>
        void Standing(int amount, string residentId = null);
        /// <summary>Word goes round the town: the card's members and the town's chatterboxes know it; <paramref name="tone"/> is how it reflects on the guild and on them (-1..1).</summary>
        void Rumor(string text, int tone = 0);
        /// <summary>A passing feeling over <paramref name="actor"/>'s head (-1: every member) for <paramref name="hours"/>.</summary>
        void Feel(int actor, Emotion feeling, float hours = 5f);
        /// <summary>The member's gear goes one tier up. False if there is nothing better for them.</summary>
        bool GearUp(int actor);
        /// <summary>A seasoned wanderer comes to the door, stronger than the guild's best; <paramref name="welcomed"/>: already well disposed. False if nobody could.</summary>
        bool Wanderer(bool welcomed);
        /// <summary>For a rolled choice: did it go well (the result shows green ✔ or red ✘).</summary>
        void Outcome(bool success);
    }

    public sealed class DilemmaChoice
    {
        public string Label;
        public DilemmaHint[] Hints = Array.Empty<DilemmaHint>();
        /// <summary>The card's gold amount this needs in the vault (-1: none).</summary>
        public int Cost = -1;
        public string NeedsItem;
        public int NeedsCount;
        public Action<IDilemmaEffects> Apply;
    }

    public sealed class DilemmaDef
    {
        public string Id, Title, Text;
        public DilemmaCast Cast;
        /// <summary>Base gold amounts ({g0}, {g1}…), scaled to the day when dealt.</summary>
        public int[] Gold = Array.Empty<int>();
        public Season? Season;
        public Func<DilemmaFacts, bool> When;
        /// <summary>Only ever comes after another card's choice.</summary>
        public bool FollowUpOnly;
        /// <summary>What is decided when nobody answers in time.</summary>
        public int Default;
        public DilemmaChoice[] Choices;
    }

    /// <summary>
    /// Decision cards (P4-111c, H3 — FTL, Reigns, Frostpunk; user decisions 2026-10-30: the outcome shown only roughly, fixed
    /// words with names filled in, 24 game hours to answer before the default, a window of their own). The storyteller deals
    /// none to two a day; each is a choice the guild has to make that costs something either way. Pure: the catalogue, when a
    /// card can come, how it reads. <see cref="DilemmaService"/> deals them and carries the choice out.
    /// P4-118 (user decision 2026-11-02): KMS's event cards (F-13) are folded in — the five that were like ones here became
    /// one card each (duel, theft, wanderer, feast, shady trader), the other five came over (noble, plague, birthday, Toby,
    /// two friends), with their effects on the townsfolk, rumours, feelings, gear and a seasoned wanderer, and their result
    /// colours. A dealt card opens its window and the game waits (<see cref="DilemmaService.AutoOpen"/>).
    /// </summary>
    public static class DilemmaRules
    {
        public const int FirstDay = 2;
        /// <summary>The hours of the day a card may be dealt.</summary>
        public static bool DealHour(int hour) => hour == 9 || hour == 17;
        public const int MaxOpen = 2;
        public const float DeadlineHours = 24f;
        /// <summary>The same card does not come again for this long.</summary>
        public const float CooldownHours = 96f;
        public const int VeteranLevel = 8, NewcomerLevel = 3;
        /// <summary>A member is a rival at this opinion or below, a friend at or above <see cref="FriendAt"/>.</summary>
        public const int RivalAt = -15, FriendAt = 40;
        /// <summary>A card of the season weighs this much more in the draw, in its season.</summary>
        public const float SeasonWeight = 2f;
        public const int MaxHistory = 12;
        /// <summary>A wanderer the guild threw a feast for starts the recruit talk this warm.</summary>
        public const int WelcomedAffinity = 60;
        /// <summary>Residents the cards are about, and the two who pass every rumour on.</summary>
        public const string Greta = "RES_greta", Mari = "RES_mari", Bartol = "RES_bartol", Toby = "RES_toby";
        public static readonly string[] Chatterboxes = { "RES_elly", "RES_bartol" };

        /// <summary>Chance the first of two wins a duel: even at the same level, ten points per level between them.</summary>
        public static float DuelChance(int levelA, int levelB) => Mathf.Clamp(0.5f + 0.1f * (levelA - levelB), 0.2f, 0.8f);

        /// <summary>Chance a card is dealt at a deal hour: the storyteller deals more in the quiet, fewer when it holds back.</summary>
        public static float DealChance(StoryPhase phase) => phase switch
        {
            StoryPhase.Relax => 0.4f,
            StoryPhase.Recovery => 0.3f,
            _ => 0.55f,
        };

        /// <summary>Gold grows with the guild: ×1 on day 1, +8% a day, at most ×3, to the nearest 5.</summary>
        public static int ScaledGold(int baseGold, int day)
            => Mathf.RoundToInt(baseGold * Mathf.Clamp(1f + 0.08f * (day - 1), 1f, 3f) / 5f) * 5;

        public static bool CanCome(DilemmaDef d, DilemmaFacts f)
            => !d.FollowUpOnly && f.Day >= FirstDay && (d.Season == null || d.Season == f.Season) && f.HasCast(d.Cast) && (d.When == null || d.When(f));

        /// <summary>A card's weight in the draw (its season's cards more, in season).</summary>
        public static float Weight(DilemmaDef d) => d.Season.HasValue ? SeasonWeight : 1f;

        /// <summary>Can this choice be made now: its gold and things in the vault.</summary>
        public static bool Affordable(DilemmaChoice c, IReadOnlyList<int> gold, int vault, Func<string, int> stock)
            => (c.Cost < 0 || c.Cost < gold.Count && vault >= gold[c.Cost]) && (c.NeedsItem == null || stock(c.NeedsItem) >= c.NeedsCount);

        // ---------------- Words ----------------

        /// <summary>A hint's words (the window shows them as chips, <see cref="DilemmaChips"/>).</summary>
        public static string HintText(DilemmaHint h) => DilemmaChips.Of(h).Text;

        public static string Hints(IReadOnlyList<DilemmaHint> hints) => hints.Count == 0 ? "변화 없음" : string.Join(" · ", hints.Select(HintText));

        // ---------------- How a result reads ----------------

        static bool IsGoodHint(DilemmaHint h) => h is DilemmaHint.GoldUp or DilemmaHint.MoodUp or DilemmaHint.FameUp or DilemmaHint.StockUp
                                                 or DilemmaHint.Growth or DilemmaHint.TownUp;
        static bool IsBadHint(DilemmaHint h) => h is DilemmaHint.GoldDown or DilemmaHint.MoodDown or DilemmaHint.FameDown or DilemmaHint.StockDown
                                                or DilemmaHint.Tired or DilemmaHint.Leaving or DilemmaHint.TownDown;

        /// <summary>
        /// A rolled choice reads by how the roll went (<paramref name="outcome"/>); a sure one by what it does — a clear gain
        /// good, a clear loss bad, a trade (gold for everyone's spirits) or a gamble whose end is still to come neither.
        /// </summary>
        public static DilemmaTone ToneOf(DilemmaChoice c, bool? outcome)
        {
            if (outcome.HasValue) return outcome.Value ? DilemmaTone.Success : DilemmaTone.Failure;
            if (c == null || c.Hints.Contains(DilemmaHint.Risk) || c.Hints.Contains(DilemmaHint.Later)) return DilemmaTone.Neutral;
            bool good = c.Hints.Any(IsGoodHint), bad = c.Hints.Any(IsBadHint);
            return good == bad ? DilemmaTone.Neutral : good ? DilemmaTone.Good : DilemmaTone.Bad;
        }

        /// <summary>The line over a result: "✔ 성공" and the like; empty for a plain one.</summary>
        public static string Badge(DilemmaTone t) => t switch
        {
            DilemmaTone.Success => "✔ 성공",
            DilemmaTone.Failure => "✘ 실패",
            DilemmaTone.Good => "▲ 좋은 결과",
            DilemmaTone.Bad => "▼ 아쉬운 결과",
            _ => "",
        };

        /// <summary>Green for a success or a good result, red for a failure or a bad one.</summary>
        public static bool IsGood(DilemmaTone t) => t is DilemmaTone.Success or DilemmaTone.Good;
        public static bool IsBad(DilemmaTone t) => t is DilemmaTone.Failure or DilemmaTone.Bad;

        /// <summary>Fills a template: {0} {1} names, {g0}.. gold, {v} village, {s} kind, each with its particles.</summary>
        public static string Fill(string text, IReadOnlyList<string> names, IReadOnlyList<int> gold, string village, string species)
        {
            if (string.IsNullOrEmpty(text)) return text;
            for (int i = 0; i < (gold?.Count ?? 0); i++) text = text.Replace("{g" + i + "}", gold[i].ToString());
            for (int i = 0; i < (names?.Count ?? 0); i++) text = Put(text, "{" + i + "}", names[i]);
            text = Put(text, "{v}", village ?? "이웃 마을");
            return Put(text, "{s}", species ?? "몬스터");
        }

        static string Put(string text, string token, string word)
        {
            if (!text.Contains(token)) return text;
            return text.Replace(token + "이(가)", word + MemoryRecorder.Josa(word, "이", "가"))
                       .Replace(token + "을(를)", word + MemoryRecorder.Josa(word, "을", "를"))
                       .Replace(token + "와(과)", word + MemoryRecorder.Josa(word, "과", "와"))
                       .Replace(token + "은(는)", word + MemoryRecorder.Josa(word, "은", "는"))
                       .Replace(token, word);
        }

        // ---------------- The cards ----------------

        static DilemmaChoice C(string label, Action<IDilemmaEffects> apply, params DilemmaHint[] hints) => new() { Label = label, Apply = apply, Hints = hints };

        static DilemmaChoice Paid(int cost, string label, Action<IDilemmaEffects> apply, params DilemmaHint[] hints)
            => new() { Label = label, Apply = apply, Hints = hints, Cost = cost };

        const DilemmaHint GoldUp = DilemmaHint.GoldUp, GoldDown = DilemmaHint.GoldDown, MoodUp = DilemmaHint.MoodUp, MoodDown = DilemmaHint.MoodDown,
            FameUp = DilemmaHint.FameUp, FameDown = DilemmaHint.FameDown, StockUp = DilemmaHint.StockUp, StockDown = DilemmaHint.StockDown,
            Growth = DilemmaHint.Growth, Tired = DilemmaHint.Tired, Risk = DilemmaHint.Risk, Later = DilemmaHint.Later, Leaving = DilemmaHint.Leaving,
            TownUp = DilemmaHint.TownUp, TownDown = DilemmaHint.TownDown;

        public const string Potion = Data.DefaultContent.Potion;
        public const string Ore = "iron_ore";

        public static readonly IReadOnlyList<DilemmaDef> All = new DilemmaDef[]
        {
            // ---- Among the members ----
            new()
            {
                Id = "duel", Title = "결투 신청", Cast = DilemmaCast.TwoRivals, Gold = new[] { 40 }, Default = 3,
                Text = "{0}이(가) 길드 앞마당에서 {1}에게 결투를 신청했습니다. 둘 사이에 쌓인 앙금이 터지기 직전입니다.",
                Choices = new[]
                {
                    C("결투를 허락한다", e =>
                    {
                        // Their levels decide it (from the event card).
                        int won = e.Chance(DuelChance(e.Level(0), e.Level(1))) ? 0 : 1, lost = 1 - won;
                        e.Think(won, ThoughtRules.DuelWon, lost);
                        e.Think(lost, ThoughtRules.DuelLost, won);
                        e.Feel(won, Emotion.Excited);
                        e.Feel(lost, Emotion.Angry);
                        e.Stress(won, -10f);
                        e.Fatigue(lost, 20f);
                        e.Relate(0, 1, 10);
                        e.Relate(1, 0, 10);
                        e.Rumor(won == 0 ? "{0}이(가) 결투에서 {1}을(를) 꺾었다" : "{1}이(가) 결투에서 {0}을(를) 꺾었다");
                        e.Outcome(won == 0);
                        e.Tell(won == 0 ? "{0}이(가) 이겼습니다. 한바탕 붙고 나니 앙금이 조금 풀렸습니다." : "{1}이(가) 이겼습니다. 한바탕 붙고 나니 앙금이 조금 풀렸습니다.");
                    }, MoodUp, MoodDown, Risk),
                    Paid(0, "훈련장에서 정식 시합으로 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Think(0, ThoughtRules.DecisionPleased);
                        e.Think(1, ThoughtRules.DecisionPleased);
                        e.Relate(0, 1, 15);
                        e.Relate(1, 0, 15);
                        e.Tell("둘은 땀을 흘린 뒤 악수했습니다.");
                    }, GoldDown, MoodUp),
                    C("직접 중재한다", e =>
                    {
                        bool ok = e.Chance(0.6f);
                        e.Relate(0, 1, ok ? 12 : -6);
                        e.Relate(1, 0, ok ? 12 : -6);
                        e.Stress(0, ok ? -5f : 5f);
                        e.Stress(1, ok ? -5f : 5f);
                        e.Feel(0, ok ? Emotion.Happy : Emotion.Angry);
                        e.Feel(1, ok ? Emotion.Happy : Emotion.Angry);
                        e.Outcome(ok);
                        e.Tell(ok ? "길드 마스터의 중재로 둘이 마지못해 악수했습니다." : "중재는 실패했습니다. 둘 다 길드 마스터가 상대 편을 든다고 여깁니다.");
                    }, MoodUp, MoodDown, Risk),
                    C("말린다", e =>
                    {
                        e.Think(0, ThoughtRules.DecisionDispleased);
                        e.Think(1, ThoughtRules.DecisionDispleased);
                        e.Feel(0, Emotion.Angry);
                        e.Tell("결투는 막았지만 둘은 씩씩대며 물러났습니다.");
                    }, MoodDown),
                },
            },
            new()
            {
                Id = "romance", Title = "소문난 사이", Cast = DilemmaCast.TwoFriends, Default = 2,
                Text = "{0}와(과) {1}이(가) 요즘 부쩍 가까워졌다는 소문이 돕니다. 의뢰 중에도 서로만 챙긴다는 말도 있습니다.",
                Choices = new[]
                {
                    C("축복한다", e =>
                    {
                        e.Think(0, ThoughtRules.Cherished, 1);
                        e.Think(1, ThoughtRules.Cherished, 0);
                        e.Relate(0, 1, 15);
                        e.Relate(1, 0, 15);
                        e.Tell("둘은 쑥스러워하면서도 무척 기뻐했습니다.");
                    }, MoodUp),
                    C("일에 지장이 없게 하라고 이른다", e =>
                    {
                        e.Think(0, ThoughtRules.DecisionDispleased);
                        e.Think(1, ThoughtRules.DecisionDispleased);
                        e.Tell("둘은 고개를 끄덕였지만 표정이 어두웠습니다.");
                    }, MoodDown),
                    C("모른 척한다", e => e.Tell("길드 마스터는 아무 말도 하지 않았습니다.")),
                },
            },
            new()
            {
                Id = "theft", Title = "금고 도둑", Cast = DilemmaCast.InTown, Gold = new[] { 60, 20 }, Default = 0,
                When = f => f.Gold >= 120,
                Text = "금고에서 {g0}G가 사라졌습니다. {0}은(는) 골목으로 달아나는 그림자를 봤다고 하지만, 몇몇은 오히려 {0}을(를) 의심합니다.",
                Choices = new[]
                {
                    C("덮어 둔다 (-{g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Tell("사라진 돈은 장부에서 지웠습니다.");
                    }, GoldDown),
                    C("{0}에게 쫓게 한다", e =>
                    {
                        e.Fatigue(0, 15f);
                        if (e.Chance(0.6f))
                        {
                            e.Feel(0, Emotion.Excited);
                            e.Standing(2);
                            e.Rumor("{0}이(가) 길드 금고를 턴 도둑을 잡았다", 1);
                            e.Outcome(true);
                            e.Tell("{0}이(가) 도둑을 붙잡아 돈을 되찾았습니다!");
                        }
                        else
                        {
                            e.Gold(-e.G(0));
                            e.Feel(0, Emotion.Angry);
                            e.Outcome(false);
                            e.Tell("도둑을 놓쳤고 {g0}G를 잃었습니다.");
                        }
                    }, Tired, TownUp, Risk),
                    Paid(1, "경비대에 조사를 맡긴다 ({g1}G)", e =>
                    {
                        e.Gold(-e.G(1));
                        bool ok = e.Chance(0.6f);
                        e.Outcome(ok);
                        if (ok)
                        {
                            e.Standing(5, Greta);
                            e.Tell("경비병 그레타가 범인을 잡았습니다. 지나가던 날품팔이였고, 돈을 되찾았습니다.");
                        }
                        else
                        {
                            e.Gold(-e.G(0));
                            e.Think(0, ThoughtRules.Accused);
                            e.Tell("범인은 끝내 못 찾았고, {0}은(는) 의심받은 것에 상처받았습니다.");
                        }
                    }, GoldDown, TownUp, Risk),
                    C("{0}을(를) 불러 추궁한다", e =>
                    {
                        if (e.Chance(0.35f))
                        {
                            e.Think(0, ThoughtRules.DecisionDispleased);
                            e.Outcome(true);
                            e.Tell("{0}이(가) 결국 털어놓고 돈을 돌려놓았습니다.");
                            return;
                        }
                        e.Gold(-e.G(0));
                        e.Think(0, ThoughtRules.Accused);
                        e.Stress(0, 20f);
                        e.Outcome(false);
                        if (e.Chance(0.3f)) e.Depart(0, "누명에 화가 난 {0}이(가) 길드를 떠났습니다.");
                        else e.Tell("{0}은(는) 결백을 주장했고, 돈은 끝내 나오지 않았습니다.");
                    }, MoodDown, Risk, Leaving),
                },
            },
            new()
            {
                Id = "tab", Title = "주점 외상", Cast = DilemmaCast.Anyone, Gold = new[] { 50 }, Default = 1,
                Text = "주점 주인이 찾아왔습니다. {0}의 외상값 {g0}G가 쌓였다며 길드가 책임지라고 합니다.",
                Choices = new[]
                {
                    Paid(0, "길드가 갚아 준다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Think(0, ThoughtRules.TabPaid);
                        e.Tell("{0}이(가) 연신 고개를 숙였습니다.");
                    }, GoldDown, MoodUp),
                    C("스스로 갚게 한다", e =>
                    {
                        e.Think(0, ThoughtRules.DecisionDispleased);
                        e.Tell("{0}은(는) 투덜대며 외상을 갚기로 했습니다.");
                    }, MoodDown),
                },
            },
            new()
            {
                Id = "retire", Title = "은퇴 고민", Cast = DilemmaCast.Veteran, Gold = new[] { 150 }, Default = 1,
                Text = "고참 {0}이(가) 이제 그만 은퇴할까 고민하고 있습니다. 몸도 마음도 지쳤다고 합니다.",
                Choices = new[]
                {
                    Paid(0, "보너스로 붙잡는다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Think(0, ThoughtRules.KeptOn);
                        e.Stress(0, -20f);
                        e.Tell("{0}은(는) 조금 더 해 보기로 했습니다.");
                    }, GoldDown, MoodUp),
                    C("푹 쉬게 한다", e =>
                    {
                        e.Stress(0, -35f);
                        e.Fatigue(0, -50f);
                        e.Think(0, ThoughtRules.WellRested);
                        e.Tell("{0}은(는) 며칠 푹 쉬며 마음을 추슬렀습니다.");
                    }, MoodUp),
                    C("명예롭게 보내 준다", e =>
                    {
                        e.Fame(5);
                        e.Depart(0, "{0}이(가) 길드에 감사 인사를 남기고 은퇴했습니다.");
                    }, FameUp, Leaving),
                },
            },
            new()
            {
                Id = "mentor", Title = "한 수 가르쳐 주세요", Cast = DilemmaCast.NewcomerAndVeteran, Gold = new[] { 60 }, Default = 1,
                Text = "신입 {0}이(가) {1}에게 한 수 배우고 싶어 합니다. {1}은(는) 귀찮은 눈치입니다.",
                Choices = new[]
                {
                    C("짝지어 가르치게 한다", e =>
                    {
                        e.Exp(0, e.G(0));
                        e.Think(0, ThoughtRules.Mentored, 1);
                        e.Fatigue(1, 15f);
                        e.Relate(0, 1, 10);
                        e.Relate(1, 0, 5);
                        e.Tell("{1}의 가르침에 {0}의 실력이 늘었습니다.");
                    }, Growth, Tired),
                    C("혼자 크게 둔다", e => e.Tell("{0}은(는) 혼자 연습하러 갔습니다.")),
                },
            },
            new()
            {
                Id = "boast", Title = "부풀린 무용담", Cast = DilemmaCast.Anyone, Default = 0,
                Text = "{0}이(가) 주점에서 자기 무용담을 잔뜩 부풀려 떠벌렸습니다. 마을에 소문이 퍼지고 있습니다.",
                Choices = new[]
                {
                    C("그냥 둔다", e =>
                    {
                        if (e.Chance(0.3f))
                        {
                            e.Fame(-4);
                            e.Outcome(false);
                            e.Tell("부풀린 이야기가 들통나 길드가 웃음거리가 됐습니다.");
                        }
                        else
                        {
                            e.Fame(4);
                            e.Outcome(true);
                            e.Tell("소문 덕에 길드 이름이 조금 알려졌습니다.");
                        }
                    }, FameUp, Risk),
                    C("바로잡는다", e =>
                    {
                        e.Think(0, ThoughtRules.DecisionDispleased);
                        e.Tell("{0}은(는) 머쓱해하며 이야기를 고쳐 말했습니다.");
                    }, MoodDown),
                },
            },
            new()
            {
                Id = "birthday", Title = "{0}의 생일", Cast = DilemmaCast.Anyone, Gold = new[] { 30 }, Default = 2,
                Text = "오늘은 {0}의 생일입니다. 본인은 아무렇지 않은 척하지만 은근히 기대하는 눈치입니다.",
                Choices = new[]
                {
                    Paid(0, "선물을 사 준다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Stress(0, -15f);
                        e.Feel(0, Emotion.Happy, 8f);
                        e.Tell("{0}이(가) 선물을 받고 환하게 웃었습니다.");
                    }, GoldDown, MoodUp),
                    C("축하한다는 말만 건넨다", e =>
                    {
                        e.Stress(0, -5f);
                        e.Feel(0, Emotion.Happy, 3f);
                        e.Tell("{0}이(가) 쑥스러워하며 고맙다고 합니다.");
                    }, MoodUp),
                    C("모른 척한다", e =>
                    {
                        e.Stress(0, 6f);
                        e.Feel(0, Emotion.Sad, 8f);
                        e.Tell("{0}이(가) 하루 종일 시무룩합니다.");
                    }, MoodDown),
                },
            },
            new()
            {
                Id = "bond", Title = "의기투합", Cast = DilemmaCast.TwoFriends, Default = 1,
                Text = "{0}와(과) {1}은(는) 요즘 늘 붙어 다닙니다. 앞으로도 같은 조로 의뢰를 다니고 싶다고 합니다.",
                Choices = new[]
                {
                    C("한 조로 밀어 준다", e =>
                    {
                        e.Relate(0, 1, 8);
                        e.Relate(1, 0, 8);
                        e.Stress(0, -3f);
                        e.Stress(1, -3f);
                        e.Feel(0, Emotion.Happy);
                        e.Feel(1, Emotion.Happy);
                        e.Tell("둘은 신이 나서 함께 다닐 계획을 세웁니다.");
                    }, MoodUp),
                    C("공과 사는 구분하라고 한다", e =>
                    {
                        e.Relate(0, 1, -2);
                        e.Relate(1, 0, -2);
                        e.Stress(0, 2f);
                        e.Stress(1, 2f);
                        e.Tell("둘은 아쉬운 얼굴로 고개를 끄덕였습니다.");
                    }, MoodDown),
                },
            },

            // ---- The town ----
            new()
            {
                Id = "refugees", Title = "성문 앞 피난민", Cast = DilemmaCast.None, Gold = new[] { 100, 40 }, Default = 2,
                Text = "{v}에서 온 피난민 몇 사람이 성문 앞에 모였습니다. 머물 곳과 먹을 것을 청합니다.",
                Choices = new[]
                {
                    Paid(0, "받아들인다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Fame(8);
                        e.Think(-1, ThoughtRules.DecisionPleased);
                        e.Tell(e.Visitor() ? "피난민이 자리를 잡았습니다. 그중 한 사람이 길드에 관심을 보입니다." : "피난민이 마을에 자리를 잡았습니다.");
                    }, GoldDown, FameUp, MoodUp),
                    Paid(1, "먹을 것만 나눠 준다 ({g1}G)", e =>
                    {
                        e.Gold(-e.G(1));
                        e.Fame(3);
                        e.Tell("피난민들은 고마워하며 길을 떠났습니다.");
                    }, GoldDown, FameUp),
                    C("돌려보낸다", e =>
                    {
                        e.Fame(-5);
                        e.Think(-1, ThoughtRules.DecisionDispleased);
                        e.Tell("피난민들은 말없이 발길을 돌렸습니다.");
                    }, FameDown, MoodDown),
                },
            },
            new()
            {
                Id = "tax_relief", Title = "세금 감면 요청", Cast = DilemmaCast.None, Default = 1,
                Text = "마을 대표가 찾아와, 올해 살림이 어려우니 오늘 길드에 내는 세금을 절반만 내게 해 달라고 합니다.",
                Choices = new[]
                {
                    C("절반만 받는다", e =>
                    {
                        e.CutTax(0.5f);
                        e.Fame(6);
                        e.Tell("마을 사람들이 길드를 다시 보게 됐습니다.");
                    }, GoldDown, FameUp),
                    C("거절한다", e =>
                    {
                        e.Fame(-3);
                        e.Tell("대표는 실망한 얼굴로 돌아갔습니다.");
                    }, FameDown),
                },
            },
            new()
            {
                Id = "feast_fund", Title = "마을 잔치", Cast = DilemmaCast.None, Gold = new[] { 150, 50 }, Default = 3,
                Text = "촌장 바르톨이 마을 잔치를 열자며 길드도 비용을 나눠 내 달라고 합니다.",
                Choices = new[]
                {
                    Paid(0, "넉넉히 낸다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Fame(5);
                        e.Think(-1, ThoughtRules.TownFeast);
                        e.Stress(-1, -10f);
                        e.Feel(-1, Emotion.Happy);
                        e.Standing(4);
                        e.Standing(8, Bartol);
                        e.Tell("성대한 잔치가 열렸고 길드원들도 실컷 즐겼습니다. 촌장도 흡족해합니다.");
                    }, GoldDown, FameUp, MoodUp, TownUp),
                    Paid(1, "조금 보탠다 ({g1}G)", e =>
                    {
                        e.Gold(-e.G(1));
                        e.Stress(-1, -5f);
                        e.Feel(-1, Emotion.Happy, 3f);
                        e.Standing(1);
                        e.Standing(3, Bartol);
                        e.Tell("조촐한 잔치가 열렸습니다. 길드원들도 한숨 돌렸습니다.");
                    }, GoldDown, MoodUp, TownUp),
                    C("길드원들만 보낸다", e =>
                    {
                        e.Stress(-1, -5f);
                        e.Standing(-1);
                        e.Standing(-4, Bartol);
                        e.Tell("길드원들은 즐거웠지만, 촌장은 길드가 인색하다고 투덜댑니다.");
                    }, MoodUp, TownDown),
                    C("거절한다", e =>
                    {
                        e.Fame(-2);
                        e.Think(-1, ThoughtRules.DecisionDispleased);
                        e.Standing(-3, Bartol);
                        e.Tell("잔치는 길드 없이 열렸습니다. 길드원들이 창문 너머로 부러운 듯 바라봅니다.");
                    }, FameDown, MoodDown, TownDown),
                },
            },
            new()
            {
                Id = "noble", Title = "귀족의 무모한 부탁", Cast = DilemmaCast.None, Gold = new[] { 150, 80 }, Default = 2,
                Text = "이웃 영지의 젊은 귀족이 금화 주머니를 흔들며, 길드원들을 데리고 몬스터 둥지를 구경하겠다고 합니다. 위험하기 짝이 없는 일입니다.",
                Choices = new[]
                {
                    C("받는다 (+{g0}G)", e =>
                    {
                        e.Gold(e.G(0));
                        e.Stress(-1, 8f);
                        e.Standing(-2);
                        e.Rumor("길드가 귀족의 위험한 나들이를 맡았다");
                        e.Tell("두둑한 돈을 받았지만 길드원들은 귀족을 지키느라 진땀을 뺐습니다.");
                    }, GoldUp, MoodDown, TownDown),
                    C("조건을 깎는다", e =>
                    {
                        bool ok = e.Chance(0.6f);
                        e.Outcome(ok);
                        if (ok)
                        {
                            e.Gold(e.G(1));
                            e.Stress(-1, 3f);
                            e.Tell("덜 위험한 길로 돌아가는 조건으로 {g1}G를 받았습니다.");
                        }
                        else
                        {
                            e.Rumor("귀족이 길드와 흥정하다 화를 내고 떠났다");
                            e.Tell("귀족은 기분이 상해 떠났습니다.");
                        }
                    }, GoldUp, Risk),
                    C("거절한다", e =>
                    {
                        e.Stress(-1, -2f);
                        e.Tell("길드원들이 안도의 한숨을 쉬었습니다.");
                    }, MoodUp),
                },
            },
            new()
            {
                Id = "plague", Title = "역병의 조짐", Cast = DilemmaCast.InTown, Gold = new[] { 60 }, Default = 2,
                When = f => f.Residents,
                Text = "마을에 기침하는 사람이 부쩍 늘었습니다. 약초상 마리가 약을 지을 약초가 모자란다며 길드에 도움을 청합니다.",
                Choices = new[]
                {
                    Paid(0, "약초 값을 댄다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Standing(5);
                        e.Standing(10, Mari);
                        e.Rumor("길드가 역병 약값을 댔다", 1);
                        e.Tell("마리가 약을 지어 돌렸고, 마을 사람들이 길드를 칭찬합니다.");
                    }, GoldDown, TownUp),
                    C("{0}을(를) 보내 약초를 캔다", e =>
                    {
                        e.Fatigue(0, 25f);
                        e.Standing(4);
                        e.Standing(8, Mari);
                        e.Feel(0, Emotion.Happy);
                        e.Rumor("{0}이(가) 역병 약초를 캐 왔다", 1);
                        e.Tell("{0}이(가) 지친 몸으로 약초 바구니를 가득 채워 왔습니다.");
                    }, Tired, TownUp),
                    C("모른 척한다", e =>
                    {
                        e.Standing(-5);
                        e.Standing(-10, Mari);
                        e.Rumor("길드가 역병을 모른 척했다", -1);
                        e.Tell("마리는 실망한 얼굴로 돌아갔습니다.");
                    }, TownDown),
                },
            },
            new()
            {
                Id = "toby", Title = "꼬마 토비의 부탁", Cast = DilemmaCast.InTown, Gold = new[] { 20 }, Default = 2,
                When = f => f.Residents,
                Text = "꼬마 토비가 {0}의 옷자락을 붙잡고, 진짜 모험가의 훈련을 구경하게 해 달라고 조릅니다.",
                Choices = new[]
                {
                    C("훈련을 보여 준다", e =>
                    {
                        e.Standing(12, Toby);
                        e.Standing(1);
                        e.Fatigue(0, 5f);
                        e.Feel(0, Emotion.Happy);
                        e.Tell("토비가 눈을 반짝이며 {0}의 몸놀림을 따라 합니다.");
                    }, TownUp, Tired),
                    Paid(0, "목검을 하나 사 준다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Standing(20, Toby);
                        e.Standing(2);
                        e.Rumor("길드가 토비에게 목검을 사 줬다", 1);
                        e.Tell("토비가 목검을 들고 온 마을을 뛰어다닙니다.");
                    }, GoldDown, TownUp),
                    C("위험하다며 돌려보낸다", e =>
                    {
                        e.Standing(-6, Toby);
                        e.Tell("토비가 입을 삐죽 내밀고 돌아갔습니다.");
                    }, TownDown),
                },
            },
            new()
            {
                Id = "gate_fund", Title = "성문 보수", Cast = DilemmaCast.None, Gold = new[] { 80 }, Default = 1,
                When = f => f.GatesWorn,
                Text = "마을 목수들이 상한 성문을 손보자며 길드에 비용을 청합니다. 따로 고치는 것보다는 쌉니다.",
                Choices = new[]
                {
                    Paid(0, "맡긴다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.RepairGates();
                        e.Tell("성문이 모두 말끔히 고쳐졌습니다.");
                    }, GoldDown),
                    C("다음에 하자", e => e.Tell("목수들은 다음을 기약했습니다.")),
                },
            },
            new()
            {
                Id = "watch_help", Title = "밤 순찰 지원", Cast = DilemmaCast.InTown, Gold = new[] { 40 }, Default = 1,
                Text = "마을 경비대가 밤 순찰에 {0}을(를) 빌려 달라고 합니다. 품삯 {g0}G를 주겠답니다.",
                Choices = new[]
                {
                    C("보낸다", e =>
                    {
                        e.Gold(e.G(0));
                        e.Fame(3);
                        e.Fatigue(0, 25f);
                        e.Tell("{0}은(는) 밤새 순찰을 돌고 피곤한 얼굴로 돌아왔습니다.");
                    }, GoldUp, FameUp, Tired),
                    C("거절한다", e =>
                    {
                        e.Fame(-2);
                        e.Tell("경비대는 다른 사람을 구했습니다.");
                    }, FameDown),
                },
            },
            new()
            {
                Id = "investment", Title = "교역로 투자", Cast = DilemmaCast.None, Gold = new[] { 200 }, Default = 1,
                Text = "상인 조합이 새 교역로에 투자하라고 권합니다. 사흘 뒤면 결과가 나온다고 합니다.",
                Choices = new[]
                {
                    Paid(0, "투자한다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.FollowUp("investment_result", 72f);
                        e.Tell("조합은 사흘 뒤에 다시 오겠다고 했습니다.");
                    }, GoldDown, Later, Risk),
                    C("거절한다", e => e.Tell("조합은 다른 투자자를 찾아갔습니다.")),
                },
            },
            new()
            {
                Id = "investment_result", Title = "교역로 투자 결과", Cast = DilemmaCast.None, FollowUpOnly = true, Gold = new[] { 200 },
                Text = "상인 조합이 교역로 투자 결과를 알리러 왔습니다.",
                Choices = new[]
                {
                    C("결과를 듣는다", e =>
                    {
                        bool ok = e.Chance(0.6f);
                        e.Outcome(ok);
                        if (ok)
                        {
                            e.Gold(e.G(0) * 2);
                            e.Tell("교역이 잘되어 투자금의 두 배를 돌려받았습니다.");
                        }
                        else e.Tell("상단이 도적을 만나 투자금을 잃었습니다.");
                    }, Risk),
                },
            },

            // ---- Out there ----
            new()
            {
                Id = "cub", Title = "다친 새끼", Cast = DilemmaCast.Anyone, Gold = new[] { 30, 60 }, Default = 1,
                When = f => f.Species != null,
                Text = "{0}이(가) 다친 {s} 새끼를 안고 돌아왔습니다. 어미는 보이지 않았답니다.",
                Choices = new[]
                {
                    Paid(0, "돌봐 준다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Think(0, ThoughtRules.CubCared);
                        e.FollowUp("cub_return", 48f);
                        e.Tell("{0}이(가) 정성껏 새끼를 돌보기 시작했습니다.");
                    }, GoldDown, MoodUp, Later),
                    C("숲으로 돌려보낸다", e => e.Tell("{0}은(는) 아쉬운 얼굴로 새끼를 숲에 놓아주었습니다.")),
                    C("팔아 버린다 (+{g1}G)", e =>
                    {
                        e.Gold(e.G(1));
                        e.Think(0, ThoughtRules.CubSold);
                        e.Tell("새끼는 행상인 손에 넘어갔습니다.");
                    }, GoldUp, MoodDown),
                },
            },
            new()
            {
                Id = "cub_return", Title = "새끼의 보은", Cast = DilemmaCast.Anyone, FollowUpOnly = true,
                Text = "다 나은 {s} 새끼가 숲으로 돌아가며, 둥지 근처에서 주운 반짝이는 돌을 물어다 놓았습니다.",
                Choices = new[]
                {
                    C("고맙게 받는다", e =>
                    {
                        e.Stock(Ore, 3);
                        e.Think(0, ThoughtRules.DecisionPleased);
                        e.Tell("철광석 3개를 얻었습니다.");
                    }, StockUp, MoodUp),
                },
            },
            new()
            {
                Id = "map", Title = "낡은 지도", Cast = DilemmaCast.None, Gold = new[] { 80 }, Default = 1,
                When = f => f.RuinToFind,
                Text = "떠돌이 하나가 오래된 유적 지도를 팔겠다고 합니다. 진짜인지는 모릅니다.",
                Choices = new[]
                {
                    Paid(0, "산다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        if (e.RuinRumour()) e.Tell("지도가 가리키는 유적 이야기가 퍼졌습니다. 의뢰가 올라올 겁니다.");
                        else
                        {
                            e.Gold(e.G(0));
                            e.Tell("지도는 이미 아는 곳이었습니다. 돈은 돌려받았습니다.");
                        }
                    }, GoldDown, Later),
                    C("거절한다", e => e.Tell("떠돌이는 어깨를 으쓱하고 떠났습니다.")),
                },
            },
            new()
            {
                Id = "aid", Title = "이웃 마을의 부탁", Cast = DilemmaCast.None, Gold = new[] { 80 }, Default = 2,
                When = f => f.Village != null,
                Text = "{v}이(가) 몬스터에 시달린다며 물약을 나눠 달라고 합니다.",
                Choices = new[]
                {
                    new DilemmaChoice
                    {
                        Label = "물약 3개를 보낸다", NeedsItem = Potion, NeedsCount = 3, Hints = new[] { StockDown, FameUp },
                        Apply = e =>
                        {
                            e.Stock(Potion, -3);
                            e.Fame(8);
                            e.Tell("{v}에서 고맙다는 편지가 왔습니다.");
                        },
                    },
                    Paid(0, "골드로 대신한다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Fame(5);
                        e.Tell("{v}은(는) 그 돈으로 물약을 샀습니다.");
                    }, GoldDown, FameUp),
                    C("거절한다", e =>
                    {
                        e.Fame(-3);
                        e.Tell("{v}의 사자는 빈손으로 돌아갔습니다.");
                    }, FameDown),
                },
            },
            new()
            {
                Id = "wanderer", Title = "떠돌이 실력자", Cast = DilemmaCast.None, Gold = new[] { 40, 20, 60, 40 }, Default = 2,
                Text = "이름난 떠돌이 검사가 하룻밤 묵어 가길 청합니다. 길드에 들어올지는 그의 마음에 달렸습니다.",
                Choices = new[]
                {
                    Paid(0, "환영 연회를 연다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Stress(-1, -3f);
                        e.Feel(-1, Emotion.Happy, 3f);
                        e.Tell(e.Wanderer(welcomed: true)
                            ? "연회 덕분에 그가 길드를 좋게 보고 있습니다. 광장에서 말을 걸어 권유해 보세요."
                            : "연회는 즐거웠지만 그는 서둘러 길을 떠났습니다.");
                    }, GoldDown, MoodUp),
                    C("재워 준다 (-{g1}G)", e =>
                    {
                        e.Gold(-e.G(1));
                        if (e.Chance(0.4f) && e.Wanderer(welcomed: false))
                        {
                            e.Outcome(true);
                            e.Tell("그가 길드에 관심을 보입니다. 영입하려면 광장에서 직접 설득해야 합니다.");
                        }
                        else if (e.Chance(0.6f))
                        {
                            e.Gold(e.G(2));
                            e.Outcome(true);
                            e.Tell("그는 떠나며 넉넉한 사례를 남겼습니다.");
                        }
                        else
                        {
                            e.Gold(-e.G(3));
                            e.Outcome(false);
                            e.Tell("아침에 보니 그와 함께 은수저가 사라졌습니다.");
                        }
                    }, GoldDown, Risk),
                    C("거절한다", e => e.Tell("그는 술집에서 하룻밤을 보내고 떠났습니다.")),
                },
            },
            new()
            {
                Id = "hunter_tip", Title = "사냥꾼의 정보", Cast = DilemmaCast.None, Gold = new[] { 60 }, Default = 1,
                Text = "사냥꾼이 근처에서 이상하게 빛나는 몬스터를 봤다며 정보를 팔겠다고 합니다.",
                Choices = new[]
                {
                    Paid(0, "산다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        if (e.RareRumour()) e.Tell("희귀 몬스터 소문이 퍼졌습니다. 곧 의뢰가 올라올 겁니다.");
                        else
                        {
                            e.Gold(e.G(0));
                            e.Tell("사냥꾼의 말은 틀린 것이었습니다. 돈은 돌려받았습니다.");
                        }
                    }, GoldDown, Later),
                    C("거절한다", e => e.Tell("사냥꾼은 다른 길드를 찾아갔습니다.")),
                },
            },

            // ---- Traders ----
            new()
            {
                Id = "contraband", Title = "수상한 상인", Cast = DilemmaCast.WeakestGear, Gold = new[] { 60, 60 }, Default = 3,
                Text = "망토를 깊게 눌러쓴 상인이 출처를 묻지 않는 조건으로 철광석 6개를 싸게 넘기겠답니다. '마법이 깃든 장비'도 있다며 {0}에게 눈짓합니다.",
                Choices = new[]
                {
                    Paid(0, "철광석을 산다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Stock(Ore, 6);
                        if (e.Chance(0.35f)) e.FollowUp("contraband_rumour", 24f);
                        e.Tell("철광석 6개를 창고에 넣었습니다.");
                    }, GoldDown, StockUp, Risk),
                    Paid(1, "{0}에게 장비를 사 준다 ({g1}G)", e =>
                    {
                        e.Gold(-e.G(1));
                        if (!e.Chance(0.5f))
                        {
                            e.Feel(0, Emotion.Sad);
                            e.Standing(-1);
                            e.Rumor("길드가 떠돌이 상인에게 가짜 장비를 샀다", -1);
                            e.Outcome(false);
                            e.Tell("가짜였습니다. 상인은 이미 사라졌습니다.");
                        }
                        else if (e.GearUp(0))
                        {
                            e.Feel(0, Emotion.Excited);
                            e.Outcome(true);
                            e.Tell("진짜였습니다! {0}의 장비가 한 단계 좋아졌습니다.");
                        }
                        else e.Tell("진짜였지만, {0}은(는) 이미 그보다 좋은 장비를 쓰고 있었습니다.");
                    }, GoldDown, Growth, Risk),
                    C("경비병에게 알린다", e =>
                    {
                        e.Standing(2);
                        e.Standing(8, Greta);
                        e.Rumor("길드가 수상한 상인을 경비대에 알렸다", 1);
                        e.Tell("경비병 그레타가 상인을 마을 밖으로 쫓아냈습니다. 주민들이 고마워합니다.");
                    }, TownUp),
                    C("거절한다", e => e.Tell("상인은 투덜거리며 다른 손님을 찾아 떠났습니다.")),
                },
            },
            new()
            {
                Id = "contraband_rumour", Title = "밀수품 소문", Cast = DilemmaCast.None, FollowUpOnly = true, Gold = new[] { 80 }, Default = 1,
                Text = "길드가 장물을 샀다는 소문이 마을에 돌기 시작했습니다.",
                Choices = new[]
                {
                    Paid(0, "입막음 값을 치른다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Tell("소문은 금세 잠잠해졌습니다.");
                    }, GoldDown),
                    C("모른 척한다", e =>
                    {
                        e.Fame(-8);
                        e.Tell("길드의 평판이 깎였습니다.");
                    }, FameDown),
                },
            },
            new()
            {
                Id = "clearance", Title = "떠돌이 약장수", Cast = DilemmaCast.None, Gold = new[] { 50 }, Default = 1,
                Text = "떠돌이 약장수가 남은 체력 물약 4개를 반값에 넘기겠답니다.",
                Choices = new[]
                {
                    Paid(0, "산다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Stock(Potion, 4);
                        e.Tell("체력 물약 4개를 창고에 넣었습니다.");
                    }, GoldDown, StockUp),
                    C("거절한다", e => e.Tell("약장수는 다음 마을로 떠났습니다.")),
                },
            },

            // ---- The seasons ----
            new()
            {
                Id = "spring_recruits", Title = "봄의 지원자들", Cast = DilemmaCast.None, Season = Seasons.Season.Spring, Gold = new[] { 60 }, Default = 1,
                Text = "봄을 맞아 모험가가 되겠다는 젊은이들이 마을에 몰려왔습니다. 길드에서 환영회를 열까요?",
                Choices = new[]
                {
                    Paid(0, "환영회를 연다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Fame(2);
                        e.Tell(e.Visitor() ? "환영회에서 한 젊은이가 길드에 관심을 보였습니다. 광장에서 이야기를 나눠 보세요." : "환영회는 즐거웠지만 길드에 남겠다는 사람은 없었습니다.");
                    }, GoldDown, FameUp),
                    C("그냥 둔다", e => e.Tell("젊은이들은 저마다 흩어졌습니다.")),
                },
            },
            new()
            {
                Id = "drought", Title = "가뭄", Cast = DilemmaCast.None, Season = Seasons.Season.Summer, Gold = new[] { 80 }, Default = 1,
                Text = "가뭄으로 우물이 말라 물값이 크게 올랐습니다. 길드원들이 목말라 합니다.",
                Choices = new[]
                {
                    Paid(0, "길드가 물을 사 온다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Think(-1, ThoughtRules.DecisionPleased);
                        e.Tell("물통이 넉넉히 채워졌습니다.");
                    }, GoldDown, MoodUp),
                    C("아껴 마시라고 한다", e =>
                    {
                        e.Think(-1, ThoughtRules.Thirsty);
                        e.Tell("길드원들이 마른 입술을 핥았습니다.");
                    }, MoodDown),
                },
            },
            new()
            {
                Id = "harvest", Title = "수확 일손", Cast = DilemmaCast.InTown, Season = Seasons.Season.Autumn, Gold = new[] { 120 }, Default = 1,
                Text = "농부들이 수확철 일손을 빌려 달라고 합니다. 사례는 넉넉히 하겠답니다.",
                Choices = new[]
                {
                    C("마을에 있는 길드원을 보낸다 (+{g0}G)", e =>
                    {
                        e.Gold(e.G(0));
                        e.Fame(4);
                        e.Fatigue(-1, 20f);
                        e.Tell("길드원들이 하루 종일 곡식을 날랐습니다.");
                    }, GoldUp, FameUp, Tired),
                    C("거절한다", e => e.Tell("농부들은 다른 일손을 구했습니다.")),
                },
            },
            new()
            {
                Id = "firewood", Title = "땔감 값", Cast = DilemmaCast.None, Season = Seasons.Season.Winter, Gold = new[] { 100 }, Default = 1,
                Text = "겨울이라 땔감 값이 크게 올랐습니다. 길드 숙소를 데울 땔감을 어떻게 할까요?",
                Choices = new[]
                {
                    Paid(0, "넉넉히 산다 ({g0}G)", e =>
                    {
                        e.Gold(-e.G(0));
                        e.Think(-1, ThoughtRules.WarmLodge);
                        e.Tell("숙소에 온기가 돌았습니다.");
                    }, GoldDown, MoodUp),
                    C("아껴 쓴다", e =>
                    {
                        e.Think(-1, ThoughtRules.ColdLodge);
                        e.Tell("숙소가 썰렁합니다.");
                    }, MoodDown),
                },
            },
        };

        public static DilemmaDef Find(string id) => All.FirstOrDefault(d => d.Id == id);
    }
}
