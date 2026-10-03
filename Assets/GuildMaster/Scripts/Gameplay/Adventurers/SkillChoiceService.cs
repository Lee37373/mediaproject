using System.Collections.Generic;
using System.Threading.Tasks;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.LLM;
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
        public bool FromLlm;
    }

    /// <summary>
    /// Learning on level-up (P4-34, 2026-09-20 user decision): the adventurer is offered what their
    /// class can still learn and the model picks one, saying which skill it gives up when they are
    /// already carrying the maximum. Falls back to the class ordering when the LLM is unavailable
    /// or answers with something that is not on offer.
    /// </summary>
    public class SkillChoiceService : MonoBehaviour
    {
        LlmGateway _gateway;
        readonly HashSet<string> _inFlight = new();

        public int LlmChoices { get; private set; }
        public int RuleChoices { get; private set; }

        public void Init(LlmGateway gateway)
        {
            _gateway = gateway;
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
        }

        void OnDestroy() => EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);

        void OnLevelUp(AdventurerLevelUpEvent e)
        {
            var a = e.Adventurer;
            if (a == null || !a.IsAlive || a.IsVisitor) return;
            var candidates = SkillCatalog.Candidates(a.Data.cls, a.Data.skillIds);
            if (candidates.Count == 0) return;                 // nothing left for this class
            if (!_inFlight.Add(a.Data.id)) return;             // one choice at a time per adventurer
            _ = ChooseAsync(a, candidates);
        }

        async Task ChooseAsync(Adventurer a, List<string> candidates)
        {
            try
            {
                string learn = null, forget = null, say = null;
                bool fromLlm = false;
                if (_gateway != null && _gateway.Enabled && _gateway.IsReady)
                {
                    var result = await _gateway.RequestAsync(SkillPrompt.BuildRequest(a, candidates));
                    if (result.Ok && LlmJson.TryParse<SkillPrompt.ResponseDto>(result.Text, out var dto))
                    {
                        (learn, forget) = SkillPrompt.Interpret(dto, candidates, a.Data.skillIds);
                        if (learn != null)
                        {
                            fromLlm = true;
                            say = LlmJson.CleanSentence(dto.say, SkillPrompt.SayGrammarChars, SkillPrompt.SayGrammarChars, false);
                        }
                    }
                }
                if (a == null || !a.IsAlive) return;
                if (learn == null) (learn, forget) = SkillCatalog.RuleChoice(a.Data.cls, a.Data.skillIds, candidates);
                if (learn != null) Apply(a, learn, forget, fromLlm, say);
            }
            finally
            {
                if (a != null) _inFlight.Remove(a.Data.id);
            }
        }

        void Apply(Adventurer a, string learn, string forget, bool fromLlm, string say)
        {
            if (!a.LearnSkill(learn, forget)) return;
            if (fromLlm) LlmChoices++; else RuleChoices++;

            string learned = SkillCatalog.NameOf(learn);
            string text = string.IsNullOrEmpty(forget)
                ? $"{a.Data.name}이(가) [{learned}]을(를) 익혔습니다."
                : $"{a.Data.name}이(가) [{SkillCatalog.NameOf(forget)}]을(를) 버리고 [{learned}]을(를) 익혔습니다.";
            if (Services.TryGet<GameLog>(out var log)) log.Add(text, LogCategory.Info);
            if (Services.TryGet<GameClock>(out var clock))
                AdventurerMemory.Add(a.Data.memories, clock.TotalHours, 2, $"[{learned}]을(를) 익혔다");
            if (!string.IsNullOrWhiteSpace(say)) EventBus.Publish(new AdventurerSpeechEvent { Adventurer = a, Text = say });
            EventBus.Publish(new SkillLearnedEvent { Adventurer = a, LearnedId = learn, ForgottenId = forget, FromLlm = fromLlm });
        }
    }
}
