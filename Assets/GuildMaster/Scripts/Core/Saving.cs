using System.Collections.Generic;

namespace GuildMaster.Core
{
    /// <summary>
    /// A system that saves its own state (P5-4). The save file holds one section per system, keyed
    /// by <see cref="SaveKey"/>, whose text is the system's own DTO (JsonUtility, snake_case).
    /// Systems are restored in the order they were registered (the composition root's order), and
    /// restoring must be quiet: no events, log lines or notifications for what is merely put back.
    /// </summary>
    public interface ISaveable
    {
        string SaveKey { get; }

        /// <summary>This system's state as JSON.</summary>
        string Capture();

        /// <summary>
        /// First pass: puts the state back and creates what the system owns, registering anything
        /// others refer to with <see cref="SaveContext.Register"/>. References to objects of other
        /// systems wait for <see cref="ISaveLink.Link"/>.
        /// </summary>
        void Restore(string json, SaveContext ctx);
    }

    /// <summary>Second pass of a restore: resolves references by save id once every section exists.</summary>
    public interface ISaveLink
    {
        void Link(SaveContext ctx);
    }

    /// <summary>Shared by the sections of one restore: the objects by save id, and what went wrong.</summary>
    public class SaveContext
    {
        readonly Dictionary<string, object> _objects = new();

        public int Version { get; }
        public readonly List<string> Warnings = new();

        public SaveContext(int version) { Version = version; }

        public void Register(string id, object obj)
        {
            if (string.IsNullOrEmpty(id) || obj == null) return;
            if (_objects.ContainsKey(id)) Warnings.Add($"duplicate save id {id}");
            _objects[id] = obj;
        }

        /// <summary>The object saved under this id, or null (missing ids are noted, not fatal).</summary>
        public T Find<T>(string id) where T : class
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_objects.TryGetValue(id, out var o) && o is T t) return t;
            Warnings.Add($"missing {typeof(T).Name} {id}");
            return null;
        }
    }
}
