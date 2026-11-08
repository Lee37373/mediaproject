using GuildMaster.Core;
using GuildMaster.Town;

namespace GuildMaster.Social
{
    /// <summary>
    /// The residents' standing favours (P4-134, user decision 2026-11-08 "둘 다"): the better a resident thinks of the guild, the
    /// more its trade does for it — 10% a step at opinion 25 · 50 · 75 (<see cref="ResidentRequestRules.Tier"/>). The systems ask
    /// here: the forge and the apothecary for their crafting time, the town for building repairs, the gates and traps for
    /// theirs, the food store for its price, the treasury for the tax, the visitors for the child's bragging. Nothing without
    /// the residents (EditMode).
    /// </summary>
    public static class ResidentPerks
    {
        public static int TierOf(ResidentJob job) => Services.TryGet<ResidentService>(out var s) ? s.TierOf(job) : 0;

        public static float Share(ResidentJob job) => ResidentRequestRules.Share(TierOf(job));

        /// <summary>Share cut off a station's crafting time: the smith's forge, the herbalist's apothecary.</summary>
        public static float CraftTimeCut(BuildingType station) => station switch
        {
            BuildingType.Blacksmith => Share(ResidentJob.Blacksmith),
            BuildingType.Apothecary => Share(ResidentJob.Herbalist),
            _ => 0f,
        };

        /// <summary>Share off a building's repair (the smith).</summary>
        public static float BuildingRepairCut => Share(ResidentJob.Blacksmith);

        /// <summary>Share off a gate's repair and the traps' rearming (the guard).</summary>
        public static float GateRepairCut => Share(ResidentJob.Guard);

        /// <summary>Share off the bakery's food (the baker).</summary>
        public static float FoodCut => Share(ResidentJob.Baker);

        /// <summary>Share added to the daily tax (the chief).</summary>
        public static float TaxBonus => Share(ResidentJob.Chief);

        /// <summary>Start affinity the child's bragging gives each new visitor.</summary>
        public static int ChildBrag => ResidentRequestRules.ChildBrag(TierOf(ResidentJob.Child));
    }
}
