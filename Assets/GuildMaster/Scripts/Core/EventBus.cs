using System;
using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Core
{
    /// <summary>
    /// Type-safe, allocation-free publish/subscribe bus. Events are structs.
    /// Systems communicate through events so modules stay decoupled.
    /// </summary>
    public static class EventBus
    {
        static readonly List<Action> s_clearers = new();

        static class Channel<T> where T : struct
        {
            public static Action<T> Handlers;
            static bool s_registered;

            public static void EnsureRegistered()
            {
                if (s_registered) return;
                s_registered = true;
                s_clearers.Add(() => { Handlers = null; });
            }
        }

        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            Channel<T>.EnsureRegistered();
            Channel<T>.Handlers -= handler;
            Channel<T>.Handlers += handler;
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            Channel<T>.Handlers -= handler;
        }

        public static void Publish<T>(T evt) where T : struct
        {
            var handlers = Channel<T>.Handlers;
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action<T>)d)(evt); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>Removes every subscriber of every channel (called on session start).</summary>
        public static void Clear()
        {
            foreach (var clear in s_clearers) clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Clear();
    }
}
