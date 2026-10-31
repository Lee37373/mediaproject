using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Core
{
    /// <summary>
    /// Minimal service locator. GameBootstrap registers systems in explicit order;
    /// consumers resolve them lazily with Get/TryGet.
    /// </summary>
    public static class Services
    {
        static readonly Dictionary<Type, object> s_services = new();

        public static void Register<T>(T service) where T : class
        {
            s_services[typeof(T)] = service ?? throw new ArgumentNullException(nameof(service));
        }

        public static void Unregister<T>(T service) where T : class
        {
            if (s_services.TryGetValue(typeof(T), out var current) && ReferenceEquals(current, service))
                s_services.Remove(typeof(T));
        }

        public static T Get<T>() where T : class
        {
            if (TryGet<T>(out var s)) return s;
            throw new InvalidOperationException($"Service not registered: {typeof(T).Name}");
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (s_services.TryGetValue(typeof(T), out var obj) && obj is T t && !(obj is UnityEngine.Object uo && uo == null))
            {
                service = t;
                return true;
            }
            service = null;
            return false;
        }

        /// <summary>
        /// Counts the sessions (every <see cref="Clear"/>, i.e. every boot of the composition root).
        /// Async work that outlives its session — an LLM answer arriving after a load or a restart —
        /// compares the number it started under and drops its result, instead of writing into the
        /// next game through the services that are registered by then.
        /// </summary>
        public static int Generation { get; private set; }

        public static void Clear()
        {
            s_services.Clear();
            Generation++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Clear();
    }
}
