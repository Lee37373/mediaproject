using System.Collections.Generic;
using GuildMaster.LLM;

namespace GuildMaster
{
    /// <summary>Everything in the game that can ask the model for something.</summary>
    public enum LlmFeature
    {
        /// <summary>Recruit talk, for what the player types (the choices are written ahead).</summary>
        Recruit,
        /// <summary>Counselling talk (면담), for what the player types.</summary>
        Counsel,
        /// <summary>Town talk between members, visitors, residents and the merchant (written ahead since 2026-10-18).</summary>
        TownTalk,
        /// <summary>Idle small talk and camp talk (BanterService).</summary>
        Banter,
        /// <summary>Which quest a member takes, or rest / shop.</summary>
        Decision,
        /// <summary>Party invitations for quests and hunts.</summary>
        Party,
        /// <summary>How a party leader splits the pay.</summary>
        Split,
        /// <summary>Which skill to learn on level-up.</summary>
        Skill,
        /// <summary>Quest proposal cards.</summary>
        Proposal,
        /// <summary>Talking a member into a quest nobody takes.</summary>
        Persuasion,
        /// <summary>Tribute talk (상납금, user decision 2026-10-29): what the member says and how much they offer.</summary>
        Tribute,
    }

    /// <summary>
    /// Where the model is used (team decision 2026-10-28: "llm을 대화에 사용하는 경우 → 영입 시 + 스트레스관리(일반대화)").
    /// Everything else has always had a rule-based or written path; with its feature off a system gets no gateway
    /// (<see cref="Gateway"/>) and takes that path, so the model is asked far less and the game does not wait on it.
    /// Banter stays on until its owner decides (it falls back to fixed pairs, not to the written town talk).
    /// Tribute was added on 2026-10-29 ("llm 활용 길드원과의 대화를 통해 적절금액 빼앗기").
    /// </summary>
    public static class LlmUse
    {
        static readonly HashSet<LlmFeature> On = new() { LlmFeature.Recruit, LlmFeature.Counsel, LlmFeature.Banter, LlmFeature.Tribute };

        public static bool Allows(LlmFeature f) => On.Contains(f);

        public static void Set(LlmFeature f, bool on)
        {
            if (on) On.Add(f); else On.Remove(f);
        }

        /// <summary>The gateway for a system that may use the model, or null when its feature is off.</summary>
        public static LlmGateway Gateway(LlmFeature f, LlmGateway gateway) => Allows(f) ? gateway : null;

        /// <summary>For systems that look the gateway up themselves: a usable gateway for this feature, or null.</summary>
        public static LlmGateway Ready(LlmFeature f)
            => Allows(f) && Core.Services.TryGet<LlmGateway>(out var gw) && gw.Enabled && gw.IsReady ? gw : null;
    }
}
