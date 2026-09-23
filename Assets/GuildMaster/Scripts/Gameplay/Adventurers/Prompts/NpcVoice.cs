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

        /// <summary>"성격: ..." (from MBTI and values) plus "특징: ..." (traits), ready to drop into a prompt.</summary>
        public static string Sheet(Adventurer a)
            => Sheet(a.Data.mbti, a.Data.values, a.Traits != null ? a.Traits.PromptHints : null);

        public static string Sheet(string mbti, string values, IEnumerable<string> traitHints = null)
        {
            var sb = new StringBuilder("성격: ");
            string temper = Temperament(mbti);
            if (temper.Length > 0) sb.Append(temper).Append(' ');
            sb.Append(ValueSentence(values));
            var hints = traitHints?.Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
            if (hints != null && hints.Count > 0) sb.Append("\n특징: ").Append(string.Join(" ", hints));
            return sb.ToString();
        }

        /// <summary>How the person talks and carries themselves, from the four MBTI letters (empty if unknown).</summary>
        public static string Temperament(string mbti)
        {
            if (string.IsNullOrEmpty(mbti) || mbti.Length < 4) return "";
            char ei = char.ToUpperInvariant(mbti[0]), sn = char.ToUpperInvariant(mbti[1]);
            char tf = char.ToUpperInvariant(mbti[2]), jp = char.ToUpperInvariant(mbti[3]);
            var parts = new[]
            {
                ei == 'E' ? "사교적이고 말이 많은 편이다." : "낯을 가리고 말수가 적은 편이다.",
                tf == 'F' ? "감정이 말투에 잘 묻어나고 상대의 기분을 살핀다." : "감정보다 이유를 따지고 담백하게 말한다.",
                sn == 'N' ? "가끔 엉뚱한 상상이나 비유를 던진다." : "눈앞의 현실적인 이야기를 즐긴다.",
                jp == 'J' ? "말투가 차분하고 정돈돼 있다." : "말투가 즉흥적이고 가볍다.",
            };
            return string.Join(" ", parts);
        }

        /// <summary>What the person cares about. Shows only when the topic touches it.</summary>
        public static string ValueSentence(string values)
        {
            string core = values switch
            {
                "돈" => "현실적이고 셈이 빠르다. 돈 이야기에는 귀가 솔깃하다.",
                "명예" => "인정받고 이름을 남기고 싶어 하며 자존심이 있다.",
                "동료" => "사람을 소중히 여기고, 함께할 사람들이 어떤지 궁금해한다.",
                "모험" => "새로운 곳과 낯선 일에 호기심이 많다.",
                "안전" => "신중하고 위험한 일은 꺼린다. 확실한 것을 좋아한다.",
                "지식" => "배우고 알아 가는 것을 좋아하고 궁금한 것이 많다.",
                _ => string.IsNullOrEmpty(values) ? "" : $"'{values}'을(를) 소중히 여긴다.",
            };
            return core.Length == 0 ? "" : core + " 다만 이런 성향은 이야기가 그쪽으로 흐를 때만 드러나고, 평소엔 평범하게 대화한다.";
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
