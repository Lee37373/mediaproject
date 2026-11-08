using System;
using System.Collections.Generic;

namespace GuildMaster.Adventurers
{
    /// <summary>What one member is to another in a fight (P4-131): a member can be several of these to the same one.</summary>
    public enum BondKind { Friend, Foe, Rival, Mentor, Pupil }

    /// <summary>A member's tally against one rival (P4-131): races won and lost. Saved with the member.</summary>
    [Serializable]
    public class RivalRecord
    {
        public string id;
        public int won, lost;
    }

    /// <summary>
    /// How the members' feelings for each other show in a fight (P4-131, F-12 — user decisions 2026-11-08: friends and foes,
    /// rivals and mentors, all three; small numbers, the show is in the lines and the effects; party making looks at the
    /// whole party; the detail window says who is what). Pure: who is what to whom, and what it does.
    /// <list type="bullet">
    /// <item>Friends (both ways at <see cref="FriendAt"/> or more), fighting within <see cref="Range"/> of each other: attack +10%.</item>
    /// <item>Foes (either way at <see cref="FoeAt"/> or below), fighting that close: they get in each other's way, attack −10%
    /// each, and a quarrel once the fight is over.</item>
    /// <item>Rivals (both ways awkward, not hostile, levels within <see cref="RivalLevelGap"/>), fighting in the same place:
    /// a race for kills — the one behind holds out longer before pulling back, the winner of a race is pleased, the loser is not.</item>
    /// <item>A mentor (<see cref="MentorLevelGap"/> levels or more above, both ways at <see cref="MentorFeeling"/> or more) near
    /// a kill: the pupil's EXP from it ×<see cref="MentorExp"/>.</item>
    /// </list>
    /// </summary>
    public static class BondRules
    {
        public const int FriendAt = 40;
        public const int FoeAt = -40;
        /// <summary>Rivals: both ways at this or below (awkward), but neither hostile.</summary>
        public const int RivalAt = -15;
        public const int RivalLevelGap = 3;
        public const int MentorLevelGap = 5;
        public const int MentorFeeling = 15;

        /// <summary>Friends and foes count when they fight this close (cells).</summary>
        public const float Range = 4f;
        public const float FriendShare = 0.10f, FoeShare = 0.10f;
        public const float MentorExp = 1.15f;

        /// <summary>A rival's kill counts in a race when the other is fighting within this many cells.</summary>
        public const float RivalRange = 12f;
        /// <summary>Behind in a race: the retreat threshold is this much lower, not below <see cref="MinRetreat"/>.</summary>
        public const float RivalPush = 0.10f, MinRetreat = 0.15f;
        /// <summary>A race is over when nobody has scored for this long (seconds), or the two are far apart.</summary>
        public const float ContestIdleSeconds = 20f;
        /// <summary>A race with fewer kills between the two is not counted.</summary>
        public const int ContestMinKills = 2;

        public const float QuarrelChance = 0.6f, QuarrelCooldownHours = 6f;
        public const float LineChance = 0.35f, LineCooldownHours = 2f, MentorLineCooldownHours = 6f;

        public static bool Friends(int ab, int ba) => ab >= FriendAt && ba >= FriendAt;

        public static bool Foes(int ab, int ba) => ab <= FoeAt || ba <= FoeAt;

        public static bool Rivals(int ab, int ba, int levelA, int levelB)
            => ab <= RivalAt && ba <= RivalAt && !Foes(ab, ba) && Math.Abs(levelA - levelB) <= RivalLevelGap;

        /// <summary>Is the first the second's mentor.</summary>
        public static bool Mentors(int mentorLevel, int pupilLevel, int mentorToPupil, int pupilToMentor)
            => mentorLevel - pupilLevel >= MentorLevelGap && mentorToPupil >= MentorFeeling && pupilToMentor >= MentorFeeling;

        /// <summary>What it is to the other, by their feelings both ways and their levels (its own first).</summary>
        public static List<BondKind> Kinds(int mine, int theirs, int myLevel, int theirLevel)
        {
            var kinds = new List<BondKind>();
            if (Friends(mine, theirs)) kinds.Add(BondKind.Friend);
            if (Foes(mine, theirs)) kinds.Add(BondKind.Foe);
            if (Rivals(mine, theirs, myLevel, theirLevel)) kinds.Add(BondKind.Rival);
            if (Mentors(theirLevel, myLevel, theirs, mine)) kinds.Add(BondKind.Mentor);
            if (Mentors(myLevel, theirLevel, mine, theirs)) kinds.Add(BondKind.Pupil);
            return kinds;
        }

        /// <summary>The share added to its attack: a friend fighting beside it, less a foe in its way.</summary>
        public static float AttackShare(bool friendNear, bool foeNear) => (friendNear ? FriendShare : 0f) - (foeNear ? FoeShare : 0f);

        /// <summary>The retreat threshold of one behind in a race.</summary>
        public static float RetreatWhenBehind(float threshold) => Math.Max(Math.Min(threshold, MinRetreat), threshold - RivalPush);

        /// <summary>How a race went for the first of the two: +1 won, −1 lost, 0 a draw or too few kills to count.</summary>
        public static int ContestResult(int mine, int theirs)
            => mine + theirs < ContestMinKills || mine == theirs ? 0 : mine > theirs ? 1 : -1;

        /// <summary>
        /// How a member would fit those already in a party (two-way feelings with each): null when it and one of them are foes
        /// (it is not asked), otherwise the average of the two-way feelings (0 for nobody) — friends of the party first.
        /// </summary>
        public static float? PartyFit(IEnumerable<(int toThem, int fromThem)> feelings)
        {
            float sum = 0f;
            int n = 0;
            foreach (var (to, from) in feelings)
            {
                if (Foes(to, from)) return null;
                sum += (to + from) / 2f;
                n++;
            }
            return n == 0 ? 0f : sum / n;
        }

        // ---------------- Words ----------------

        public static string Label(BondKind k) => k switch
        {
            BondKind.Friend => "친구",
            BondKind.Foe => "앙숙",
            BondKind.Rival => "라이벌",
            BondKind.Mentor => "스승",
            _ => "제자",
        };

        /// <summary>What the bond does, for the detail window.</summary>
        public static string Effect(BondKind k) => k switch
        {
            BondKind.Friend => $"가까이서 함께 싸우면 공격 +{FriendShare * 100f:0}%",
            BondKind.Foe => $"가까이서 함께 싸우면 공격 −{FoeShare * 100f:0}%, 싸움 뒤 말다툼",
            BondKind.Rival => "같은 싸움에서 처치 경쟁, 지고 있으면 후퇴를 늦춤",
            BondKind.Mentor => $"곁에서 잡으면 경험치 +{(MentorExp - 1f) * 100f:0}%",
            _ => $"곁에서 잡으면 제자의 경험치 +{(MentorExp - 1f) * 100f:0}%",
        };

        static readonly string[] FriendLines = { "{0}, 등은 내가 맡을게!", "{0}랑 같이라면 무섭지 않아!", "가자, {0}!", "{0}, 이번에도 잘 부탁해!" };
        static readonly string[] FoeLines = { "{0}, 거기 비켜!", "하필 {0}랑 같이 싸우게 되다니…", "{0}, 내 앞 막지 마!" };
        static readonly (string first, string answer)[] Quarrels =
        {
            ("{0}, 네가 방해만 안 했어도 금방 끝났어.", "누가 할 소리! 너야말로 발목 잡지 마."),
            ("{0}, 아까 그거 일부러 그런 거지?", "착각도 정도껏 해."),
            ("다음엔 {0}랑 같이 안 나갈 거야.", "바라던 바야."),
        };
        static readonly string[] AheadLines = { "{0}, 이번엔 내가 앞섰다!", "봤어, {0}? 하나 더!", "{0}, 따라올 수 있겠어?" };
        static readonly string[] BehindLines = { "{0}한테 질 순 없지!", "아직 안 끝났어, {0}!", "{0}보다 하나라도 더…!" };
        static readonly string[] MentorLines = { "잘했어, {0}! 그렇게 하는 거야.", "{0}, 방금 그 움직임 좋았어.", "{0}, 숨 고르고 다음!" };

        public static string FriendLine(string other, int pick) => Fill(FriendLines, other, pick);
        public static string FoeLine(string other, int pick) => Fill(FoeLines, other, pick);
        public static (string first, string answer) Quarrel(string other, int pick)
        {
            var q = Quarrels[Index(pick, Quarrels.Length)];
            return (Put(q.first, other), Put(q.answer, other));
        }
        public static string AheadLine(string other, int pick) => Fill(AheadLines, other, pick);
        public static string BehindLine(string other, int pick) => Fill(BehindLines, other, pick);
        public static string MentorLine(string pupil, int pick) => Fill(MentorLines, pupil, pick);
        public static string WonLine(string other) => $"{other}, 이번 승부는 내 승리야.";
        public const string LostLine = "…다음엔 안 져.";

        static string Fill(string[] lines, string name, int pick) => Put(lines[Index(pick, lines.Length)], name);
        static int Index(int pick, int count) => ((pick % count) + count) % count;

        /// <summary>The name in, with "랑" or "이랑" after it as it ends.</summary>
        static string Put(string line, string name)
        {
            name ??= "";
            return line.Replace("{0}랑", name + MemoryRecorder.Josa(name, "이랑", "랑")).Replace("{0}", name);
        }
    }
}
