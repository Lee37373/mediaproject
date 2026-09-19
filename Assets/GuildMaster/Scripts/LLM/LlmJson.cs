using System;
using System.Text;
using UnityEngine;

namespace GuildMaster.LLM
{
    /// <summary>JSON helpers built on JsonUtility (DTO fields use snake_case names directly).</summary>
    public static class LlmJson
    {
        /// <summary>Parses the first top-level JSON object in the text. Returns false on any failure.</summary>
        public static bool TryParse<T>(string text, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return false;
            try
            {
                value = JsonUtility.FromJson<T>(text.Substring(start, end - start + 1));
                return value != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string ToJson(object o, bool pretty = false) => JsonUtility.ToJson(o, pretty);

        /// <summary>Escapes a string for safe embedding in prompt text / JSON.</summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    default: sb.Append(c < ' ' ? ' ' : c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Indents compact JSON for reading (string contents are left untouched).</summary>
        public static string Pretty(string json, string indent = "  ")
        {
            if (string.IsNullOrEmpty(json)) return json;
            var sb = new StringBuilder(json.Length * 2);
            int depth = 0;
            bool inString = false, escaped = false;
            foreach (char c in json)
            {
                if (inString)
                {
                    sb.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        sb.Append(c).Append('\n');
                        depth++;
                        for (int i = 0; i < depth; i++) sb.Append(indent);
                        break;
                    case '}':
                    case ']':
                        sb.Append('\n');
                        depth = Math.Max(0, depth - 1);
                        for (int i = 0; i < depth; i++) sb.Append(indent);
                        sb.Append(c);
                        break;
                    case ',':
                        sb.Append(c).Append('\n');
                        for (int i = 0; i < depth; i++) sb.Append(indent);
                        break;
                    case ':': sb.Append(": "); break;
                    case ' ':
                    case '\n':
                    case '\r':
                    case '\t':
                        break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Trims model output text for display (removes quotes/whitespace, caps length).</summary>
        public static string CleanLine(string s, int maxLen = 100)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim().Trim('"').Trim();
            return s.Length > maxLen ? s.Substring(0, maxLen - 1) + "…" : s;
        }

        /// <summary>
        /// Cleans a generated line so it never ends mid-sentence. The grammar allows up to
        /// <paramref name="grammarCap"/> chars; text that long was cut by the grammar, not ended by
        /// the model. Over-long or cut text is shortened to its last complete sentence within
        /// <paramref name="maxLen"/>; with none, it is dropped ("") or, if
        /// <paramref name="dropIfUnfinished"/> is false, cut at a word boundary with "…".
        /// </summary>
        public static string CleanSentence(string s, int maxLen, int grammarCap, bool dropIfUnfinished)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Replace("\\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim().Trim('"').Trim();
            bool cutByGrammar = s.Length >= grammarCap;
            if (!cutByGrammar && s.Length <= maxLen) return s;

            int end = LastSentenceEnd(s, maxLen);
            if (end >= Math.Min(6, maxLen / 3)) return s.Substring(0, end).Trim();
            if (dropIfUnfinished) return "";
            int space = s.LastIndexOf(' ', Math.Min(s.Length, maxLen) - 1);
            int cut = space > maxLen / 3 ? space : Math.Min(s.Length, maxLen - 1);
            return s.Substring(0, cut).TrimEnd(' ', ',') + "…";
        }

        const string Terminators = ".!?…~";
        // Common Korean sentence-final syllables; only trusted before a space/comma or at the end.
        // Kept short on purpose: 지/어/고 etc. also end words mid-sentence ("끝까지 찾아서").
        const string KoreanEndings = "다요까죠네야";

        /// <summary>Length of the longest prefix (≤ maxLen) that ends a sentence, or 0.</summary>
        static int LastSentenceEnd(string s, int maxLen)
        {
            int limit = Math.Min(s.Length, maxLen);
            for (int i = limit - 1; i >= 0; i--)
            {
                char c = s[i];
                if (Terminators.IndexOf(c) >= 0)
                {
                    int j = i + 1;
                    while (j < s.Length && j < maxLen && Terminators.IndexOf(s[j]) >= 0) j++;
                    return j;
                }
                bool boundary = i + 1 >= s.Length || s[i + 1] == ' ' || s[i + 1] == ',';
                if (boundary && KoreanEndings.IndexOf(c) >= 0) return i + 1;
            }
            return 0;
        }
    }
}
