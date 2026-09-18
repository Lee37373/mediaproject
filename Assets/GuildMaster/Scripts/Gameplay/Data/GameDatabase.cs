using System.Collections.Generic;
using GuildMaster.Traits;
using UnityEngine;

namespace GuildMaster.Data
{
    /// <summary>
    /// Registry of all content definitions. Loaded from Resources/Data/GameDatabase when it
    /// exists (designer-edited assets); otherwise built from DefaultContent in memory.
    /// </summary>
    [CreateAssetMenu(menuName = "GuildMaster/Game Database", fileName = "GameDatabase")]
    public class GameDatabase : ScriptableObject
    {
        public List<ItemDefinition> items = new();
        public List<MonsterDefinition> monsters = new();
        public List<TraitDefinition> traits = new();

        Dictionary<string, ItemDefinition> _items;
        Dictionary<string, MonsterDefinition> _monsters;
        Dictionary<string, TraitDefinition> _traits;

        public static GameDatabase Load()
        {
            var asset = Resources.Load<GameDatabase>("Data/GameDatabase");
            var db = asset ? asset : DefaultContent.Build();
            db.BuildIndex();
            return db;
        }

        public void BuildIndex()
        {
            _items = new Dictionary<string, ItemDefinition>();
            foreach (var i in items) if (i) _items[i.id] = i;
            _monsters = new Dictionary<string, MonsterDefinition>();
            foreach (var m in monsters) if (m) _monsters[m.id] = m;
            _traits = new Dictionary<string, TraitDefinition>();
            foreach (var t in traits) if (t) _traits[t.id] = t;
        }

        public ItemDefinition Item(string id) => id != null && _items.TryGetValue(id, out var i) ? i : null;
        public MonsterDefinition Monster(string id) => id != null && _monsters.TryGetValue(id, out var m) ? m : null;
        public TraitDefinition Trait(string id) => id != null && _traits.TryGetValue(id, out var t) ? t : null;
    }
}
