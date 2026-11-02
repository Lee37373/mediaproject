using System;
using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Dilemmas;
using GuildMaster.Town;
using GuildMaster.Welfare;

namespace GuildMaster.Guide
{
    /// <summary>A step of the "다음 할 일" checklist was done for the first time.</summary>
    public struct GuideStepDoneEvent { public GuideStep Step; }

    /// <summary>
    /// Which steps of the "다음 할 일" checklist (F-19) the guild master has done, from the game's events. Steps that no
    /// event covers (a look at the quest board, talking to a visitor, opening the relationship graph) are marked by
    /// whoever sees them happen.
    /// </summary>
    public class GuideTracker : IDisposable
    {
        readonly HashSet<GuideStep> _done = new();

        public GuideTracker()
        {
            EventBus.Subscribe<CounselEndedEvent>(OnCounsel);
            EventBus.Subscribe<BuildingLevelChangedEvent>(OnLevel);
            EventBus.Subscribe<BuildingBuiltEvent>(OnBuilt);
            EventBus.Subscribe<DilemmaResolvedEvent>(OnCard);
        }

        public void Dispose()
        {
            EventBus.Unsubscribe<CounselEndedEvent>(OnCounsel);
            EventBus.Unsubscribe<BuildingLevelChangedEvent>(OnLevel);
            EventBus.Unsubscribe<BuildingBuiltEvent>(OnBuilt);
            EventBus.Unsubscribe<DilemmaResolvedEvent>(OnCard);
        }

        public bool IsDone(GuideStep s) => _done.Contains(s);
        public int DoneCount => _done.Count;
        public bool AllDone => _done.Count >= GuideRules.Order.Length;

        public void Mark(GuideStep s)
        {
            if (_done.Add(s)) EventBus.Publish(new GuideStepDoneEvent { Step = s });
        }

        void OnCounsel(CounselEndedEvent e) => Mark(GuideStep.Counsel);
        void OnLevel(BuildingLevelChangedEvent e) { if (e.Level > 1) Mark(GuideStep.Build); }
        void OnBuilt(BuildingBuiltEvent e) => Mark(GuideStep.Build);
        void OnCard(DilemmaResolvedEvent e) { if (!e.ByDeadline) Mark(GuideStep.Decision); }
    }
}
