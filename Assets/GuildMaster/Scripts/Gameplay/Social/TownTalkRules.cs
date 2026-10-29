using System;
using GuildMaster.Welfare;

namespace GuildMaster.Social
{
    /// <summary>Who someone is in a town talk.</summary>
    public enum TalkerKind { Member, Visitor, Merchant, Resident }

    /// <summary>
    /// What a town talk is about. Chosen from who is talking and how they feel (<see cref="TownTalkRules.ChooseKind"/>).
    /// </summary>
    public enum TownTalkKind
    {
        /// <summary>Everyday talk.</summary>
        Chat,
        /// <summary>A is a friend and B is going through a hard time: A cheers B up.</summary>
        Comfort,
        /// <summary>Two who do not get on (or are on edge) clash.</summary>
        Quarrel,
        /// <summary>Two who fell out lately try to make it up.</summary>
        MakeUp,
        /// <summary>A passes on something they heard (<see cref="Rumor"/>) — about the guild, a member, the world outside.</summary>
        Gossip,
        /// <summary>A member and a visitor: guild life, whether it is worth joining.</summary>
        Welcome,
        /// <summary>With the travelling merchant: haggling, prices, the road.</summary>
        Trade,
        /// <summary>
        /// A member (or a resident who likes the guild) asks a visitor to join, and the visitor answers on the spot
        /// (user request 2026-10-07: recruiting in town without the guild master, as a visible event).
        /// </summary>
        Invite,
    }

    /// <summary>How the talk went. The model picks one from <see cref="TownTalkRules.OutcomesFor"/>; code turns it into effects.</summary>
    public enum TalkOutcome { Warm, Neutral, Tense, Fight, Reconciled, Joined, Declined }

    /// <summary>What a talk does to the two (A first). Feelings are A→B and B→A; stress is positive = more stress.</summary>
    public struct TalkEffect
    {
        public int FeelAB, FeelBA;
        public float StressA, StressB;
        /// <summary>For a visitor on either side: change of their <see cref="Adventurers.AdventurerData.recruitAffinity"/>.</summary>
        public int VisitorAffinity;
    }

    /// <summary>The situation a pair is in when a talk may start.</summary>
    public struct TalkContext
    {
        public TalkerKind KindA, KindB;
        /// <summary>A's feeling towards B and back (0 when one side is the merchant).</summary>
        public int FeelAB, FeelBA;
        public float StressA, StressB;
        /// <summary>The two had a fight not long ago (<see cref="TownTalkRules.MakeUpWindowHours"/>).</summary>
        public bool RecentFight;
        /// <summary>A knows a rumor B has not heard.</summary>
        public bool AHasNews;
        /// <summary>The visitor's opinion of the guild (<see cref="Adventurers.AdventurerData.recruitAffinity"/>) when one side is a visitor, else -1.</summary>
        public int VisitorAffinity;
        /// <summary>A resident's opinion of the guild (<see cref="ResidentProfile.GuildOpinion"/>) when A is a resident, else 0.</summary>
        public int ResidentOpinionA;
    }

    /// <summary>
    /// Everyday life in town between anyone who is there (user request 2026-10-01: "모든 npc간의 대화 … 진짜 사람처럼
    /// 대화와 상호작용"): members, visitors and the merchant. Chooses what a talk is about and turns how it went into
    /// effects on feelings, stress and a visitor's opinion of the guild. The model only writes the lines and says how
    /// it went; the effects are fixed here, so they are the same for every model and testable. Pure logic.
    /// </summary>
    public static class TownTalkRules
    {
        /// <summary>A fight this recent can be made up.</summary>
        public const float MakeUpWindowHours = 36f;
        /// <summary>A pair that just talked waits this long before the next talk.</summary>
        public const float PairCooldownHours = 4f;
        public const float QuarrelChanceHostile = 0.35f;
        public const float QuarrelChanceStressed = 0.2f;
        public const float MakeUpChance = 0.5f;
        public const float GossipChance = 0.4f;

        public static bool IsOnEdge(float stress) => stress >= StressRules.TenseAt;
        public static bool IsStruggling(float stress) => stress >= StressRules.StrainedAt;

        /// <summary>
        /// What the pair talks about. <paramref name="roll"/> is in [0, 1). A comfort talk needs A to be the friend
        /// and B the one struggling: the caller tries both orders (<see cref="WantsComfort"/>).
        /// </summary>
        public static TownTalkKind ChooseKind(in TalkContext c, float roll)
        {
            bool merchant = c.KindA == TalkerKind.Merchant || c.KindB == TalkerKind.Merchant;
            if (merchant) return c.AHasNews && roll < 0.5f ? TownTalkKind.Gossip : TownTalkKind.Trade;

            bool members = c.KindA == TalkerKind.Member && c.KindB == TalkerKind.Member;
            bool visitorAndMember = (c.KindA == TalkerKind.Visitor) != (c.KindB == TalkerKind.Visitor);
            if (members)
            {
                if (c.RecentFight && Math.Min(c.FeelAB, c.FeelBA) > -60 && roll < MakeUpChance) return TownTalkKind.MakeUp;
                if (WantsComfort(c)) return TownTalkKind.Comfort;
                if (roll < QuarrelChance(c)) return TownTalkKind.Quarrel;
                if (c.AHasNews && roll < QuarrelChance(c) + GossipChance) return TownTalkKind.Gossip;
                return TownTalkKind.Chat;
            }
            if (visitorAndMember || (c.KindA == TalkerKind.Resident && c.KindB == TalkerKind.Visitor) || (c.KindA == TalkerKind.Visitor && c.KindB == TalkerKind.Resident))
            {
                if (WantsInvite(c) && roll < InviteChance) return TownTalkKind.Invite;
                return c.AHasNews && roll < 0.65f ? TownTalkKind.Gossip : TownTalkKind.Welcome;
            }
            // Residents with each other or with members, two visitors: small talk, passing on what they heard.
            return c.AHasNews && roll < 0.5f ? TownTalkKind.Gossip : TownTalkKind.Chat;
        }

        public const float InviteChance = 0.45f;
        /// <summary>A visitor this warm to the guild may be asked to join in town.</summary>
        public const int InviteMinAffinity = 45;
        /// <summary>A visitor says yes to an invitation in town from here.</summary>
        public const int InviteJoinAffinity = 55;

        /// <summary>A (a member, or a resident who thinks well of the guild) could ask B (a visitor warm enough) to join.</summary>
        public static bool WantsInvite(in TalkContext c)
            => c.KindB == TalkerKind.Visitor && c.VisitorAffinity >= InviteMinAffinity
               && (c.KindA == TalkerKind.Member || (c.KindA == TalkerKind.Resident && c.ResidentOpinionA >= ResidentProfile.GoodOpinion));

        /// <summary>The model's answer checked against the visitor's real opinion: no yes below <see cref="InviteJoinAffinity"/>.</summary>
        public static TalkOutcome ResolveInvite(TalkOutcome model, int visitorAffinity)
            => model == TalkOutcome.Joined && visitorAffinity < InviteJoinAffinity ? TalkOutcome.Neutral : model;

        /// <summary>The answer when there is no model to write the talk.</summary>
        public static TalkOutcome InviteVerdict(int visitorAffinity)
            => visitorAffinity >= InviteJoinAffinity + 5 ? TalkOutcome.Joined
             : visitorAffinity >= InviteMinAffinity ? TalkOutcome.Neutral
             : TalkOutcome.Declined;

        /// <summary>How a resident's opinion of the guild colours what a visitor makes of a talk with them.</summary>
        public static int WordOfMouth(int residentOpinion)
            => residentOpinion >= ResidentProfile.GoodOpinion ? 3 : residentOpinion <= -ResidentProfile.GoodOpinion ? -4 : 0;

        /// <summary>A resident's opinion of the guild after a talk with a member.</summary>
        public static int OpinionDelta(TalkOutcome o) => o switch
        {
            TalkOutcome.Warm or TalkOutcome.Reconciled => 2,
            TalkOutcome.Tense => -2,
            TalkOutcome.Fight => -4,
            _ => 0,
        };

        /// <summary>A resident hearing news about the guild.</summary>
        public const int RumorOpinion = 3;

        /// <summary>A (a member who likes B) sees B struggling.</summary>
        public static bool WantsComfort(in TalkContext c)
            => c.KindA == TalkerKind.Member && c.KindB == TalkerKind.Member
               && IsStruggling(c.StressB) && !IsStruggling(c.StressA) && c.FeelAB >= StressRules.FriendRelation;

        /// <summary>Hostile pairs clash often, pairs on edge now and then; friends hardly ever.</summary>
        public static float QuarrelChance(in TalkContext c)
        {
            int worst = Math.Min(c.FeelAB, c.FeelBA);
            float p = worst <= -15 ? QuarrelChanceHostile : 0f;
            if (IsOnEdge(c.StressA) || IsOnEdge(c.StressB)) p += worst < StressRules.FriendRelation ? QuarrelChanceStressed : 0.05f;
            return p;
        }

        // ---------------- How a talk goes without a model (user decision 2026-10-18) ----------------

        /// <summary>
        /// How a talk goes when nobody writes it on the spot: friends mostly get on, rivals clash, people on edge snap,
        /// a visitor who likes the guild warms to it, a resident's opinion of the guild colours what they tell a
        /// visitor. <paramref name="roll"/> is in [0, 1). Invitations follow the visitor's opinion (<see cref="InviteVerdict"/>).
        /// </summary>
        public static TalkOutcome RollOutcome(TownTalkKind kind, in TalkContext c, float roll)
        {
            float like = Math.Clamp((c.FeelAB + c.FeelBA) / 200f, -1f, 1f);
            bool edge = IsOnEdge(c.StressA) || IsOnEdge(c.StressB);
            switch (kind)
            {
                case TownTalkKind.Invite:
                    return InviteVerdict(c.VisitorAffinity);
                case TownTalkKind.Quarrel:
                {
                    float fight = Math.Clamp(0.3f - like * 0.4f + (edge ? 0.1f : 0f), 0.1f, 0.75f);
                    float tense = 0.35f;
                    float reconcile = like > 0.2f ? 0.1f : 0.03f;
                    if (roll < fight) return TalkOutcome.Fight;
                    if (roll < fight + tense) return TalkOutcome.Tense;
                    if (roll >= 1f - reconcile) return TalkOutcome.Reconciled;
                    return TalkOutcome.Neutral;
                }
                case TownTalkKind.MakeUp:
                {
                    float reconciled = Math.Clamp(0.45f + like * 0.4f, 0.15f, 0.85f);
                    if (roll < reconciled) return TalkOutcome.Reconciled;
                    if (roll < reconciled + 0.1f) return TalkOutcome.Warm;
                    if (roll < reconciled + 0.35f) return TalkOutcome.Tense;
                    return TalkOutcome.Neutral;
                }
                case TownTalkKind.Comfort:
                {
                    float warm = Math.Clamp(0.55f + like * 0.4f, 0.3f, 0.9f);
                    if (roll >= 0.93f) return TalkOutcome.Tense;
                    return roll < warm ? TalkOutcome.Warm : TalkOutcome.Neutral;
                }
                case TownTalkKind.Trade:
                    return roll < 0.4f ? TalkOutcome.Warm : roll >= 0.9f ? TalkOutcome.Tense : TalkOutcome.Neutral;
                default:
                {
                    // Chat, Gossip, Welcome.
                    float mood = like;
                    if (kind == TownTalkKind.Welcome && c.KindA == TalkerKind.Resident) mood = Math.Clamp(c.ResidentOpinionA / 50f, -1f, 1f);
                    else if (kind == TownTalkKind.Welcome && c.VisitorAffinity >= 0) mood = Math.Clamp((c.VisitorAffinity - 40) / 60f, -1f, 1f);
                    float warm = Math.Clamp(0.35f + mood * 0.4f, 0.1f, 0.8f);
                    float tense = Math.Clamp(0.1f - mood * 0.25f + (edge ? 0.1f : 0f), 0.03f, 0.5f);
                    if (roll < warm) return TalkOutcome.Warm;
                    if (roll >= 1f - tense) return TalkOutcome.Tense;
                    return TalkOutcome.Neutral;
                }
            }
        }

        /// <summary>
        /// Whether to swap who speaks first so the written scripts fit: the member or resident welcomes the visitor,
        /// the merchant opens a trade, a resident opens a chat.
        /// </summary>
        public static bool ShouldSwap(TownTalkKind kind, TalkerKind a, TalkerKind b) => kind switch
        {
            TownTalkKind.Welcome => a == TalkerKind.Visitor && b != TalkerKind.Visitor,
            TownTalkKind.Trade => b == TalkerKind.Merchant && a != TalkerKind.Merchant,
            TownTalkKind.Chat => b == TalkerKind.Resident && a != TalkerKind.Resident,
            _ => false,
        };

        /// <summary>The outcomes the model may pick for this kind of talk (the grammar's enum).</summary>
        public static TalkOutcome[] OutcomesFor(TownTalkKind kind) => kind switch
        {
            TownTalkKind.Quarrel => new[] { TalkOutcome.Fight, TalkOutcome.Tense, TalkOutcome.Neutral, TalkOutcome.Reconciled },
            TownTalkKind.MakeUp => new[] { TalkOutcome.Reconciled, TalkOutcome.Warm, TalkOutcome.Neutral, TalkOutcome.Tense },
            TownTalkKind.Invite => new[] { TalkOutcome.Joined, TalkOutcome.Neutral, TalkOutcome.Declined },
            _ => new[] { TalkOutcome.Warm, TalkOutcome.Neutral, TalkOutcome.Tense },
        };

        public static string Code(TalkOutcome o) => o switch
        {
            TalkOutcome.Warm => "warm",
            TalkOutcome.Neutral => "neutral",
            TalkOutcome.Tense => "tense",
            TalkOutcome.Fight => "fight",
            TalkOutcome.Joined => "joined",
            TalkOutcome.Declined => "declined",
            _ => "reconciled",
        };

        public static TalkOutcome ParseOutcome(string code, TownTalkKind kind)
        {
            foreach (var o in OutcomesFor(kind)) if (Code(o) == code) return o;
            return TalkOutcome.Neutral;
        }

        /// <summary>
        /// What the talk does. Feelings only move between two adventurers (the caller skips the merchant); stress
        /// only matters for members; a visitor's opinion of the guild moves with how the talk went.
        /// </summary>
        public static TalkEffect Effects(TownTalkKind kind, TalkOutcome o)
        {
            var e = kind switch
            {
                TownTalkKind.Comfort => o switch
                {
                    TalkOutcome.Warm => E(4, 6, 0f, -10f),
                    TalkOutcome.Neutral => E(2, 3, 0f, -5f),
                    _ => E(-2, -3, 1f, 2f),
                },
                TownTalkKind.Quarrel => o switch
                {
                    TalkOutcome.Fight => E(-10, -10, 8f, 8f),
                    TalkOutcome.Tense => E(-5, -5, 4f, 4f),
                    TalkOutcome.Reconciled => E(6, 6, -3f, -3f),
                    _ => E(-2, -2, 2f, 2f),
                },
                TownTalkKind.MakeUp => o switch
                {
                    TalkOutcome.Reconciled => E(12, 12, -5f, -5f),
                    TalkOutcome.Warm => E(6, 6, -3f, -3f),
                    TalkOutcome.Tense => E(-3, -3, 2f, 2f),
                    TalkOutcome.Fight => E(-8, -8, 6f, 6f),
                    _ => E(1, 1, 0f, 0f),
                },
                TownTalkKind.Invite => o switch
                {
                    TalkOutcome.Joined => E(5, 5, -1f, -1f),
                    TalkOutcome.Declined => E(-1, -1, 0f, 0f),
                    _ => E(1, 1, 0f, 0f),
                },
                TownTalkKind.Trade => o switch
                {
                    TalkOutcome.Warm => E(0, 0, -2f, -2f),
                    TalkOutcome.Tense => E(0, 0, 1f, 1f),
                    _ => E(0, 0, 0f, 0f),
                },
                // Chat, Gossip, Welcome
                _ => o switch
                {
                    TalkOutcome.Warm => E(3, 3, -1f, -1f),
                    TalkOutcome.Neutral => E(1, 1, 0f, 0f),
                    TalkOutcome.Tense => E(-3, -3, 2f, 2f),
                    TalkOutcome.Fight => E(-6, -6, 5f, 5f),
                    _ => E(3, 3, -1f, -1f),
                },
            };
            // Stronger than at first (user report 2026-10-07: recruiting in town felt like it never happened).
            e.VisitorAffinity = kind == TownTalkKind.Trade ? 0
                : kind == TownTalkKind.Invite ? (o == TalkOutcome.Neutral ? 3 : o == TalkOutcome.Declined ? -2 : 0)
                : o switch
                {
                    TalkOutcome.Warm or TalkOutcome.Reconciled => kind == TownTalkKind.Welcome ? 7 : 4,
                    TalkOutcome.Neutral => kind == TownTalkKind.Welcome ? 3 : 1,
                    TalkOutcome.Tense => -4,
                    _ => -8,
                };
            return e;
        }

        static TalkEffect E(int ab, int ba, float sa, float sb) => new() { FeelAB = ab, FeelBA = ba, StressA = sa, StressB = sb };

        /// <summary>A visitor who hears something about the guild: good news warms them to it, bad news cools them.</summary>
        public const int RumorAffinity = 5;
        /// <summary>Hearing about a member colours how one feels about them (뒷담).</summary>
        public const int RumorFeeling = 2;
    }
}
