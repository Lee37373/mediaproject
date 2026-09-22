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
            static Action<T> s_handlers;
            /// <summary>Invocation list as an array, rebuilt lazily after (un)subscribing, so Publish does not allocate.</summary>
            static Action<T>[] s_snapshot;
            static bool s_registered;

            public static void Add(Action<T> handler)
            {
                if (!s_registered)
                {
                    s_registered = true;
                    s_clearers.Add(() => { s_handlers = null; s_snapshot = null; });
                }
                s_handlers -= handler;
                s_handlers += handler;
                s_snapshot = null;
            }

            public static void Remove(Action<T> handler)
            {
                s_handlers -= handler;
                s_snapshot = null;
            }

            /// <summary>
            /// Current handlers, or null. The array is never modified afterwards, so a handler that
            /// (un)subscribes while an event is being delivered does not disturb that delivery.
            /// </summary>
            public static Action<T>[] Snapshot()
            {
                if (s_snapshot != null || s_handlers == null) return s_snapshot;
                var list = s_handlers.GetInvocationList();
                var array = new Action<T>[list.Length];
                for (int i = 0; i < list.Length; i++) array[i] = (Action<T>)list[i];
                return s_snapshot = array;
            }
        }

        public static void Subscribe<T>(Action<T> handler) where T : struct => Channel<T>.Add(handler);

        public static void Unsubscribe<T>(Action<T> handler) where T : struct => Channel<T>.Remove(handler);

        public static void Publish<T>(T evt) where T : struct
        {
            var handlers = Channel<T>.Snapshot();
            if (handlers == null) return;
            foreach (var handler in handlers)
            {
                try { handler(evt); }
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
