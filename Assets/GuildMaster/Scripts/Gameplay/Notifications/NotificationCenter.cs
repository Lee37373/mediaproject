using System;
using System.Collections.Generic;
using System.Globalization;
using GuildMaster.Core;
using UnityEngine;

namespace GuildMaster.Notifications
{
    /// <summary>How urgent a notification is; the UI colours and times toasts by it.</summary>
    public enum NotificationKind { Info, Success, Quest, Warning, Danger }

    /// <summary>Where clicking a notification leads. The UI layer decides how to open each target.</summary>
    public enum NotificationTarget { None, QuestBoard, Negotiation, Roster, Adventurer, Location, Report, Management, Proposals, Memorial, GuildRank, Bestiary, Dilemmas, News, Resident }

    /// <summary>A pointer from a notification to something in the game (data only, so Gameplay needs no UI types).</summary>
    public readonly struct NotificationLink
    {
        public NotificationTarget Target { get; }
        /// <summary>Quest id (Negotiation), adventurer id (Adventurer, Management), day number (Report) or fallen record id (Memorial).</summary>
        public string Id { get; }
        /// <summary>World position (Location).</summary>
        public Vector2 Position { get; }

        NotificationLink(NotificationTarget target, string id, Vector2 position)
        {
            Target = target;
            Id = id;
            Position = position;
        }

        /// <summary>Save (P5-4): a link as it was stored.</summary>
        internal static NotificationLink Restore(NotificationTarget target, string id, Vector2 position) => new(target, id, position);

        public bool IsNone => Target == NotificationTarget.None;

        public static NotificationLink None => default;
        public static NotificationLink QuestBoard() => new(NotificationTarget.QuestBoard, null, default);
        public static NotificationLink Negotiation(string questId) => new(NotificationTarget.Negotiation, questId, default);
        public static NotificationLink Roster() => new(NotificationTarget.Roster, null, default);
        public static NotificationLink Adventurer(string adventurerId) => new(NotificationTarget.Adventurer, adventurerId, default);
        public static NotificationLink Location(Vector2 position) => new(NotificationTarget.Location, null, position);
        public static NotificationLink Report(int day) => new(NotificationTarget.Report, day.ToString(CultureInfo.InvariantCulture), default);
        /// <summary>The guild master's care window for this member (F-2).</summary>
        public static NotificationLink Management(string adventurerId) => new(NotificationTarget.Management, adventurerId, default);
        /// <summary>The quest proposal list (F-8).</summary>
        public static NotificationLink Proposals() => new(NotificationTarget.Proposals, null, default);
        /// <summary>The guild's rank window (P4-106).</summary>
        public static NotificationLink GuildRank() => new(NotificationTarget.GuildRank, null, default);
        /// <summary>The bestiary window (P4-107).</summary>
        public static NotificationLink Bestiary() => new(NotificationTarget.Bestiary, null, default);
        /// <summary>The decision cards window (P4-111c).</summary>
        public static NotificationLink Dilemmas() => new(NotificationTarget.Dilemmas, null, default);
        /// <summary>The wall of the fallen, on this record (F-6).</summary>
        public static NotificationLink Memorial(int recordId) => new(NotificationTarget.Memorial, recordId.ToString(CultureInfo.InvariantCulture), default);
        /// <summary>The town newspaper of this day (P4-130).</summary>
        public static NotificationLink News(int day) => new(NotificationTarget.News, day.ToString(CultureInfo.InvariantCulture), default);
        /// <summary>A resident and its request window (P4-134).</summary>
        public static NotificationLink Resident(string residentId) => new(NotificationTarget.Resident, residentId, default);
    }

    public class Notification
    {
        public int Id { get; internal set; }
        public NotificationKind Kind { get; internal set; }
        public string Title { get; internal set; }
        public string Text { get; internal set; }
        /// <summary>Same key = same subject (e.g. "levelup:A1"): repeats merge into the unread entry instead of piling up.</summary>
        public string Key { get; internal set; }
        public NotificationLink Link { get; internal set; }
        /// <summary>Game time of the latest occurrence, e.g. "2일 14:30".</summary>
        public string TimeLabel { get; internal set; }
        public float Hour { get; internal set; }
        /// <summary>How many occurrences this entry stands for (1 = a single event).</summary>
        public int Count { get; internal set; } = 1;
        public bool Read { get; internal set; }
        /// <summary>False = only listed, no toast on screen.</summary>
        public bool Toast { get; internal set; } = true;

        public bool HasLink => !Link.IsNone;
    }

    /// <summary>Published when a notification is added, or when a repeat was merged into an existing unread one.</summary>
    public struct NotificationPostedEvent { public Notification Notification; public bool Merged; }

    /// <summary>Published when the list changes without a new post (read, resolved, cleared, trimmed).</summary>
    public struct NotificationsChangedEvent { }

    /// <summary>
    /// The player's notification list (F-1): what happened that deserves attention, newest last.
    /// Pure logic with no Unity objects, so it is covered by EditMode tests. Sources post to it
    /// (see <see cref="NotificationSources"/>); the UI shows toasts and the list.
    /// </summary>
    public class NotificationCenter : ISaveable
    {
        /// <summary>Oldest entries are dropped beyond this many.</summary>
        public const int MaxEntries = 60;

        readonly List<Notification> _entries = new();
        readonly HashSet<string> _onceKeys = new();
        readonly Func<float> _hourNow;
        readonly Func<string> _timeLabel;
        int _nextId = 1;

        /// <param name="hourNow">Game time in hours (for <see cref="Notification.Hour"/>).</param>
        /// <param name="timeLabel">Readable game time for the list ("2일 14:30").</param>
        public NotificationCenter(Func<float> hourNow = null, Func<string> timeLabel = null)
        {
            _hourNow = hourNow;
            _timeLabel = timeLabel;
        }

        /// <summary>Oldest first.</summary>
        public IReadOnlyList<Notification> Entries => _entries;

        // ---------------- Save (P5-4) ----------------

        [Serializable]
        class SavedEntry
        {
            public int id;
            public NotificationKind kind;
            public string title;
            public string text;
            public string key;
            public NotificationTarget target;
            public string link_id;
            public Vector2 link_position;
            public string time_label;
            public float hour;
            public int count;
            public bool read;
            public bool toast;
        }

        [Serializable]
        class SaveData
        {
            public int next_id;
            public List<SavedEntry> entries = new();
            public List<string> once = new();
        }

        public string SaveKey => "notifications";

        public string Capture()
        {
            var s = new SaveData { next_id = _nextId };
            foreach (var e in _entries)
                s.entries.Add(new SavedEntry
                {
                    id = e.Id, kind = e.Kind, title = e.Title, text = e.Text, key = e.Key, target = e.Link.Target, link_id = e.Link.Id,
                    link_position = e.Link.Position, time_label = e.TimeLabel, hour = e.Hour, count = e.Count, read = e.Read, toast = e.Toast,
                });
            s.once.AddRange(_onceKeys);
            s.once.Sort(StringComparer.Ordinal);
            return JsonUtility.ToJson(s);
        }

        /// <summary>The list as it was (no toasts pop up for old news).</summary>
        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _nextId = s.next_id;
            _entries.Clear();
            foreach (var e in s.entries)
                _entries.Add(new Notification
                {
                    Id = e.id, Kind = e.kind, Title = e.title, Text = e.text, Key = e.key,
                    Link = NotificationLink.Restore(e.target, e.link_id, e.link_position), TimeLabel = e.time_label, Hour = e.hour,
                    Count = e.count, Read = e.read, Toast = e.toast,
                });
            _onceKeys.Clear();
            foreach (var k in s.once) _onceKeys.Add(k);
            EventBus.Publish(new NotificationsChangedEvent());
        }

        public int Unread
        {
            get
            {
                int n = 0;
                foreach (var e in _entries) if (!e.Read) n++;
                return n;
            }
        }

        /// <summary>
        /// Adds a notification. With a <paramref name="key"/>, a repeat merges into the newest unread entry
        /// of that key (count up, text and time refreshed, the higher urgency kept) instead of adding another.
        /// </summary>
        public Notification Post(NotificationKind kind, string title, string text, string key = null,
                                 NotificationLink link = default, bool toast = true)
        {
            var existing = key == null ? null : FindUnread(key);
            if (existing != null)
            {
                existing.Count++;
                existing.Text = text;
                existing.Title = title;
                if (kind > existing.Kind) existing.Kind = kind;
                if (!link.IsNone) existing.Link = link;
                existing.Toast |= toast;
                Stamp(existing);
                // Newest last: a merged entry counts as the latest news.
                _entries.Remove(existing);
                _entries.Add(existing);
                EventBus.Publish(new NotificationPostedEvent { Notification = existing, Merged = true });
                return existing;
            }

            var n = new Notification
            {
                Id = _nextId++, Kind = kind, Title = title, Text = text, Key = key, Link = link, Toast = toast,
            };
            Stamp(n);
            _entries.Add(n);
            bool trimmed = false;
            while (_entries.Count > MaxEntries) { _entries.RemoveAt(0); trimmed = true; }
            EventBus.Publish(new NotificationPostedEvent { Notification = n, Merged = false });
            if (trimmed) EventBus.Publish(new NotificationsChangedEvent());
            return n;
        }

        /// <summary>Like <see cref="Post"/>, but each key is ever announced once (returns null on later calls).</summary>
        public Notification PostOnce(string key, NotificationKind kind, string title, string text,
                                     NotificationLink link = default, bool toast = true)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("PostOnce needs a key.", nameof(key));
            if (!_onceKeys.Add(key)) return null;
            return Post(kind, title, text, key, link, toast);
        }

        public void MarkRead(int id)
        {
            var n = _entries.Find(e => e.Id == id);
            if (n == null || n.Read) return;
            n.Read = true;
            EventBus.Publish(new NotificationsChangedEvent());
        }

        public void MarkAllRead()
        {
            bool any = false;
            foreach (var e in _entries)
            {
                if (e.Read) continue;
                e.Read = true;
                any = true;
            }
            if (any) EventBus.Publish(new NotificationsChangedEvent());
        }

        /// <summary>The subject settled (quest taken, raid over): its unread entries no longer need attention.</summary>
        public void Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            bool any = false;
            foreach (var e in _entries)
            {
                if (e.Key != key || e.Read) continue;
                e.Read = true;
                any = true;
            }
            if (any) EventBus.Publish(new NotificationsChangedEvent());
        }

        /// <summary>Empties the list. Keys already announced by <see cref="PostOnce"/> stay announced.</summary>
        public void Clear()
        {
            if (_entries.Count == 0) return;
            _entries.Clear();
            EventBus.Publish(new NotificationsChangedEvent());
        }

        Notification FindUnread(string key)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Key == key && !_entries[i].Read) return _entries[i];
            return null;
        }

        void Stamp(Notification n)
        {
            n.Hour = _hourNow?.Invoke() ?? 0f;
            n.TimeLabel = _timeLabel?.Invoke() ?? "";
        }
    }
}
