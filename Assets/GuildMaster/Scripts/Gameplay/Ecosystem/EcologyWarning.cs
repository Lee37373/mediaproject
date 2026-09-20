using GuildMaster.Data;

namespace GuildMaster.Ecosystem
{
    /// <summary>
    /// How worried the guild should be about a species it hunts (P5-1). Pure, and it reads only
    /// what the guild has observed — a species nobody has seen in a while simply looks scarce, which
    /// is the point: the player is told what the guild believes, not what the simulation knows.
    /// </summary>
    public enum EcologyStatus { Healthy, Thinning, Endangered }

    public static class EcologyWarning
    {
        /// <summary>Observed individuals at or below this are a last handful.</summary>
        public const int EndangeredAt = 3;
        /// <summary>Below half of what a nest naturally holds, the guild should ease off.</summary>
        public const float ThinningFraction = 0.5f;

        /// <summary>
        /// Status from the guild's own count. <paramref name="knownNests"/> is how many nests of the
        /// species it has found: with none, it has no basis for an opinion.
        /// </summary>
        public static EcologyStatus Status(MonsterDefinition species, int observed, int knownNests)
        {
            if (species == null || knownNests <= 0) return EcologyStatus.Healthy;
            if (observed <= EndangeredAt) return EcologyStatus.Endangered;
            float natural = UnityEngine.Mathf.Max(1, species.initialPopulation * knownNests);
            return observed <= natural * ThinningFraction ? EcologyStatus.Thinning : EcologyStatus.Healthy;
        }

        public static string Label(EcologyStatus status) => status switch
        {
            EcologyStatus.Endangered => "멸종 위기",
            EcologyStatus.Thinning => "개체 감소",
            _ => "",
        };

        /// <summary>One line for the quest board when the player is about to hunt them anyway.</summary>
        public static string Advice(MonsterDefinition species, EcologyStatus status)
        {
            if (species == null || status == EcologyStatus.Healthy) return "";
            string name = species.displayName;
            string topic = Adventurers.MemoryRecorder.Josa(name, "은", "는");
            return status == EcologyStatus.Endangered
                ? $"⚠ {name}{topic} 멸종 위기입니다. 더 잡으면 이 땅에서 사라질 수 있습니다."
                : $"{name}의 개체가 눈에 띄게 줄었습니다.";
        }
    }
}
