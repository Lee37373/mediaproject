using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GuildMaster.Adventurers;
using GuildMaster.Adventurers.Prompts;
using GuildMaster.Core;
using GuildMaster.LLM;
using GuildMaster.Session;

namespace GuildMaster.Social
{
    /// <summary>
    /// Prompt, DTO and grammar for a town talk (user request 2026-10-01): four lines between any two people in town
    /// — a, b, a, b — about what <see cref="TownTalkRules.ChooseKind"/> picked, and how it ended
    /// (<see cref="TalkOutcome"/>). Written as one request so a whole exchange costs one generation.
    /// </summary>
    public static class TownTalkPrompt
    {
        public const int LineMaxChars = 56;
        public const int Lines = 4;

        public const string SystemPrompt =
            "너는 판타지 세계의 한 마을에서 두 사람 a, b가 나누는 짧은 대화를 쓴다. 실제 사람끼리 말하듯 자연스러워야 한다.\n" +
            "- 네 줄이다. l1은 a, l2는 b, l3은 a, l4는 b의 대사이고, 각 줄은 바로 앞 줄에 직접 반응하며 이어진다(맞장구, 반박, 되묻기, 농담 등).\n" +
            "- 각 줄은 짧은 한국어 구어체 한 문장(길어야 두 문장, 40자 안팎)이다. 말투와 태도는 각자의 성격과 서로에 대한 마음을 그대로 따른다. 두 사람의 말투가 달라야 한다.\n" +
            "- 대사 앞에 이름이나 \"이름:\"을 붙이지 않는다. 서로 모르는 사이라면 상대가 모를 길드 안 사정을 아는 척하지 않는다.\n" +
            "- [대화 거리]를 따른다. 가진 정보를 다 늘어놓지 않는다. 지난 대화(있으면)와 같은 말은 반복하지 않는다.\n" +
            "- 자기 이름은 부르지 않는다. 영어, 숫자, 지문(괄호 속 행동 묘사)은 쓰지 않는다.\n" +
            "- outcome: 대화가 어떻게 끝났는지 주어진 것 중 하나. warm(기분 좋게 끝남), neutral(그저 그렇게 끝남, 권유라면 좀 더 생각해 보기로 함), tense(서먹하거나 날이 선 채 끝남), fight(크게 싸우고 끝남), reconciled(화해하고 끝남), joined(권유를 받아들여 길드에 들어가기로 함), declined(권유를 거절함).\n" +
            "반드시 JSON 한 개로만 답한다.";

        [Serializable]
        public class ResponseDto
        {
            public string l1, l2, l3, l4;
            public string outcome;
        }

        public const string MerchantSheet =
            "성격: 넉살 좋고 말이 많은 떠돌이 행상인이다. 흥정을 즐기고, 여러 마을을 돌며 들은 이야기를 곧잘 늘어놓는다. 누구에게나 싹싹한 해요체로 말한다.";

        static string Role(Talker t) => t.Kind switch
        {
            TalkerKind.Merchant => "떠돌이 행상인",
            TalkerKind.Resident => $"{t.Resident.Profile.JobLabel}, 이 마을 주민",
            TalkerKind.Visitor => $"{AdventurerData.ClassLabel(t.Adventurer.Data.cls)}, 아직 길드에 들어오지 않은 방문자",
            _ => $"길드원 {AdventurerData.ClassLabel(t.Adventurer.Data.cls)}, 레벨 {t.Adventurer.Data.level}",
        };

        static void AppendPerson(StringBuilder sb, string tag, Talker x, Talker other)
        {
            sb.Append($"[{tag}] {x.Name} ({Role(x)})\n");
            if (x.Resident != null)
            {
                var p = x.Resident.Profile;
                string temper = NpcVoice.Temperament(p.Mbti);
                sb.Append("성격: ").Append(temper.Length > 0 ? temper + " " : "").Append(NpcVoice.ValueSentence(p.Values)).Append('\n');
                sb.Append("사람됨과 말투: ").Append(p.Persona).Append('\n');
                sb.Append("길드에 대한 생각: ").Append(p.OpinionText).Append('\n');
                int f = p.FeelingToward(other.Id);
                sb.Append($"{other.Name}에 대한 마음: {(other.Kind == TalkerKind.Visitor && f == 0 ? "처음 보는 사람" : Relationships.Label(f))}.\n");
                return;
            }
            if (x.Adventurer == null)
            {
                sb.Append(MerchantSheet).Append('\n');
                return;
            }
            var a = x.Adventurer;
            sb.Append(NpcVoice.Sheet(a)).Append('\n');
            sb.Append("상태: ").Append(NpcVoice.Condition(a)).Append('\n');
            var memories = AdventurerMemory.SelectForPrompt(a.Data.memories, 3).Select(m => m.Text).ToList();
            if (memories.Count > 0) sb.Append("겪은 일: ").Append(string.Join(" / ", memories)).Append('\n');
            if (other.Adventurer != null)
            {
                int feeling = Relationships.Get(a, other.Adventurer);
                sb.Append($"{other.Name}에 대한 마음: {Relationships.Label(feeling)}. {other.Name}에게 {NpcVoice.ToneToward(feeling)} 말한다.\n");
            }
            else if (other.Resident != null)
            {
                int f = other.Resident.Profile.FeelingToward(x.Id);
                sb.Append(x.Kind == TalkerKind.Visitor
                    ? $"{other.Name}은(는) 이 마을 주민이다. 처음 보는 사이라 예의를 지키며 말한다.\n"
                    : $"{other.Name}은(는) 마을에서 오가며 아는 주민이다({Relationships.Label(f)}). {NpcVoice.ToneToward(f)} 말한다.\n");
            }
            else sb.Append($"{other.Name}와는 가볍게 아는 사이라 예의를 지키며 말한다.\n");
        }

        /// <summary>What the talk is about, in words for the model.</summary>
        /// <param name="detail">Comfort: what weighs on B. Quarrel: A's grievance. MakeUp: what the fight was about.</param>
        public static string Topic(TownTalkKind kind, Talker a, Talker b, string rumor, string detail)
        {
            switch (kind)
            {
                case TownTalkKind.Comfort:
                    return $"{b.Name}은(는) 요즘 많이 힘들다" + (string.IsNullOrEmpty(detail) ? "." : $"({detail}).") +
                           $" {a.Name}은(는) {b.Name}을(를) 아끼는 사이라 먼저 다가가 위로하고 힘이 되어 준다. {b.Name}의 반응은 성격대로(고마워하거나, 괜찮은 척하거나, 털어놓거나).";
                // Both made much more concrete (live test 2026-10-01: kanana turned a "quarrel" into polite small
                // talk and a "making up" into catching up, without the fight ever coming up).
                case TownTalkKind.Quarrel:
                    return $"말다툼이다. {a.Name}은(는) {b.Name}에게 불만이 있다: {(string.IsNullOrEmpty(detail) ? "요즘 하는 짓이 영 거슬린다" : detail)}. " +
                           $"l1에서 {a.Name}이(가) 인사 없이 곧장 그 불만을 꺼내 따지고, {b.Name}은(는) 성격대로 발끈하거나 비꼬며 맞받아친다. " +
                           "네 줄 내내 서로 날 선 말을 주고받으며, 쉽게 사과하거나 수긍하지 않는다. 욕설은 쓰지 않는다.";
                case TownTalkKind.MakeUp:
                    return "화해를 시도하는 장면이다. 둘은 얼마 전 크게 다퉜다" + (string.IsNullOrEmpty(detail) ? ". " : $"(발단: {detail}). ") +
                           $"l1에서 {a.Name}이(가) 그 다툼을 직접 언급하며 어색하게 먼저 사과하거나 화해를 청한다. " +
                           $"{b.Name}이(가) 받아 줄지는 성격과 마음에 달렸다(받아 주면 reconciled, 아직 서운하면 tense).";
                case TownTalkKind.Gossip:
                    return $"{a.Name}이(가) 들은 이야기를 {b.Name}에게 전한다: \"{rumor}\". {b.Name}은(는) 성격대로 반응한다(놀람, 걱정, 의심, 흥미, 뒷말 등).";
                case TownTalkKind.Invite:
                    return (a.Kind == TalkerKind.Resident
                               ? $"{a.Name}은(는) 이 마을 주민으로서 {b.Name}에게 저 모험가 길드에 한번 들어가 보라고 권한다(길드에 대해 좋게 본 점을 들어)."
                               : $"{a.Name}이(가) {b.Name}에게 우리 길드에 들어오라고 직접 권한다(보수, 동료, 생활 등 구체적으로).") +
                           $" {b.Name}은(는) l2와 l4에서 마음 가는 대로 답하고, l4에서 결론을 분명히 말한다: 들어가겠다(joined), 좀 더 생각해 보겠다(neutral), 거절한다(declined)." +
                           (string.IsNullOrEmpty(detail) ? "" : $" {b.Name}의 지금 마음: {detail}");
                case TownTalkKind.Welcome when a.Kind == TalkerKind.Resident || b.Kind == TalkerKind.Resident:
                    var res = a.Kind == TalkerKind.Resident ? a : b;
                    var guest = a.Kind == TalkerKind.Resident ? b : a;
                    return $"{res.Name}은(는) 이 마을 주민이고, {guest.Name}은(는) 길드에 들어올까 고민 중인 방문자다. 마을 사정과 모험가 길드 이야기를 한다. " +
                           $"{res.Name}은(는) 위 '길드에 대한 생각' 그대로 솔직히 말한다(좋게 보면 추천하고, 탐탁지 않으면 흉을 본다).";
                case TownTalkKind.Welcome:
                    var visitor = a.Kind == TalkerKind.Visitor ? a : b;
                    return $"{visitor.Name}은(는) 아직 길드에 들어오지 않은 방문자다. l1부터 길드 이야기로 시작해, 길드 생활이 어떤지(보수, 동료, 위험, 길드 마스터), 들어올 만한지 이야기한다. " +
                           "길드원은 자기가 겪은 대로 솔직하게 말하고(좋은 점만 말하지 않아도 된다), 방문자는 자기가 중요하게 여기는 것을 기준으로 묻고 반응한다.";
                case TownTalkKind.Trade:
                    return "행상인과의 대화다. 물건값 흥정, 요즘 장사, 오는 길에 본 것 중 하나로 가볍게 이야기한다.";
                default:
                    var resident = a.Resident != null ? a : b.Resident != null ? b : null;
                    if (resident != null)
                    {
                        var other = resident == a ? b : a;
                        return other.Resident != null
                            ? "같은 마을 주민끼리의 일상 이야기. 각자의 일, 마을 살림, 날씨, 요즘 길드 소식 중 하나만 고른다."
                            : $"{resident.Name}의 일({resident.Resident.Profile.JobLabel})과 관련된 이야기나 {other.Name}의 근황 중 하나만 고른다.";
                    }
                    bool strangers = a.Kind != TalkerKind.Member && b.Kind != TalkerKind.Member;
                    return strangers
                        ? "둘 다 이 마을에 잠시 들른 사람이다. 이 마을과 모험가 길드에 대한 인상, 어디서 왔는지 같은 이야기를 가볍게 나눈다."
                        : "가벼운 일상 이야기. 최근 일, 서로의 근황, 마을 풍경 중 하나만 고른다.";
            }
        }

        public static string BuildUserText(TownTalkKind kind, Talker a, Talker b, string rumor = null, string detail = null)
        {
            var sb = new StringBuilder();
            // The topic goes first and is repeated at the end: small models drift to generic small talk otherwise.
            string topic = Topic(kind, a, b, rumor, detail);
            sb.Append("[대화 거리]\n").Append(topic).Append("\n\n");
            AppendPerson(sb, "a", a, b);
            sb.Append('\n');
            AppendPerson(sb, "b", b, a);
            // Guild news only for two members: strangers in town do not know it (live test 2026-10-01: two visitors
            // chatted about members' new skills). Lines with map labels ("둥지 #43") read oddly in speech.
            bool locals = (a.Kind == TalkerKind.Member || a.Kind == TalkerKind.Resident) && (b.Kind == TalkerKind.Member || b.Kind == TalkerKind.Resident);
            if (kind == TownTalkKind.Chat && locals && Services.TryGet<GameLog>(out var log))
            {
                var events = log.Recent(12).Where(l => !l.StartsWith("방문자") && !l.Contains("#")).TakeLast(3).ToList();
                if (events.Count > 0) sb.Append("\n[마을의 최근 소식]\n").Append(string.Join("\n", events.Select(e => "- " + e))).Append('\n');
            }
            if (a.Adventurer != null)
            {
                var last = DialogueLog.LastBanterWith(a.Adventurer.Data, b.Name, 2).Select(l => l.Format()).ToList();
                if (last.Count > 0) sb.Append("\n[지난번 둘의 대화]\n").Append(string.Join("\n", last)).Append('\n');
            }
            sb.Append($"\n이제 {a.Name}(a)이(가) {b.Name}(b)에게 먼저 말을 건다. 반드시 [대화 거리]대로: ").Append(topic);
            return sb.ToString();
        }

        public static string BuildGrammar(TownTalkKind kind)
            => new GbnfObject()
                .String("l1", LineMaxChars).String("l2", LineMaxChars).String("l3", LineMaxChars).String("l4", LineMaxChars)
                .Enum("outcome", TownTalkRules.OutcomesFor(kind).Select(TownTalkRules.Code))
                .Build();

        public static LlmRequest BuildRequest(TownTalkKind kind, Talker a, Talker b, string rumor = null, string detail = null) => new()
        {
            System = SystemPrompt + $"\na는 {a.Name}, b는 {b.Name}이다.",
            User = BuildUserText(kind, a, b, rumor, detail),
            Grammar = BuildGrammar(kind),
            MaxTokens = 300,
            Temperature = 0.9f,
            Priority = LlmPriority.Banter,
            Preemptible = true,   // yields to decisions, parties and dialogue windows
            DedupeKey = "towntalk",
            TimeoutSeconds = 45f,
            Tag = $"towntalk:{kind}:{a.Id}+{b.Id}",
        };

        /// <summary>
        /// "야, 잘 지내? 다리안: 응." → "야, 잘 지내?": the model sometimes writes both speakers into one line (live test
        /// 2026-10-01). Cut where a speaker tag starts after the beginning.
        /// </summary>
        public static string CutAtSpeakerTag(string line, string nameA, string nameB)
        {
            if (string.IsNullOrEmpty(line)) return line;
            int cut = line.Length;
            foreach (var n in new[] { nameA, nameB })
            {
                if (string.IsNullOrEmpty(n)) continue;
                foreach (var tag in new[] { n + ":", n + " :", n + "：" })
                {
                    int at = line.IndexOf(tag, 1, System.StringComparison.Ordinal);
                    if (at > 0 && at < cut) cut = at;
                }
            }
            return cut < line.Length ? line.Substring(0, cut).TrimEnd() : line;
        }

        /// <summary>"테오: 요즘 어때?" said by 테오 → "요즘 어때?" (the model sometimes writes a script).</summary>
        public static string StripSpeakerTag(string line, string name)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(name) || !line.StartsWith(name)) return line;
            string rest = line.Substring(name.Length).TrimStart();
            if (!rest.StartsWith(":") && !rest.StartsWith("：")) return line;
            rest = rest.Substring(1).TrimStart();
            return rest.Length > 0 ? rest : line;
        }

        /// <summary>
        /// The lines cleaned to whole sentences, each with who says it (0 = a, 1 = b). Needs the first two; a third
        /// without a fourth is fine, a fourth without a third is dropped. Empty when the answer is unusable.
        /// </summary>
        public static List<(int speaker, string text)> Interpret(ResponseDto dto, string nameA, string nameB)
        {
            var result = new List<(int, string)>();
            if (dto == null) return result;
            var raw = new[] { dto.l1, dto.l2, dto.l3, dto.l4 };
            for (int i = 0; i < raw.Length; i++)
            {
                string name = i % 2 == 0 ? nameA : nameB;
                string line = StripOwnName(StripLeadingMarks(StripSpeakerTag(LlmJson.CleanSentence(CutAtSpeakerTag(raw[i], nameA, nameB), LineMaxChars, LineMaxChars, true), name)), name);
                if (string.IsNullOrWhiteSpace(line)) break;
                result.Add((i % 2, line));
            }
            if (result.Count < 2) result.Clear();
            return result;
        }

        // ---- Line clean-up (were BanterPrompt's, which main removed; copied here at the 2026-10-31 merge) ----

        static readonly System.Text.RegularExpressions.Regex LeadingMark =
            new(@"^\s*[\(（][^\)）]{0,20}[\)）]\s*");

        /// <summary>Drops a stage direction the model put in front ("(웃으며) ...").</summary>
        public static string StripLeadingMarks(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            string cur = line;
            for (int i = 0; i < 2; i++)
            {
                string next = LeadingMark.Replace(cur, "");
                if (next.Length == 0) return cur;
                cur = next;
            }
            return cur;
        }

        static readonly string[] VocativeSuffixes = { "아", "야", "이", "" };

        /// <summary>Drops the speaker calling themselves by name at the start ("아리스, ...").</summary>
        public static string StripOwnName(string line, string ownName)
        {
            if (string.IsNullOrEmpty(line)) return line;
            // The model sometimes writes names as "[아리스]" (JSON-ish habit).
            line = line.Replace("[", "").Replace("]", "");
            if (string.IsNullOrEmpty(ownName)) return line;
            foreach (var suffix in VocativeSuffixes)
            {
                string head = ownName + suffix;
                if (!line.StartsWith(head) || line.Length <= head.Length) continue;
                char next = line[head.Length];
                if (next != ',' && next != '.' && next != '!' && next != ' ') continue;
                string rest = line.Substring(head.Length).TrimStart(',', '.', '!', ' ');
                return rest.Length > 0 ? rest : line;
            }
            return line;
        }
    }
}
