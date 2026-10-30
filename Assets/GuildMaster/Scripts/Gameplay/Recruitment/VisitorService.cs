using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using GuildMaster.Core;
using GuildMaster.World;
using UnityEngine;

namespace GuildMaster.Recruitment
{
    public struct VisitorArrivedEvent { public Adventurer Visitor; }

    /// <summary>
    /// Spawns recruitable visitors at a town gate every 6–8 game hours. They stay on the plaza
    /// for 12 hours (longer while talking) and then leave (see VisitorStates). The player can also
    /// pay for a recruitment notice to bring the next one sooner.
    /// </summary>
    public class VisitorService : MonoBehaviour, ISaveable, ISaveLink, ISaveResume
    {
        public const float StayHours = 12f;
        // P4-38 (was 12–20): hunting in parties needs a bigger roster, and deaths have to be
        // replaceable; this brings roughly two candidates a day. P4-68 (was 8–12, user decision
        // 2026-09-27): the 5-day run lost two of four members and recruited none; about three a day.
        const float MinIntervalHours = 6f;
        const float MaxIntervalHours = 8f;
        const int MaxConcurrent = 2;

        /// <summary>Gold a recruitment notice costs (P4-38, user decision 2026-09-21).</summary>
        public const int NoticeCost = 80;
        /// <summary>Game hours after posting a notice within which an applicant turns up.</summary>
        public const float NoticeMinHours = 1f;
        public const float NoticeMaxHours = 3f;

        readonly List<Adventurer> _visitors = new();
        /// <summary>Visitors who came as refugees from another village (P4-84): outside the cap, no sight of their own until they join.</summary>
        readonly HashSet<string> _refugees = new();
        GameClock _clock;
        WorldService _world;
        float _nextVisitHour;
        /// <summary>Game hour the applicant of a posted notice arrives; negative when none is posted.</summary>
        float _noticeHour = -1f;

        public IReadOnlyList<Adventurer> Visitors => _visitors;
        public bool NoticePosted => _noticeHour >= 0f;

        /// <summary>Why a notice cannot be posted right now, or null when it can (shown on the button).</summary>
        public string CannotPostNoticeReason()
        {
            if (NoticePosted) return "지원자가 오는 중입니다";
            if (Services.TryGet<Economy.GuildTreasury>(out var t) && !t.CanAfford(NoticeCost)) return "골드가 부족합니다";
            return null;
        }

        /// <summary>Pays for a notice; an applicant arrives within a few game hours.</summary>
        public bool PostNotice()
        {
            if (CannotPostNoticeReason() != null) return false;
            if (!Services.TryGet<Economy.GuildTreasury>(out var t) || !t.TrySpendGold(NoticeCost, Economy.GoldReason.Recruit)) return false;
            _noticeHour = _clock.TotalHours + GameRandom.Shared.Range(NoticeMinHours, NoticeMaxHours);
            if (Services.TryGet<Session.GameLog>(out var log))
                log.Add($"모집 공고를 붙였습니다(-{NoticeCost}G). 곧 지원자가 찾아옵니다.", Session.LogCategory.Economy);
            return true;
        }

        // ---------------- Save (P5-4) ----------------

        [System.Serializable]
        class SaveData
        {
            public float next_visit;
            public float notice;
            public List<Adventurer.AdventurerSave> visitors = new();
            public List<string> refugees = new();
        }

        readonly List<(Adventurer visitor, Adventurer.AdventurerSave saved)> _restoring = new();

        public string SaveKey => "visitors";

        /// <summary>A talk in progress is not saved: the visitor is back on the plaza, ready to be spoken to.</summary>
        public string Capture()
        {
            var s = new SaveData { next_visit = _nextVisitHour, notice = _noticeHour };
            foreach (var v in _visitors) if (v != null && v.IsAlive) s.visitors.Add(v.CaptureSave());
            foreach (var v in _visitors) if (v != null && v.IsAlive && _refugees.Contains(v.Data.id)) s.refugees.Add(v.Data.id);
            s.refugees.Sort(System.StringComparer.Ordinal);
            return JsonUtility.ToJson(s);
        }

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _nextVisitHour = s.next_visit;
            _noticeHour = s.notice;
            _restoring.Clear();
            _refugees.Clear();
            foreach (var id in s.refugees) _refugees.Add(id);
            foreach (var saved in s.visitors)
            {
                var v = Adventurer.RestoreSave(saved, transform, ctx);
                if (_refugees.Contains(v.Data.id)) Unsighted(v, true);
                _visitors.Add(v);
                _restoring.Add((v, saved));
            }
        }

        /// <summary>
        /// One of another village's people (P4-84: a candidate, a refugee) is not the guild's eyes and hides in the fog like a
        /// monster; joining the guild gives it both back — shown at once, wherever it stands.
        /// </summary>
        public static void Unsighted(Adventurer a, bool on)
        {
            if (a.TryGetComponent<Fog.VisionSource>(out var eyes)) eyes.enabled = !on;
            bool hidden = a.TryGetComponent<Fog.FogHideable>(out var hide);
            if (on && !hidden) a.gameObject.AddComponent<Fog.FogHideable>();
            else if (!on && hidden)
            {
                hide.SetFogVisible(true);
                DestroyImmediate(hide);
            }
        }

        public void Link(SaveContext ctx)
        {
            foreach (var (v, saved) in _restoring) v.LinkSave(saved, ctx);
        }

        public void Resume(SaveContext ctx)
        {
            foreach (var (v, saved) in _restoring) v.ResumeSave(saved, ctx);
            _restoring.Clear();
        }

        public void Init(GameClock clock, WorldService world)
        {
            _clock = clock;
            _world = world;
            // First visitor comes early so the feature is discoverable.
            _nextVisitHour = clock.TotalHours + GameRandom.Shared.Range(3f, 6f);
            EventBus.Subscribe<VisitorLeftTownEvent>(OnLeft);
            EventBus.Subscribe<AdventurerJoinedEvent>(OnJoined);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<VisitorLeftTownEvent>(OnLeft);
            EventBus.Unsubscribe<AdventurerJoinedEvent>(OnJoined);
        }

        void OnLeft(VisitorLeftTownEvent e)
        {
            _visitors.Remove(e.Visitor);
            if (e.Visitor != null) _refugees.Remove(e.Visitor.Data.id);
        }

        void OnJoined(AdventurerJoinedEvent e)
        {
            _visitors.Remove(e.Adventurer);
            if (e.Adventurer != null && _refugees.Remove(e.Adventurer.Data.id)) Unsighted(e.Adventurer, false);
        }

        void Update()
        {
            if (_clock == null) return;
            // A paid notice always brings its applicant, whoever else is already in town.
            if (NoticePosted && _clock.TotalHours >= _noticeHour)
            {
                _noticeHour = -1f;
                SpawnVisitor();
            }
            if (_clock.TotalHours < _nextVisitHour) return;
            // A known guild draws them more often (P4-106).
            _nextVisitHour = _clock.TotalHours + GameRandom.Shared.Range(MinIntervalHours, MaxIntervalHours) * Guild.GuildRankService.RankVisitInterval;
            _visitors.RemoveAll(v => v == null);
            if (RegularVisitors >= MaxConcurrent) return;
            SpawnVisitor();
        }

        /// <summary>
        /// A refugee from another village on its way here (P4-84): kept and saved with the visitors from now on, but not
        /// counted against <see cref="MaxConcurrent"/> (user decision 2026-10-18: they come whoever else is in town).
        /// </summary>
        public void Adopt(Adventurer refugee)
        {
            if (refugee == null || _visitors.Contains(refugee)) return;
            refugee.transform.SetParent(transform, true);
            _visitors.Add(refugee);
            _refugees.Add(refugee.Data.id);
        }

        /// <summary>Visitors who count against the cap: everyone but refugees.</summary>
        int RegularVisitors => _visitors.Count(v => v != null && !_refugees.Contains(v.Data.id));

        /// <summary>The next visitor comes within <paramref name="hours"/> (the storyteller, for a guild near its end — P4-111).</summary>
        public void CallSoon(float hours) => _nextVisitHour = Mathf.Min(_nextVisitHour, _clock.TotalHours + hours);

        /// <summary>Spawns a visitor at a random gate now (also used by debug commands).</summary>
        public Adventurer SpawnVisitor()
        {
            var rng = GameRandom.Shared;
            // A higher guild hall attracts more experienced visitors (P4-1), and so does the guild's rank (P4-106: both).
            int bonus = (Services.TryGet<Town.UpgradeService>(out var upgrades) ? upgrades.VisitorLevelBonus : 0) + Guild.GuildRankService.RankVisitorBonus;
            int level = Mathf.Max(1, 1 + (_clock.Day - 1) / 4 + rng.Range(-1, 2) + bonus);
            var data = AdventurerGenerator.Generate(rng, level);
            var gates = _world.Map.Gates;
            var cell = gates.Count > 0 ? gates[rng.Range(0, gates.Count)] : _world.Map.TownCenter;
            var visitor = Adventurer.Create(data, GridMap.CellToWorld(cell), transform);
            visitor.BecomeVisitor(_clock.TotalHours + StayHours);
            _visitors.Add(visitor);
            EventBus.Publish(new VisitorArrivedEvent { Visitor = visitor });
            return visitor;
        }
    }
}
