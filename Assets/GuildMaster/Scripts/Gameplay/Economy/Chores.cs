using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.Welfare;
using UnityEngine;

namespace GuildMaster.Economy
{
    /// <summary>The odd jobs the guild master can do for the townsfolk (user decision 2026-10-29: 주민 일손 돕기 미니게임).</summary>
    public enum ChoreKind
    {
        /// <summary>대장장이 한스의 담금질: a timing game.</summary>
        Forge,
        /// <summary>약초상 마리의 약초 고르기: pair matching.</summary>
        Herbs,
        /// <summary>빵집 엘리의 빵 반죽: a rhythm game.</summary>
        Dough,
    }

    /// <summary>
    /// Small income on demand (user decision 2026-10-29): help a resident with their work in a short minigame, once a
    /// day each, while they are up. Better play earns more gold and a better opinion of the guild. Pure rules.
    /// </summary>
    public static class ChoreRules
    {
        /// <summary>Gold for a perfect run (in steps of 5).</summary>
        public const int MaxPay = 30;

        public static readonly ChoreKind[] All = { ChoreKind.Forge, ChoreKind.Herbs, ChoreKind.Dough };

        public static string ResidentId(ChoreKind k) => k switch
        {
            ChoreKind.Forge => "RES_hans",
            ChoreKind.Herbs => "RES_mari",
            _ => "RES_elly",
        };

        public static string Resident(ChoreKind k) => k switch
        {
            ChoreKind.Forge => "대장장이 한스",
            ChoreKind.Herbs => "약초상 마리",
            _ => "빵집 엘리",
        };

        public static string Title(ChoreKind k) => k switch
        {
            ChoreKind.Forge => "담금질 돕기",
            ChoreKind.Herbs => "약초 고르기",
            _ => "빵 반죽",
        };

        public static string HowTo(ChoreKind k) => k switch
        {
            ChoreKind.Forge => $"오가는 바늘이 초록 칸에 들어왔을 때 망치를 내리치세요 (F 또는 버튼). {ForgeGame.Strikes}번 두드리고, 칠수록 빨라집니다.",
            ChoreKind.Herbs => $"카드를 두 장씩 뒤집어 같은 약초를 찾으세요. {HerbGame.TimeLimit:0}초 안에 {HerbGame.Pairs}쌍. 틀린 횟수가 많으면 덜 받습니다.",
            _ => "오른쪽에서 오는 반죽이 세로선에 닿을 때 누르세요 (F 또는 버튼). 박자에 맞을수록 좋고, 헛치면 깎입니다.",
        };

        public static int Pay(float score)
        {
            score = Mathf.Clamp01(score);
            if (score < 0.1f) return 0;
            return Mathf.Max(5, Mathf.RoundToInt(MaxPay * score / 5f) * 5);
        }

        public static int Opinion(float score) => score >= 0.9f ? 4 : score >= 0.6f ? 3 : score >= 0.3f ? 1 : 0;

        public static string Grade(float score) => score >= 0.9f ? "완벽" : score >= 0.6f ? "좋음" : score >= 0.3f ? "그럭저럭" : "엉망";

        /// <summary>What the resident says at the end, by how it went.</summary>
        public static string Thanks(ChoreKind k, float score)
        {
            bool good = score >= 0.6f, ok = score >= 0.3f;
            return k switch
            {
                ChoreKind.Forge => good ? "흠. 손이 제법 맞는군. 또 와." : ok ? "그럭저럭이군. 날이 좀 울었어." : "…쇠 버릴 뻔했다. 수고비는 주지.",
                ChoreKind.Herbs => good ? "어머, 하나도 안 섞였네요! 정말 고마워요." : ok ? "조금 헷갈리셨죠? 그래도 도움이 됐어요." : "아… 다시 골라야겠어요. 그래도 와 주셔서 고마워요.",
                _ => good ? "와, 반죽이 쫀쫀해요! 빵 맛있게 나오겠어요!" : ok ? "음~ 나쁘지 않아요! 다음엔 박자만 더 맞춰 봐요!" : "어머, 반죽이 질척해요… 그래도 고마워요!",
            };
        }

        public static int Day(float totalHours) => Mathf.FloorToInt(totalHours / 24f);
    }

    /// <summary>One day's limit and the pay for the odd jobs (2026-10-29). Saved as "chores" (P4-119).</summary>
    public class ChoreService : ISaveable
    {
        readonly GuildTreasury _treasury;
        readonly Dictionary<ChoreKind, int> _lastDay = new();

        public int Earned { get; private set; }
        public int Done { get; private set; }

        public ChoreService(GuildTreasury treasury) { _treasury = treasury; }

        public CareCheck CanDo(ChoreKind k, float totalHours, float hourOfDay)
        {
            if (_lastDay.TryGetValue(k, out var day) && day == ChoreRules.Day(totalHours))
                return CareCheck.No("오늘은 이미 도왔습니다. 내일 다시 오세요.");
            if (hourOfDay < Social.Resident.WakeHour || hourOfDay >= Social.Resident.SleepHour)
                return CareCheck.No($"{ChoreRules.Resident(k)}{MemoryRecorderJosa(ChoreRules.Resident(k))} 쉬는 시간입니다 ({Social.Resident.WakeHour}시~{Social.Resident.SleepHour}시).");
            return CareCheck.Yes;
        }

        static string MemoryRecorderJosa(string name) => Adventurers.MemoryRecorder.Josa(name, "은", "는");

        /// <summary>The job was started: it counts as the day's, finished or not.</summary>
        public void Begin(ChoreKind k, float totalHours) => _lastDay[k] = ChoreRules.Day(totalHours);

        /// <summary>Pays for a finished (or abandoned) job; returns the gold and the opinion gained.</summary>
        public (int pay, int opinion) Complete(ChoreKind k, float score)
        {
            int pay = ChoreRules.Pay(score);
            int opinion = ChoreRules.Opinion(score);
            if (pay > 0) _treasury?.AddGold(pay, GoldReason.SideJob);
            Earned += pay;
            Done++;
            if (opinion != 0 && Services.TryGet<Social.ResidentService>(out var residents))
                residents.Find(ChoreRules.ResidentId(k))?.Profile.AddOpinion(opinion);
            if (Services.TryGet<Session.GameLog>(out var log))
                log.Add($"길드 마스터가 {ChoreRules.Resident(k)}의 {ChoreRules.Title(k)}를 도왔습니다 ({ChoreRules.Grade(score)}, {pay}G).");
            return (pay, opinion);
        }

        // ---------------- Save (P4-119) ----------------

        [Serializable]
        class SaveData
        {
            /// <summary>Per <see cref="ChoreRules.All"/>: the day the job was last done, -1 = never.</summary>
            public int[] last_day;
            public int earned;
            public int done;
        }

        public string SaveKey => "chores";

        public string Capture() => JsonUtility.ToJson(new SaveData
        {
            last_day = ChoreRules.All.Select(k => _lastDay.TryGetValue(k, out var d) ? d : -1).ToArray(),
            earned = Earned, done = Done,
        });

        public void Restore(string json, SaveContext ctx)
        {
            var s = JsonUtility.FromJson<SaveData>(json);
            _lastDay.Clear();
            for (int i = 0; i < ChoreRules.All.Length && s.last_day != null && i < s.last_day.Length; i++)
                if (s.last_day[i] >= 0) _lastDay[ChoreRules.All[i]] = s.last_day[i];
            Earned = s.earned;
            Done = s.done;
        }
    }

    // ---------------------------------------------------------------- the three minigames

    /// <summary>
    /// 담금질 (timing): a needle sweeps across the bar; strike while it is in the green zone. The closer to the middle
    /// the better; every strike makes it faster and the zone narrower.
    /// </summary>
    public class ForgeGame
    {
        public const int Strikes = 5;
        public const float StartSpeed = 0.7f, SpeedStep = 0.22f, StartWidth = 0.2f, WidthStep = 0.025f, MinWidth = 0.09f;

        readonly GameRandom _rng;
        readonly List<float> _hits = new();
        float _dir = 1f;

        public float Needle { get; private set; }
        public float ZoneCenter { get; private set; }
        public float ZoneWidth { get; private set; } = StartWidth;
        public float Speed { get; private set; } = StartSpeed;
        public IReadOnlyList<float> Hits => _hits;
        public bool Over => _hits.Count >= Strikes;
        public float Score => _hits.Sum() / Strikes;

        public ForgeGame(GameRandom rng)
        {
            _rng = rng ?? GameRandom.Shared;
            NewZone();
        }

        /// <summary>For tests: puts the needle and the zone where they are wanted.</summary>
        public void Set(float needle, float center, float width)
        {
            Needle = needle;
            ZoneCenter = center;
            ZoneWidth = width;
        }

        void NewZone() => ZoneCenter = Mathf.Lerp(ZoneWidth, 1f - ZoneWidth, _rng.Value);

        public void Tick(float dt)
        {
            if (Over) return;
            Needle += _dir * Speed * dt;
            if (Needle >= 1f) { Needle = 2f - Needle; _dir = -1f; }
            if (Needle <= 0f) { Needle = -Needle; _dir = 1f; }
            Needle = Mathf.Clamp01(Needle);
        }

        public static float Quality(float needle, float center, float width)
        {
            float d = Mathf.Abs(needle - center);
            return d <= width * 0.25f ? 1f : d <= width * 0.5f ? 0.6f : d <= width ? 0.2f : 0f;
        }

        public float Strike()
        {
            if (Over) return 0f;
            float q = Quality(Needle, ZoneCenter, ZoneWidth);
            _hits.Add(q);
            Speed += SpeedStep;
            ZoneWidth = Mathf.Max(MinWidth, ZoneWidth - WidthStep);
            NewZone();
            return q;
        }
    }

    /// <summary>약초 고르기 (pair matching): twelve cards, six herbs; find the pairs before time runs out.</summary>
    public class HerbGame
    {
        public static readonly string[] Herbs = { "쑥", "박하", "감초", "인삼", "라벤더", "로즈마리" };
        public static int Pairs => Herbs.Length;
        public const float TimeLimit = 45f;
        /// <summary>Misses that cost nothing (it is a memory game).</summary>
        public const int FreeMisses = 4;
        public const float MissPenalty = 0.05f;

        public enum Flip { Ignored, First, Match, Miss }

        readonly int[] _cards;
        readonly bool[] _matched;

        public int Count => _cards.Length;
        public int? First { get; private set; }
        public int? Second { get; private set; }
        public int Misses { get; private set; }
        public int Found { get; private set; }
        public float Elapsed { get; private set; }
        public bool Over => Found >= Pairs || Elapsed >= TimeLimit;
        public float TimeLeft => Mathf.Max(0f, TimeLimit - Elapsed);
        public float Score => (float)Found / Pairs * Mathf.Clamp01(1f - MissPenalty * Mathf.Max(0, Misses - FreeMisses));

        public HerbGame(GameRandom rng) : this(Deal(rng ?? GameRandom.Shared)) { }

        /// <summary>For tests: a fixed layout (herb index per card).</summary>
        public HerbGame(int[] cards)
        {
            _cards = cards;
            _matched = new bool[cards.Length];
        }

        static int[] Deal(GameRandom rng)
        {
            var list = new List<int>();
            for (int i = 0; i < Herbs.Length; i++) { list.Add(i); list.Add(i); }
            rng.Shuffle(list);
            return list.ToArray();
        }

        public string HerbAt(int slot) => Herbs[_cards[slot]];
        public bool IsMatched(int slot) => _matched[slot];
        public bool IsFaceUp(int slot) => _matched[slot] || slot == First || slot == Second;

        public void Tick(float dt) { if (!Over) Elapsed += dt; }

        /// <summary>Turns a missed pair face down again (also done by the next pick).</summary>
        public void HideMiss()
        {
            if (Second == null) return;
            First = null;
            Second = null;
        }

        public Flip Pick(int slot)
        {
            if (Over || slot < 0 || slot >= _cards.Length || _matched[slot]) return Flip.Ignored;
            if (Second != null) HideMiss();
            if (First == null) { First = slot; return Flip.First; }
            if (slot == First) return Flip.Ignored;
            if (_cards[slot] == _cards[First.Value])
            {
                _matched[slot] = _matched[First.Value] = true;
                First = null;
                Found++;
                return Flip.Match;
            }
            Second = slot;
            Misses++;
            return Flip.Miss;
        }
    }

    /// <summary>빵 반죽 (rhythm): knead on the beat. Stray presses cost a little so mashing does not pay.</summary>
    public class DoughGame
    {
        public const float Beat = 0.5f, Start = 2f, Perfect = 0.08f, Good = 0.16f, Window = 0.25f;
        /// <summary>Seconds a note is on screen before its beat.</summary>
        public const float Lead = 1.6f;
        public const float StrayPenalty = 0.04f;
        public const int Beats = 16;

        readonly List<float> _notes;
        readonly float[] _results;

        public IReadOnlyList<float> Notes => _notes;
        public float Time { get; private set; }
        public int Strays { get; private set; }
        public bool Over => _notes.Count == 0 || Time > _notes[_notes.Count - 1] + Window + 0.4f;
        public float Score => _notes.Count == 0 ? 0f
            : Mathf.Clamp01(_results.Where(r => r > 0f).Sum() / _notes.Count - StrayPenalty * Strays);
        /// <summary>Notes hit or gone by so far.</summary>
        public int Judged => _results.Count(r => !float.IsNaN(r));

        public DoughGame(GameRandom rng) : this(Pattern(rng ?? GameRandom.Shared)) { }

        public DoughGame(List<float> notes)
        {
            _notes = notes;
            _results = Enumerable.Repeat(float.NaN, notes.Count).ToArray();
        }

        /// <summary>Sixteen beats; the first of every four always has a note, the rest most of the time.</summary>
        static List<float> Pattern(GameRandom rng)
        {
            var notes = new List<float>();
            for (int i = 0; i < Beats; i++)
                if (i % 4 == 0 || rng.Chance(0.6f)) notes.Add(Start + i * Beat);
            return notes;
        }

        /// <summary>The result of a note: NaN while pending, 0 missed, else its quality.</summary>
        public float Result(int i) => _results[i];

        public void Tick(float dt)
        {
            Time += dt;
            for (int i = 0; i < _notes.Count; i++)
                if (float.IsNaN(_results[i]) && Time - _notes[i] > Window) _results[i] = 0f;
        }

        public static float Quality(float offset)
        {
            float d = Mathf.Abs(offset);
            return d <= Perfect ? 1f : d <= Good ? 0.6f : d <= Window ? 0.3f : 0f;
        }

        /// <summary>A press: hits the nearest waiting note in reach; -1 for a stray press.</summary>
        public float Press()
        {
            if (Over) return -1f;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < _notes.Count; i++)
            {
                if (!float.IsNaN(_results[i])) continue;
                float d = Mathf.Abs(Time - _notes[i]);
                if (d <= Window && d < bestD) { best = i; bestD = d; }
            }
            if (best < 0) { Strays++; return -1f; }
            float q = Quality(Time - _notes[best]);
            _results[best] = q;
            return q;
        }
    }
}
