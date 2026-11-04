using GuildMaster.Core;
using GuildMaster.Session;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    public struct SkillLearnedEvent
    {
        public Adventurer Adventurer;
        public string LearnedId;
        /// <summary>Null when nothing had to be given up.</summary>
        public string ForgottenId;
    }

    /// <summary>
    /// Learning on level-up (P4-34, 2026-09-20 user decision): the adventurer takes what its class can still learn in
    /// the class ordering, giving up a skill when it already carries the maximum (<see cref="SkillCatalog.RuleChoice"/>).
    /// The model used to pick; since P4-91 (user decision 2026-10-19) the rules do.
    /// </summary>
    public class SkillChoiceService : MonoBehaviour
    {
        public int Choices { get; private set; }

        public void Init() => EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);

        void OnDestroy() => EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);

        void OnLevelUp(AdventurerLevelUpEvent e)
        {
            var a = e.Adventurer;
            if (a == null || !a.IsAlive || a.IsVisitor) return;
            var candidates = SkillCatalog.Candidates(a.Data.cls, a.Data.skillIds, a.Data.level);
            if (candidates.Count == 0) return; // nothing this level opens is left
            // P4-126: higher grades open with the level, and each member has its own taste within a grade.
            var (learn, forget) = SkillCatalog.RuleChoice(a.Data.cls, a.Data.skillIds, candidates, a.Data.level, a.Data.id);
            if (learn != null) Apply(a, learn, forget);
        }

        void Apply(Adventurer a, string learn, string forget)
        {
            if (!a.LearnSkill(learn, forget)) return;
            Choices++;

            string learned = SkillCatalog.NameOf(learn);
            string text = string.IsNullOrEmpty(forget)
                ? $"{a.Data.name}이(가) [{learned}]을(를) 익혔습니다."
                : $"{a.Data.name}이(가) [{SkillCatalog.NameOf(forget)}]을(를) 버리고 [{learned}]을(를) 익혔습니다.";
            if (Services.TryGet<GameLog>(out var log)) log.Add(text, LogCategory.Info);
            if (Services.TryGet<GameClock>(out var clock))
                AdventurerMemory.Add(a.Data.memories, clock.TotalHours, 2, $"[{learned}]을(를) 익혔다");
            EventBus.Publish(new SkillLearnedEvent { Adventurer = a, LearnedId = learn, ForgottenId = forget });
        }
    }
}
