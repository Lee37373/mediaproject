using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.LLM;

namespace GuildMaster.Adventurers.Prompts
{
    /// <summary>
    /// Shared building blocks for prompts where an NPC speaks (recruit talk, persuasion, small talk).
    /// The model gets the NPC as a person, not as data: temperament and values are described in
    /// words (raw "ENTP" / "가치: 돈" made every answer steer to money), game numbers become plain
    /// feelings, and earlier turns of a conversation go in as real chat turns.
    /// </summary>
    public static class NpcVoice
    {
        // ---- the character sheet ----

        /// <summary>
        /// "성격: ..." (from MBTI and values) plus "특징: ..." (traits and the speech habits they bring),
        /// ready to drop into a prompt.
        /// </summary>
        public static string Sheet(Adventurer a)
            => Sheet(a.Data.mbti, a.Data.values, a.Traits != null ? a.Traits.PromptHints : null, a.Data.traitIds);

        public static string Sheet(string mbti, string values, IEnumerable<string> traitHints = null, IEnumerable<string> traitIds = null)
        {
            var sb = new StringBuilder("성격: ");
            string temper = Temperament(mbti);
            if (temper.Length > 0) sb.Append(temper).Append(' ');
            sb.Append(ValueSentence(values));
            var hints = traitHints?.Where(h => !string.IsNullOrWhiteSpace(h)).ToList() ?? new List<string>();
            var habits = TraitHabits(traitIds);
            if (hints.Count > 0 || habits.Length > 0)
            {
                sb.Append("\n특징: ").Append(string.Join(" ", hints));
                if (habits.Length > 0) sb.Append(hints.Count > 0 ? " " : "").Append(habits);
            }
            return sb.ToString();
        }

        // Made much more concrete (user report 2026-09-29: "대화가 성격이나 가치관에 상관없이 너무 유사").
        // Each letter now changes something the model can actually do in one short line — length,
        // what it asks back, how it takes praise — instead of an adjective it smooths over.

        /// <summary>How the person talks and carries themselves, from the four MBTI letters (empty if unknown).</summary>
        public static string Temperament(string mbti)
        {
            if (string.IsNullOrEmpty(mbti) || mbti.Length < 4) return "";
            char ei = char.ToUpperInvariant(mbti[0]), sn = char.ToUpperInvariant(mbti[1]);
            char tf = char.ToUpperInvariant(mbti[2]), jp = char.ToUpperInvariant(mbti[3]);
            var parts = new[]
            {
                ei == 'E'
                    ? "사교적이고 말이 많은 편이다. 묻지 않은 자기 이야기도 곁들이고, 상대에게 자주 되묻는다."
                    : "낯을 가리고 말수가 적은 편이다. 먼저 묻는 일이 드물고, 대답을 짧게 끊거나 말끝을 흐린다.",
                tf == 'F'
                    ? "감정이 말투에 잘 묻어나고 상대의 기분을 살핀다. 고맙다, 걱정된다, 서운하다 같은 말을 쉽게 하고 칭찬에 약하다."
                    : "감정보다 이유를 따지고 담백하게 말한다. 조건과 근거를 묻고, 빈말이나 칭찬에는 잘 넘어가지 않는다.",
                sn == 'N'
                    ? "가끔 엉뚱한 상상이나 비유를 던지고, 앞날이나 큰 그림 이야기를 좋아한다."
                    : "눈앞의 현실적인 이야기를 즐기고, 얼마·언제·어디서처럼 구체적인 것을 묻는다.",
                jp == 'J'
                    ? "말투가 차분하고 정돈돼 있다. 약속과 계획을 중시해 애매한 대답을 싫어한다."
                    : "말투가 즉흥적이고 가볍다. 농담을 섞고 화제를 이리저리 옮긴다.",
            };
            return string.Join(" ", parts);
        }

        /// <summary>
        /// What the person cares about, as an attitude: what they weigh every offer by, what they bring up
        /// themselves and what puts them off. It colours the whole talk (it used to be told to show "only
        /// when the topic touches it", which is why every visitor sounded alike); saying it outright is
        /// still left for when they open up or are asked.
        /// </summary>
        public static string ValueSentence(string values)
        {
            string core = values switch
            {
                "돈" => "현실적이고 셈이 빠르다. 돈 이야기에는 귀가 솔깃하다. 어떤 제안이든 결국 얼마를 받는지, 비용은 누가 대는지로 따져 보고, 손해 보는 조건이면 흥정부터 한다.",
                "명예" => "인정받고 이름을 남기고 싶어 하며 자존심이 있다. 자기 실력과 이력을 내세우고, 길드가 얼마나 이름난 곳인지, 큰일을 맡을 수 있는지를 따진다. 무시당하면 발끈한다.",
                "동료" => "사람을 소중히 여기고, 함께할 사람들이 어떤지 궁금해한다. 조건보다 길드 분위기와 길드원들이 서로 어떻게 지내는지를 먼저 묻고, 따뜻한 말에 마음이 열린다.",
                "모험" => "새로운 곳과 낯선 일에 호기심이 많다. 아직 아무도 안 가 본 곳, 이상한 소문, 강한 몬스터 이야기에 신이 나고, 지루하고 반복적인 일은 대놓고 싫어한다.",
                "안전" => "신중하고 위험한 일은 꺼린다. 확실한 것을 좋아한다. 다치거나 죽은 사람이 있었는지, 무리한 일을 억지로 시키지 않는지, 치료와 휴식은 되는지를 꼼꼼히 확인하고, 허세 섞인 말엔 오히려 불안해한다.",
                "지식" => "배우고 알아 가는 것을 좋아하고 궁금한 것이 많다. 유적, 오래된 기록, 몬스터의 습성처럼 알아 갈 거리가 있는지 묻고, 대답을 되받아 파고들며, 성의 없는 대답엔 실망한다.",
                _ => string.IsNullOrEmpty(values) ? "" : $"'{values}'을(를) 소중히 여긴다.",
            };
            return core.Length == 0 ? "" : core + " 이런 속내는 태도와 고르는 화제에 늘 묻어나지만, 한마디로 딱 잘라 밝히는 건 마음을 열었거나 직접 질문받았을 때다.";
        }

        /// <summary>What this person brings up or presses on by themselves in a first talk (recruitment).</summary>
        public static string ValueAgenda(string values) => values switch
        {
            "돈" => "보수, 계약금, 전리품 분배, 비용을 누가 대는지",
            "명예" => "길드의 명성, 큰 의뢰나 강적, 네 실력을 알아봐 주는지",
            "동료" => "길드원들이 어떤 사람인지, 분위기, 서로 챙겨 주는지",
            "모험" => "미개척지, 소문난 던전, 강하거나 희귀한 몬스터",
            "안전" => "다치거나 죽은 사람이 있었는지, 위험한 의뢰를 거절할 수 있는지, 치료와 휴식",
            "지식" => "유적, 오래된 기록, 몬스터의 습성, 배울 수 있는 것",
            _ => "길드가 너에게 무엇을 해 줄 수 있는지",
        };

        /// <summary>
        /// How a trait shows in the way someone talks (the trait's prompt hint says what they are like;
        /// this says what it sounds like). Unknown ids add nothing.
        /// </summary>
        public static string TraitHabit(string traitId) => traitId switch
        {
            "brave" => "강한 적이나 위험한 일 이야기가 나오면 눈을 반짝이고 무용담을 늘어놓는다.",
            "sturdy" => "몸이 튼튼하다는 걸 은근히 자랑한다.",
            "marksman" => "자기 솜씨가 정확하다는 데 자부심이 있어 기회만 되면 내세운다.",
            "diligent" => "무슨 일이든 끝까지 해내겠다는 태도로, 할 일을 구체적으로 묻는다.",
            "quick_learner" => "새로 배울 수 있는지, 얼마나 빨리 성장할 수 있는지에 관심을 보인다.",
            "nimble" => "몸놀림이 빠르다는 걸 자랑하듯 말한다.",
            "hawkeye" => "눈썰미가 좋아 상대나 주변의 사소한 것을 짚어 말하곤 한다.",
            "sociable" => "처음 만난 사이에도 금방 친근하게 굴고, 길드 사람들 이야기를 듣고 싶어 한다.",
            "loyal" => "믿을 만한 곳인지 진지하게 확인하고, 한번 정하면 끝까지 간다는 말을 한다.",
            "coward" => "위험한 이야기에 움찔하고, 다치거나 죽을 일은 없는지 거듭 확인한다.",
            "frail" => "몸이 약한 걸 신경 써서 무리한 일은 못 한다는 말을 흘린다.",
            "clumsy" => "실수가 잦은 걸 스스로 알아 자신 없는 말투가 섞인다.",
            "lazy" => "귀찮은 티를 내고, 쉬는 시간이나 편한 일부터 묻는다.",
            "greedy" => "무슨 이야기든 자기 몫과 보상으로 끌고 가고 조건을 흥정하려 든다.",
            "sluggish" => "서두르는 걸 싫어하고 느긋하게 군다.",
            "loner" => "여럿이 몰려다니는 걸 꺼려 혼자 움직일 수 있는지 묻고, 대답이 퉁명스럽다.",
            "arrogant" => "자기 실력을 은근히 자랑하고 상대를 한 수 아래로 보며 떠본다. 칭찬은 당연하게 받아들인다.",
            _ => "",
        };

        public static string TraitHabits(IEnumerable<string> traitIds)
            => traitIds == null ? "" : string.Join(" ", traitIds.Select(TraitHabit).Where(h => h.Length > 0));

        // ---- how a visitor addresses the guild master (recruitment) ----

        /// <summary>Speech level towards a stranger who is courting them: the most audible difference between visitors.</summary>
        public enum Register { Warm, Formal, Curt, Haughty }

        public static Register RecruitRegister(string mbti, IEnumerable<string> traitIds)
        {
            var ids = traitIds?.ToList() ?? new List<string>();
            if (ids.Contains("arrogant")) return Register.Haughty;
            bool introvert = !string.IsNullOrEmpty(mbti) && char.ToUpperInvariant(mbti[0]) == 'I';
            bool thinker = mbti != null && mbti.Length >= 3 && char.ToUpperInvariant(mbti[2]) == 'T';
            bool judging = mbti != null && mbti.Length >= 4 && char.ToUpperInvariant(mbti[3]) == 'J';
            if (ids.Contains("loner") || (introvert && thinker)) return Register.Curt;
            if (thinker && judging) return Register.Formal;
            return Register.Warm;
        }

        /// <summary>The "how you speak" rule for a recruitment talk: speech level plus length.</summary>
        public static string RegisterRule(Register register, string mbti)
        {
            string level = register switch
            {
                Register.Haughty => "길드 마스터를 한 수 아래로 보고 반말 섞인 거만한 말투(\"~지\", \"~거든\", \"뭐, 나쁘지 않네\")로 말한다.",
                Register.Curt => "무뚝뚝하고 짧은 존댓말(\"~요.\")로 군더더기 없이 말한다. 먼저 친한 척하지 않는다.",
                Register.Formal => "딱딱하고 정중한 \"~습니다/~습니까\" 체로 말한다.",
                _ => "밝고 친근한 해요체로, \"와\", \"오\" 같은 감탄사를 섞어 말한다.",
            };
            bool introvert = !string.IsNullOrEmpty(mbti) && char.ToUpperInvariant(mbti[0]) == 'I';
            string length = introvert ? "대개 한 문장, 짧으면 몇 마디로 끝낸다." : "보통 두세 문장으로 말한다.";
            return level + " " + length;
        }

        /// <summary>
        /// A sample reply to "여기서 뭐 하세요?" from someone with the same values and speech level, so the
        /// model's first lines differ per visitor (a single shared example pulled everyone into one voice).
        /// </summary>
        public static string ExampleReply(string values, Register register)
        {
            int r = (int)register;
            string[] lines = values switch
            {
                "돈" => new[]
                {
                    "일거리 찾고 있었어요! 근데 그쪽 길드는 한 건 하면 얼마나 쳐줘요?",
                    "보수가 괜찮은 의뢰를 찾고 있습니다. 귀 길드의 보수 조건부터 듣고 싶군요.",
                    "일거리요. …보수는 얼마예요?",
                    "일감 좀 보고 있었지. 날 쓰려면 싸게는 안 될 거야.",
                },
                "명예" => new[]
                {
                    "제 이름 걸 만한 일을 찾고 있어요! 그쪽 길드, 좀 알려진 곳이에요?",
                    "이름을 걸 만한 일을 찾고 있습니다. 귀 길드는 어떤 큰일을 해 왔습니까?",
                    "제 실력에 맞는 일을 찾아요. 큰 의뢰, 있어요?",
                    "나한테 어울리는 무대를 찾는 중이지. 너희 길드가 그 정도는 되나?",
                },
                "동료" => new[]
                {
                    "같이 다닐 사람들을 찾고 있었어요. 길드엔 어떤 분들이 있어요? 다들 사이 좋아요?",
                    "믿고 등을 맡길 동료를 찾고 있습니다. 길드원들은 어떤 분들입니까?",
                    "…같이 일할 사람들이 어떤지가 궁금해요.",
                    "쓸 만한 동료가 있나 보는 중이야. 너희 길드엔 누가 있는데?",
                },
                "모험" => new[]
                {
                    "지도에 없는 데를 찾고 있어요! 이 근처에 아직 아무도 안 가 본 곳 있어요?",
                    "아직 가 보지 못한 곳을 찾고 있습니다. 이 지방에 미개척지가 있습니까?",
                    "안 가 본 곳이요. 그런 의뢰 있어요?",
                    "이 동네는 좀 시시해서 말이야. 짜릿한 데 없어?",
                },
                "안전" => new[]
                {
                    "위험하지 않은 일을 알아보고 있었어요. 혹시 무서운 의뢰를 억지로 맡기고 그러진 않죠?",
                    "무리 없는 일을 찾고 있습니다. 길드원이 크게 다친 일은 없었습니까?",
                    "…위험한 건 싫어요. 다친 사람 많아요?",
                    "괜히 목숨 걸 생각은 없어. 너희 길드, 사람 함부로 굴리는 데 아니지?",
                },
                "지식" => new[]
                {
                    "이 지방 유적 기록을 모으고 있어요! 혹시 길드에 오래된 자료 같은 거 있어요?",
                    "이 지방의 옛 기록을 조사하고 있습니다. 길드에 참고할 만한 자료가 있습니까?",
                    "조사 중이에요. 몬스터 기록 같은 거 있어요?",
                    "여긴 아는 사람이 별로 없는 동네 같던데. 너희 길드엔 좀 배울 게 있나?",
                },
                _ => new[]
                {
                    "잠깐 쉬어 가는 중이에요. 그쪽은 무슨 일이세요?",
                    "잠시 들렀습니다. 무슨 용건이십니까?",
                    "그냥 들렀어요.",
                    "지나가던 길이야. 왜?",
                },
            };
            return lines[r];
        }

        // ---- game numbers as plain feelings ----

        /// <summary>Body, mind and purse in a sentence (no raw numbers).</summary>
        public static string Condition(Adventurer a)
            => Condition(a.HpRatio, a.Data.fatigue, a.Data.stress, a.Data.gold);

        public static string Condition(float hpRatio, float fatigue, float stress, int gold)
        {
            var parts = new List<string>
            {
                hpRatio >= 0.8f ? "몸은 멀쩡하다" : hpRatio >= 0.5f ? "몸이 조금 상했다" : "상처가 깊어 몸이 무겁다",
            };
            if (fatigue >= 70f) parts.Add("많이 지쳐 있다");
            else if (fatigue >= 40f) parts.Add("약간 피곤하다");
            if (stress >= 70f) parts.Add("신경이 곤두서 있다");
            else if (stress >= 40f) parts.Add("마음이 좀 무겁다");
            parts.Add(gold < 40 ? "주머니 사정이 빠듯하다" : gold < 120 ? "돈은 그럭저럭 있다" : "돈 걱정은 없다");
            return string.Join(", ", parts) + ".";
        }

        /// <summary>How the visitor feels about the guild master (affinity 0-100), in the bands the verdict uses (join needs 50).</summary>
        public static string RecruitMood(int affinity)
            => affinity < 25 ? "이 사람이 좀 불쾌하다. 쌀쌀맞게 대하지만, 무례한 말을 듣기 전에는 대화를 먼저 끝내지 않는다."
             : affinity < 40 ? "아직 경계하고 있다. 믿음이 가지 않는다."
             : affinity < 50 ? "반반이다. 관심은 가지만 마음이 정해지지 않았다."
             : affinity < 65 ? "호감이 간다. 조건이 괜찮으면 들어갈 수도 있다."
             : affinity < 85 ? "많이 끌린다. 들어가도 좋겠다는 생각이 든다."
             : "이미 마음을 정했다. 들어가고 싶다.";

        /// <summary>How willing the member is to take the quest (willingness vs. the bar for a yes), as a feeling.</summary>
        public static string PersuadeMood(int willingness, int threshold)
        {
            int gap = willingness - threshold;
            return gap >= 15 ? "맡아도 좋겠다는 마음이 굳었다."
                 : gap >= 0 ? "맡아도 되겠다는 쪽으로 기울었다."
                 : gap >= -15 ? "아직 망설여진다. 조금만 더 확신이 들면 맡을 수 있다."
                 : gap >= -35 ? "내키지 않는다. 지금 조건으로는 맡고 싶지 않다."
                 : "맡을 마음이 전혀 없다.";
        }

        /// <summary>How one member talks to another, from the feeling towards them (see <see cref="Relationships.Label"/>).</summary>
        public static string ToneToward(int feeling)
            => feeling >= 40 ? "허물없고 다정한 반말로, 농담도 섞어 가며"
             : feeling >= 15 ? "편안하고 친근한 반말로"
             : feeling > -15 ? "예의를 지키되 담백한 존댓말(해요체)로"
             : feeling > -40 ? "퉁명스럽고 가시 돋친 반말로"
             : "차갑고 날 선 짧은 반말로, 쏘아붙이듯";

        // ---- conversation as real chat turns ----

        /// <summary>Spoken when the transcript would otherwise begin with the NPC's own line (chat turns must start with the user).</summary>
        public const string OpeningUser = "(길드 마스터가 말을 건다.)";

        /// <summary>
        /// Fills <c>Messages</c> (earlier turns) and <c>User</c> (the newest player line plus an optional
        /// aside) from a transcript. Same-speaker neighbours are merged so the turns alternate, and the
        /// last turn is always the player's.
        /// </summary>
        public static void SetChat(LlmRequest req, IEnumerable<(bool fromPlayer, string text)> turns, int maxLines, string aside = null)
        {
            var lines = new List<(bool user, string text)>();
            foreach (var (fromPlayer, text) in turns.TakeLast(maxLines))
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                string t = text.Trim();
                if (lines.Count > 0 && lines[lines.Count - 1].user == fromPlayer)
                    lines[lines.Count - 1] = (fromPlayer, lines[lines.Count - 1].text + " " + t);
                else lines.Add((fromPlayer, t));
            }
            if (lines.Count > 0 && !lines[0].user) lines.Insert(0, (true, OpeningUser));
            if (lines.Count == 0 || !lines[lines.Count - 1].user) lines.Add((true, "(잠시 말이 없다.)"));

            var last = lines[lines.Count - 1];
            req.Messages = lines.Take(lines.Count - 1).Select(l => new LlmMessage(l.user, l.text)).ToList();
            req.User = string.IsNullOrEmpty(aside) ? last.text : last.text + "\n" + aside;
        }
    }
}
