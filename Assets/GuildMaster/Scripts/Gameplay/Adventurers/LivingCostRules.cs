using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// A member's keep (P4-68, user decision 2026-09-27): meals once a day wherever they are, and
    /// lodging for every hour they rest in town; a camp out in the wild is free. The gold goes to the
    /// guild. What a member cannot pay goes on the slate, and the next gold they earn clears it
    /// first. Pure purse arithmetic, covered by tests; <see cref="LivingCosts"/> does the charging.
    /// </summary>
    public static class LivingCostRules
    {
        /// <summary>
        /// Charges <paramref name="amount"/> against a purse: pays what it can, the rest becomes debt.
        /// Returns the gold actually paid.
        /// </summary>
        public static int Charge(ref int gold, ref int debt, int amount)
        {
            if (amount <= 0) return 0;
            int paid = Mathf.Min(Mathf.Max(0, gold), amount);
            gold -= paid;
            debt += amount - paid;
            return paid;
        }

        /// <summary>Clears as much debt as the purse holds. Returns the gold paid.</summary>
        public static int Repay(ref int gold, ref int debt)
        {
            int paid = Mathf.Min(Mathf.Max(0, gold), Mathf.Max(0, debt));
            gold -= paid;
            debt -= paid;
            return paid;
        }

        /// <summary>What the member can call their own: gold less what they owe (negative when in debt).</summary>
        public static int NetWorth(int gold, int debt) => gold - debt;

        /// <summary>"골드 120" or "골드 0 (빚 30)" for the UI.</summary>
        public static string Purse(int gold, int debt) => debt > 0 ? $"골드 {gold} (빚 {debt})" : $"골드 {gold}";
    }
}
