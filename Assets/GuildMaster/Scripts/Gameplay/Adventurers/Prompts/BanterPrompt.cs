using System;
using System.Linq;
using GuildMaster.Core;
using GuildMaster.LLM;
using GuildMaster.Session;

namespace GuildMaster.Adventurers.Prompts
{
    /// <summary>
    /// Idle small talk between two guild members in town (P3-5). Topic: recent events and each
    /// other's traits. Two short lines: A speaks, B answers. DTO field names are the JSON keys.
    /// </summary>
    public static class BanterPrompt
    {
        /// <summary>Spec asks for ~50 chars; the cap leaves room to finish the sentence (see CleanSentence).</summary>
        public const int LineMaxChars = 80;
        public const int RecentEventCount = 4;

        public const string SystemPrompt =
            "너는 판타지 모험가 길드의 두 모험가(a, b)가 마을에서 쉬며 나누는 짧은 잡담을 쓴다. " +
            "주어진 JSON의 성격(mbti, values), 특성(traits), 기억(memories), 길드의 최근 소식(recent_events)을 소재로 삼는다. last_talk는 두 사람이 지난번에 나눈 대화다(있으면 이어지는 화제나 그 뒤의 일을 말해도 좋지만 똑같은 말을 반복하지 않는다). " +
            "a가 먼저 말을 걸고 b가 자기 성격대로 대답한다. 서로 이름을 불러도 좋다. " +
            "각 대사는 자연스러운 한국어 구어체 한두 문장으로 짧게 쓴다. 영어를 섞지 않는다.\n" +
            "feeling_to_other는 상대에 대한 현재 감정이다(친함/호감/보통/껄끄러움/적대). 말투에 반영한다: 친하면 편하고 다정하게, 껄끄러우면 퉁명스럽거나 비꼬게.\n" +
            "a_to_b와 b_to_a는 이 대화 뒤 감정의 변화다(-5~5). 대화가 즐겁거나 공감되면 +, 서운하거나 성격이 부딪치면 -, 별일 없으면 0 근처. " +
            "형식 예: {\"a\":\"(a의 대사)\",\"b\":\"(b의 대사)\",\"a_to_b\":1,\"b_to_a\":0} — a와 b에는 반드시 말로 된 대사만 쓰고 숫자나 설명은 쓰지 않는다. 숫자는 a_to_b, b_to_a에만 쓴다. " +
            "반드시 JSON 한 개로만 답한다.";

        [Serializable]
        public class SpeakerDto
        {
            public string name;
            public string @class;
            public int level;
            public string mbti;
            public string values;
            public string[] traits;
            public int hp_percent;
            public string money_status;
            public string[] memories;
            /// <summary>"친함(+42)" — this speaker's feeling towards the other one.</summary>
            public string feeling_to_other;
        }

        [Serializable]
        public class ContextDto
        {
            public SpeakerDto a;
            public SpeakerDto b;
            public string[] recent_events;
            public string[] last_talk;
        }

        [Serializable]
        public class ResponseDto
        {
            public string a;
            public string b;
            public int a_to_b;
            public int b_to_a;
        }

        static SpeakerDto Speaker(Adventurer x, Adventurer other) => new()
        {
            name = x.Data.name,
            @class = AdventurerData.ClassLabel(x.Data.cls),
            level = x.Data.level,
            mbti = x.Data.mbti,
            values = x.Data.values,
            traits = x.Traits != null ? x.Traits.PromptHints.ToArray() : Array.Empty<string>(),
            hp_percent = (int)(x.HpRatio * 100f),
            money_status = DecisionPrompt.MoneyLabel(x.Data.gold),
            memories = AdventurerMemory.SelectForPrompt(x.Data.memories, 3).Select(m => m.Format()).ToArray(),
            feeling_to_other = Relationships.Describe(Relationships.Get(x, other)),
        };

        public static ContextDto BuildContext(Adventurer a, Adventurer b) => new()
        {
            a = Speaker(a, b),
            b = Speaker(b, a),
            // Visitor arrivals/departures flood the log and would become the only topic.
            recent_events = Services.TryGet<GameLog>(out var log)
                ? log.Recent(12).Where(l => !l.StartsWith("방문자")).TakeLast(RecentEventCount).ToArray()
                : Array.Empty<string>(),
            last_talk = DialogueLog.LastBanterWith(a.Data, b.Data.name, 2).Select(l => l.Format()).ToArray(),
        };

        public static string Grammar { get; } = new GbnfObject()
            .String("a", LineMaxChars)
            .String("b", LineMaxChars)
            .Int("a_to_b", 1)
            .Int("b_to_a", 1)
            .Build();

        /// <summary>Names who says what: without it the model mixes up speaker and listener.</summary>
        public static string SystemFor(Adventurer a, Adventurer b)
            => SystemPrompt + $"\na는 {a.Data.name}이(가) {b.Data.name}에게 하는 말, b는 {b.Data.name}이(가) {a.Data.name}에게 하는 대답이다. " +
               "자기 이름을 부르지 않는다.";

        public static LlmRequest BuildRequest(Adventurer a, Adventurer b) => new()
        {
            System = SystemFor(a, b),
            User = LlmJson.ToJson(BuildContext(a, b)),
            Grammar = Grammar,
            // Short on purpose: banter must not hold up decisions for long.
            MaxTokens = 220,
            Temperature = 0.9f,
            Priority = LlmPriority.Banter,
            Preemptible = true, // yields to any decision/party/dialogue request mid-generation
            DedupeKey = "banter",
            TimeoutSeconds = 40f, // preemptible, so a long wait never blocks real work
            Tag = $"banter:{a.Data.id}+{b.Data.id}",
        };

        /// <summary>Both lines cleaned to whole sentences; false if either is empty (→ fixed pair).</summary>
        public static bool TryInterpret(ResponseDto dto, out string lineA, out string lineB, string nameA = null, string nameB = null)
        {
            lineA = StripOwnName(StripLeadingMarks(LlmJson.CleanSentence(dto?.a, LineMaxChars, LineMaxChars, true)), nameA);
            lineB = StripOwnName(StripLeadingMarks(LlmJson.CleanSentence(dto?.b, LineMaxChars, LineMaxChars, true)), nameB);
            return !string.IsNullOrEmpty(lineA) && !string.IsNullOrEmpty(lineB);
        }

        public const int MaxDelta = 5;

        /// <summary>Model's feeling change clamped to ±MaxDelta (the grammar allows one digit).</summary>
        public static int ClampDelta(int d) => Math.Max(-MaxDelta, Math.Min(MaxDelta, d));

        static readonly System.Text.RegularExpressions.Regex LeadingMark =
            new(@"^\s*[\(（][^\)）]{0,20}[\)）]\s*");

        /// <summary>
        /// Small models sometimes open a line with a score ("(0) ...") or a stage direction
        /// ("(고개를 끄덕이며) ..."); drop such a leading parenthesis (keeps the line if nothing else is left).
        /// </summary>
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

        /// <summary>
        /// The model often opens a speaker's line with the speaker's own name ("로웬, 오늘…" said by
        /// 로웬) despite the instruction; drop such a leading self-address.
        /// </summary>
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

        /// <summary>Fallback pairs when the LLM is unavailable or answers badly.</summary>
        public static readonly (string a, string b)[] FixedPairs =
        {
            ("오늘따라 마을이 조용하네.", "폭풍 전의 고요일지도 모르지."),
            ("요즘 의뢰 보수가 어때?", "그럭저럭이야. 물약값만 안 올랐으면 좋겠어."),
            ("다음엔 같이 나가볼래?", "좋지. 등 뒤는 맡겨줘."),
            ("어제 숲에서 이상한 소리 들었어.", "고블린이겠지. 조심해서 나쁠 건 없어."),
            ("배고프다. 여관 스튜 먹으러 갈까?", "또 스튜야? 뭐, 싫진 않아."),
            ("길드 마스터가 요즘 바빠 보여.", "우리가 일을 잘하면 좀 편해지겠지."),
        };
    }
}
