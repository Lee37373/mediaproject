using System;
using System.Collections.Generic;
using System.IO;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Session;
using UnityEngine;

namespace GuildMaster.Save
{
    /// <summary>Published once a saved game has been put back (after every section restored and linked).</summary>
    public struct GameLoadedEvent { public int Slot; public int Warnings; }

    /// <summary>What a slot holds, for the save/load window.</summary>
    public struct SlotInfo
    {
        public int Slot;
        public bool Exists;
        /// <summary>The file is there but cannot be used (unreadable or from an incompatible version).</summary>
        public string Problem;
        public SaveSummary Summary;
        public DateTime SavedAt;
    }

    /// <summary>
    /// Save and load (P5-4, user decision 2026-10-04: a full snapshot, manual saves, several slots).
    /// Systems register themselves in the composition root's order; a save asks each for its section,
    /// a load rebuilds the world from the seed (<see cref="GameSettings.PendingLoad"/>) and hands each
    /// section back: first <see cref="ISaveable.Restore"/> for all, then <see cref="ISaveLink.Link"/>.
    /// </summary>
    public class SaveService
    {
        public const int SlotCount = 5;

        readonly List<ISaveable> _saveables = new();
        readonly int _seed;

        public SaveService(int seed) { _seed = seed; }

        /// <summary>Slot the current game was loaded from or last saved to (-1 = none).</summary>
        public int CurrentSlot { get; private set; } = -1;

        public IReadOnlyList<ISaveable> Saveables => _saveables;

        // ---------------- What leaving would lose (P6-1) ----------------

        /// <summary>Game hour (<see cref="GameClock.TotalHours"/>) of the last save or load, or of the start of a
        /// game never saved (NaN until the composition root marks it — then nothing counts as unsaved).</summary>
        public float CleanAtHours { get; private set; } = float.NaN;
        /// <summary>Real time of the last save or load (null: this game was neither saved nor loaded yet).</summary>
        public DateTime? CleanAtReal { get; private set; }
        /// <summary>The game was loaded (true) rather than saved (false) at <see cref="CleanAtReal"/>.</summary>
        public bool CleanByLoad { get; private set; }

        /// <summary>The composition root marks a fresh game's first hour: nothing to lose yet.</summary>
        public void MarkStart(float totalHours)
        {
            CleanAtHours = totalHours;
            CleanAtReal = null;
            CleanByLoad = false;
        }

        /// <summary>Game hours played since the last save or load (or the start) — 0 when leaving loses nothing.</summary>
        public float UnsavedHours(float nowHours) => float.IsNaN(CleanAtHours) ? 0f : Mathf.Max(0f, nowHours - CleanAtHours);

        void MarkClean(float totalHours, bool byLoad)
        {
            CleanAtHours = totalHours;
            CleanAtReal = DateTime.Now;
            CleanByLoad = byLoad;
        }

        public void Register(ISaveable saveable)
        {
            if (saveable == null) return;
            if (_saveables.Exists(s => s.SaveKey == saveable.SaveKey))
                throw new InvalidOperationException("save key used twice: " + saveable.SaveKey);
            _saveables.Add(saveable);
        }

        // ---------------- Saving ----------------

        public SaveFile Capture()
        {
            var file = new SaveFile { seed = _seed, saved_at = DateTime.UtcNow.ToString("o") };
            if (Services.TryGet<GameClock>(out var clock))
            {
                file.summary.day = clock.Day;
                file.summary.hour = clock.Hour;
                file.summary.total_hours = clock.TotalHours;
            }
            if (Services.TryGet<GuildTreasury>(out var treasury)) file.summary.gold = treasury.Gold;
            if (Services.TryGet<GuildRoster>(out var roster)) file.summary.members = roster.Members.Count;
            foreach (var s in _saveables) file.sections.Add(new SaveSection { key = s.SaveKey, json = s.Capture() });
            return file;
        }

        public static string Folder => FolderOverride ?? Path.Combine(Application.persistentDataPath, "saves");

        /// <summary>Tests write their slots elsewhere, never over the player's saves.</summary>
        public static string FolderOverride { get; set; }
        public static string PathOf(int slot) => Path.Combine(Folder, $"slot{slot}.json");

        /// <summary>Writes the game to a slot (a temporary file first, so a failed write keeps the old save).</summary>
        public bool Save(int slot, out string error)
        {
            error = null;
            if (slot < 0 || slot >= SlotCount) { error = "슬롯 번호가 잘못되었습니다"; return false; }
            try
            {
                var file = Capture();
                var text = JsonUtility.ToJson(file);
                Directory.CreateDirectory(Folder);
                string path = PathOf(slot), temp = path + ".tmp";
                File.WriteAllText(temp, text);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                CurrentSlot = slot;
                MarkClean(file.summary.total_hours, byLoad: false);
                return true;
            }
            catch (Exception e)
            {
                error = "저장하지 못했습니다: " + e.Message;
                Debug.LogWarning("[Save] " + error);
                return false;
            }
        }

        // ---------------- Reading slots ----------------

        public static bool TryRead(int slot, out SaveFile file, out string error)
        {
            file = null;
            error = null;
            string path = PathOf(slot);
            if (!File.Exists(path)) { error = "빈 슬롯입니다"; return false; }
            try { file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path)); }
            catch (Exception e) { error = "파일을 읽을 수 없습니다: " + e.Message; return false; }
            if (file == null || file.sections == null) { error = "파일이 손상되었습니다"; return false; }
            if (file.version != SaveFile.CurrentVersion)
            {
                error = file.version > SaveFile.CurrentVersion ? "더 새 버전에서 만든 저장입니다" : "예전 버전의 저장이라 불러올 수 없습니다";
                return false;
            }
            return true;
        }

        public static SlotInfo Peek(int slot)
        {
            var info = new SlotInfo { Slot = slot, Exists = File.Exists(PathOf(slot)) };
            if (!info.Exists) return info;
            if (!TryRead(slot, out var file, out var error)) { info.Problem = error; return info; }
            info.Summary = file.summary;
            if (DateTime.TryParse(file.saved_at, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at)) info.SavedAt = at.ToLocalTime();
            return info;
        }

        public static bool Delete(int slot)
        {
            try
            {
                if (!File.Exists(PathOf(slot))) return false;
                File.Delete(PathOf(slot));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] delete failed: " + e.Message);
                return false;
            }
        }

        // ---------------- Loading ----------------

        /// <summary>Starts the saved game: the scene restarts with the save's seed and the save handed over.</summary>
        public static bool Load(int slot, out string error)
        {
            if (!TryRead(slot, out var file, out error)) return false;
            Hand(file, slot);
            GameSettings.ReloadScene(showTitle: false);
            return true;
        }

        /// <summary>Hands a save to the next boot (the game reloads the scene; tests recreate the roots).</summary>
        public static void Hand(SaveFile file, int slot = -1)
        {
            GameSettings.PendingLoad = file;
            GameSettings.PendingLoadSlot = slot;
            GameSettings.PendingSeed = file.seed;
        }

        /// <summary>
        /// Puts every section back (called by the composition root once every system exists).
        /// A section without a system is skipped, a system without a section stays as it booted;
        /// both are noted. Returns the warnings.
        /// </summary>
        public List<string> Restore(SaveFile file, int slot)
        {
            var ctx = new SaveContext(file.version);
            var restored = new List<ISaveable>();
            foreach (var s in _saveables)
            {
                var section = file.Find(s.SaveKey);
                if (section == null) { ctx.Warnings.Add("no section " + s.SaveKey); continue; }
                try
                {
                    s.Restore(section.json, ctx);
                    restored.Add(s);
                }
                catch (Exception e)
                {
                    ctx.Warnings.Add($"{s.SaveKey}: {e.Message}");
                    Debug.LogException(e);
                }
            }
            foreach (var section in file.sections)
                if (!_saveables.Exists(s => s.SaveKey == section.key)) ctx.Warnings.Add("unknown section " + section.key);
            foreach (var s in restored)
            {
                if (s is not ISaveLink link) continue;
                try { link.Link(ctx); }
                catch (Exception e)
                {
                    ctx.Warnings.Add($"{s.SaveKey} link: {e.Message}");
                    Debug.LogException(e);
                }
            }
            foreach (var s in restored)
            {
                if (s is not ISaveResume resume) continue;
                try { resume.Resume(ctx); }
                catch (Exception e)
                {
                    ctx.Warnings.Add($"{s.SaveKey} resume: {e.Message}");
                    Debug.LogException(e);
                }
            }
            // A fresh stream rather than the seed's first numbers again (replay is not promised).
            GameRandom.Shared = new GameRandom(unchecked(file.seed * 31 + Mathf.RoundToInt(file.summary.total_hours * 60f)));
            CurrentSlot = slot;
            MarkClean(Services.TryGet<GameClock>(out var clock) ? clock.TotalHours : file.summary.total_hours, byLoad: true);
            foreach (var w in ctx.Warnings) Debug.LogWarning("[Save] " + w);
            EventBus.Publish(new GameLoadedEvent { Slot = slot, Warnings = ctx.Warnings.Count });
            return ctx.Warnings;
        }
    }
}
