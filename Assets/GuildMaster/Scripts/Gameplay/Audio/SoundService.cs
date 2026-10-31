using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Fog;
using GuildMaster.Incidents;
using GuildMaster.Monsters;
using GuildMaster.Notifications;
using GuildMaster.Player;
using GuildMaster.Quests;
using GuildMaster.Recruitment;
using GuildMaster.Session;
using GuildMaster.Units;
using UnityEngine;

namespace GuildMaster.Audio
{
    /// <summary>Clip names under Resources/Audio/Sfx (and the town loop under Resources/Audio/Music).</summary>
    public static class SoundKeys
    {
        public const string Click = "ui_click", Open = "ui_open", Close = "ui_close", Error = "ui_error", Notify = "notify";
        public const string CoinGain = "coin_gain", CoinSpend = "coin_spend";
        public const string HitSword = "hit_sword", HitBlunt = "hit_blunt", Arrow = "arrow_shot", HitMagic = "hit_magic";
        public const string Cast = "magic_cast", MonsterDie = "monster_die", PersonDown = "person_down";
        public const string LevelUp = "level_up", Heal = "heal", QuestComplete = "quest_complete";
        public const string RecruitSuccess = "recruit_success", RecruitFail = "recruit_fail", EventCard = "event_card";
        public const string DayStart = "day_start", NightFall = "night_fall";
        public const string TownMusic = "bgm_town_loop";
    }

    /// <summary>Which sound an event makes, how loud, and how often it may repeat (pure, tested).</summary>
    public static class SoundRules
    {
        /// <summary>The sound of a blow: by the attacker's class; monsters thump, the guild master slashes.</summary>
        public static string HitKey(bool attackerIsMonster, AdventurerClass? attackerClass)
        {
            if (attackerIsMonster) return SoundKeys.HitBlunt;
            return attackerClass switch
            {
                AdventurerClass.Ranger => SoundKeys.Arrow,
                AdventurerClass.Mage => SoundKeys.HitMagic,
                AdventurerClass.Priest => SoundKeys.HitBlunt,
                _ => SoundKeys.HitSword,
            };
        }

        /// <summary>Gold in jingles up, gold out clinks down; nothing for zero.</summary>
        public static string GoldKey(int amount) => amount > 0 ? SoundKeys.CoinGain : amount < 0 ? SoundKeys.CoinSpend : null;

        /// <summary>Morning bells at 6:00, evening bells at 20:00 (the clock's day/night edges).</summary>
        public static string HourKey(int hour) => hour == 6 ? SoundKeys.DayStart : hour == 20 ? SoundKeys.NightFall : null;

        /// <summary>Shortest gap (real seconds) between two plays of one sound, so a busy fight or x8 speed stays readable.</summary>
        public static float MinGap(string key) => key switch
        {
            SoundKeys.HitSword or SoundKeys.HitBlunt or SoundKeys.Arrow or SoundKeys.HitMagic => 0.07f,
            SoundKeys.Click => 0.03f,
            SoundKeys.Heal or SoundKeys.Cast or SoundKeys.MonsterDie => 0.25f,
            SoundKeys.CoinGain or SoundKeys.CoinSpend => 0.35f,
            SoundKeys.Notify => 1.2f,
            SoundKeys.RecruitSuccess or SoundKeys.RecruitFail or SoundKeys.QuestComplete => 1.5f,
            _ => 0.15f,
        };

        /// <summary>Mix level per sound (before the player's effects volume).</summary>
        public static float Level(string key) => key switch
        {
            SoundKeys.HitSword or SoundKeys.HitBlunt or SoundKeys.Arrow or SoundKeys.HitMagic => 0.55f,
            SoundKeys.MonsterDie or SoundKeys.Cast or SoundKeys.Heal => 0.65f,
            SoundKeys.Click or SoundKeys.Open or SoundKeys.Close => 0.6f,
            _ => 0.85f,
        };
    }

    /// <summary>Remembers when each sound last played.</summary>
    public class SoundThrottle
    {
        readonly Dictionary<string, float> _last = new();

        public bool Allow(string key, float now)
        {
            if (_last.TryGetValue(key, out var t) && now - t < SoundRules.MinGap(key)) return false;
            _last[key] = now;
            return true;
        }
    }

    /// <summary>Call sites outside the gameplay systems (UI buttons and windows) play sounds through here.</summary>
    public static class Sound
    {
        public static void Play(string key, float volume = 1f)
        {
            if (Services.TryGet<SoundService>(out var s)) s.Play(key, volume);
        }

        public static void Click() => Play(SoundKeys.Click);
    }

    /// <summary>
    /// Sound effects and the town music (P4-9, 2026-10-31). Clips are the generated set under Resources/Audio; when they
    /// are missing the game is simply silent. World sounds only play for what the player can see (fog and camera), with
    /// a minimum gap per sound so fights and x8 speed don't turn into noise. Volumes come from <see cref="GameSettings"/>.
    /// </summary>
    public class SoundService : MonoBehaviour
    {
        const int Voices = 8;
        const float MusicLevel = 0.55f;

        readonly Dictionary<string, AudioClip> _clips = new();
        readonly SoundThrottle _throttle = new();
        readonly List<AudioSource> _voices = new();
        AudioSource _music;
        int _next;

        public bool HasClips => _clips.Count > 0;

        public void Init()
        {
            foreach (var clip in Resources.LoadAll<AudioClip>("Audio/Sfx")) _clips[clip.name] = clip;
            foreach (var clip in Resources.LoadAll<AudioClip>("Audio/Music")) _clips[clip.name] = clip;
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            for (int i = 0; i < Voices; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                _voices.Add(src);
            }
            _music = gameObject.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.loop = true;
            _music.spatialBlend = 0f;
            GameSettings.VolumeChanged += ApplyVolumes;
            ApplyVolumes();
            if (_clips.TryGetValue(SoundKeys.TownMusic, out var town))
            {
                _music.clip = town;
                _music.Play();
            }

            EventBus.Subscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Subscribe<UnitDiedEvent>(OnUnitDied);
            EventBus.Subscribe<UnitHealedEvent>(OnHealed);
            EventBus.Subscribe<SkillUsedEvent>(OnSkill);
            EventBus.Subscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Subscribe<AdventurerDiedEvent>(OnAdventurerDied);
            EventBus.Subscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Subscribe<PlayerDownedEvent>(OnPlayerDowned);
            EventBus.Subscribe<GoldFlowEvent>(OnGold);
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
            EventBus.Subscribe<RecruitmentEndedEvent>(OnRecruitEnded);
            EventBus.Subscribe<EventCardDrawnEvent>(OnEventCard);
            EventBus.Subscribe<NotificationPostedEvent>(OnNotification);
            EventBus.Subscribe<HourPassedEvent>(OnHour);
        }

        void OnDestroy()
        {
            GameSettings.VolumeChanged -= ApplyVolumes;
            EventBus.Unsubscribe<UnitDamagedEvent>(OnDamaged);
            EventBus.Unsubscribe<UnitDiedEvent>(OnUnitDied);
            EventBus.Unsubscribe<UnitHealedEvent>(OnHealed);
            EventBus.Unsubscribe<SkillUsedEvent>(OnSkill);
            EventBus.Unsubscribe<AdventurerLevelUpEvent>(OnLevelUp);
            EventBus.Unsubscribe<AdventurerDiedEvent>(OnAdventurerDied);
            EventBus.Unsubscribe<AdventurerJoinedEvent>(OnJoined);
            EventBus.Unsubscribe<PlayerDownedEvent>(OnPlayerDowned);
            EventBus.Unsubscribe<GoldFlowEvent>(OnGold);
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
            EventBus.Unsubscribe<RecruitmentEndedEvent>(OnRecruitEnded);
            EventBus.Unsubscribe<EventCardDrawnEvent>(OnEventCard);
            EventBus.Unsubscribe<NotificationPostedEvent>(OnNotification);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
        }

        void ApplyVolumes()
        {
            if (_music != null) _music.volume = GameSettings.MusicVolume * MusicLevel;
        }

        /// <summary>Plays a clip once (throttled per clip). Combat sounds get a slight random pitch.</summary>
        public void Play(string key, float volume = 1f)
        {
            if (string.IsNullOrEmpty(key) || !_clips.TryGetValue(key, out var clip)) return;
            float sfx = GameSettings.SfxVolume;
            if (sfx <= 0.001f || !_throttle.Allow(key, Time.unscaledTime)) return;
            var src = FreeVoice();
            src.pitch = key.StartsWith("hit_") || key == SoundKeys.Arrow ? Random.Range(0.93f, 1.07f) : 1f;
            src.PlayOneShot(clip, Mathf.Clamp01(volume * SoundRules.Level(key) * sfx));
        }

        /// <summary>Round robin over a few sources, so a pitch change for one shot doesn't bend the others.</summary>
        AudioSource FreeVoice()
        {
            var v = _voices[_next];
            _next = (_next + 1) % _voices.Count;
            return v;
        }

        // ---------------- Events ----------------

        void OnDamaged(UnitDamagedEvent e)
        {
            if (e.Target == null || e.Amount <= 0f || !Seen(e.Target.Position)) return;
            var cls = e.Source is Adventurer a && a.Data != null ? a.Data.cls : (AdventurerClass?)null;
            Play(SoundRules.HitKey(e.Source is Monster, cls), e.Critical ? 1f : 0.75f);
        }

        void OnUnitDied(UnitDiedEvent e)
        {
            if (e.Victim is Monster m && Seen(m.Position)) Play(SoundKeys.MonsterDie);
        }

        void OnHealed(UnitHealedEvent e)
        {
            if (e.Target != null && e.Amount > 0f && Seen(e.Target.Position)) Play(SoundKeys.Heal);
        }

        void OnSkill(SkillUsedEvent e)
        {
            if (e.User != null && Seen(e.User.Position)) Play(SoundKeys.Cast);
        }

        void OnLevelUp(AdventurerLevelUpEvent e) => Play(SoundKeys.LevelUp);
        void OnAdventurerDied(AdventurerDiedEvent e) => Play(SoundKeys.PersonDown);
        void OnPlayerDowned(PlayerDownedEvent e) => Play(SoundKeys.PersonDown);
        void OnJoined(AdventurerJoinedEvent e) => Play(SoundKeys.RecruitSuccess);
        void OnGold(GoldFlowEvent e) => Play(SoundRules.GoldKey(e.Amount), 0.8f);
        void OnQuestCompleted(QuestCompletedEvent e) => Play(SoundKeys.QuestComplete);
        void OnEventCard(EventCardDrawnEvent e) => Play(SoundKeys.EventCard);
        void OnHour(HourPassedEvent e) => Play(SoundRules.HourKey(e.Hour), 0.8f);

        void OnRecruitEnded(RecruitmentEndedEvent e)
        {
            if (e.Outcome == RecruitOutcome.Joined) Play(SoundKeys.RecruitSuccess);
            else if (e.Outcome == RecruitOutcome.Left) Play(SoundKeys.RecruitFail);
        }

        void OnNotification(NotificationPostedEvent e)
        {
            if (!e.Merged) Play(SoundKeys.Notify, 0.7f);
        }

        static bool Seen(Vector2 p)
        {
            if (Services.TryGet<FogOfWar>(out var fog) && !fog.IsVisible(p)) return false;
            if (!Services.TryGet<CameraController>(out var camCtl) || camCtl.Camera == null) return true;
            var cam = camCtl.Camera;
            float hh = cam.orthographicSize + 1f, hw = hh * cam.aspect + 1f;
            var d = p - (Vector2)cam.transform.position;
            return Mathf.Abs(d.x) <= hw && Mathf.Abs(d.y) <= hh;
        }
    }
}
