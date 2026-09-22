using GuildMaster.Core;
using GuildMaster.Quests;

namespace GuildMaster.Proposals
{
    /// <summary>Wording of the quest proposals (F-8) that does not come from the LLM: labels and the rule-based fallback text.</summary>
    public static class ProposalTexts
    {
        public static string StyleLabel(ProposalStyle style) => style switch
        {
            ProposalStyle.Safe => "안전",
            ProposalStyle.Balanced => "균형",
            _ => "모험",
        };

        public static string KindLabel(QuestType type) => "토벌";

        public static string FallbackTitle(ProposalPlan p) => $"{p.Target.Label} 토벌 x{p.Count}";

        static readonly string[] SafePitch =
        {
            "여유 있게 다녀올 수 있는 일입니다. 보상은 조금 적어도 걱정이 없습니다.",
            "무리하지 않고 끝낼 수 있는 의뢰입니다. 길드원의 부담이 적습니다.",
            "안전하게 경험을 쌓기 좋은 의뢰입니다.",
        };

        static readonly string[] BalancedPitch =
        {
            "지금 길드의 실력에 알맞은 의뢰입니다. 보상도 무난합니다.",
            "적당히 힘들고 적당히 남는, 균형 잡힌 일입니다.",
            "제 몫을 하는 길드원이라면 무리 없이 해낼 만한 의뢰입니다.",
        };

        static readonly string[] BoldPitch =
        {
            "위험하지만 보상이 큽니다. 준비를 단단히 하고 나서야 합니다.",
            "빠듯한 싸움이 되겠지만 그만큼 남는 것도 많습니다.",
            "실력을 시험해 볼 만한 의뢰입니다. 다칠 각오는 해야 합니다.",
        };

        public static string FallbackPitch(ProposalPlan p, GameRandom rng = null)
        {
            var lines = p.Style switch
            {
                ProposalStyle.Safe => SafePitch,
                ProposalStyle.Balanced => BalancedPitch,
                _ => BoldPitch,
            };
            return rng != null ? rng.Pick(lines) : lines[0];
        }

        /// <summary>Gives the plan its rule-based title and pitch (used when the LLM did not word it).</summary>
        public static void ApplyFallback(ProposalPlan p, GameRandom rng = null)
        {
            p.Title = FallbackTitle(p);
            p.Pitch = FallbackPitch(p, rng);
            p.FromLlm = false;
        }

        public static string NewProposals(int count) => $"새 의뢰 제안 {count}건이 도착했습니다. 마음에 드는 의뢰를 골라 등록하세요.";

        public const string NoMembers = "길드원이 없어 제안할 의뢰가 없습니다.";
        public const string NoTargets = "제안할 만한 몬스터나 둥지를 아직 확인하지 못했습니다. 마을 밖을 탐색해 정보를 모아 보세요.";
        public const string NoAffordable = "지금 길드 자금으로 감당할 수 있는 제안이 없습니다.";
    }
}
