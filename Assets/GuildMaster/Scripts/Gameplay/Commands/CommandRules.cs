using System.Collections.Generic;
using UnityEngine;

namespace GuildMaster.Commands
{
    /// <summary>The guild master's orders (P4-132, H6 = F-16).</summary>
    public enum MasterCommand { Cheer, Focus, Rally, Retreat }

    /// <summary>Why an order cannot be given right now.</summary>
    public enum CommandRefusal { None, Down, Power, Cooldown, NobodyNear, NoTarget, TargetFar, FlagTooFar, FlagIndoors }

    /// <summary>
    /// The guild master's orders, as numbers and words (P4-132; user decision 2026-11-08: all four — a cheer, a focus on one
    /// monster, a rally flag for the town's defence, a retreat horn; a command gauge they share; heard only near the master;
    /// followed by mood and nature). Pure: <see cref="MasterCommands"/> does them.
    /// </summary>
    public static class CommandRules
    {
        public static readonly MasterCommand[] All = { MasterCommand.Cheer, MasterCommand.Focus, MasterCommand.Rally, MasterCommand.Retreat };

        // ---------------- The gauge ----------------

        public const float MaxPower = 100f;
        public const float StartPower = 60f;
        /// <summary>It fills by itself this much a game hour (empty to full in about a day).</summary>
        public const float PowerPerHour = 4f;
        /// <summary>A quest done, and a raid seen off: the guild stands taller behind its master.</summary>
        public const float QuestDonePower = 3f, RaidHeldPower = 15f;

        public static float Cost(MasterCommand c) => c switch
        {
            MasterCommand.Cheer => 30f,
            MasterCommand.Focus => 20f,
            MasterCommand.Rally => 25f,
            _ => 35f,
        };

        /// <summary>Game hours before the same order can be given again.</summary>
        public static float CooldownHours(MasterCommand c) => c switch
        {
            MasterCommand.Cheer => 2f,
            MasterCommand.Focus => 0.5f,
            MasterCommand.Rally => 1f,
            _ => 3f,
        };

        public static float Regen(float power, float hours) => Mathf.Clamp(power + Mathf.Max(0f, hours) * PowerPerHour, 0f, MaxPower);

        // ---------------- Reach and effects ----------------

        /// <summary>How far the master's voice (and horn) carries, in cells; a focus target must be this close to the master too.</summary>
        public const float Reach = 12f;
        /// <summary>A focus target is the monster nearest the clicked point within this many cells.</summary>
        public const float PickRadius = 1.5f;
        /// <summary>The cheer: a share added to the attack, for this many seconds (game time — 1.5 game hours).</summary>
        public const float CheerShare = 0.25f, CheerSeconds = 15f;
        /// <summary>A focus order stands this long (seconds), or until the monster falls.</summary>
        public const float FocusSeconds = 20f;
        /// <summary>The flag stands this many game hours.</summary>
        public const float FlagHours = 24f;
        /// <summary>The flag goes inside the walls or this close outside them (cells): it marks where the town is held.</summary>
        public const float FlagReach = 12f;
        /// <summary>Called home by the horn: not out to the town's defence again for this many game hours.</summary>
        public const float HornHoldHours = 1f;

        // ---------------- Can it be given ----------------

        /// <summary>What stands in the way before anyone is asked (the master, the gauge, the cooldown).</summary>
        public static CommandRefusal CanAfford(MasterCommand c, bool masterUp, float power, float hoursToReady)
        {
            if (!masterUp) return CommandRefusal.Down;
            if (hoursToReady > 0f) return CommandRefusal.Cooldown;
            if (power + 1e-3f < Cost(c)) return CommandRefusal.Power;
            return CommandRefusal.None;
        }

        public static string Why(CommandRefusal r, MasterCommand c, float power, float hoursToReady) => r switch
        {
            CommandRefusal.Down => "길드 마스터가 쓰러져 있습니다.",
            CommandRefusal.Cooldown => $"{Label(c)}: {Hours(hoursToReady)} 뒤에 다시 쓸 수 있습니다.",
            CommandRefusal.Power => $"{Label(c)}: 지휘력이 모자랍니다 ({Mathf.FloorToInt(power)}/{Cost(c):0}).",
            CommandRefusal.NobodyNear => c switch
            {
                MasterCommand.Cheer => $"응원: 마스터 {Reach:0}칸 안에 싸우는 길드원이 없습니다.",
                MasterCommand.Retreat => $"후퇴 나팔: 마스터 {Reach:0}칸 안에 물러날 길드원이 없습니다.",
                _ => $"{Label(c)}: 마스터 {Reach:0}칸 안에 명령을 들을 길드원이 없습니다.",
            },
            CommandRefusal.NoTarget => "집중 공격: 그곳에 보이는 몬스터가 없습니다.",
            CommandRefusal.TargetFar => $"집중 공격: 마스터 {Reach:0}칸 안의 몬스터만 고를 수 있습니다.",
            CommandRefusal.FlagTooFar => $"집결 깃발: 성벽 안이나 성벽에서 {FlagReach:0}칸 안에만 꽂을 수 있습니다.",
            CommandRefusal.FlagIndoors => "집결 깃발: 건물 안에는 꽂을 수 없습니다. 바깥으로 나가서 꽂으세요.",
            _ => "",
        };

        /// <summary>"1.5시간", "40분".</summary>
        public static string Hours(float h) => h >= 1f ? $"{h:0.#}시간" : $"{Mathf.Max(1, Mathf.CeilToInt(h * 60f))}분";

        // ---------------- Who follows ----------------

        /// <summary>Mood at which a member follows three times in four; each point of mood is a point of chance.</summary>
        const float BaseChance = 0.75f, PerMood = 0.01f, MinChance = 0.1f;

        /// <summary>
        /// How likely a member is to do as it is told (user decision 2026-11-08: by mood and nature — no new number): three in
        /// four at an even mood (50), every point of mood a point more or less, so a member in high spirits always follows and one
        /// about to break only half the time. The loyal follow more, the arrogant less; and each order has the natures it suits —
        /// the brave hate to fall back and like to charge, cowards the other way round, the sociable warm to a cheer, the
        /// honour-bound hold their ground.
        /// </summary>
        public static float ObeyChance(MasterCommand c, float mood, IReadOnlyList<string> traits, string values)
        {
            float p = BaseChance + (mood - 50f) * PerMood;
            if (Has(traits, "loyal")) p += 0.2f;
            if (Has(traits, "arrogant")) p -= 0.15f;
            switch (c)
            {
                case MasterCommand.Retreat:
                    if (Has(traits, "brave")) p -= 0.15f;
                    if (Has(traits, "coward")) p += 0.2f;
                    if (values == "명예") p -= 0.1f;
                    if (values == "안전") p += 0.1f;
                    break;
                case MasterCommand.Focus:
                    if (Has(traits, "brave")) p += 0.1f;
                    if (Has(traits, "coward")) p -= 0.1f;
                    if (values == "명예") p += 0.05f;
                    break;
                case MasterCommand.Cheer:
                    if (Has(traits, "sociable")) p += 0.1f;
                    if (Has(traits, "loner")) p -= 0.1f;
                    if (values == "동료") p += 0.05f;
                    break;
                case MasterCommand.Rally:
                    if (values == "동료") p += 0.1f;
                    if (Has(traits, "loner")) p -= 0.1f;
                    break;
            }
            return Mathf.Clamp(p, MinChance, 1f);
        }

        static bool Has(IReadOnlyList<string> ids, string id)
        {
            if (ids == null) return false;
            foreach (var t in ids) if (t == id) return true;
            return false;
        }

        // ---------------- Words ----------------

        public static string Label(MasterCommand c) => c switch
        {
            MasterCommand.Cheer => "응원",
            MasterCommand.Focus => "집중 공격",
            MasterCommand.Rally => "집결 깃발",
            _ => "후퇴 나팔",
        };

        /// <summary>The key that gives it (5–8, after the speed keys 1–4).</summary>
        public static string Key(MasterCommand c) => ((int)c + 5).ToString();

        /// <summary>What the master shouts (over its head).</summary>
        public static string Shout(MasterCommand c) => c switch
        {
            MasterCommand.Cheer => "힘내라!",
            MasterCommand.Focus => "저놈부터 쳐라!",
            MasterCommand.Rally => "깃발 아래로!",
            _ => "후퇴하라!",
        };

        public static string Describe(MasterCommand c) => c switch
        {
            MasterCommand.Cheer => $"마스터 {Reach:0}칸 안의 길드원이 {CheerSeconds:0}초 동안 공격 +{CheerShare * 100f:0}%. 근처에 싸우는 길드원이 있어야 합니다.",
            MasterCommand.Focus => $"고른 몬스터 하나를 마스터 {Reach:0}칸 안의 길드원이 함께 노립니다({FocusSeconds:0}초, 쓰러질 때까지). 몬스터도 마스터 {Reach:0}칸 안이어야 합니다.",
            MasterCommand.Rally => $"마스터가 선 자리에 깃발을 꽂습니다({FlagHours:0}시간). 침공을 막을 때 따르는 길드원은 성문 대신 깃발 아래에서 막습니다. 성벽 안이나 성벽 {FlagReach:0}칸 안에만.",
            _ => $"마스터 {Reach:0}칸 안의 길드원이 싸움을 그만두고 마을로 물러납니다. 물러난 길드원은 {HornHoldHours:0}시간 동안 침공 방어에 나서지 않습니다. 호위는 마스터 곁에 남습니다.",
        };

        static readonly string[] CheerYes = { "힘이 난다!", "마스터가 보고 있어!", "좋아, 한 번 더!" };
        static readonly string[] CheerNo = { "(응원은 됐고...)", "(지금 그럴 기분 아니야.)" };
        static readonly string[] FocusYes = { "저놈부터!", "알겠어, 저 녀석!", "집중해서 친다!" };
        static readonly string[] FocusNo = { "(내 앞의 적부터 처리할래.)", "(내 방식대로 할 거야.)" };
        static readonly string[] RallyYes = { "깃발로 모여!", "깃발 쪽으로!" };
        static readonly string[] RallyNo = { "(여기서 막는 게 나아.)", "(깃발이고 뭐고...)" };
        static readonly string[] RetreatYes = { "나팔이다! 물러나자!", "후퇴 신호다, 돌아가자!" };
        static readonly string[] RetreatNo = { "(여기서 물러설 순 없어!)", "(조금만 더 버티면 돼.)" };

        /// <summary>What a member says as it follows (<paramref name="roll"/> 0–99 picks the words).</summary>
        public static string ObeyLine(MasterCommand c, int roll) => Pick(c switch
        {
            MasterCommand.Cheer => CheerYes,
            MasterCommand.Focus => FocusYes,
            MasterCommand.Rally => RallyYes,
            _ => RetreatYes,
        }, roll);

        /// <summary>What a member mutters as it does as it pleases.</summary>
        public static string RefuseLine(MasterCommand c, int roll) => Pick(c switch
        {
            MasterCommand.Cheer => CheerNo,
            MasterCommand.Focus => FocusNo,
            MasterCommand.Rally => RallyNo,
            _ => RetreatNo,
        }, roll);

        static string Pick(string[] lines, int roll) => lines[Mathf.Abs(roll) % lines.Length];

        /// <summary>One line for the log and the command bar: how many heard, how many followed, who did not.</summary>
        public static string Summary(MasterCommand c, int heard, IReadOnlyList<string> refused)
        {
            int followed = heard - (refused?.Count ?? 0);
            string verb = c switch
            {
                MasterCommand.Cheer => "힘을 냈습니다",
                MasterCommand.Focus => "달려듭니다",
                MasterCommand.Rally => "깃발을 따릅니다",
                _ => "물러납니다",
            };
            string head = followed == heard ? $"{Label(c)}: {heard}명 모두 {verb}." : $"{Label(c)}: {heard}명 중 {followed}명이 {verb}.";
            if (refused == null || refused.Count == 0) return head;
            return head + $" ({string.Join(", ", refused)}은(는) 따르지 않음)";
        }
    }
}
