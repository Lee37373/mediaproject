using System.Collections.Generic;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Ecosystem;
using GuildMaster.Places;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Quests
{
    /// <summary>
    /// What errands a client could ask of the guild now (P4-133), from what the guild knows only: the gathering spots it has
    /// found, the dens it has seen occupied, the villages it knows standing. Shared by the villages' own errands
    /// (<see cref="ErrandPoster"/>) and the proposal cards; the numbers are <see cref="ErrandRules"/>'.
    /// </summary>
    public static class ErrandOffers
    {
        /// <summary>A gathering errand there is ground for: the spots of one kind the guild knows.</summary>
        public struct Gather
        {
            public PlaceKind Kind;
            public string ItemId;
            /// <summary>What the ready spots give at the least (<see cref="ErrandRules.GatherSupply"/>).</summary>
            public int Supply;
            /// <summary>The ready spot nearest town: where the errand is posted (the members go to whichever is nearest them).</summary>
            public Vector2 Point;
        }

        /// <summary>The known spots of this kind, if any is ready (something to take).</summary>
        public static bool TryGather(PlaceKind kind, out Gather offer)
        {
            offer = default;
            if (!Services.TryGet<PlaceService>(out var places) || !Services.TryGet<WorldService>(out var world)) return false;
            int ready = 0;
            float best = float.MaxValue;
            Vector2 point = default;
            foreach (var p in places.Known(kind))
            {
                if (!places.IsReady(p)) continue;
                ready++;
                float d = Vector2.Distance(p.WorldPosition, world.TownCenterWorld);
                if (d >= best) continue;
                best = d;
                point = p.WorldPosition;
            }
            if (ready == 0) return false;
            offer = new Gather
            {
                Kind = kind, ItemId = PlaceRules.GatherItem(kind), Point = point,
                Supply = ErrandRules.GatherSupply(ready, kind == PlaceKind.OreVein),
            };
            return true;
        }

        /// <summary>A parts errand there is ground for: a den the guild has seen occupied, and the part its kind drops.</summary>
        public struct Collect
        {
            public Nest Den;
            public MonsterDefinition Species;
            public LootEntry Entry;
            public float PerKill;
            /// <summary>Parts the den can give (<see cref="ErrandRules.CollectSupply"/>).</summary>
            public int Supply;
        }

        /// <summary>
        /// The dens the guild has seen occupied whose kind drops a part worth asking for, that nobody asks the parts of yet;
        /// within <paramref name="reach"/> of <paramref name="near"/> when given (a village asks for the dens that trouble it).
        /// </summary>
        public static List<Collect> Collects(Vector2? near = null, float reach = float.MaxValue)
        {
            var list = new List<Collect>();
            if (!Services.TryGet<GuildIntel>(out var intel) || !Services.TryGet<QuestBoard>(out var board)) return list;
            foreach (var n in intel.KnownNests)
            {
                var species = intel.SpeciesOf(n);
                var seen = intel.Get(n);
                if (species == null || !seen.Observed || seen.LastCount <= 0 || n.IsLair || board.HasActiveCollectOn(n)) continue;
                if (intel.KnownLordAt(n) != null) continue;
                if (near is { } at && Vector2.Distance(at, n.WorldPosition) > reach) continue;
                if (QuestBoard.HuntPointError(n.WorldPosition) != null) continue;
                var entry = ErrandRules.CollectEntry(species);
                if (entry == null) continue;
                float per = ErrandRules.PerKill(entry);
                list.Add(new Collect
                {
                    Den = n, Species = species, Entry = entry, PerKill = per,
                    Supply = ErrandRules.CollectSupply(seen.LastCount, per),
                });
            }
            return list;
        }

        /// <summary>The villages the guild knows and has not seen in ruins (a delivery's destinations).</summary>
        public static List<Villages.Village> Destinations()
        {
            var list = new List<Villages.Village>();
            if (!Services.TryGet<Villages.VillageService>(out var villages)) return list;
            foreach (var v in villages.Villages)
                if (v.Known && !v.SeenRuined) list.Add(v);
            return list;
        }

        // ---------------- Drafts ----------------

        static float FromTown(Vector2 p) => Services.TryGet<WorldService>(out var w) ? Vector2.Distance(p, w.TownCenterWorld) : 0f;
        static int LevelAt(Vector2 p) => Services.TryGet<EcosystemManager>(out var eco) ? eco.BirthLevelAt(p) : 1;
        static int ValueOf(string itemId) => Services.TryGet<GameDatabase>(out var db) && db.Item(itemId) is { } i ? i.baseValue : 0;

        /// <summary>A gathering errand for <paramref name="wanted"/> items (fewer if the spots give less); false if too few.</summary>
        public static bool GatherDraft(Gather g, int wanted, float factor, out QuestDraft draft)
        {
            draft = default;
            int count = ErrandRules.Count(wanted, g.Supply);
            if (count <= 0) return false;
            var danger = QuestBoard.HuntDanger(LevelAt(g.Point), 1);
            int gold = ErrandRules.GatherGold(ValueOf(g.ItemId), count, danger, FromTown(g.Point), factor);
            draft = new QuestDraft
            {
                Type = QuestType.Gather, ItemId = g.ItemId, Count = count, Point = g.Point, HasPoint = true, Radius = 6f,
                RewardGold = gold, RewardExp = ErrandRules.Exp(QuestType.Gather, gold), MaxParty = ErrandRules.PartyFor(QuestType.Gather, danger),
            };
            return true;
        }

        /// <summary>A parts errand on this den for <paramref name="wanted"/> parts (fewer if the den gives less); false if too few.</summary>
        public static bool CollectDraft(Collect c, int wanted, float factor, out QuestDraft draft)
        {
            draft = default;
            int count = ErrandRules.Count(wanted, c.Supply);
            if (count <= 0) return false;
            int kills = ErrandRules.CollectKills(count, c.PerKill);
            var point = c.Den.WorldPosition;
            var danger = QuestBoard.HuntDanger(c.Species, kills, point, QuestBoard.HuntRadius);
            int gold = ErrandRules.CollectGold(danger, kills, c.Entry.item.baseValue, count, FromTown(point), factor);
            draft = new QuestDraft
            {
                Type = QuestType.Collect, Species = c.Species, ItemId = c.Entry.item.id, Count = count, Point = point, HasPoint = true,
                Radius = QuestBoard.HuntRadius, RewardGold = gold, RewardExp = ErrandRules.Exp(QuestType.Collect, gold),
                MaxParty = ErrandRules.PartyFor(QuestType.Collect, danger),
            };
            return true;
        }

        /// <summary>Goods to carry to this village (its doorstep).</summary>
        public static QuestDraft DeliverDraft(Villages.Village v, string goods, float factor)
        {
            var point = v.Doorstep;
            var danger = QuestBoard.HuntDanger(LevelAt(point), 1);
            int gold = ErrandRules.DeliverGold(danger, FromTown(point), factor);
            return new QuestDraft
            {
                Type = QuestType.Deliver, GoodsName = goods, Count = 1, Point = point, HasPoint = true, Radius = ErrandRules.DeliverReach,
                RewardGold = gold, RewardExp = ErrandRules.Exp(QuestType.Deliver, gold), MaxParty = ErrandRules.PartyFor(QuestType.Deliver, danger),
            };
        }
    }
}
