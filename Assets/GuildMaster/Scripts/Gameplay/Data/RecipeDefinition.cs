using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Data
{
    /// <summary>
    /// Crafting recipe (P4-2): made at a station building (apothecary / blacksmith) once that
    /// building's craft tier is high enough; takes game hours in the station's queue.
    /// </summary>
    public class RecipeDefinition : ScriptableObject
    {
        public string id;
        public string outputItemId;
        public int outputCount = 1;
        public List<ItemCost> inputs = new();
        public int goldCost;
        public float hours = 2f;
        public Town.BuildingType station;
        /// <summary>Minimum <see cref="BuildingLevel.craftTier"/> of the station.</summary>
        public int requiredTier = 1;
    }
}
