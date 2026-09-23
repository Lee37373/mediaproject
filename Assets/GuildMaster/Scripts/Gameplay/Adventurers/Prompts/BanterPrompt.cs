using System;
using System.Linq;
using System.Text;
using GuildMaster.Core;
using GuildMaster.LLM;
using GuildMaster.Session;

namespace GuildMaster.Adventurers.Prompts
{
    /// <summary>
    /// Idle small talk between two guild members in town (P3-5), or a member and a visitor still
    /// deciding whether to join (user request 2026-09-23; see BanterService.ApplyVisitorAffinity).
    /// Topic: recent events and each other's traits. Two short lines: A speaks, B answers. DTO field
    /// names are the JSON keys.
    /// </summary>
    public static class BanterPrompt
    {
        /// <summary>Spec asks for ~50 chars; the cap leaves room to finish the sentence (see CleanSentence).</summary>
        public const int LineMaxChars = 80;
        public const int RecentEventCount = 4;

        public const string SystemPrompt =
            "너는 판타지 모험가 길드의 두 모험가 a, b가 마을에서 쉬며 나누는 짧은 잡담을 쓴다. 실제 동료끼리 말하듯 자연스러워야 한다.\n" +
            "- 딱 두 줄이다. a가 먼저 말을 걸고, b가 그 말에 직접 반응해 대답한다(맞장구, 반박, 되묻기 등).\n" +
            "- 각 줄은 한두 문장의 자연스러운 한국어 구어체다. 말투는 아래에 적힌, 서로에 대한 감정에 맞춘다.\n" +
            "- 소재는 길드의 최근 소식, 각자의 일, 서로에 대한 감정 중 하나만 가볍게 고른다. 가진 정보를 다 늘어놓지 않는다.\n" +
            "- 둘 중 하나가 아직 길드에 들어오지 않은 방문자라면, 그 사실이 자연스럽게 대화에 묻어나도 된다(적응, 텃세 없이 궁금해하는 정도).\n" +
            "- 지난 대화(있으면)와 같은 말은 반복하지 않는다. 이어지는 이야기나 그 뒤의 일을 말해도 좋다.\n" +
            "- 자기 이름은 부르지 않는다. 영어, 숫자, 지문(괄호 속 행동 묘사)은 쓰지 않는다.\n" +
            "- a_to_b, b_to_a: 이 대화 뒤 상대에 대한 감정 변화(-5~5). 즐겁거나 공감되면 +, 서운하거나 부딪치면 -, 별일 없으면 0.\n" +
            "형식 예(다른 인물이고 분위기만 참고): {\"a\":\"밥은 먹었어? 오늘 안색이 안 좋아 보여.\",\"b\":\"어젯밤에 잠을 설쳤어. 괜찮아, 고마워.\",\"a_to_b\":1,\"b_to_a\":1}\n" +
            "반드시 JSON 한 개로만 답한다.";

        [Serializable]
        public class ResponseDto
        {
            public string a;
            public string b;
            public int a_to_b;
            public int b_to_a;
        }

        static void AppendPerson(StringBuilder sb, string tag, Adventurer x, Adventurer other)
        {
            var d = x.Data;
            sb.Append($"[{tag}] {d.name} ({AdventurerData.ClassLabel(d.cls)}, 레벨 {d.level})");
            if (x.IsVisitor) sb.Append(" — 아직 길드에 들어오지 않은 방문자");
            sb.Append('\n');
            sb.Append(NpcVoice.Sheet(x)).Append('\n');
            sb.Append("상태: ").Append(NpcVoice.Condition(x)).Append('\n');
            var memories = AdventurerMemory.SelectForPrompt(d.memories, 3).Select(m => m.Format()).ToList();
            if (memories.Count > 0) sb.Append("겪은 일: ").Append(string.Join(" / ", memories)).Append('\n');
            int feeling = Relationships.Get(x, other);
            sb.Append($"{other.Data.name}에 대한 마음: {Relationships.Label(feeling)}. {d.name}은(는) {other.Data.name}에게 {NpcVoice.ToneToward(feeling)} 말한다.\n");
        }

        /// <summary>The scene as readable text: two people (character sheets, feelings, tone), the guild's news, the last talk.</summary>
        public static string BuildUserText(Adventurer a, Adventurer b)
        {
            var sb = new StringBuilder();
            AppendPerson(sb, "a", a, b);
            sb.Append('\n');
            AppendPerson(sb, "b", b, a);
            // Visitor arrivals/departures flood the log and would become the only topic.
            var events = Services.TryGet<GameLog>(out var log)
                ? log.Recent(12).Where(l => !l.StartsWith("방문자")).TakeLast(RecentEventCount).ToList()
                : new System.Collections.Generic.List<string>();
            if (events.Count > 0)
            {
                sb.Append("\n[길드의 최근 소식]\n");
                foreach (var ev in events) sb.Append("- ").Append(ev).Append('\n');
            }
            var last = DialogueLog.LastBanterWith(a.Data, b.Data.name, 2).Select(l => l.Format()).ToList();
            if (last.Count > 0)
            {
                sb.Append("\n[지난번 둘의 대화]\n");
                foreach (var line in last) sb.Append(line).Append('\n');
            }
            sb.Append($"\n이제 {a.Data.name}(a)이(가) {b.Data.name}(b)에게 먼저 말을 건다.");
            return sb.ToString();
        }

        public static string Grammar { get; } = new GbnfObject()
            .String("a", LineMaxChars)
            .String("b", LineMaxChars)
            .Int("a_to_b", 1)
            .Int("b_to_a", 1)
            .Build();

        /// <summary>Names who says what: without it the model mixes up speaker and listener.</summary>
        public static string SystemFor(Adventurer a, Adventurer b)
            => SystemPrompt + $"\na는 {a.Data.name}이(가) {b.Data.name}에게 하는 말, b는 {b.Data.name}이(가) {a.Data.name}에게 하는 대답이다.";

        public static LlmRequest BuildRequest(Adventurer a, Adventurer b) => new()
        {
            System = SystemFor(a, b),
            User = BuildUserText(a, b),
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
