using GuildMaster.Adventurers;
using GuildMaster.Core;

namespace GuildMaster.Welfare
{
    /// <summary>Wording of the care system (F-2), kept apart from the logic so it can be tested and reworded in one place.</summary>
    public static class CareTexts
    {
        /// <summary>Subject particle by final consonant: "펠릭스가", "아리스". Names come out right whatever they end in.</summary>
        static string Ga(string word) => word + MemoryRecorder.Josa(word, "이", "가");

        /// <summary>Object particle by final consonant.</summary>
        static string Reul(string word) => word + MemoryRecorder.Josa(word, "을", "를");

        // ---------------- Demands ----------------

        public static string DemandTitle(CareDemand d) => d switch
        {
            CareDemand.Talk => "면담 요구",
            CareDemand.Leave => "휴가 요구",
            CareDemand.Bonus => "보너스 요구",
            _ => "요구",
        };

        public static string DemandNotice(string name, CareDemand d, int stress) => d switch
        {
            CareDemand.Talk => $"{Ga(name)} 길드 마스터와 이야기하고 싶어 합니다. (스트레스 {stress})",
            CareDemand.Leave => $"{Ga(name)} 휴가를 원합니다. (스트레스 {stress})",
            CareDemand.Bonus => $"{Ga(name)} 보너스를 원합니다. (스트레스 {stress})",
            _ => $"{Ga(name)} 뭔가를 원합니다.",
        };

        /// <summary>What the member says out loud when asking.</summary>
        public static string DemandLine(CareDemand d) => d switch
        {
            CareDemand.Talk => "마스터, 잠깐 얘기 좀 할 수 있을까요?",
            CareDemand.Leave => "며칠만이라도 쉬고 싶어요...",
            CareDemand.Bonus => "일한 만큼은 받아야 힘이 나죠.",
            _ => "",
        };

        public static string DemandIgnored(string name, int grudges)
            => grudges >= CareRules.QuitGrudges
                ? $"{name}의 요구를 들어주지 않아 불만이 쌓였습니다. 이대로면 길드를 떠날 수 있습니다."
                : $"{name}의 요구를 들어주지 않아 불만이 생겼습니다.";

        public static string ResignNotice(string name)
            => $"{Ga(name)} 길드를 떠날 생각을 하고 있습니다. {CareRules.ResignNoticeHours:0}시간 안에 면담이나 휴가로 마음을 풀어 주세요.";

        public static string LimitWarning(string name, int stress)
            => $"{name}의 스트레스가 한계입니다. (스트레스 {stress}) 더 두면 의뢰를 포기하거나 길드를 떠날 수 있습니다.";

        public static string Sabotage(string name, string questTitle)
            => $"{Ga(name)} 스트레스를 못 이기고 '{questTitle}' 의뢰를 포기했습니다.";

        public static string SabotageLine => "더는 못 하겠어! 돌아가겠다!";

        public static string Quit(string name, QuitCause cause)
            => cause == QuitCause.Neglect
                ? $"{Ga(name)} 계속된 무관심에 실망해 길드를 떠났습니다."
                : $"{Ga(name)} 스트레스를 견디지 못하고 길드를 떠났습니다.";

        public static string QuitLine(QuitCause cause)
            => cause == QuitCause.Neglect ? "이 길드에는 더 있을 이유가 없네요. 그동안 고마웠어요." : "죄송합니다. 더는 못 버티겠어요.";

        public static string LeaveEndedLine => "푹 쉬었다. 다시 일해야지!";

        // ---------------- The guild master's replies ----------------

        static readonly string[] TalkDefault = { "고맙습니다. 좀 나아졌어요.", "이야기하고 나니 마음이 가벼워졌습니다." };
        static readonly string[] TalkMoney = { "말씀은 고마운데... 결국 일한 만큼 받는 게 제일이죠.", "말씀만으로도 조금은 풀리네요. 조금만요." };
        static readonly string[] TalkHonor = { "제 노력을 알아봐 주시니 힘이 납니다.", "인정받는 기분이네요. 다시 해 보겠습니다." };
        static readonly string[] TalkFriends = { "이렇게 들어 주시니 마음이 놓입니다.", "다 같이 버틴다고 생각하면 견딜 만해요." };
        static readonly string[] TalkAdventure = { "답답했는데 얘기하고 나니 좀 낫네요. 새 의뢰가 기대돼요.", "바람이라도 쐬고 싶었는데, 한결 낫습니다." };
        static readonly string[] TalkSafety = { "솔직히 무서웠습니다. 들어 주셔서 감사합니다.", "너무 무리하지 않게만 배려해 주세요." };
        static readonly string[] TalkKnowledge = { "생각을 정리하니 한결 낫습니다.", "이야기하다 보니 뭐가 걱정이었는지 알겠네요." };

        public static string InterviewReply(string values, GameRandom rng)
        {
            var lines = values switch
            {
                "돈" => TalkMoney,
                "명예" => TalkHonor,
                "동료" => TalkFriends,
                "모험" => TalkAdventure,
                "안전" => TalkSafety,
                "지식" => TalkKnowledge,
                _ => TalkDefault,
            };
            return rng != null ? rng.Pick(lines) : lines[0];
        }

        public static string LeaveReply => "배려 감사합니다. 푹 쉬고 오겠습니다.";

        public static string BonusReply(string values) => values switch
        {
            "돈" => "역시 마스터! 이 정도면 기운이 나죠.",
            "명예" => "성의는 고맙습니다. 인정해 주셨다는 게 더 기쁩니다.",
            _ => "감사히 받겠습니다. 힘내서 일할게요.",
        };

        // ---------------- Results shown in the management window ----------------

        public static string Relieved(string what, float relief, int before, int after)
            => $"{what}: 스트레스 -{relief:0} ({before} → {after})";

        public static string BonusResult(int gold, float relief, int before, int after)
            => $"보너스 {gold}G를 지급했습니다. 스트레스 -{relief:0} ({before} → {after})";

        public static string Fired(string name) => $"{Reul(name)} 해고했습니다.";

        // ---------------- Memories (what the member remembers, and the LLM reads) ----------------

        public static string MemoryTalked => "길드 마스터와 면담을 하며 속을 털어놓았다";
        public static string MemoryLeave => "길드 마스터가 휴가를 주어 푹 쉬었다";
        public static string MemoryBonus(int gold) => $"길드 마스터에게 보너스 {gold}G를 받았다";
        public static string MemoryAsked(CareDemand d) => $"스트레스가 쌓여 길드 마스터에게 {Reul(CareRules.Label(d))} 청했다";
        public static string MemoryIgnored(CareDemand d) => $"{CareRules.Label(d)} 요청을 길드 마스터가 들어주지 않았다";
        public static string MemoryFriendLeft(string name) => $"동료 {Ga(name)} 길드를 떠났다";
    }
}
