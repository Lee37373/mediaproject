using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace GuildMaster.Core
{
    /// <summary>Optional callbacks for pooled objects.</summary>
    public interface IPoolable
    {
        void OnSpawned();
        void OnDespawned();
    }

    /// <summary>
    /// Keyed GameObject pools built on UnityEngine.Pool. Pools are registered with a
    /// factory, so no prefab assets are required (sprites are generated in code).
    /// </summary>
    public class PoolService : MonoBehaviour
    {
        class Entry
        {
            public ObjectPool<GameObject> Pool;
            public Transform Root;
        }

        readonly Dictionary<string, Entry> _pools = new();
        readonly Dictionary<GameObject, string> _owner = new();
        static readonly List<IPoolable> s_tmp = new();

        public void Register(string key, Func<GameObject> factory, int prewarm = 0, int maxSize = 512)
        {
            if (_pools.ContainsKey(key)) return;
            var root = new GameObject($"Pool:{key}").transform;
            root.SetParent(transform, false);
            var entry = new Entry { Root = root };
            entry.Pool = new ObjectPool<GameObject>(
                createFunc: () =>
                {
                    var go = factory();
                    go.transform.SetParent(root, false);
                    _owner[go] = key;
                    return go;
                },
                actionOnGet: go => go.SetActive(true),
                actionOnRelease: go => { go.SetActive(false); go.transform.SetParent(root, false); },
                actionOnDestroy: go => { _owner.Remove(go); if (go) Destroy(go); },
                collectionCheck: false,
                defaultCapacity: Mathf.Max(8, prewarm),
                maxSize: maxSize);
            _pools[key] = entry;

            if (prewarm > 0)
            {
                var warm = new List<GameObject>(prewarm);
                for (int i = 0; i < prewarm; i++) warm.Add(entry.Pool.Get());
                foreach (var go in warm) entry.Pool.Release(go);
            }
        }

        public bool Has(string key) => _pools.ContainsKey(key);

        public GameObject Spawn(string key, Vector3 position, Transform parent = null)
        {
            if (!_pools.TryGetValue(key, out var entry))
                throw new InvalidOperationException($"Pool not registered: {key}");
            var go = entry.Pool.Get();
            if (parent) go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.GetComponentsInChildren(true, s_tmp);
            foreach (var p in s_tmp) p.OnSpawned();
            return go;
        }

        public T Spawn<T>(string key, Vector3 position, Transform parent = null) where T : Component
            => Spawn(key, position, parent).GetComponent<T>();

        public void Despawn(GameObject go)
        {
            if (!go) return;
            if (!_owner.TryGetValue(go, out var key) || !_pools.TryGetValue(key, out var entry))
            {
                Destroy(go);
                return;
            }
            if (!go.activeSelf) return; // already released
            go.GetComponentsInChildren(true, s_tmp);
            foreach (var p in s_tmp) p.OnDespawned();
            entry.Pool.Release(go);
        }

        public int CountActive(string key) => _pools.TryGetValue(key, out var e) ? e.Pool.CountActive : 0;
    }
}
