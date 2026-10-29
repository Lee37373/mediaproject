using GuildMaster.LLM;

namespace GuildMaster.Incidents
{
    /// <summary>
    /// The model describes an event card's situation in a few lively sentences (F-13). It only describes: the choices
    /// and what they do come from <see cref="EventCards"/>, so a bad answer costs nothing but the description, and the
    /// fixed text stays when the model is busy or fails.
    /// </summary>
    public static class EventCardPrompt
    {
        public const int MaxChars = 170;
        const int GrammarCap = 200;

        public class ResponseDto
        {
            public string text;
        }

        const string SystemPrompt =
            "너는 중세 판타지 모험가 길드 경영 게임의 내레이터다. 주어진 사건 상황을 두세 문장으로 생생하게 묘사한다.\n" +
            "규칙: 주어진 사실만 쓴다. 새 인물, 숫자, 금액, 결과, 선택지는 쓰지 않는다. 등장인물 이름은 주어진 그대로 쓴다. " +
            "한국어 해라체 서술(~다)로 쓰고, 따옴표 대사는 한 마디까지만 넣을 수 있다.";

        public static LlmRequest BuildRequest(DrawnCard card, string castNotes, string time) => new()
        {
            System = SystemPrompt,
            User = $"사건: {card.Title}\n상황: {card.Narration}\n" + (string.IsNullOrEmpty(castNotes) ? "" : $"등장인물: {castNotes}\n") +
                   $"때: {time}\n위 상황을 두세 문장으로 묘사하라.",
            Grammar = new GbnfObject().String("text", GrammarCap).Build(),
            MaxTokens = 220,
            Temperature = 0.8f,
            Priority = LlmPriority.Banter,
            Preemptible = true,
            DedupeKey = "eventcard",
            TimeoutSeconds = 40f,
            Tag = "eventcard:" + card.Def.Id,
        };

        /// <summary>The cleaned description, or null when it is unusable (empty, or leaves out who it is about).</summary>
        public static string Interpret(ResponseDto dto, DrawnCard card)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.text)) return null;
            string text = LlmJson.CleanSentence(dto.text, MaxChars, GrammarCap, false);
            if (string.IsNullOrWhiteSpace(text) || text.Length < 12) return null;
            if (card.A.HasValue && !text.Contains(card.A.Value.Name)) return null;
            if (card.B.HasValue && !text.Contains(card.B.Value.Name)) return null;
            return text;
        }
    }
}
