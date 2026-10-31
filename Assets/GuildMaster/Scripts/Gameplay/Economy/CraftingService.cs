using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Data;
using GuildMaster.Town;
using UnityEngine;

namespace GuildMaster.Economy
{
    public struct CraftCompletedEvent { public RecipeDefinition Recipe; public int Count; }

    /// <summary>
    /// Crafting (P4-2): the apothecary and the blacksmith each run one queue. Ordering pays the
    /// materials and gold up front; jobs advance with game time (apothecary level speeds potions
    /// up) and put the product into the guild stock. Recipes unlock with the station's craft tier
    /// (P4-1). Stock targets (user decision) keep a product topped up automatically.
    /// </summary>
    public class CraftingService : MonoBehaviour, ISaveable
    {
        public const int MaxQueuePerStation = 3;
        const float AutoCheckSeconds = 1f;

        public class Job
        {
            public RecipeDefinition Recipe;
            public float Hours;     // progress in game hours
        }

        GuildTreasury _treasury;
        GameDatabase _db;
        GameClock _clock;
        TownService _town;
        readonly Dictionary<BuildingType, List<Job>> _queues = new();
        readonly Dictionary<string, int> _targets = new();
        float _autoTimer;

        public IReadOnlyList<RecipeDefinition> Recipes => _db.recipes;

        public void Init(GuildTreasury treasury, GameDatabase db, GameClock clock, TownService town)
        {
            _treasury = treasury;
            _db = db;
            _clock = clock;
            _town = town;
        }

        public IReadOnlyList<Job> Queue(BuildingType station)
            => _queues.TryGetValue(station, out var q) ? q : (IReadOnlyList<Job>)System.Array.Empty<Job>();

        // ---------------- Station state ----------------

        BuildingLevel StationLevel(BuildingType station)
        {
            var b = _town.Get(station);
            var def = _db.Building(station);
            return b != null && def != null ? def.Level(b.Level) : null;
        }

        public bool IsUnlocked(RecipeDefinition r) => (StationLevel(r.station)?.craftTier ?? 0) >= r.requiredTier;

        float Speed(BuildingType station) => Mathf.Max(0.1f, StationLevel(station)?.craftSpeed ?? 1f);

        /// <summary>Game hours one unit takes at the station's current speed.</summary>
        public float EffectiveHours(RecipeDefinition r) => r.hours / Speed(r.station);

        // ---------------- Ordering ----------------

        /// <summary>Why this recipe cannot be ordered now, or null.</summary>
        public string CannotCraftReason(RecipeDefinition r)
        {
            var station = _town.Get(r.station);
            if (station == null || station.IsDestroyed) return "제작 건물이 없습니다.";
            if (!IsUnlocked(r)) return $"{station.DisplayName} 레벨이 부족합니다.";
            if (Queue(r.station).Count >= MaxQueuePerStation) return "대기열이 가득 찼습니다.";
            if (_treasury.Gold < r.goldCost) return "골드가 부족합니다.";
            foreach (var c in r.inputs)
                if (_treasury.GetStock(c.itemId) < c.count)
                    return $"{_db.Item(c.itemId)?.displayName ?? c.itemId}이(가) 부족합니다.";
            return null;
        }

        public bool TryOrder(RecipeDefinition r, out string error)
        {
            error = CannotCraftReason(r);
            if (error != null) return false;
            if (r.goldCost > 0 && !_treasury.TrySpendGold(r.goldCost, GoldReason.Craft)) { error = "골드가 부족합니다."; return false; }
            foreach (var c in r.inputs) _treasury.TryRemoveStock(c.itemId, c.count);
            if (!_queues.TryGetValue(r.station, out var q)) _queues[r.station] = q = new List<Job>();
            q.Add(new Job { Recipe = r });
            return true;
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SavedJob
        {
            public BuildingType station;
            public string recipe;
            public float hours;
        }

        [System.Serializable]
        class SaveData
        {
            /// <summary>Every station's queue, in order (stations sorted).</summary>
            public List<SavedJob> jobs = new();
            public List<string> target_recipes = new();
            public List<int> targets = new();
        }

        public string SaveKey => "crafting";

        public string Capture()
        {
            var s = new SaveData();
            var stations = _queues.Keys.ToList();
            stations.Sort();
            foreach (var station in stations)
                foreach (var job in _queues[station])
                    s.jobs.Add(new SavedJob { station = station, recipe = job.Recipe.id, hours = job.Hours });
            var ids = _targets.Keys.ToList();
            ids.Sort(System.StringComparer.Ordinal);
            foreach (var id in ids) { s.target_recipes.Add(id); s.targets.Add(_targets[id]); }
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _queues.Clear();
            foreach (var j in s.jobs)
            {
                var recipe = _db.recipes.FirstOrDefault(r => r.id == j.recipe);
                if (recipe == null) { ctx.Warnings.Add("crafting: unknown recipe " + j.recipe); continue; }
                if (!_queues.TryGetValue(j.station, out var q)) _queues[j.station] = q = new List<Job>();
                q.Add(new Job { Recipe = recipe, Hours = j.hours });
            }
            _targets.Clear();
            for (int i = 0; i < s.target_recipes.Count && i < s.targets.Count; i++) _targets[s.target_recipes[i]] = s.targets[i];
        }

        /// <summary>Stock level to keep (0 = no auto-crafting).</summary>
        public int Target(RecipeDefinition r) => _targets.TryGetValue(r.id, out var t) ? t : 0;

        public void SetTarget(RecipeDefinition r, int target) => _targets[r.id] = Mathf.Clamp(target, 0, 99);

        /// <summary>Units already on the way (queued) for a product.</summary>
        int Incoming(string itemId) => _queues.Values.SelectMany(q => q).Where(j => j.Recipe.outputItemId == itemId).Sum(j => j.Recipe.outputCount);

        // ---------------- Ticking ----------------

        void Update()
        {
            if (_clock == null) return;
            float hours = Time.deltaTime / _clock.RealSecondsPerGameHour;
            foreach (var kv in _queues)
            {
                var q = kv.Value;
                if (q.Count == 0) continue;
                var station = _town.Get(kv.Key);
                if (station == null || station.IsDestroyed) continue;   // paused until rebuilt
                var job = q[0];
                job.Hours += hours * Speed(kv.Key);
                if (job.Hours < job.Recipe.hours) continue;
                q.RemoveAt(0);
                _treasury.AddStock(job.Recipe.outputItemId, job.Recipe.outputCount);
                EventBus.Publish(new CraftCompletedEvent { Recipe = job.Recipe, Count = job.Recipe.outputCount });
            }

            _autoTimer -= Time.unscaledDeltaTime;
            if (_autoTimer > 0f) return;
            _autoTimer = AutoCheckSeconds;
            foreach (var r in _db.recipes)
            {
                int target = Target(r);
                if (target <= 0) continue;
                if (_treasury.GetStock(r.outputItemId) + Incoming(r.outputItemId) >= target) continue;
                if (CannotCraftReason(r) == null) TryOrder(r, out _);
            }
        }
    }
}
