using System.Collections.Generic;
using GuildMaster.Adventurers;
using GuildMaster.CameraControl;
using GuildMaster.Core;
using GuildMaster.Economy;
using GuildMaster.Fog;
using GuildMaster.Dilemmas;
using GuildMaster.Monsters;
using GuildMaster.Notifications;
using GuildMaster.Player;
using GuildMaster.Quests;
using GuildMaster.Recruitment;
using GuildMaster.Seasons;
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
        /// <summary>An event card's result (2026-11-01): a success or a good turn, a failure or a bad one, or plain.</summary>
        public const string CardSuccess = "card_success", CardFail = "card_fail", CardResult = "card_result";
        public const string TownMusic = "bgm_town_loop";
        /// <summary>The odd-job minigames (2026-11-11): the anvil's "깡~" by how well the hammer landed, the glancing
        /// "팅~", the quench; herb cards; dough slaps; and the result jingles.</summary>
        public const string ForgePerfect = "forge_perfect", ForgeGood = "forge_good", ForgeWeak = "forge_weak", ForgeMiss = "forge_miss";
        public const string ForgeQuench = "forge_quench", ForgeStart = "forge_start";
        public const string HerbFlip = "herb_flip", HerbMatch = "herb_match", HerbMiss = "herb_miss";
        public const string DoughPerfect = "dough_perfect", DoughGood = "dough_good", DoughMiss = "dough_miss";
        public const string ChoreGreat = "chore_great", ChoreOk = "chore_ok", ChoreBad = "chore_bad";

        /// <summary>The season's day or night tune (2026-11-01), e.g. "bgm_winter_night".</summary>
        public static string Music(Season season, bool day) => $"bgm_{season.ToString().ToLowerInvariant()}_{(day ? "day" : "night")}";
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

        /// <summary>The sound of a decision card's result (P4-118, from the event cards): like the recruit talk's, success and failure each have one.</summary>
        public static string CardKey(DilemmaTone tone) => DilemmaRules.IsGood(tone) ? SoundKeys.CardSuccess
            : DilemmaRules.IsBad(tone) ? SoundKeys.CardFail : SoundKeys.CardResult;

        /// <summary>A hammer blow or a press in the odd jobs (2026-11-11), by the game's quality (0 or less = a miss).</summary>
        public static string ChoreHitKey(ChoreKind kind, float quality) => kind == ChoreKind.Forge
            ? quality >= 1f ? SoundKeys.ForgePerfect : quality >= 0.6f ? SoundKeys.ForgeGood : quality > 0f ? SoundKeys.ForgeWeak : SoundKeys.ForgeMiss
            : quality >= 1f ? SoundKeys.DoughPerfect : quality > 0f ? SoundKeys.DoughGood : SoundKeys.DoughMiss;

        /// <summary>The jingle at the end of an odd job: 좋음 and up, 그럭저럭, 엉망 (the grade's edges).</summary>
        public static string ChoreResultKey(float score) => score >= 0.6f ? SoundKeys.ChoreGreat : score >= 0.3f ? SoundKeys.ChoreOk : SoundKeys.ChoreBad;

        /// <summary>The sound a clip is a take of: "hit_sword_v3" → "hit_sword"; other names are their own sound.</summary>
        public static string TakeOf(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return clipName;
            int i = clipName.LastIndexOf("_v", System.StringComparison.Ordinal);
            if (i <= 0 || i + 2 >= clipName.Length) return clipName;
            for (int k = i + 2; k < clipName.Length; k++) if (!char.IsDigit(clipName[k])) return clipName;
            return clipName.Substring(0, i);
        }

        /// <summary>Day music from 6:00, night music from 20:00: the same edges as the clock's night and the bells.</summary>
        public const int DayFrom = 6, NightFrom = 20;

        public static bool IsDayHour(int hour) => hour >= DayFrom && hour < NightFrom;

        /// <summary>The tune for this season and hour (user request 2026-11-01: "낮과 밤 계절에 따른 bgm").</summary>
        public static string MusicKey(Season season, int hour) => SoundKeys.Music(season, IsDayHour(hour));

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
            SoundKeys.CardSuccess or SoundKeys.CardFail or SoundKeys.CardResult => 1.0f,
            SoundKeys.HerbFlip or SoundKeys.DoughGood or SoundKeys.DoughPerfect or SoundKeys.DoughMiss => 0.05f,
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

    /// <summary>
    /// One tune giving way to the next (user decision 2026-11-01: fade out, a short pause, fade in — two tunes never
    /// play over each other). Real seconds, so a paused game or x8 speed changes music the same. Pure, tested.
    /// </summary>
    public class MusicFader
    {
        public const float FadeOutSeconds = 3f, PauseSeconds = 1.5f, FadeInSeconds = 3f;

        enum Phase { Silent, In, Playing, Out, Pause }
        Phase _phase = Phase.Silent;
        float _pause;

        /// <summary>The tune sounding now (null in the pause between two tunes).</summary>
        public string Current { get; private set; }
        /// <summary>The tune waiting for the current one to fade out.</summary>
        public string Pending { get; private set; }
        /// <summary>Volume 0..1 before the player's music volume.</summary>
        public float Level { get; private set; }

        /// <summary>Asks for a tune. Returns the tune to start right now, or null (it starts later, or is already on).</summary>
        public string Request(string key, bool instant = false)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (key == Current && Pending == null) return null;
            if (key == Pending) return null;
            if (instant || Current == null && _phase != Phase.Pause)
            {
                Current = key;
                Pending = null;
                Level = instant ? 1f : 0f;
                _phase = instant ? Phase.Playing : Phase.In;
                return key;
            }
            if (key == Current) // changed its mind while fading out: back up
            {
                Pending = null;
                _phase = Phase.In;
                return null;
            }
            Pending = key;
            if (_phase != Phase.Pause) _phase = Phase.Out;
            return null;
        }

        /// <summary>Moves the fade on. Returns a tune to start now (after the pause), else null.</summary>
        public string Tick(float dt)
        {
            switch (_phase)
            {
                case Phase.In:
                    Level = Mathf.Min(1f, Level + dt / FadeInSeconds);
                    if (Level >= 1f) _phase = Phase.Playing;
                    break;
                case Phase.Out:
                    Level = Mathf.Max(0f, Level - dt / FadeOutSeconds);
                    if (Level <= 0f)
                    {
                        Current = null;
                        _pause = PauseSeconds;
                        _phase = Phase.Pause;
                    }
                    break;
                case Phase.Pause:
                    _pause -= dt;
                    if (_pause <= 0f && Pending != null)
                    {
                        Current = Pending;
                        Pending = null;
                        Level = 0f;
                        _phase = Phase.In;
                        return Current;
                    }
                    break;
            }
            return null;
        }
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
    /// Candidate hit sounds to compare while playing (2026-11-12, the team: "타격 사운드가 좀 아쉽다"). Four sets under
    /// Resources/Audio/HitSets/{A,B,C,D}, each with hit_sword / hit_blunt / arrow_shot / hit_magic in a few takes; the
    /// chosen one (kept in PlayerPrefs, picked from the GuildMaster menu) stands in for the hit sounds in Audio/Sfx.
    /// Empty = the sounds the game ships with. Once one is chosen it replaces the files and this goes.
    /// </summary>
    public static class HitSoundSets
    {
        public const string PrefKey = "dev.hit_sound_set";
        public static readonly string[] All = { "", "A", "B", "C", "D" };

        public static string Label(string set) => set switch
        {
            "A" => "A 묵직한 액션",
            "B" => "B 경쾌한 캐주얼",
            "C" => "C 실감 무기",
            "D" => "D 과장된 만화풍",
            _ => "지금 쓰는 소리",
        };

        public static string Current
        {
            get => PlayerPrefs.GetString(PrefKey, "");
            set
            {
                PlayerPrefs.SetString(PrefKey, value ?? "");
                PlayerPrefs.Save();
            }
        }

        /// <summary>The sounds a set replaces: the four blows (by attacker class, see <see cref="SoundRules.HitKey"/>).</summary>
        public static bool IsHitKey(string key) => key == SoundKeys.HitSword || key == SoundKeys.HitBlunt || key == SoundKeys.Arrow
                                                  || key == SoundKeys.HitMagic;
    }

    /// <summary>
    /// Sound effects and the music (P4-9, 2026-10-31). The music follows the season and day/night (2026-11-01): eight
    /// tunes, changed at 6:00, 20:00 and when a season turns by <see cref="MusicFader"/>; the old town loop stands in for
    /// a missing one. Clips are the generated set under Resources/Audio; when they
    /// are missing the game is simply silent. World sounds only play for what the player can see (fog and camera), with
    /// a minimum gap per sound so fights and x8 speed don't turn into noise. Volumes come from <see cref="GameSettings"/>.
    /// </summary>
    public class SoundService : MonoBehaviour
    {
        const int Voices = 8;
        const float MusicLevel = 0.55f;

        readonly Dictionary<string, AudioClip> _clips = new();
        /// <summary>
        /// Takes of one sound: "hit_sword", "hit_sword_v2", ... (2026-11-01, recorded sounds come in several takes);
        /// a play picks one at random, so repeated hits don't sound like a machine.
        /// </summary>
        readonly Dictionary<string, List<AudioClip>> _takes = new();
        readonly SoundThrottle _throttle = new();
        readonly List<AudioSource> _voices = new();
        AudioSource _music;
        readonly MusicFader _fader = new();
        float _musicCheck;
        int _next;

        /// <summary>The tune sounding now (null between two tunes).</summary>
        public string MusicKey => _fader.Current;

        public bool HasClips => _clips.Count > 0;

        public void Init()
        {
            foreach (var clip in Resources.LoadAll<AudioClip>("Audio/Sfx"))
            {
                _clips[clip.name] = clip;
                string key = SoundRules.TakeOf(clip.name);
                if (!_takes.TryGetValue(key, out var list)) _takes[key] = list = new List<AudioClip>();
                list.Add(clip);
            }
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
            PickMusic(instant: true);

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
            EventBus.Subscribe<DilemmaDealtEvent>(OnCardDealt);
            EventBus.Subscribe<DilemmaResolvedEvent>(OnCardResolved);
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
            EventBus.Unsubscribe<DilemmaDealtEvent>(OnCardDealt);
            EventBus.Unsubscribe<DilemmaResolvedEvent>(OnCardResolved);
            EventBus.Unsubscribe<NotificationPostedEvent>(OnNotification);
            EventBus.Unsubscribe<HourPassedEvent>(OnHour);
        }

        void ApplyVolumes()
        {
            if (_music != null) _music.volume = GameSettings.MusicVolume * MusicLevel * _fader.Level;
        }

        /// <summary>Asks for the tune of the season and hour now.</summary>
        void PickMusic(bool instant = false)
        {
            int hour = Services.TryGet<GameClock>(out var clock) ? clock.Hour : 8;
            string want = SoundRules.MusicKey(SeasonService.Now, hour);
            if (!_clips.ContainsKey(want)) want = SoundKeys.TownMusic;
            if (!_clips.ContainsKey(want)) return;
            StartMusic(_fader.Request(want, instant));
            ApplyVolumes();
        }

        void StartMusic(string key)
        {
            if (key == null || !_clips.TryGetValue(key, out var clip)) return;
            _music.Stop();
            _music.clip = clip;
            _music.Play();
        }

        void Update()
        {
            // A loaded or new game may start in another season or at night: look again now and then.
            _musicCheck -= Time.unscaledDeltaTime;
            if (_musicCheck <= 0f) { _musicCheck = 1f; PickMusic(); }
            StartMusic(_fader.Tick(Time.unscaledDeltaTime));
            if (_fader.Current == null && _music.isPlaying) _music.Stop();
            ApplyVolumes();
        }

        readonly Dictionary<string, Dictionary<string, List<AudioClip>>> _hitSets = new();

        /// <summary>The takes of a hit sound in a candidate set (null when the set or the sound is missing).</summary>
        List<AudioClip> CandidateTakes(string set, string key)
        {
            if (string.IsNullOrEmpty(set)) return null;
            if (!_hitSets.TryGetValue(set, out var byKey))
            {
                _hitSets[set] = byKey = new Dictionary<string, List<AudioClip>>();
                foreach (var clip in Resources.LoadAll<AudioClip>("Audio/HitSets/" + set))
                {
                    string k = SoundRules.TakeOf(clip.name);
                    if (!byKey.TryGetValue(k, out var list)) byKey[k] = list = new List<AudioClip>();
                    list.Add(clip);
                }
            }
            return byKey.TryGetValue(key, out var takes) && takes.Count > 0 ? takes : null;
        }

        /// <summary>The four blows twice each, as in a fight (the GuildMaster menu's 들어보기).</summary>
        public void PreviewHits() => StartCoroutine(PreviewRoutine());

        System.Collections.IEnumerator PreviewRoutine()
        {
            foreach (var key in new[] { SoundKeys.HitSword, SoundKeys.HitBlunt, SoundKeys.Arrow, SoundKeys.HitMagic })
                for (int i = 0; i < 3; i++)
                {
                    Play(key, i == 2 ? 1f : 0.75f); // the third one as a critical hit
                    yield return new WaitForSecondsRealtime(i == 2 ? 0.7f : 0.38f);
                }
        }

        /// <summary>Plays a clip once (throttled per clip). Combat sounds get a slight random pitch.</summary>
        public void Play(string key, float volume = 1f)
        {
            if (string.IsNullOrEmpty(key)) return;
            var takes = HitSoundSets.IsHitKey(key) ? CandidateTakes(HitSoundSets.Current, key) : null;
            if (takes == null && (!_takes.TryGetValue(key, out takes) || takes.Count == 0)) return;
            var clip = takes[Random.Range(0, takes.Count)];
            float sfx = GameSettings.SfxVolume;
            if (sfx <= 0.001f || !_throttle.Allow(key, Time.unscaledTime)) return;
            var src = FreeVoice();
            src.pitch = key.StartsWith("hit_") || key == SoundKeys.Arrow ? Random.Range(0.93f, 1.07f)
                : key.StartsWith("forge_") || key.StartsWith("dough_") ? Random.Range(0.96f, 1.04f) : 1f;
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
        void OnCardDealt(DilemmaDealtEvent e) => Play(SoundKeys.EventCard);
        void OnCardResolved(DilemmaResolvedEvent e) => Play(SoundRules.CardKey(e.Tone));
        void OnHour(HourPassedEvent e)
        {
            Play(SoundRules.HourKey(e.Hour), 0.8f);
            PickMusic(); // 6:00 / 20:00, and the first hour of a new season
        }

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
