using UnityEngine;
using GuildMaster.Data;
using GuildMaster.Ecosystem;

namespace GuildMaster.Adventurers
{
    /// <summary>
    /// Rules for hunting without a quest (P4-37, user request 2026-09-20): with an empty board the
    /// guild used to stand in the plaza all day. Members now walk out on their own, kill what they
    /// can handle and bring the loot back. Pure, so the decisions can be tested.
    /// </summary>
    public static class FreeHunt
    {
        /// <summary>How far from the town centre a free hunt roams (user decision: 40 cells, wide).</summary>
        public const float Radius = 40f;
        /// <summary>Game hours one trip lasts before heading back to sell what it found.</summary>
        public const float MaxHours = 8f;
        /// <summary>
        /// Items worth walking home with. This is the real brake on a free hunt: the walk back,
        /// the sale and the walk out again cost game hours, so the countryside gets a breather
        /// between rounds. Counted in items, not stacks — loot merges by id, so a hunter with
        /// twenty jellies still carries one stack (first measured run: the slimes were wiped out
        /// in two game hours because the "full bag" never triggered).
        /// </summary>
        public const int FullBag = 12;

        /// <summary>Leave before the trip turns into a funeral: nobody ordered this hunt.</summary>
        public const float GoHomeHpRatio = 0.6f;
        /// <summary>Allies this close count towards the strength used to judge a fight.</summary>
        public const float AllyRadius = 8f;

        /// <summary>
        /// Whether this member should set off with a band of this size. A priest's attack is a
        /// candle — it heals a party, it does not clear a field — so it stays home rather than
        /// hunt alone (user report 2026-09-20: "the healer is going out on its own, that makes no
        /// sense"). Everyone else may go alone if nobody else feels like it.
        /// </summary>
        public static bool WorthGoing(AdventurerClass cls, int bandSize)
            => bandSize >= 2 || cls != AdventurerClass.Priest;

        /// <summary>
        /// Whether a fight is worth picking (user decision: avoid what we cannot handle). Same
        /// margin the quest board shows the player, so "무모" is refused here too, but the strength
        /// is divided by how many of them are in sight: a lone goblin and four goblins are not the
        /// same fight, and the first measured run lost a Lv1 priest to the difference.
        /// </summary>
        public static bool CanTake(float power, int partySize, DangerLevel danger, int enemies = 1)
            => QuestRisk.Label(QuestRisk.Margin(power, partySize, danger) / Mathf.Max(1, enemies)) != QuestRisk.Reckless;

        /// <summary>A pack hunter never travels alone: assume at least this many (user request).</summary>
        public const int AssumedPackSize = 3;
        /// <summary>Share of a nearby den that would actually pile in; the rest stays home.</summary>
        public const float NestJoinFraction = 0.5f;

        /// <summary>
        /// How many to plan for after spotting some (user request 2026-09-20: "seeing one should
        /// tell them there are probably more"). Sight is narrow and packs spread out, so what is
        /// visible is a floor, not the count: a pack hunter brings friends, and standing by a den
        /// means part of it can join in. <paramref name="nestNearby"/> is what the guild last
        /// counted there — it comes from <see cref="GuildIntel"/>, so it is what the whole guild has
        /// seen between them, not just this member.
        ///
        /// Only species that actually gang up get the extra: slimes neither hunt in packs nor come
        /// at people, so a den of them is still one slime at a time, and reckoning with the whole
        /// den made a measured run refuse every fight there was.
        /// </summary>
        public static int ExpectedEnemies(int seen, MonsterDefinition species, int nestNearby)
        {
            int expected = Mathf.Max(1, seen);
            if (species == null || (!species.packHunter && !species.aggressiveToHumans)) return expected;
            if (species.packHunter) expected = Mathf.Max(expected, AssumedPackSize);
            return Mathf.Max(expected, Mathf.CeilToInt(nestNearby * NestJoinFraction));
        }

        /// <summary>
        /// Whether a species is off limits for a self-chosen hunt: the guild's own endangered
        /// warning (P5-1) calls it off, and the player can still post a quest against them on
        /// purpose. Only "멸종 위기", not "개체 감소" — the count comes from the nests the guild has
        /// found, so one den it last saw emptied already reads low, and sparing at the milder step
        /// left the members with nothing to hunt at all (measured run, day 1: everything spared).
        /// </summary>
        public static bool Spare(EcologyStatus status) => status == EcologyStatus.Endangered;

        /// <summary>
        /// Time to walk home. A newly posted quest wins: the player's orders come before a hunt the
        /// member picked for itself. Hurt counts too, and well before the normal retreat threshold —
        /// a quest is worth bleeding for, an afternoon's hunting is not.
        /// </summary>
        public static bool ShouldGoHome(float hoursHunting, int lootItems, bool questAvailable, float hpRatio = 1f)
            => questAvailable || hoursHunting >= MaxHours || lootItems >= FullBag || hpRatio < GoHomeHpRatio;
    }
}
