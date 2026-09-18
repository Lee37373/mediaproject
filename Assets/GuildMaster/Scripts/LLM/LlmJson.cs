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
    }
}
