using System;
using System.Collections.Generic;
using System.Linq;
using GuildMaster.Adventurers;
using UnityEngine;

namespace GuildMaster.Reports
{
    /// <summary>What a newspaper article is about (P4-130, F-9). Saved as numbers: add new kinds at the end.</summary>
    public enum NewsKind
    {
        Death, LastStand, Wipe, LowHp, Retreat,
        BigFight, BigFoeSlain, DragonSlain, LordSlain, NemesisFell, NemesisDeed,
        DragonSighted, DragonLair, DragonHoard, DragonPlundered, DragonOverTown,
        Raid, GateBroken, Horde, BuildingLost,
        RankUp, Joined, Left, LevelUp, QuestDone, QuestFailed, Decision, Break, Inspiration,
        Quarrel, MakeUp, Comfort, Friends, Enemies,
        Disaster, DisasterOver, Incident, Festival, Season, Village, VillageLost, VillageRaid, Discovery, Merchant,
        Gold, Quiet,
        Memorial,
    }

    /// <summary>One story of a day: what the newspaper prints, and who or where a click on it goes to.</summary>
    [Serializable]
    public class NewsArticle
    {
        public NewsKind kind;
        public float score;
        /// <summary>Game time in total hours.</summary>
        public float hour;
        /// <summary>Stories with the same topic on a day are one story: the better one is printed (null = its own).</summary>
        public string topic;
        public string headline;
        public string body;
        /// <summary>A short line for a grouped story ("아리스(Lv.5)", "'고블린 토벌'"), or null.</summary>
        public string entry;
        /// <summary>Ids of the people it is about, the one to go to first.</summary>
        public List<string> people = new();
        public bool has_place;
        public float x, y;

        public string Subject => people.Count > 0 ? people[0] : null;
        public Vector2 Place => new(x, y);
        public int Day => Mathf.FloorToInt(hour / 24f) + 1;

        public NewsArticle At(Vector2 at)
        {
            has_place = true;
            x = Mathf.Round(at.x * 10f) / 10f;
            y = Mathf.Round(at.y * 10f) / 10f;
            return this;
        }

        public NewsArticle About(params string[] ids)
        {
            foreach (var id in ids)
                if (!string.IsNullOrEmpty(id) && !people.Contains(id)) people.Add(id);
            return this;
        }

        public NewsArticle About(IEnumerable<string> ids) => About(ids?.ToArray() ?? Array.Empty<string>());

        public NewsArticle On(string t)
        {
            topic = t;
            return this;
        }
    }

    /// <summary>A day's newspaper: its stories, the first is the day's big moment.</summary>
    [Serializable]
    public class NewsPaper
    {
        public int day;
        public List<NewsArticle> articles = new();
        /// <summary>Handed to the guild master (in the morning after the day).</summary>
        public bool delivered;

        public NewsArticle Lead => articles.Count > 0 ? articles[0] : null;
    }

    /// <summary>
    /// The town newspaper's numbers and words (P4-130, F-9 — user decisions 2026-11-05: it opens by itself in the morning,
    /// three to five stories out of everything that happened, written lines only — no model). How newsworthy each kind of
    /// story is, which are printed, and how they read. Pure, covered by EditMode tests.
    /// </summary>
    public static class NewsRules
    {
        /// <summary>The paper is handed over at this hour of the morning after its day.</summary>
        public const int DeliveryHour = 6;
        /// <summary>Stories printed: at least this many when there are (with fillers), at most <see cref="MaxArticles"/>.</summary>
        public const int MinArticles = 3;
        public const int MaxArticles = 5;
        /// <summary>A story beyond the first three is printed only from this score.</summary>
        public const float ExtraThreshold = 40f;
        /// <summary>No more than this many stories of one kind in a paper.</summary>
        public const int PerKind = 2;
        /// <summary>Stories kept for the day still running (the least newsworthy go first).</summary>
        public const int DeskCapacity = 120;
        public const int PaperCapacity = 14;

        public static float Score(NewsKind k) => k switch
        {
            NewsKind.DragonSlain => 105f,
            NewsKind.LordSlain => 100f,
            NewsKind.Death => 95f,
            NewsKind.RankUp => 90f,
            NewsKind.NemesisFell => 88f,
            NewsKind.BigFoeSlain => 80f,
            NewsKind.DragonPlundered => 80f,
            NewsKind.DragonOverTown => 78f,
            NewsKind.LastStand => 75f,
            NewsKind.GateBroken => 72f,
            NewsKind.NemesisDeed => 70f,
            NewsKind.DragonHoard => 68f,
            NewsKind.DragonSighted => 65f,
            NewsKind.Raid => 62f,
            NewsKind.Wipe => 60f,
            NewsKind.BuildingLost => 60f,
            NewsKind.DragonLair => 60f,
            NewsKind.BigFight => 58f,
            NewsKind.Horde => 56f,
            NewsKind.Break => 55f,
            NewsKind.Left => 55f,
            NewsKind.DisasterOver => 54f,
            NewsKind.Disaster => 52f,
            NewsKind.VillageLost => 52f,
            NewsKind.Incident => 50f,
            NewsKind.Joined => 50f,
            NewsKind.Enemies => 48f,
            NewsKind.Quarrel => 46f,
            NewsKind.MakeUp => 45f,
            NewsKind.VillageRaid => 45f,
            NewsKind.Decision => 42f,
            NewsKind.LowHp => 40f,
            NewsKind.Friends => 40f,
            NewsKind.QuestFailed => 40f,
            NewsKind.Festival => 40f,
            NewsKind.Inspiration => 38f,
            NewsKind.Season => 35f,
            NewsKind.Village => 35f,
            NewsKind.Comfort => 34f,
            NewsKind.QuestDone => 30f,
            NewsKind.Retreat => 30f,
            NewsKind.LevelUp => 28f,
            NewsKind.Discovery => 22f,
            NewsKind.Merchant => 18f,
            NewsKind.Gold => 10f,
            NewsKind.Memorial => 44f,
            _ => 5f,
        };

        public static string Tag(NewsKind k) => k switch
        {
            NewsKind.Death => "부고",
            NewsKind.LastStand => "구사일생",
            NewsKind.Wipe => "전멸 위기",
            NewsKind.LowHp => "위기",
            NewsKind.Retreat => "후퇴",
            NewsKind.BigFight => "결전",
            NewsKind.BigFoeSlain => "토벌",
            NewsKind.DragonSlain or NewsKind.DragonSighted or NewsKind.DragonLair or NewsKind.DragonHoard => "드래곤",
            NewsKind.DragonPlundered or NewsKind.DragonOverTown => "드래곤 습격",
            NewsKind.LordSlain => "지대 정화",
            NewsKind.NemesisFell or NewsKind.NemesisDeed => "원수",
            NewsKind.Raid => "마을 방어",
            NewsKind.GateBroken => "성문",
            NewsKind.Horde => "대규모 무리",
            NewsKind.BuildingLost => "피해",
            NewsKind.RankUp => "길드 등급",
            NewsKind.Joined => "영입",
            NewsKind.Left => "이별",
            NewsKind.LevelUp => "성장",
            NewsKind.QuestDone or NewsKind.QuestFailed => "의뢰",
            NewsKind.Decision => "결정",
            NewsKind.Break => "사건",
            NewsKind.Inspiration => "영감",
            NewsKind.Quarrel => "다툼",
            NewsKind.MakeUp => "화해",
            NewsKind.Comfort or NewsKind.Friends => "우정",
            NewsKind.Enemies => "앙숙",
            NewsKind.Disaster or NewsKind.DisasterOver => "재해",
            NewsKind.Incident => "사건",
            NewsKind.Festival => "축제",
            NewsKind.Season => "계절",
            NewsKind.Village or NewsKind.VillageLost or NewsKind.VillageRaid => "이웃 마을",
            NewsKind.Discovery => "발견",
            NewsKind.Merchant => "장터",
            NewsKind.Gold => "살림",
            NewsKind.Memorial => "추모",
            _ => "단신",
        };

        public static LineTone Tone(NewsKind k) => k switch
        {
            NewsKind.Death or NewsKind.Wipe or NewsKind.GateBroken or NewsKind.NemesisDeed or NewsKind.DragonPlundered
                or NewsKind.DragonOverTown or NewsKind.BuildingLost or NewsKind.Break or NewsKind.Left or NewsKind.QuestFailed
                or NewsKind.Quarrel or NewsKind.Enemies or NewsKind.Disaster or NewsKind.VillageLost or NewsKind.VillageRaid => LineTone.Bad,
            NewsKind.DragonSlain or NewsKind.LordSlain or NewsKind.NemesisFell or NewsKind.BigFoeSlain or NewsKind.RankUp
                or NewsKind.LastStand or NewsKind.Raid or NewsKind.Joined or NewsKind.LevelUp or NewsKind.QuestDone or NewsKind.MakeUp
                or NewsKind.Friends or NewsKind.Comfort or NewsKind.DisasterOver or NewsKind.Incident or NewsKind.Festival
                or NewsKind.Inspiration or NewsKind.DragonHoard => LineTone.Good,
            NewsKind.Quiet => LineTone.Muted,
            _ => LineTone.Normal,
        };

        /// <summary>Close calls: not printed for someone who died that day.</summary>
        public static bool IsCloseCall(NewsKind k) => k is NewsKind.LastStand or NewsKind.Wipe or NewsKind.LowHp or NewsKind.Retreat;

        /// <summary>Several of these on a day are one story listing them all.</summary>
        public static bool IsGrouped(NewsKind k) => k is NewsKind.LevelUp or NewsKind.QuestDone or NewsKind.QuestFailed or NewsKind.Joined or NewsKind.Discovery;

        // ---------------- Choosing ----------------

        /// <summary>
        /// The day's paper out of everything noted on it: one story per topic (the more newsworthy), grouped kinds in one
        /// story each, no close call for someone who died, then the best by score — at most <see cref="PerKind"/> of a kind,
        /// three whatever their score and up to five above <see cref="ExtraThreshold"/>. Fillers (the treasury, a quiet day)
        /// make up a thin day. <paramref name="report"/> may be null.
        /// </summary>
        public static NewsPaper Compose(int day, IEnumerable<NewsArticle> noted, DailyReport report)
        {
            var items = (noted ?? Enumerable.Empty<NewsArticle>()).Where(a => a != null).ToList();

            // One story per topic: the more newsworthy, the later on a tie (it knows more).
            var byTopic = new Dictionary<string, NewsArticle>();
            var merged = new List<NewsArticle>();
            foreach (var a in items)
            {
                if (a.topic == null) { merged.Add(a); continue; }
                if (byTopic.TryGetValue(a.topic, out var had))
                {
                    if (a.score < had.score) { had.About(a.people); continue; }
                    a.About(had.people);
                    if (!a.has_place && had.has_place) a.At(had.Place);
                    merged[merged.IndexOf(had)] = a;
                }
                else merged.Add(a);
                byTopic[a.topic] = a;
            }

            var dead = new HashSet<string>(merged.Where(a => a.kind == NewsKind.Death).Select(a => a.Subject).Where(id => id != null));
            merged.RemoveAll(a => IsCloseCall(a.kind) && a.people.Any(dead.Contains));

            var stories = new List<NewsArticle>();
            foreach (var group in merged.GroupBy(a => IsGrouped(a.kind) ? a.kind.ToString() : null))
            {
                if (group.Key == null) stories.AddRange(group);
                else stories.Add(Group(group.ToList()));
            }

            var paper = new NewsPaper { day = day };
            var perKind = new Dictionary<NewsKind, int>();
            foreach (var a in stories.OrderByDescending(s => s.score).ThenBy(s => s.hour))
            {
                if (paper.articles.Count >= MaxArticles) break;
                if (paper.articles.Count >= MinArticles && a.score < ExtraThreshold) break;
                perKind.TryGetValue(a.kind, out int n);
                if (n >= PerKind) continue;
                perKind[a.kind] = n + 1;
                paper.articles.Add(a);
            }

            if (paper.articles.Count < MinArticles && report != null && report.Net != 0) paper.articles.Add(Gold(report));
            if (paper.articles.Count == 0) paper.articles.Add(Quiet(day));
            return paper;
        }

        /// <summary>Several stories of one grouped kind as one (a single one stays as written).</summary>
        static NewsArticle Group(List<NewsArticle> list)
        {
            if (list.Count == 1) return list[0];
            var first = list[0];
            var entries = list.Select(a => a.entry ?? a.headline).ToList();
            string all = DailyReportTexts.Names(entries);
            var g = new NewsArticle
            {
                kind = first.kind, hour = first.hour, entry = all,
                score = list.Max(a => a.score) + Mathf.Min(12f, 3f * (list.Count - 1)),
            };
            foreach (var a in list) g.About(a.people);
            if (first.has_place) g.At(first.Place);
            switch (first.kind)
            {
                case NewsKind.LevelUp:
                    g.headline = Variant(all, $"{list.Count}명이 한 단계 성장", "길드원들, 나란히 성장하다");
                    g.body = $"{all}{Josa(all, "이", "가")} 더 강해졌다.";
                    break;
                case NewsKind.QuestDone:
                    g.headline = $"의뢰 {list.Count}건 달성";
                    g.body = $"{all} 의뢰를 해내고 보상을 받았다.";
                    break;
                case NewsKind.QuestFailed:
                    g.headline = $"의뢰 {list.Count}건 실패";
                    g.body = $"{all} 의뢰를 놓쳤다. 다음에는 더 잘 준비해야겠다.";
                    break;
                case NewsKind.Joined:
                    g.headline = $"새 길드원 {list.Count}명";
                    g.body = $"{all}{Josa(all, "이", "가")} 길드에 들어왔다. 길드 홀이 북적인다.";
                    break;
                default:
                    g.headline = $"새로 찾은 곳 {list.Count}곳";
                    g.body = $"{all}.";
                    break;
            }
            return g;
        }

        // ---------------- Words ----------------

        static string Josa(string w, string withBatchim, string without) => MemoryRecorder.Josa(w, withBatchim, without);

        /// <summary>One of the wordings, the same one for the same seed (no shared random: the paper reads the same after loading).</summary>
        public static string Variant(string seed, params string[] options)
        {
            if (options == null || options.Length == 0) return "";
            uint h = 2166136261;
            foreach (char c in seed ?? "") h = (h ^ c) * 16777619;
            return options[(int)(h % (uint)options.Length)];
        }

        static NewsArticle Make(NewsKind kind, string headline, string body, float? score = null)
            => new() { kind = kind, score = score ?? Score(kind), headline = headline, body = body };

        /// <summary>"아리스와 토르" for the people in a story, or "길드원들" when none is known.</summary>
        public static string Who(IReadOnlyList<string> names) => names == null || names.Count == 0 ? "길드원들" : Director.HighlightRules.Names(names);

        public static NewsArticle Death(string name, string killer)
            => Make(NewsKind.Death,
                Variant(name, $"{name}, 끝내 돌아오지 못하다", $"길드원 {name} 잠들다"),
                (killer != null ? $"{name}{Josa(name, "이", "가")} {killer}{Josa(killer, "과", "와")} 싸우다 목숨을 잃었다." : $"{name}{Josa(name, "이", "가")} 목숨을 잃었다.")
                + " 길드는 슬픔에 잠겼다.");

        /// <summary>The memorial wall (2026-11-15): a hero remembered, an anniversary kept, a relic handed on.</summary>
        public static NewsArticle Hero(string name, string reason)
            => Make(NewsKind.Memorial, Variant(name, $"영웅 {name}, 길드의 이름으로 기억되다", $"{name}, 영웅으로 비석에 새겨지다"),
                $"{reason}. 길드는 그의 비석을 금빛으로 둘렀다.");

        public static NewsArticle Anniversary(string name, int years, int gathered)
            => Make(NewsKind.Memorial, $"{name}의 {years}주기",
                gathered > 0 ? $"길드원 {gathered}명이 저녁 무렵 그를 기렸다." : "길드는 조용히 그를 기렸다.");

        public static NewsArticle Heirloom(string heir, string dead, string item)
            => Make(NewsKind.Memorial, $"{heir}, {dead}의 {item}{Josa(item, "을", "를")} 물려받다",
                $"{dead}의 뜻을 잇겠다고 다짐했다.");

        public static NewsArticle LastStand(string name, string foe)
            => Make(NewsKind.LastStand,
                Variant(name, $"{name}, 죽음의 문턱에서 돌아오다", $"구사일생! {name} 살아 돌아오다"),
                foe != null ? $"{foe}의 일격에 쓰러질 뻔했지만 끝내 버텨 냈다." : "쓰러질 뻔했지만 끝내 버텨 냈다.");

        public static NewsArticle Wipe(IReadOnlyList<string> names)
        {
            string who = Who(names);
            return Make(NewsKind.Wipe, $"{who}, 전멸 위기를 넘기다", "함께 몰려 한꺼번에 쓰러질 뻔했지만 모두 살아남았다.");
        }

        public static NewsArticle LowHp(string name, string foe)
            => Make(NewsKind.LowHp,
                Variant(name, $"{name}, 아슬아슬한 고비를 넘기다", $"{name}, 위기일발"),
                foe != null ? $"{foe}{Josa(foe, "과", "와")} 싸우다 체력이 바닥까지 떨어졌지만 살아남았다." : "싸우다 체력이 바닥까지 떨어졌지만 살아남았다.");

        public static NewsArticle Retreat(string name, string threat)
            => Make(NewsKind.Retreat, threat != null ? $"{name}, {threat} 앞에서 물러서다" : $"{name}, 한발 물러서다", "무리하지 않고 몸을 뺐다. 다음을 기약한다.");

        public static NewsArticle BigFight(IReadOnlyList<string> names, string foe, string fight)
        {
            string who = Who(names);
            return Make(NewsKind.BigFight, $"{who}, {foe}{Josa(foe, "과", "와")} 맞서다", $"{fight}. 치열한 싸움이 벌어졌지만 결판은 나지 않았다.");
        }

        public static NewsArticle BigFoeSlain(IReadOnlyList<string> names, string foe, string fight)
        {
            string who = Who(names);
            return Make(NewsKind.BigFoeSlain,
                Variant(foe, $"{foe} 쓰러지다!", $"{who}, {foe}{Josa(foe, "을", "를")} 꺾다"),
                $"{fight}에서 {who}{Josa(who, "이", "가")} {foe}{Josa(foe, "을", "를")} 쓰러뜨렸다.");
        }

        public static NewsArticle DragonSlain(string dragon, IReadOnlyList<string> names)
        {
            string who = names == null || names.Count == 0 ? "길드" : Who(names);
            return Make(NewsKind.DragonSlain, "드래곤 쓰러지다! 마을이 환호하다",
                $"{who}{Josa(who, "이", "가")} {dragon}{Josa(dragon, "을", "를")} 쓰러뜨렸다. 둥지의 보물은 이제 길드 몫이다.");
        }

        public static NewsArticle LordSlain(string lord, string region, int fallen, int total, IReadOnlyList<string> names)
        {
            string who = names == null || names.Count == 0 ? "길드" : Who(names);
            return Make(NewsKind.LordSlain, $"{region}의 군주 {lord} 토벌",
                $"{who}{Josa(who, "이", "가")} {lord}{Josa(lord, "을", "를")} 쓰러뜨려 {region}{Josa(region, "이", "가")} 정화되었다. (군주 {fallen}/{total})");
        }

        public static NewsArticle NemesisFell(string nemesis, string slayer)
            => Make(NewsKind.NemesisFell,
                slayer != null ? $"{slayer}, 원수 {nemesis}{Josa(nemesis, "을", "를")} 쓰러뜨리다" : $"원수 {nemesis} 쓰러지다",
                slayer != null ? "오랜 원한이 풀렸다. 복수를 다짐하던 동료들이 한숨 돌렸다." : "누가 쓰러뜨렸는지는 아무도 모른다.");

        public static NewsArticle NemesisDeed(string oldName, string newName, string victim, bool killed, bool isNew)
            => Make(NewsKind.NemesisDeed,
                isNew ? $"새 원수 등장: {newName}" : $"원수 {oldName}, 또 나타나다",
                $"{victim}{Josa(victim, "을", "를")} {(killed ? "쓰러뜨린" : "죽을 뻔하게 몰아붙인")} {oldName}{Josa(oldName, "이", "가")} '{newName}'{Josa(newName, "이라", "라")} 불리게 되었다.");

        public static NewsArticle DragonSighted(string dragon, bool first)
            => first
                ? Make(NewsKind.DragonSighted, "하늘에 드래곤이 나타나다", $"길드가 {dragon}{Josa(dragon, "을", "를")} 처음 보았다. 모두가 숨을 죽였다.")
                : Make(NewsKind.DragonSighted, $"{dragon}, 다시 하늘을 날다", "드래곤이 또 모습을 드러냈다.", Score(NewsKind.DragonSighted) - 25f);

        public static NewsArticle DragonLair(int value)
            => Make(NewsKind.DragonLair, "드래곤의 둥지를 찾다", $"금화 더미가 {value}G어치는 되어 보인다. 드래곤이 비운 틈을 노려 볼 만하다.");

        public static NewsArticle DragonHoard(string name, int value, bool woke)
            => Make(NewsKind.DragonHoard, $"{name}, 드래곤의 보물을 챙기다", $"{value}G어치를 들고 나왔다." + (woke ? " 그 바람에 드래곤이 깨어났다!" : ""));

        public static NewsArticle DragonPlundered(int gold)
            => Make(NewsKind.DragonPlundered, "드래곤, 마을을 덮치다", $"불길 속에 길드 금고에서 {gold}G가 사라졌다.");

        public static NewsArticle DragonOverTown(string dragon, IReadOnlyList<string> names)
            => Make(NewsKind.DragonOverTown, $"{dragon}, 마을 위에 나타나다",
                names != null && names.Count > 0 ? $"{Who(names)}{Josa(Who(names), "이", "가")} 하늘을 올려다보며 맞섰다." : "마을 사람들이 몸을 숨겼다.");

        public static NewsArticle Raid(int killed, bool withdrawn, IReadOnlyList<string> defenders)
        {
            string who = defenders != null && defenders.Count > 0 ? Who(defenders) : null;
            string head = withdrawn ? "적이 물러가다" : Variant(killed.ToString(), "마을 방어 성공", "성문을 지켜 내다");
            string body = (who != null ? $"{who}{Josa(who, "이", "가")} 몰려온 적을 막아 냈다." : "몰려온 적을 막아 냈다.") + $" 쓰러뜨린 적 {killed}.";
            return Make(NewsKind.Raid, head, body);
        }

        public static NewsArticle RaidFought(string species, string direction, IReadOnlyList<string> defenders)
        {
            string at = string.IsNullOrEmpty(direction) ? "성문" : $"{direction}쪽 성문";
            string who = Who(defenders);
            return Make(NewsKind.Raid, $"{at}에 {species} 무리", $"{who}{Josa(who, "이", "가")} 성문 앞에서 맞섰다.", Score(NewsKind.Raid) - 5f);
        }

        public static NewsArticle GateBroken() => Make(NewsKind.GateBroken, "성문이 뚫리다", "적이 성 안까지 들어왔다. 성문을 고쳐야 한다.");

        public static NewsArticle Horde(string species, IReadOnlyList<string> names)
            => Make(NewsKind.Horde, $"{species} 대규모 무리 출몰",
                names != null && names.Count > 0 ? $"{Who(names)}{Josa(Who(names), "이", "가")} 무리와 마주쳤다." : "길드가 무리가 지나가는 것을 보았다.");

        public static NewsArticle BuildingLost(string building)
            => Make(NewsKind.BuildingLost, $"{building}{Josa(building, "이", "가")} 무너지다", "다시 세우려면 손이 많이 갈 것 같다.");

        public static NewsArticle RankUp(string rank, int grant)
            => Make(NewsKind.RankUp, $"길드, {rank}에 오르다",
                grant > 0 ? $"마을이 하사금 {DailyReportTexts.Money(grant)}를 보냈다. 길드의 이름이 멀리 퍼진다." : "길드의 이름이 멀리 퍼진다.");

        public static NewsArticle Joined(string name, string classLabel)
        {
            var a = Make(NewsKind.Joined, $"새 얼굴: {classLabel} {name}", $"{name}{Josa(name, "이", "가")} 길드에 들어왔다. 길드 홀이 북적인다.");
            a.entry = name;
            return a;
        }

        public static NewsArticle Left(string name)
            => Make(NewsKind.Left, $"{name}, 길드를 떠나다", "작별 인사를 남기고 마을을 나섰다.");

        public static NewsArticle LevelUp(string name, int level)
        {
            var a = Make(NewsKind.LevelUp, $"{name}, Lv.{level} 달성", $"{name}{Josa(name, "이", "가")} 한 단계 더 강해졌다.");
            a.entry = $"{name}(Lv.{level})";
            return a;
        }

        public static NewsArticle QuestDone(string title)
        {
            var a = Make(NewsKind.QuestDone, $"'{title}' 의뢰 달성", "의뢰처가 약속한 보상을 받았다.");
            a.entry = $"'{title}'";
            return a;
        }

        public static NewsArticle QuestFailed(string title)
        {
            var a = Make(NewsKind.QuestFailed, $"'{title}' 의뢰 실패", "다음에는 더 잘 준비해야겠다.");
            a.entry = $"'{title}'";
            return a;
        }

        public static NewsArticle Decision(string title, string choice, string result)
            => Make(NewsKind.Decision, $"{title}: '{choice}'", result);

        public static NewsArticle Break(string name, string what, string other)
            => Make(NewsKind.Break, $"{name}, 끝내 무너지다",
                $"{what}{(other != null ? $"({other}{Josa(other, "과", "와")})" : "")}. 기분이 너무 가라앉아 있었다.");

        public static NewsArticle Inspiration(string name, string what, string effect)
            => Make(NewsKind.Inspiration, $"{name}에게 {what}", $"{effect}.");

        public static NewsArticle Quarrel(string a, string b)
            => Make(NewsKind.Quarrel, Variant(a + b, $"{a}{Josa(a, "과", "와")} {b}, 크게 다투다", $"{a}{Josa(a, "과", "와")} {b} 사이에 고성"),
                "마을 사람들 사이에 말이 돌고 있다.");

        public static NewsArticle MakeUp(string a, string b)
            => Make(NewsKind.MakeUp, $"{a}{Josa(a, "과", "와")} {b}, 화해하다", "서로 사과하고 손을 맞잡았다.");

        public static NewsArticle Comfort(string a, string b)
            => Make(NewsKind.Comfort, $"{a}, 힘들어하는 {b}{Josa(b, "을", "를")} 다독이다", "따뜻한 말 한마디가 오갔다.");

        public static NewsArticle Friends(string a, string b)
            => Make(NewsKind.Friends, $"{a}{Josa(a, "과", "와")} {b}, 단짝이 되다", "요즘 둘이 붙어 다닌다는 소문이다.");

        public static NewsArticle Enemies(string a, string b)
            => Make(NewsKind.Enemies, $"{a}{Josa(a, "과", "와")} {b}, 앙숙이 되다", "같은 파티에 넣을 때는 조심해야겠다.");

        public static NewsArticle Disaster(string name, string area)
            => Make(NewsKind.Disaster, $"{area}에 {name}", "마을이 손을 보탤 사람을 구하고 있다.");

        public static NewsArticle DisasterOver(string name, string nameEul)
            => Make(NewsKind.DisasterOver, $"{name} 수습", $"길드가 {nameEul} 수습했다. 마을이 한시름 놓았다.");

        public static NewsArticle Incident(string tag, string text) => Make(NewsKind.Incident, $"{tag} 마무리", text);

        public static NewsArticle Festival() => Make(NewsKind.Festival, "수확 축제 열리다", "마을 사람들과 길드원들이 함께 웃고 떠들었다.");

        public static NewsArticle Season(string season, string changes)
            => Make(NewsKind.Season, $"{season}{Josa(season, "이", "가")} 오다", changes + ".");

        public static NewsArticle Village(string village, string region)
            => Make(NewsKind.Village, $"이웃 마을 {village} 발견", $"{region}에 사람이 사는 마을이 있었다.");

        public static NewsArticle VillageLost(string village)
            => Make(NewsKind.VillageLost, $"{village}, 폐허가 되다", "다시 세워지지 않는다고 한다.");

        public static NewsArticle VillageRaid(string village, string species, int raiders)
            => Make(NewsKind.VillageRaid, $"{village}에 {species} {raiders}마리", "그 마을이 도움을 청하고 있다.");

        public static NewsArticle Discovery(string place, string what)
        {
            var a = Make(NewsKind.Discovery, $"새 장소: {place}", what);
            a.entry = place;
            return a;
        }

        public static NewsArticle Merchant() => Make(NewsKind.Merchant, "행상인이 다녀가다", "광장에 노점이 섰다. 창고의 물건을 팔 수 있었다.");

        public static NewsArticle Gold(DailyReport r)
            => Make(NewsKind.Gold, $"길드 금고 {DailyReportTexts.SignedMoney(r.Net)}",
                $"수입 {DailyReportTexts.Money(r.Income)}, 지출 {DailyReportTexts.Money(r.Expense)}. 하루를 마친 금고에는 {DailyReportTexts.Money(r.GoldEnd)}가 남았다.");

        public static NewsArticle Quiet(int day)
            => Make(NewsKind.Quiet, Variant(day.ToString(), "조용한 하루", "별일 없던 하루"), "큰일 없이 하루가 지나갔다. 길드원들은 저마다 할 일을 했다.");
    }
}
