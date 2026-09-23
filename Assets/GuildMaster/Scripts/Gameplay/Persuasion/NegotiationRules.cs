using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Persuasion
{
    /// <summary>
    /// Pure rules of gold negotiation over a quest nobody took (P4-25). On top of the persuasion talk the
    /// guild master may raise the quest's gold reward; how much that moves an adventurer depends on their
    /// greed (values "돈", the gold-greed trait) and on how large the raise is compared with the posted reward.
    /// An adventurer who is close to saying yes may also name a price (a counter-offer).
    /// No Unity objects are touched here so everything is covered by EditMode tests.
    /// </summary>
    public static class NegotiationRules
    {
        /// <summary>Offers and counter-offers are multiples of this many gold.</summary>
        public const int Step = 5;
        /// <summary>The raise (all offers of one talk together) may not exceed this share of the posted reward…</summary>
        public const float MaxRaiseRatio = 1f;
        /// <summary>…but is always allowed up to this much, so cheap quests can still be sweetened.</summary>
        public const int MinCapGold = 30;
        /// <summary>Largest willingness that gold alone can add.</summary>
        public const int MaxGain = 60;
        /// <summary>Willingness gained by doubling the reward, for an adventurer with greed 1.</summary>
        const float GainPerDoubling = 40f;
        /// <summary>Rewards below this count as this much when the raise is compared with the posted reward.</summary>
        const int MinBaseGold = 20;
        /// <summary>A counter-offer aims this far above the threshold, so it is not a coin flip.</summary>
        public const int CounterMargin = 5;
        /// <summary>The asked price is this much above the least gold that would do (room to haggle).</summary>
        public const float AskPadding = 1.2f;

        /// <summary>How much gold weighs for this adventurer (0.5 .. 2): 돈 loves it, 명예/모험 care less.</summary>
        public static float Greed(string values, float goldGreed)
        {
            float v = values == "돈" ? 1.3f : (values == "명예" || values == "모험") ? 0.8f : 1f;
            return Mathf.Clamp(goldGreed * v, 0.5f, 2f);
        }

        /// <summary>Most extra gold one talk may promise in total.</summary>
        public static int RaiseCap(int baseRewardGold)
            => Mathf.Max(MinCapGold, Mathf.RoundToInt(baseRewardGold * MaxRaiseRatio));

        /// <summary>Most extra gold that can still be offered now (cap left, and what the treasury holds), in whole steps.</summary>
        public static int MaxOffer(int baseRewardGold, int alreadyOffered, int treasuryGold)
        {
            int room = Mathf.Min(RaiseCap(baseRewardGold) - alreadyOffered, treasuryGold);
            return room < Step ? 0 : room / Step * Step;
        }

        /// <summary>
        /// Willingness that <paramref name="totalExtra"/> gold on top of the posted reward adds. Depends on the
        /// total only, so splitting a raise into several offers gains nothing.
        /// </summary>
        public static int WillingnessFromExtra(int totalExtra, int baseRewardGold, float greed)
        {
            if (totalExtra <= 0) return 0;
            float gain = GainPerDoubling * greed * totalExtra / Mathf.Max(MinBaseGold, baseRewardGold);
            return Mathf.Min(MaxGain, Mathf.FloorToInt(gain));
        }

        /// <summary>The least total extra gold (multiple of <see cref="Step"/>) that adds <paramref name="gain"/> willingness; -1 when gold alone cannot.</summary>
        public static int ExtraForWillingness(int gain, int baseRewardGold, float greed)
        {
            if (gain <= 0) return 0;
            if (gain > MaxGain) return -1;
            int extra = Mathf.CeilToInt(gain * Mathf.Max(MinBaseGold, baseRewardGold) / (GainPerDoubling * greed));
            extra = CeilToStep(Mathf.Max(extra, Step));
            while (WillingnessFromExtra(extra, baseRewardGold, greed) < gain) extra += Step;
            return extra;
        }

        /// <summary>
        /// The extra gold an adventurer asks for on top of what was already offered (0 = no counter-offer).
        /// <paramref name="willingness"/> already includes the gain from <paramref name="offered"/>.
        /// No counter is made when the adventurer may not accept, is already willing, or when the price
        /// would exceed <paramref name="maxAdd"/> (the cap or the treasury).
        /// </summary>
        public static int CounterOffer(int willingness, int threshold, int offered, int baseRewardGold, float greed, bool canAccept, int maxAdd)
        {
            if (!canAccept || maxAdd < Step) return 0;
            int gap = threshold + CounterMargin - willingness;
            if (gap <= 0) return 0;
            int neededGain = WillingnessFromExtra(offered, baseRewardGold, greed) + gap;
            int totalNeeded = ExtraForWillingness(neededGain, baseRewardGold, greed);
            if (totalNeeded < 0) return 0;
            int least = Mathf.Max(Step, totalNeeded - offered);
            if (least > maxAdd) return 0;
            int ask = CeilToStep(Mathf.CeilToInt(least * AskPadding));
            return Mathf.Min(ask, maxAdd);
        }

        /// <summary>Extra gold for a quick "+N%" offer, rounded to whole steps and never below one step.</summary>
        public static int PercentOffer(int baseRewardGold, int percent)
            => Mathf.Max(Step, Mathf.RoundToInt(baseRewardGold * percent / 100f / Step) * Step);

        static int CeilToStep(int gold) => (gold + Step - 1) / Step * Step;
    }

    /// <summary>Adventurer lines in a negotiation, by how greedy they are (rule-based; no model needed).</summary>
    public static class NegotiationLines
    {
        enum Tone { Modest, Neutral, Greedy }

        static Tone ToneOf(float greed) => greed >= 1.2f ? Tone.Greedy : greed <= 0.9f ? Tone.Modest : Tone.Neutral;

        static readonly string[] DealGreedy = { "돈 되는 이야기네요. 계약합시다!", "그 보상이면 목숨 값은 되겠군요. 맡죠." };
        static readonly string[] DealNeutral = { "그 조건이라면 맡겠습니다.", "좋아요, 그 보상이라면 다녀오죠." };
        static readonly string[] DealModest = { "돈 때문만은 아니지만... 성의를 봐서 맡겠습니다.", "그렇게까지 하시니 거절할 수 없네요. 맡을게요." };

        static readonly string[] CloseGreedy = { "솔깃하네요. 조금만 더 얹으면 어떨까요?", "나쁘지 않은데... 아직 위험에 비해 짜네요." };
        static readonly string[] CloseNeutral = { "마음이 움직이긴 하는데, 아직 부족해요.", "조금만 더 보태 주시면 생각해 볼게요." };
        static readonly string[] CloseModest = { "성의는 고맙지만, 아직 마음이 무겁네요.", "돈보다 다른 이유가 있으면 더 좋겠어요." };

        static readonly string[] FarGreedy = { "그 푼돈으로 목숨을 걸라고요?", "그 정도로는 어림도 없죠." };
        static readonly string[] FarNeutral = { "그 정도로는 어렵겠어요.", "보상을 좀 더 생각해 보셔야 할 것 같아요." };
        static readonly string[] FarModest = { "돈이 문제가 아니에요. 이 의뢰는 정말 위험해서...", "보상만으로는 마음이 안 움직이네요." };

        static readonly string[] AskGreedy = { "{0}G만 더 얹으면 맡겠습니다. 이 위험이면 그 정도는 받아야죠.", "{0}G 더 주시죠. 그럼 계약합니다." };
        static readonly string[] AskNeutral = { "{0}G만 더 주시면 맡겠습니다.", "보상에 {0}G를 더 얹어 주신다면 해 보죠." };
        static readonly string[] AskModest = { "죄송하지만... {0}G만 더 있으면 마음이 놓일 것 같아요.", "{0}G만 더 보태 주시면 맡을 수 있겠어요." };

        static readonly string[] Declined = { "그렇군요. 그럼 다른 방법을 생각해 보죠.", "알겠습니다. 마음이 바뀌면 말씀하세요.", "음, 그럼 이야기를 더 해 보죠." };

        static string[] ByTone(Tone t, string[] greedy, string[] neutral, string[] modest)
            => t == Tone.Greedy ? greedy : t == Tone.Modest ? modest : neutral;

        /// <summary>Reply to a raise: a deal, "almost", or "not with that".</summary>
        public static string OfferReply(float greed, int willingnessAfter, int threshold, bool canAccept, GameRandom rng)
        {
            var t = ToneOf(greed);
            if (canAccept && willingnessAfter >= threshold) return rng.Pick(ByTone(t, DealGreedy, DealNeutral, DealModest));
            if (willingnessAfter >= threshold - 15) return rng.Pick(ByTone(t, CloseGreedy, CloseNeutral, CloseModest));
            return rng.Pick(ByTone(t, FarGreedy, FarNeutral, FarModest));
        }

        /// <summary>The asked price ("{ask}G more").</summary>
        public static string CounterLine(float greed, int ask, GameRandom rng)
            => string.Format(rng.Pick(ByTone(ToneOf(greed), AskGreedy, AskNeutral, AskModest)), ask);

        /// <summary>The adventurer's answer when the guild master turns the counter-offer down.</summary>
        public static string DeclinedLine(GameRandom rng) => rng.Pick(Declined);

        /// <summary>The adventurer's answer when the guild master takes the counter-offer.</summary>
        public static string DealLine(float greed, GameRandom rng) => rng.Pick(ByTone(ToneOf(greed), DealGreedy, DealNeutral, DealModest));
    }
}
