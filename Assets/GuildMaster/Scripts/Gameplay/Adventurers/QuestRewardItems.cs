using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Economy;
using GuildMaster.Quests;
using UnityEngine;

namespace GuildMaster.Adventurers
{
    /// <summary>A member got the item a quest paid besides the gold (P4-133): what it did with it.</summary>
    public struct QuestItemRewardedEvent
    {
        public Adventurer Member;
        public Quest Quest;
        public string ItemId;
        public int Count;
        /// <summary>"마셔 둠", "새 장비로 바꿈", or the gold it sold the rest for.</summary>
        public string How;
        public int SoldFor;
    }

    /// <summary>
    /// The item a client pays besides the gold, into the pack of the member who did most (P4-133, user decision 2026-11-08):
    /// potions and flares it keeps to use (up to what it carries), gear better than its own it puts on, and anything else —
    /// or what is over — it sells at the counter there and then, the gold its own (not the party's).
    /// </summary>
    public static class QuestRewardItems
    {
        public static void Give(Adventurer a, Quest q, ItemStack item, TradeService trade)
        {
            if (a == null || string.IsNullOrEmpty(item.itemId) || item.count <= 0) return;
            var def = Services.TryGet<GameDatabase>(out var db) ? db.Item(item.itemId) : null;
            string name = def != null ? def.displayName : item.itemId;
            var d = a.Data;
            int left = item.count;
            string how = "";
            if (item.itemId == DefaultContent.Potion)
            {
                int keep = Mathf.Clamp(ErrandRules.PotionKeep - d.potions, 0, left);
                d.potions += keep;
                left -= keep;
                if (keep > 0) how = "물약은 챙겨 둔다.";
            }
            else if (item.itemId == Flares.FlareRules.ItemId)
            {
                int keep = Mathf.Clamp(Flares.FlareRules.Carry - d.flares, 0, left);
                d.flares += keep;
                left -= keep;
                if (keep > 0) how = "조명탄은 챙겨 둔다.";
            }
            else if (def != null && def.category == ItemCategory.Gear && def.gearTier > d.gearTier)
            {
                d.gearTier = def.gearTier;
                a.RecalculateStats();
                left--;
                how = "새 장비로 바꿔 꼈다!";
            }
            if (left > 0) d.AddLoot(item.itemId, left);
            // What it does not keep it sells at the counter (by the guild's loot rule, as any loot) — its own gold.
            int sold = left > 0 && trade != null ? trade.BuyLoot(a) : 0;
            if (sold > 0) how = how.Length == 0 ? $"팔아서 {sold}G를 벌었다." : how + $" 나머지는 팔아 {sold}G.";
            a.Say(ErrandRules.RewardLine(name, item.count, how));
            EventBus.Publish(new QuestItemRewardedEvent { Member = a, Quest = q, ItemId = item.itemId, Count = item.count, How = how, SoldFor = sold });
        }
    }
}
